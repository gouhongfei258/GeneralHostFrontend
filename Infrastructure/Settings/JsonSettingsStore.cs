using System.Text.Json;
using System.Threading.Channels;
using GeneralHostFrontend.Core.Settings;

namespace GeneralHostFrontend.Infrastructure.Settings;

public sealed class JsonSettingsStore<TSettings> : ISettingsStore<TSettings>
    where TSettings : class, new()
{
    private readonly string _filePath;
    private readonly ISettingsValidator<TSettings> _validator;
    private readonly Channel<TSettings> _changes = Channel.CreateUnbounded<TSettings>();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true
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
        Current = JsonSerializer.Deserialize<TSettings>(json, _jsonOptions)
            ?? new TSettings();

        return Current;
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
        Current = settings;
        _changes.Writer.TryWrite(settings);
    }

    public async IAsyncEnumerable<TSettings> WatchAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return Current;

        await foreach (var settings in _changes.Reader.ReadAllAsync(cancellationToken))
        {
            yield return settings;
        }
    }
}
