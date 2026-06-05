using System.Collections.Concurrent;
using System.Threading.Channels;
using GeneralHostFrontend.Core.Logging;

namespace GeneralHostFrontend.Infrastructure.Logging;

public sealed class InMemoryLiveLogService : ILiveLogService
{
    private readonly ConcurrentQueue<LogEntry> _entries = new();
    private readonly Channel<LogEntry> _channel = Channel.CreateUnbounded<LogEntry>();
    private readonly int _capacity;

    public InMemoryLiveLogService(int capacity = 20_000)
    {
        _capacity = capacity;
    }

    public void Write(LogEntry entry)
    {
        _entries.Enqueue(entry);
        while (_entries.Count > _capacity && _entries.TryDequeue(out _))
        {
        }

        _channel.Writer.TryWrite(entry);
    }

    public IReadOnlyList<LogEntry> Snapshot(LogFilter filter, int maxCount)
    {
        return _entries
            .Where(entry => Matches(entry, filter))
            .TakeLast(maxCount)
            .ToArray();
    }

    public async IAsyncEnumerable<LogEntry> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var entry in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return entry;
        }
    }

    private static bool Matches(LogEntry entry, LogFilter filter)
    {
        if (filter.MinimumLevel.HasValue && entry.Level < filter.MinimumLevel)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(filter.Keyword)
            || entry.Message.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase)
            || entry.Source.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase);
    }
}
