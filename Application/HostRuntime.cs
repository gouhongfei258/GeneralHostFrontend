using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Logging;
using GeneralHostFrontend.Core.Logic;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Core.Tags;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace GeneralHostFrontend.Application;

public sealed class HostRuntime : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ICommunicationConnectionPool _connectionPool;
    private readonly ITagDataPipeline _pipeline;
    private readonly ILogicGraphStore _logicStore;
    private readonly ILogicCodeGenerator _logicCodeGenerator;
    private readonly ILogicCompiler _logicCompiler;
    private readonly ILogger<HostRuntime> _logger;
    private readonly List<Task> _workers = new();
    private readonly object _tagCacheSync = new();
    private readonly Dictionary<string, TagValue> _latestTags = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _changedTags = new(StringComparer.OrdinalIgnoreCase);
    private HostSettings _settings;
    private CancellationTokenSource? _runCts;
    private ICompiledHostLogic? _compiledLogic;

    public HostRuntime(
        ISettingsStore<HostSettings> settingsStore,
        ICommunicationConnectionPool connectionPool,
        ITagDataPipeline pipeline,
        ILogicGraphStore logicStore,
        ILogicCodeGenerator logicCodeGenerator,
        ILogicCompiler logicCompiler,
        ILogger<HostRuntime> logger)
    {
        _settings = settingsStore.Current;
        _connectionPool = connectionPool;
        _pipeline = pipeline;
        _logicStore = logicStore;
        _logicCodeGenerator = logicCodeGenerator;
        _logicCompiler = logicCompiler;
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

            await StartUnlockedAsync(cancellationToken);
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
                await StartUnlockedAsync(cancellationToken);
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

    private async Task StartUnlockedAsync(CancellationToken cancellationToken)
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

        await StartLogicAsync(settings, runToken);
    }

    private async Task StopUnlockedAsync()
    {
        if (_workers.Count == 0 && _compiledLogic is null)
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
        await UnloadLogicAsync();
        _runCts?.Dispose();
        _runCts = null;
        State = HostRuntimeState.Stopped;
        _logger.LogInformation("Host runtime stopped.");
    }

    private async Task StartLogicAsync(HostSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            var document = await _logicStore.LoadAsync(cancellationToken);
            var generated = _logicCodeGenerator.Generate(document);
            if (!generated.IsValid)
            {
                _logger.LogWarning(
                    "Generated host logic was not started because graph validation failed: {Diagnostics}",
                    string.Join(Environment.NewLine, generated.Diagnostics));
                return;
            }

            var compiled = await _logicCompiler.CompileAsync(generated.Code, cancellationToken);
            if (!compiled.IsValid || compiled.CompiledLogic is null)
            {
                _logger.LogWarning(
                    "Generated host logic was not started because compilation failed: {Diagnostics}",
                    string.Join(Environment.NewLine, compiled.Diagnostics));
                return;
            }

            _compiledLogic = compiled.CompiledLogic;
            _workers.Add(Task.Run(
                () => RunLogicAsync(compiled.CompiledLogic, document, settings, cancellationToken),
                CancellationToken.None));
            _logger.LogInformation("Generated host logic loaded in collectible compile domain {AssemblyName}.", compiled.CompiledLogic.AssemblyName);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Generated host logic failed to start.");
        }
    }

    private async Task UnloadLogicAsync()
    {
        if (_compiledLogic is null)
        {
            return;
        }

        var assemblyName = _compiledLogic.AssemblyName;
        await _compiledLogic.DisposeAsync();
        _compiledLogic = null;
        _logger.LogInformation("Generated host logic compile domain {AssemblyName} unloaded.", assemblyName);
    }

    private async Task RunLogicAsync(
        ICompiledHostLogic compiledLogic,
        LogicGraphDocument document,
        HostSettings settings,
        CancellationToken cancellationToken)
    {
        var executionCount = 0L;

        while (!cancellationToken.IsCancellationRequested)
        {
            var executionId = Interlocked.Increment(ref executionCount);
            var stopwatch = Stopwatch.StartNew();
            var context = new HostLogicContext(
                settings,
                document.PlcStructs ?? Array.Empty<LogicPlcStructDefinition>(),
                _connectionPool,
                _pipeline,
                _logger,
                GetCachedTag,
                UpdateCachedTag,
                TakeChangedTagsSnapshot());

            try
            {
                await compiledLogic.Instance.ExecuteAsync(context, cancellationToken);
                stopwatch.Stop();
                _logger.LogDebug(
                    "Generated host logic execution {ExecutionId} completed in {ElapsedMilliseconds} ms.",
                    executionId,
                    stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                stopwatch.Stop();
                _logger.LogError(
                    ex,
                    "Generated host logic execution {ExecutionId} failed after {ElapsedMilliseconds} ms.",
                    executionId,
                    stopwatch.ElapsedMilliseconds);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
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
                        UpdateCachedTag(value);
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

    private TagValue? GetCachedTag(string tagName)
    {
        lock (_tagCacheSync)
        {
            return _latestTags.TryGetValue(tagName, out var value) ? value : null;
        }
    }

    private void UpdateCachedTag(TagValue value)
    {
        lock (_tagCacheSync)
        {
            if (!_latestTags.TryGetValue(value.TagName, out var current)
                || !Equals(current.Value, value.Value)
                || current.Quality != value.Quality)
            {
                _changedTags.Add(value.TagName);
            }

            _latestTags[value.TagName] = value;
        }
    }

    private IReadOnlySet<string> TakeChangedTagsSnapshot()
    {
        lock (_tagCacheSync)
        {
            if (_changedTags.Count == 0)
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }

            var snapshot = new HashSet<string>(_changedTags, StringComparer.OrdinalIgnoreCase);
            _changedTags.Clear();
            return snapshot;
        }
    }

    private sealed class HostLogicContext : IHostLogicContext
    {
        private readonly HostSettings _settings;
        private readonly IReadOnlyDictionary<string, LogicPlcStructDefinition> _schemas;
        private readonly ICommunicationConnectionPool _connectionPool;
        private readonly ITagDataPipeline _pipeline;
        private readonly ILogger _logger;
        private readonly Func<string, TagValue?> _getCachedTag;
        private readonly Action<TagValue> _updateCachedTag;
        private readonly IReadOnlySet<string> _changedTags;

        public HostLogicContext(
            HostSettings settings,
            IReadOnlyList<LogicPlcStructDefinition> schemas,
            ICommunicationConnectionPool connectionPool,
            ITagDataPipeline pipeline,
            ILogger logger,
            Func<string, TagValue?> getCachedTag,
            Action<TagValue> updateCachedTag,
            IReadOnlySet<string> changedTags)
        {
            _settings = settings;
            _schemas = schemas.ToDictionary(schema => schema.Name, StringComparer.OrdinalIgnoreCase);
            _connectionPool = connectionPool;
            _pipeline = pipeline;
            _logger = logger;
            _getCachedTag = getCachedTag;
            _updateCachedTag = updateCachedTag;
            _changedTags = changedTags;
        }

        public async Task<object?> ReadTagAsync(string tagName, CancellationToken cancellationToken)
        {
            var value = await ReadTagValueAsync(tagName, LogicTagReadMode.Cached, cancellationToken);
            return value?.Value;
        }

        public async Task<TagValue?> ReadTagValueAsync(string tagName, LogicTagReadMode mode, CancellationToken cancellationToken)
        {
            var tag = FindTag(tagName);
            if (tag is null)
            {
                _logger.LogWarning("Generated host logic attempted to read unknown tag {TagName}.", tagName);
                return null;
            }

            if (mode is LogicTagReadMode.Cached)
            {
                var cached = _getCachedTag(tag.Name);
                _logger.LogDebug("Generated host logic read cached tag {TagName}: {Value}.", tag.Name, cached?.DisplayValue ?? "-");
                return cached;
            }

            var endpoint = FindEndpoint(tag.DeviceId);
            if (endpoint is null)
            {
                _logger.LogWarning("Generated host logic attempted to read tag {TagName} on unknown device {DeviceId}.", tag.Name, tag.DeviceId);
                return null;
            }

            var driver = await _connectionPool.GetOrCreateAsync(endpoint, _settings.Communication, cancellationToken);
            var value = await driver.ReadAsync(tag, cancellationToken);
            _updateCachedTag(value);
            await _pipeline.PublishAsync(value, cancellationToken);
            _logger.LogInformation("Generated host logic direct-read tag {TagName}: {Value}.", value.TagName, value.DisplayValue);
            return value;
        }

        public async Task<IReadOnlyDictionary<string, object?>> ReadPlcStructAsync(LogicPlcStructReadRequest request, CancellationToken cancellationToken)
        {
            if (!_schemas.TryGetValue(request.SchemaName, out var schema))
            {
                _logger.LogWarning("Generated host logic attempted to read unknown PLC struct schema {SchemaName}.", request.SchemaName);
                return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            }

            var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var field in schema.Fields)
            {
                var tag = FindTagByDeviceAndAddress(request.DeviceId, field.Address)
                    ?? new TagDefinition(
                        $"{request.SchemaName}.{field.Name}",
                        request.DeviceId,
                        field.Address,
                        field.DataType,
                        TagAccessMode.ReadOnly,
                        TimeSpan.FromSeconds(1),
                        field.EngineeringUnit);

                TagValue? value = request.Mode is LogicTagReadMode.Cached
                    ? _getCachedTag(tag.Name)
                    : null;

                if (value is null)
                {
                    var endpoint = FindEndpoint(request.DeviceId);
                    if (endpoint is null)
                    {
                        _logger.LogWarning(
                            "Generated host logic attempted to read PLC struct {SchemaName} on unknown device {DeviceId}.",
                            request.SchemaName,
                            request.DeviceId);
                        break;
                    }

                    var driver = await _connectionPool.GetOrCreateAsync(endpoint, _settings.Communication, cancellationToken);
                    value = await driver.ReadAsync(tag, cancellationToken);
                    _updateCachedTag(value);
                    await _pipeline.PublishAsync(value, cancellationToken);
                }

                values[field.Name] = value.Value;
            }

            _logger.LogInformation(
                "Generated host logic read PLC struct {SchemaName} from {DeviceId} with {FieldCount} field(s).",
                request.SchemaName,
                request.DeviceId,
                values.Count);
            return values;
        }

        public async Task WriteTagAsync(string tagName, object? value, CancellationToken cancellationToken)
        {
            var tag = FindTag(tagName);
            if (tag is null)
            {
                _logger.LogWarning("Generated host logic attempted to write unknown tag {TagName}.", tagName);
                return;
            }

            if (!tag.CanWrite)
            {
                _logger.LogWarning("Generated host logic attempted to write read-only tag {TagName}.", tag.Name);
                return;
            }

            var endpoint = FindEndpoint(tag.DeviceId);
            if (endpoint is null)
            {
                _logger.LogWarning("Generated host logic attempted to write tag {TagName} on unknown device {DeviceId}.", tag.Name, tag.DeviceId);
                return;
            }

            var driver = await _connectionPool.GetOrCreateAsync(endpoint, _settings.Communication, cancellationToken);
            await driver.WriteAsync(new WriteTagCommand(tag, value), cancellationToken);

            var sample = new TagValue(tag.Name, value, TagQuality.Good, DateTimeOffset.Now, tag.EngineeringUnit, tag.LowerLimit, tag.UpperLimit);
            _updateCachedTag(sample);
            await _pipeline.PublishAsync(sample, cancellationToken);
            _logger.LogInformation("Generated host logic wrote tag {TagName}: {Value}.", tag.Name, sample.DisplayValue);
        }

        public bool HasTagChanged(string tagName)
            => _changedTags.Contains(tagName);

        public void Log(string message)
            => _logger.LogInformation("Generated host logic: {Message}", message);

        private TagDefinition? FindTag(string tagName)
            => _settings.Tags.FirstOrDefault(tag => string.Equals(tag.Name, tagName, StringComparison.OrdinalIgnoreCase));

        private TagDefinition? FindTagByDeviceAndAddress(string deviceId, string address)
            => _settings.Tags.FirstOrDefault(tag =>
                string.Equals(tag.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(tag.Address, address, StringComparison.OrdinalIgnoreCase));

        private CommunicationEndpoint? FindEndpoint(string deviceId)
            => _settings.Devices.FirstOrDefault(device => string.Equals(device.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
    }
}
