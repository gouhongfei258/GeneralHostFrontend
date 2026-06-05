namespace GeneralHostFrontend.Core.Logging;

public enum HostLogLevel
{
    Trace,
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public sealed record LogEntry(
    DateTimeOffset Timestamp,
    HostLogLevel Level,
    string Source,
    string Message,
    Exception? Exception = null);

public sealed record LogFilter(
    HostLogLevel? MinimumLevel = null,
    string? Keyword = null);

public interface ILiveLogService
{
    void Write(LogEntry entry);

    IReadOnlyList<LogEntry> Snapshot(LogFilter filter, int maxCount);

    IAsyncEnumerable<LogEntry> WatchAsync(CancellationToken cancellationToken = default);
}
