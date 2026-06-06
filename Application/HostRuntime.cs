using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Logging;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Core.Tags;
using Microsoft.Extensions.Logging;

namespace GeneralHostFrontend.Application;

public sealed class HostRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ICommunicationConnectionPool _connectionPool;
    private readonly ITagDataPipeline _pipeline;
    private readonly ILogger<HostRuntime> _logger;
    private readonly List<Task> _workers = new();
    private HostSettings _settings;
    private CancellationTokenSource? _runCts;

    public HostRuntime(
        ISettingsStore<HostSettings> settingsStore,
        ICommunicationConnectionPool connectionPool,
        ITagDataPipeline pipeline,
        ILogger<HostRuntime> logger)
    {
        _settings = settingsStore.Current;
        _connectionPool = connectionPool;
        _pipeline = pipeline;
        _logger = logger;
    }

    public IReadOnlyList<TagDefinition> Tags => _settings.Tags;

    public IReadOnlyCollection<DriverStatus> DeviceStatuses => _connectionPool.GetStatuses();

    public HostRuntimeState State { get; private set; } = HostRuntimeState.Stopped;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (State is HostRuntimeState.Running || _workers.Count > 0)
            {
                return;
            }

            StartUnlocked(cancellationToken);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task StopAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopUnlockedAsync();
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ApplySettingsAsync(HostSettings settings, CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (EqualityComparer<HostSettings>.Default.Equals(_settings, settings))
            {
                return;
            }

            var resumeScanning = State is HostRuntimeState.Running || _workers.Count > 0;
            if (resumeScanning)
            {
                _logger.LogInformation("Host settings changed. Restarting runtime scan with {TagCount} tag(s).", settings.Tags.Count);
                await StopUnlockedAsync();
            }

            _settings = settings;

            if (resumeScanning)
            {
                StartUnlocked(cancellationToken);
            }
            else
            {
                _logger.LogInformation("Host settings changed. Runtime will use {TagCount} tag(s) on next start.", settings.Tags.Count);
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _lifecycleLock.Dispose();
    }

    private void StartUnlocked(CancellationToken cancellationToken)
    {
        _runCts?.Dispose();
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var runToken = _runCts.Token;
        State = HostRuntimeState.Running;
        _logger.LogInformation("Host runtime starting.");

        var settings = _settings;
        foreach (var tagGroup in settings.Tags.Where(tag => tag.CanRead).GroupBy(tag => tag.DeviceId))
        {
            var endpoint = settings.Devices.FirstOrDefault(device => device.DeviceId == tagGroup.Key);
            if (endpoint is null)
            {
                _logger.LogWarning("No endpoint configured for device '{DeviceId}'.", tagGroup.Key);
                continue;
            }

            _workers.Add(Task.Run(() => ScanDeviceAsync(endpoint, settings.Communication, tagGroup.ToArray(), runToken), CancellationToken.None));
            _workers.Add(Task.Run(() => HeartbeatDeviceAsync(endpoint, settings.Communication, runToken), CancellationToken.None));
        }
    }

    private async Task StopUnlockedAsync()
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

    private async Task ScanDeviceAsync(
        CommunicationEndpoint endpoint,
        CommunicationOptions options,
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
    {
        var driver = await _connectionPool.GetOrCreateAsync(endpoint, options, cancellationToken);
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
                    await Task.Delay(options.ReconnectDelay, cancellationToken);
                    await driver.ConnectAsync(cancellationToken);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private async Task HeartbeatDeviceAsync(CommunicationEndpoint endpoint, CommunicationOptions options, CancellationToken cancellationToken)
    {
        var driver = await _connectionPool.GetOrCreateAsync(endpoint, options, cancellationToken);
        using var timer = new PeriodicTimer(options.HeartbeatPeriod);

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
                await Task.Delay(options.ReconnectDelay, cancellationToken);
            }
        }
    }
}
