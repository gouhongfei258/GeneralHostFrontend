using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using GeneralHostFrontend.Core.Settings;

namespace GeneralHostFrontend.Infrastructure.Settings;

public sealed class JsonSettingsStore<TSettings> : ISettingsStore<TSettings>
    where TSettings : class, new()
{
    private readonly string _filePath;
    private readonly ISettingsValidator<TSettings> _validator;
    private readonly object _sync = new();
    private readonly List<Channel<TSettings>> _watchers = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonSettingsStore(string filePath, ISettingsValidator<TSettings> validator)
    {
        _filePath = filePath;
        _validator = validator;
        Current = new TSettings();
    }

    public TSettings Current { get; private set; }

    public async Task<TSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_filePath))
        {
            await SaveAsync(Current, cancellationToken);
            return Current;
        }

        var json = File.ReadAllText(_filePath);
        var settings = JsonSerializer.Deserialize<TSettings>(json, _jsonOptions)
            ?? new TSettings();

        SetCurrent(settings, publish: false);
        return settings;
    }

    public async Task SaveAsync(TSettings settings, CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        cancellationToken.ThrowIfCancellationRequested();

        var validation = _validator.Validate(settings);
        if (!validation.IsValid)
        {
            var message = string.Join(Environment.NewLine, validation.Messages.Select(item => $"{item.Field}: {item.Message}"));
            throw new InvalidOperationException(message);
        }

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, _jsonOptions);
        File.WriteAllText(_filePath, json);
        SetCurrent(settings, publish: true);
    }

    public async IAsyncEnumerable<TSettings> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var channel = Channel.CreateUnbounded<TSettings>();
        TSettings current;

        lock (_sync)
        {
            current = Current;
            _watchers.Add(channel);
        }

        try
        {
            yield return current;

            await foreach (var settings in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return settings;
            }
        }
        finally
        {
            lock (_sync)
            {
                _watchers.Remove(channel);
            }

            channel.Writer.TryComplete();
        }
    }

    private void SetCurrent(TSettings settings, bool publish)
    {
        Channel<TSettings>[] watchers;
        lock (_sync)
        {
            Current = settings;
            watchers = publish ? _watchers.ToArray() : Array.Empty<Channel<TSettings>>();
        }

        foreach (var watcher in watchers)
        {
            watcher.Writer.TryWrite(settings);
        }
    }
}
