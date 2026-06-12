namespace GeneralHostFrontend.Core.Database;

public sealed record SortDescriptor(string Field, bool Descending);

public sealed record FilterDescriptor(string Field, string Operator, string? Value);

public sealed record PagedQuery(
    string Table,
    int PageIndex,
    int PageSize,
    IReadOnlyList<FilterDescriptor>? Filters = null,
    IReadOnlyList<SortDescriptor>? Sorts = null);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int PageIndex,
    int PageSize,
    long TotalCount);

public sealed record DatabaseTableInfo(
    string Name,
    IReadOnlyList<DatabaseColumnInfo> Columns);

public sealed record DatabaseColumnInfo(
    string Name,
    string DataType,
    bool IsNullable,
    bool IsPrimaryKey);

public sealed record DatabaseHealth(
    string Provider,
    string FilePath,
    long FileSizeBytes,
    bool IsHealthy,
    string? Message,
    DateTimeOffset CheckedAt);

public interface IDataViewerQueryService
{
    Task<IReadOnlyList<DatabaseTableInfo>> GetTablesAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<IReadOnlyDictionary<string, object?>>> QueryAsync(PagedQuery query, CancellationToken cancellationToken = default);

    Task ExportCsvAsync(PagedQuery query, Stream output, CancellationToken cancellationToken = default);

    Task ExportExcelAsync(PagedQuery query, Stream output, CancellationToken cancellationToken = default);
}

public interface IDatabaseHealthMonitor
{
    Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default);
}
