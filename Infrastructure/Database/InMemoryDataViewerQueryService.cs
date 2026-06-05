using System.Globalization;
using System.Text;
using GeneralHostFrontend.Core.Database;

namespace GeneralHostFrontend.Infrastructure.Database;

public sealed class InMemoryDataViewerQueryService : IDataViewerQueryService, IDatabaseHealthMonitor
{
    private readonly IReadOnlyList<IReadOnlyDictionary<string, object?>> _alarmHistory;

    public InMemoryDataViewerQueryService()
    {
        _alarmHistory = Enumerable.Range(1, 120)
            .Select(index => new Dictionary<string, object?>
            {
                ["Id"] = index,
                ["Time"] = DateTimeOffset.Now.AddMinutes(-index * 3),
                ["Level"] = index % 9 == 0 ? "Error" : index % 4 == 0 ? "Warning" : "Info",
                ["Code"] = $"ALM-{1000 + index}",
                ["Message"] = index % 2 == 0 ? "Temperature trend exceeded warning band." : "Station cycle time fluctuated.",
                ["Confirmed"] = index % 5 == 0
            })
            .Cast<IReadOnlyDictionary<string, object?>>()
            .ToArray();
    }

    public Task<IReadOnlyList<DatabaseTableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DatabaseTableInfo> tables = new[]
        {
            new DatabaseTableInfo("AlarmHistory", new[]
            {
                new DatabaseColumnInfo("Id", "integer", false, true),
                new DatabaseColumnInfo("Time", "text", false, false),
                new DatabaseColumnInfo("Level", "text", false, false),
                new DatabaseColumnInfo("Code", "text", false, false),
                new DatabaseColumnInfo("Message", "text", false, false),
                new DatabaseColumnInfo("Confirmed", "integer", false, false)
            })
        };

        return Task.FromResult(tables);
    }

    public Task<PagedResult<IReadOnlyDictionary<string, object?>>> QueryAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var source = ResolveTable(query.Table);
        source = ApplyFilters(source, query.Filters);
        source = ApplySorts(source, query.Sorts);

        var total = source.LongCount();
        var items = source
            .Skip(Math.Max(0, query.PageIndex) * Math.Max(1, query.PageSize))
            .Take(Math.Max(1, query.PageSize))
            .ToArray();

        return Task.FromResult(new PagedResult<IReadOnlyDictionary<string, object?>>(items, query.PageIndex, query.PageSize, total));
    }

    public async Task ExportCsvAsync(PagedQuery query, Stream output, CancellationToken cancellationToken = default)
    {
        var result = await QueryAsync(query with { PageIndex = 0, PageSize = int.MaxValue / 2 }, cancellationToken);
        if (result.Items.Count == 0)
        {
            return;
        }

        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        var columns = result.Items[0].Keys.ToArray();
        await writer.WriteLineAsync(string.Join(",", columns.Select(EscapeCsv)));

        foreach (var row in result.Items)
        {
            var line = string.Join(",", columns.Select(column => EscapeCsv(Format(row.GetValueOrDefault(column)))));
            await writer.WriteLineAsync(line);
        }
    }

    public Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        var health = new DatabaseHealth(
            "InMemory",
            "N/A",
            0,
            true,
            "Embedded data viewer adapter is running in memory mode.",
            DateTimeOffset.Now);

        return Task.FromResult(health);
    }

    private IEnumerable<IReadOnlyDictionary<string, object?>> ResolveTable(string table)
        => table.Equals("AlarmHistory", StringComparison.OrdinalIgnoreCase)
            ? _alarmHistory
            : Array.Empty<IReadOnlyDictionary<string, object?>>();

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ApplyFilters(
        IEnumerable<IReadOnlyDictionary<string, object?>> source,
        IReadOnlyList<FilterDescriptor>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return source;
        }

        foreach (var filter in filters.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
        {
            source = source.Where(row =>
                row.TryGetValue(filter.Field, out var value)
                && Format(value).Contains(filter.Value!, StringComparison.OrdinalIgnoreCase));
        }

        return source;
    }

    private static IEnumerable<IReadOnlyDictionary<string, object?>> ApplySorts(
        IEnumerable<IReadOnlyDictionary<string, object?>> source,
        IReadOnlyList<SortDescriptor>? sorts)
    {
        var firstSort = sorts?.FirstOrDefault();
        if (firstSort is null)
        {
            return source;
        }

        return firstSort.Descending
            ? source.OrderByDescending(row => row.GetValueOrDefault(firstSort.Field))
            : source.OrderBy(row => row.GetValueOrDefault(firstSort.Field));
    }

    private static string Format(object? value)
    {
        return value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeCsv(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }
}
