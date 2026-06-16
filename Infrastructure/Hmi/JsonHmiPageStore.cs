using System.Text.Json;
using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.Infrastructure.Hmi;

public sealed class JsonHmiPageStore : IHmiPageStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _directoryPath;

    public JsonHmiPageStore(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var names = Directory
            .EnumerateFiles(_directoryPath, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (names.Length == 0)
        {
            IReadOnlyList<string> defaultNames = new[] { HmiPageDefaults.MainPageId };
            return Task.FromResult(defaultNames);
        }

        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    public async Task<HmiPageDocument> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var pageId = SanitizeName(id);
        var path = GetPagePath(pageId);
        if (!File.Exists(path))
        {
            var defaultDocument = HmiPageDocument.CreateDefault(pageId, ToDisplayName(pageId));
            await SaveAsync(defaultDocument, cancellationToken);
            return defaultDocument;
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<HmiPageDocument>(
            stream,
            SerializerOptions,
            cancellationToken);

        return NormalizeDocument(document, pageId);
    }

    public async Task SaveAsync(HmiPageDocument document, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var normalized = NormalizeDocument(document, SanitizeName(document.Id));
        var path = GetPagePath(normalized.Id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var path = GetPagePath(id);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetPagePath(string id)
        => Path.Combine(_directoryPath, $"{SanitizeName(id)}.json");

    private static string SanitizeName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? HmiPageDefaults.MainPageId : name.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(trimmed.Select(character => invalidCharacters.Contains(character) ? '-' : character));
    }

    private static string ToDisplayName(string id)
        => string.Equals(id, HmiPageDefaults.MainPageId, StringComparison.OrdinalIgnoreCase)
            ? HmiPageDefaults.MainPageName
            : id;

    private static HmiPageDocument NormalizeDocument(HmiPageDocument? document, string fallbackId)
    {
        if (document is null)
        {
            return HmiPageDocument.CreateDefault(fallbackId, ToDisplayName(fallbackId));
        }

        var id = SanitizeName(string.IsNullOrWhiteSpace(document.Id) ? fallbackId : document.Id);
        var name = string.IsNullOrWhiteSpace(document.Name) ? ToDisplayName(id) : document.Name.Trim();
        var grid = document.Grid ?? HmiGridDefinition.Default;
        var widgets = (document.Widgets ?? Array.Empty<HmiWidgetDefinition>())
            .Select(NormalizeWidget)
            .ToArray();

        return document with
        {
            SchemaVersion = Math.Max(document.SchemaVersion, HmiPageDocument.CurrentSchemaVersion),
            Id = id,
            Name = name,
            Width = document.Width <= 0 ? HmiPageDefaults.Width : document.Width,
            Height = document.Height <= 0 ? HmiPageDefaults.Height : document.Height,
            Background = string.IsNullOrWhiteSpace(document.Background) ? HmiPageDefaults.Background : document.Background,
            Grid = grid.Size <= 0 ? grid with { Size = HmiGridDefinition.Default.Size } : grid,
            Widgets = widgets
        };
    }

    private static HmiWidgetDefinition NormalizeWidget(HmiWidgetDefinition widget)
        => widget with
        {
            Bindings = widget.Bindings ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Properties = widget.Properties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Events = widget.Events ?? Array.Empty<HmiEventDefinition>()
        };
}
