using MyMBG.Models;
using Npgsql;

namespace MyMBG.Data;

public sealed class EntityMetadataProvider
{
    private static readonly Dictionary<string, string[]> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["users"] = ["users", "user", "pengguna"],
        ["bahan-baku"] = ["bahan_baku", "bahanbaku", "bahan-baku", "bahan"],
        ["resep"] = ["resep", "recipes", "recipe"],
        // Pivot resep ↔ bahan (ResepFormPage uses logical name "resep-bahan")
        ["resep-bahan"] = ["resep_bahan", "resep-bahan", "resepbahan"],
        ["produksi"] = ["produksi", "production", "sesi_produksi"],
        ["distribusi"] = ["distribusi", "distribution"]
    };

    private readonly NpgsqlDataSource _dataSource;
    private readonly Dictionary<string, EntityDefinition> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    public EntityMetadataProvider(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<IReadOnlyDictionary<string, EntityDefinition>> GetAllAsync()
    {
        if (_loaded)
        {
            return _cache;
        }

        await LoadAsync();
        return _cache;
    }

    public async Task<EntityDefinition?> GetByLogicalNameAsync(string logicalName)
    {
        await GetAllAsync();
        _cache.TryGetValue(logicalName, out var entity);
        return entity;
    }

    private async Task LoadAsync()
    {
        if (_loaded)
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync();

        const string tablesSql =
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
            ORDER BY table_name
            """;

        var allTables = new List<string>();
        await using (var cmd = new NpgsqlCommand(tablesSql, connection))
        await using (var reader = await cmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                allTables.Add(reader.GetString(0));
            }
        }

        foreach (var pair in Aliases)
        {
            var table = ResolveTableName(pair.Value, allTables);
            if (table is null)
            {
                continue;
            }

            var columns = await LoadColumnsAsync(connection, table);
            var pk = columns.FirstOrDefault(c => c.IsPrimaryKey)?.Name
                     ?? columns.FirstOrDefault()?.Name;
            if (pk is null)
            {
                continue;
            }

            _cache[pair.Key] = new EntityDefinition(pair.Key, table, pk, columns);
        }

        _loaded = true;
    }

    private static string? ResolveTableName(IEnumerable<string> aliases, List<string> allTables)
    {
        foreach (var alias in aliases)
        {
            var exact = allTables.FirstOrDefault(t => string.Equals(t, alias, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        foreach (var alias in aliases)
        {
            var contains = allTables.FirstOrDefault(t => t.Contains(alias, StringComparison.OrdinalIgnoreCase));
            if (contains is not null)
            {
                return contains;
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<ColumnDefinition>> LoadColumnsAsync(NpgsqlConnection connection, string tableName)
    {
        const string sql =
            """
            SELECT
                c.column_name,
                c.data_type,
                c.is_nullable,
                c.column_default,
                c.udt_name,
                EXISTS (
                    SELECT 1
                    FROM information_schema.table_constraints tc
                    JOIN information_schema.key_column_usage kcu
                      ON tc.constraint_name = kcu.constraint_name
                     AND tc.table_schema = kcu.table_schema
                    WHERE tc.constraint_type = 'PRIMARY KEY'
                      AND tc.table_schema = 'public'
                      AND tc.table_name = c.table_name
                      AND kcu.column_name = c.column_name
                ) AS is_primary_key
            FROM information_schema.columns c
            WHERE c.table_schema = 'public'
              AND c.table_name = @tableName
            ORDER BY c.ordinal_position;
            """;

        var output = new List<ColumnDefinition>();
        await using var cmd = new NpgsqlCommand(sql, connection);
        cmd.Parameters.AddWithValue("tableName", tableName);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            output.Add(new ColumnDefinition(
                reader.GetString(0),
                reader.GetString(1),
                string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase),
                !reader.IsDBNull(3),
                reader.GetBoolean(5),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return output;
    }
}
