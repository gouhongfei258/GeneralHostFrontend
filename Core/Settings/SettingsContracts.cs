namespace GeneralHostFrontend.Core.Settings;

public sealed record ValidationMessage(string Field, string Message);

public sealed record SettingsValidationResult(bool IsValid, IReadOnlyList<ValidationMessage> Messages)
{
    public static SettingsValidationResult Success { get; } = new(true, Array.Empty<ValidationMessage>());

    public static SettingsValidationResult Failed(params ValidationMessage[] messages) => new(false, messages);
}

public interface ISettingsValidator<in TSettings>
{
    SettingsValidationResult Validate(TSettings settings);
}

public interface ISettingsStore<TSettings>
{
    TSettings Current { get; }

    Task<TSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(TSettings settings, CancellationToken cancellationToken = default);

    IAsyncEnumerable<TSettings> WatchAsync(CancellationToken cancellationToken = default);
}
