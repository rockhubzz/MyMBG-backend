namespace MyMBG.Models;

public sealed record ColumnDefinition(
    string Name,
    string DataType,
    bool IsNullable,
    bool HasDefaultValue,
    bool IsPrimaryKey,
    string? UdtName = null);

public sealed record EntityDefinition(
    string LogicalName,
    string TableName,
    string PrimaryKey,
    IReadOnlyList<ColumnDefinition> Columns);

public sealed record ListQuery(int Page = 1, int PageSize = 20, string? Search = null);

public sealed record PagedResult(
    int Page,
    int PageSize,
    int Total,
    IReadOnlyList<Dictionary<string, object?>> Items);
