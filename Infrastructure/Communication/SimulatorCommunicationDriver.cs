using System.Collections.Concurrent;
using System.Threading.Channels;
using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class SimulatorCommunicationDriver : ICommunicationDriver
{
    private readonly CommunicationOptions _options;
    private readonly Channel<DriverStatus> _statusChannel = Channel.CreateUnbounded<DriverStatus>();
    private readonly ConcurrentDictionary<string, object?> _values = new();
    private readonly SemaphoreSlim _operationGate;
    private readonly object _randomLock = new();
    private readonly Random _random = new();
    private DriverStatus _status;
    private bool _disposed;

    public SimulatorCommunicationDriver(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        Endpoint = endpoint;
        _options = options;
        _operationGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentOperations));
        _status = new DriverStatus(endpoint.DeviceId, DriverState.Created, null, DateTimeOffset.Now, null);
    }

    private CommunicationEndpoint Endpoint { get; }

    public string DeviceId => Endpoint.DeviceId;

    public DriverKind Kind => Endpoint.Kind;

    public DriverStatus Status => _status;

    public async IAsyncEnumerable<DriverStatus> WatchStatusAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return _status;

        await foreach (var status in _statusChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return status;
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetStatus(DriverState.Connecting, "Connecting simulator device.");
        await Task.Delay(TimeSpan.FromMilliseconds(120), cancellationToken);
        SetStatus(DriverState.Connected, "Simulator connected.");
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetStatus(DriverState.Disconnected, "Disconnected.");
        return Task.CompletedTask;
    }

    public async Task<TagValue> ReadAsync(TagDefinition tag, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!tag.CanRead)
        {
            return ToValue(tag, null, TagQuality.AccessDenied);
        }

        await RunLimitedAsync(cancellationToken);

        if (_status.State is not DriverState.Connected)
        {
            return ToValue(tag, null, TagQuality.Disconnected);
        }

        var value = _values.AddOrUpdate(tag.Name, _ => CreateValue(tag), (_, current) => MutateValue(tag, current));
        return ToValue(tag, ApplyScaling(tag, value), TagQuality.Good);
    }

    public async Task<IReadOnlyList<TagValue>> ReadBatchAsync(IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken = default)
    {
        var values = new List<TagValue>(tags.Count);
        foreach (var tag in tags)
        {
            values.Add(await ReadAsync(tag, cancellationToken));
        }

        return values;
    }

    public async Task WriteAsync(WriteTagCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!command.Tag.CanWrite)
        {
            throw new InvalidOperationException($"Tag '{command.Tag.Name}' is read-only.");
        }

        await RunLimitedAsync(cancellationToken);
        _values[command.Tag.Name] = command.Value;
    }

    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);

        if (_status.State is DriverState.Connected)
        {
            _status = _status with { LastHeartbeatAt = DateTimeOffset.Now };
            _statusChannel.Writer.TryWrite(_status);
            return true;
        }

        return false;
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _operationGate.Dispose();
        SetStatus(DriverState.Disposed, "Disposed.");
        _statusChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private async Task RunLimitedAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var delay = _options.MaxOperationsPerSecond <= 0
                ? 1
                : Math.Max(1, 1000 / _options.MaxOperationsPerSecond);
            await Task.Delay(delay, cancellationToken);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private object? CreateValue(TagDefinition tag)
    {
        return tag.DataType switch
        {
            TagDataType.Boolean => NextDouble() > 0.25,
            TagDataType.String => "SIM",
            TagDataType.Bytes => Array.Empty<byte>(),
            _ => NextDouble() * ((tag.UpperLimit ?? 100) - (tag.LowerLimit ?? 0)) + (tag.LowerLimit ?? 0)
        };
    }

    private object? MutateValue(TagDefinition tag, object? current)
    {
        if (tag.DataType is TagDataType.Boolean)
        {
            return NextDouble() > 0.02 ? current : !(current as bool? ?? false);
        }

        if (current is not IConvertible)
        {
            return CreateValue(tag);
        }

        var lower = tag.LowerLimit ?? 0;
        var upper = tag.UpperLimit ?? 100;
        var delta = (NextDouble() - 0.5) * Math.Max(0.1, (upper - lower) / 50);
        var next = Math.Clamp(Convert.ToDouble(current), lower, upper) + delta;
        return Math.Clamp(next, lower, upper);
    }

    private object? ApplyScaling(TagDefinition tag, object? value)
    {
        if (tag.Scaling is null || value is not IConvertible)
        {
            return value;
        }

        return tag.Scaling.Convert(Convert.ToDouble(value));
    }

    private TagValue ToValue(TagDefinition tag, object? value, TagQuality quality)
        => new(tag.Name, value, quality, DateTimeOffset.Now, tag.EngineeringUnit, tag.LowerLimit, tag.UpperLimit);

    private double NextDouble()
    {
        lock (_randomLock)
        {
            return _random.NextDouble();
        }
    }

    private void SetStatus(DriverState state, string? message)
    {
        _status = new DriverStatus(DeviceId, state, message, DateTimeOffset.Now, _status.LastHeartbeatAt);
        _statusChannel.Writer.TryWrite(_status);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
