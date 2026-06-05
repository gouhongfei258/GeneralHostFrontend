using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Logging;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Tags;
using Microsoft.Extensions.Logging;

namespace GeneralHostFrontend.Application;

public sealed class HostRuntime : IAsyncDisposable
{
    private readonly HostSettings _settings;
    private readonly ICommunicationConnectionPool _connectionPool;
    private readonly ITagDataPipeline _pipeline;
    private readonly ILogger<HostRuntime> _logger;
    private readonly List<Task> _workers = new();
    private CancellationTokenSource? _runCts;

    public HostRuntime(
        HostSettings settings,
        ICommunicationConnectionPool connectionPool,
        ITagDataPipeline pipeline,
        ILogger<HostRuntime> logger)
    {
        _settings = settings;
        _connectionPool = connectionPool;
        _pipeline = pipeline;
        _logger = logger;
    }

    public IReadOnlyList<TagDefinition> Tags => _settings.Tags;

    public IReadOnlyCollection<DriverStatus> DeviceStatuses => _connectionPool.GetStatuses();

    public HostRuntimeState State { get; private set; } = HostRuntimeState.Stopped;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_workers.Count > 0)
        {
            return Task.CompletedTask;
        }

        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        State = HostRuntimeState.Running;
        _logger.LogInformation("Host runtime starting.");

        foreach (var tagGroup in _settings.Tags.Where(tag => tag.CanRead).GroupBy(tag => tag.DeviceId))
        {
            var endpoint = _settings.Devices.FirstOrDefault(device => device.DeviceId == tagGroup.Key);
            if (endpoint is null)
            {
                _logger.LogWarning("No endpoint configured for device '{DeviceId}'.", tagGroup.Key);
                continue;
            }

            _workers.Add(Task.Run(() => ScanDeviceAsync(endpoint, tagGroup.ToArray(), _runCts.Token), CancellationToken.None));
            _workers.Add(Task.Run(() => HeartbeatDeviceAsync(endpoint, _runCts.Token), CancellationToken.None));
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_workers.Count == 0)
        {
            State = HostRuntimeState.Stopped;
            return;
        }

        State = HostRuntimeState.Stopping;
        if (_runCts is not null)
        {
            await _runCts.CancelAsync();
        }

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
        }

        _workers.Clear();
        _runCts?.Dispose();
        _runCts = null;
        State = HostRuntimeState.Stopped;
        _logger.LogInformation("Host runtime stopped.");
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }

    private async Task ScanDeviceAsync(CommunicationEndpoint endpoint, IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        var driver = await _connectionPool.GetOrCreateAsync(endpoint, _settings.Communication, cancellationToken);
        var nextScan = tags.ToDictionary(tag => tag.Name, _ => DateTimeOffset.MinValue);

        while (!cancellationToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.Now;
            var dueTags = tags.Where(tag => nextScan[tag.Name] <= now).ToArray();

            if (dueTags.Length > 0)
            {
                try
                {
                    var values = await driver.ReadBatchAsync(dueTags, cancellationToken);
                    foreach (var value in values)
                    {
                        await _pipeline.PublishAsync(value, cancellationToken);
                    }

                    foreach (var tag in dueTags)
                    {
                        nextScan[tag.Name] = now + tag.ScanPeriod;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Scan failed for device {DeviceId}.", endpoint.DeviceId);
                    await Task.Delay(_settings.Communication.ReconnectDelay, cancellationToken);
                    await driver.ConnectAsync(cancellationToken);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private async Task HeartbeatDeviceAsync(CommunicationEndpoint endpoint, CancellationToken cancellationToken)
    {
        var driver = await _connectionPool.GetOrCreateAsync(endpoint, _settings.Communication, cancellationToken);
        using var timer = new PeriodicTimer(_settings.Communication.HeartbeatPeriod);

        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                var ok = await driver.HeartbeatAsync(cancellationToken);
                if (!ok)
                {
                    _logger.LogWarning("Heartbeat failed for device {DeviceId}. Reconnecting.", endpoint.DeviceId);
                    await driver.ConnectAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Heartbeat exception for device {DeviceId}.", endpoint.DeviceId);
                await Task.Delay(_settings.Communication.ReconnectDelay, cancellationToken);
            }
        }
    }
}
