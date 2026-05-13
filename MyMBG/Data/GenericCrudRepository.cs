using System.Text.Json;
using MyMBG.Models;
using Npgsql;

namespace MyMBG.Data;

public sealed class GenericCrudRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly EntityMetadataProvider _metadataProvider;

    public GenericCrudRepository(NpgsqlDataSource dataSource, EntityMetadataProvider metadataProvider)
    {
        _dataSource = dataSource;
        _metadataProvider = metadataProvider;
    }

    public async Task<IReadOnlyDictionary<string, EntityDefinition>> GetEntitiesAsync()
        => await _metadataProvider.GetAllAsync();

    public async Task<EntityDefinition?> GetEntityAsync(string logicalName)
        => await _metadataProvider.GetByLogicalNameAsync(logicalName);

    public async Task<PagedResult> ListAsync(string logicalName, ListQuery query)
    {
        var entity = await RequireEntityAsync(logicalName);
        await using var connection = await _dataSource.OpenConnectionAsync();

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var offset = (page - 1) * pageSize;

        var whereSql = "";
        var searchColumns = entity.Columns
            .Where(c => c.DataType.Contains("character", StringComparison.OrdinalIgnoreCase) ||
                        c.DataType.Contains("text", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var search = query.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(search) && searchColumns.Count > 0)
        {
            var parts = searchColumns.Select(c => $"{Quote(c.Name)} ILIKE @search");
            whereSql = $"WHERE ({string.Join(" OR ", parts)})";
        }

        var totalSql = $"SELECT COUNT(*) FROM {Quote(entity.TableName)} {whereSql}";
        await using var totalCmd = new NpgsqlCommand(totalSql, connection);
        if (!string.IsNullOrWhiteSpace(search) && searchColumns.Count > 0)
        {
            totalCmd.Parameters.AddWithValue("search", $"%{search}%");
        }

        var total = Convert.ToInt32(await totalCmd.ExecuteScalarAsync());

        var listSql =
            $"""
             SELECT *
             FROM {Quote(entity.TableName)}
             {whereSql}
             ORDER BY {Quote(entity.PrimaryKey)} DESC
             LIMIT @limit OFFSET @offset
             """;
        await using var listCmd = new NpgsqlCommand(listSql, connection);
        listCmd.Parameters.AddWithValue("limit", pageSize);
        listCmd.Parameters.AddWithValue("offset", offset);
        if (!string.IsNullOrWhiteSpace(search) && searchColumns.Count > 0)
        {
            listCmd.Parameters.AddWithValue("search", $"%{search}%");
        }

        var items = await ReadRowsAsync(listCmd);
        return new PagedResult(page, pageSize, total, items);
    }

    public async Task<Dictionary<string, object?>?> GetByIdAsync(string logicalName, string id)
    {
        var entity = await RequireEntityAsync(logicalName);
        await using var connection = await _dataSource.OpenConnectionAsync();

        var sql =
            $"""
             SELECT *
             FROM {Quote(entity.TableName)}
             WHERE {Quote(entity.PrimaryKey)} = @id
             LIMIT 1
             """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", ConvertInputValue(id, entity.Columns.First(c => c.Name == entity.PrimaryKey)));
        var rows = await ReadRowsAsync(cmd);
        return rows.FirstOrDefault();
    }

    public async Task<Dictionary<string, object?>> CreateAsync(string logicalName, JsonElement payload)
    {
        var entity = await RequireEntityAsync(logicalName);
        var payloadMap = ExtractObject(payload);
        var allowedColumns = entity.Columns.Where(c => !c.IsPrimaryKey).ToList();
        // Only columns in the payload; omitted columns use PostgreSQL DEFAULTs.
        var writable = allowedColumns
            .Where(c => payloadMap.ContainsKey(c.Name))
            .ToList();

        if (writable.Count == 0)
        {
            throw new InvalidOperationException("Tidak ada field yang bisa disimpan.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync();
        var columnSql = string.Join(", ", writable.Select(c => Quote(c.Name)));
        var valueSql = string.Join(", ", writable.Select(c => $"@{c.Name}"));
        var sql =
            $"""
             INSERT INTO {Quote(entity.TableName)} ({columnSql})
             VALUES ({valueSql})
             RETURNING *
             """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        foreach (var col in writable)
        {
            object? raw = payloadMap.TryGetValue(col.Name, out var val) ? val : null;
            var convertedValue = ConvertInputValue(raw, col);
            AddCommandParameter(cmd, col, convertedValue ?? DBNull.Value);
        }

        var rows = await ReadRowsAsync(cmd);
        return rows[0];
    }

    public async Task<Dictionary<string, object?>?> UpdateAsync(string logicalName, string id, JsonElement payload)
    {
        var entity = await RequireEntityAsync(logicalName);
        var payloadMap = ExtractObject(payload);
        var allowed = entity.Columns.Where(c => !c.IsPrimaryKey && payloadMap.ContainsKey(c.Name)).ToList();
        if (allowed.Count == 0)
        {
            throw new InvalidOperationException("Tidak ada field yang diperbarui.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync();
        var setSql = string.Join(", ", allowed.Select(c => $"{Quote(c.Name)} = @{c.Name}"));
        var sql =
            $"""
             UPDATE {Quote(entity.TableName)}
             SET {setSql}
             WHERE {Quote(entity.PrimaryKey)} = @id
             RETURNING *
             """;

        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", ConvertInputValue(id, entity.Columns.First(c => c.Name == entity.PrimaryKey)));
        foreach (var col in allowed)
        {
            var convertedValue = ConvertInputValue(payloadMap[col.Name], col);
            AddCommandParameter(cmd, col, convertedValue ?? DBNull.Value);
        }

        var rows = await ReadRowsAsync(cmd);
        return rows.FirstOrDefault();
    }

    public async Task<bool> DeleteAsync(string logicalName, string id)
    {
        var entity = await RequireEntityAsync(logicalName);
        await using var connection = await _dataSource.OpenConnectionAsync();
        var sql =
            $"""
             DELETE FROM {Quote(entity.TableName)}
             WHERE {Quote(entity.PrimaryKey)} = @id
             """;
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("id", ConvertInputValue(id, entity.Columns.First(c => c.Name == entity.PrimaryKey)));
        var affected = await cmd.ExecuteNonQueryAsync();
        return affected > 0;
    }

    private async Task<EntityDefinition> RequireEntityAsync(string logicalName)
    {
        var entity = await _metadataProvider.GetByLogicalNameAsync(logicalName);
        return entity ?? throw new InvalidOperationException($"Entity '{logicalName}' tidak ditemukan di schema database.");
    }

    private static void AddCommandParameter(NpgsqlCommand cmd, ColumnDefinition col, object value)
    {
        if (value is DBNull)
        {
            cmd.Parameters.AddWithValue(col.Name, DBNull.Value);
            return;
        }

        if (string.Equals(col.DataType, "USER-DEFINED", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(col.UdtName)
            && value is string enumLabel)
        {
            var dataTypeName = col.UdtName.Contains('.', StringComparison.Ordinal)
                ? col.UdtName
                : $"public.{col.UdtName}";
            cmd.Parameters.Add(new NpgsqlParameter(col.Name, enumLabel) { DataTypeName = dataTypeName });
            return;
        }

        cmd.Parameters.AddWithValue(col.Name, value);
    }

    private static string Quote(string identifier) => $"\"{identifier.Replace("\"", "\"\"")}\"";

    private static async Task<List<Dictionary<string, object?>>> ReadRowsAsync(NpgsqlCommand command)
    {
        var rows = new List<Dictionary<string, object?>>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }

            rows.Add(row);
        }

        return rows;
    }

    private static Dictionary<string, JsonElement> ExtractObject(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Payload harus berupa object JSON.");
        }

        var map = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in payload.EnumerateObject())
        {
            map[prop.Name] = prop.Value;
        }

        return map;
    }

    private static object ConvertInputValue(object? rawValue, ColumnDefinition column)
    {
        if (rawValue is null)
        {
            return DBNull.Value;
        }

        if (rawValue is string inputText)
        {
            return ConvertString(inputText, column);
        }

        if (rawValue is JsonElement jsonElement)
        {
            return ConvertJson(jsonElement, column);
        }

        return rawValue;
    }

    private static object ConvertJson(JsonElement json, ColumnDefinition column)
    {
        if (json.ValueKind == JsonValueKind.Null)
        {
            return DBNull.Value;
        }

        if (json.ValueKind == JsonValueKind.String)
        {
            return ConvertString(json.GetString() ?? string.Empty, column);
        }

        var type = column.DataType.ToLowerInvariant();
        return type switch
        {
            "integer" => json.GetInt32(),
            "bigint" => json.GetInt64(),
            "smallint" => json.GetInt16(),
            "boolean" => json.GetBoolean(),
            "real" => json.GetSingle(),
            "double precision" => json.GetDouble(),
            "numeric" => json.GetDecimal(),
            "uuid" => Guid.Parse(json.GetString() ?? string.Empty),
            _ => json.ToString()
        };
    }

    private static object ConvertString(string value, ColumnDefinition column)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DBNull.Value;
        }

        var type = column.DataType.ToLowerInvariant();
        return type switch
        {
            "integer" => int.Parse(value),
            "bigint" => long.Parse(value),
            "smallint" => short.Parse(value),
            "boolean" => bool.Parse(value),
            "real" => float.Parse(value),
            "double precision" => double.Parse(value),
            "numeric" => decimal.Parse(value),
            "uuid" => Guid.Parse(value),
            "date" => DateOnly.Parse(value),
            "timestamp without time zone" => DateTime.Parse(value),
            "timestamp with time zone" => DateTime.Parse(value),
            _ => value
        };
    }
}
