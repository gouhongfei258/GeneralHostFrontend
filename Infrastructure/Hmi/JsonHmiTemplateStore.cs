using System.Text.Json;
using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.Infrastructure.Hmi;

public sealed class JsonHmiTemplateStore : IHmiTemplateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _directoryPath;

    public JsonHmiTemplateStore(string directoryPath)
    {
        _directoryPath = directoryPath;
    }

    public Task<IReadOnlyList<HmiWidgetTemplateDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var descriptors = Directory
            .EnumerateFiles(_directoryPath, "*.json")
            .Select(path =>
            {
                try
                {
                    using var stream = File.OpenRead(path);
                    var document = JsonSerializer.Deserialize<HmiWidgetTemplateDocument>(stream, SerializerOptions);
                    return document is null
                        ? null
                        : new HmiWidgetTemplateDescriptor(document.Id, document.Name, document.Widgets.Count);
                }
                catch (JsonException)
                {
                    return null;
                }
                catch (IOException)
                {
                    return null;
                }
            })
            .Where(descriptor => descriptor is not null)
            .Select(descriptor => descriptor!)
            .OrderBy(descriptor => descriptor.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<HmiWidgetTemplateDescriptor>>(descriptors);
    }

    public async Task<HmiWidgetTemplateDocument> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var path = GetTemplatePath(id);
        if (!File.Exists(path))
        {
            return new HmiWidgetTemplateDocument(SanitizeName(id), id, Array.Empty<HmiWidgetDefinition>());
        }

        await using var stream = File.OpenRead(path);
        var document = await JsonSerializer.DeserializeAsync<HmiWidgetTemplateDocument>(
            stream,
            SerializerOptions,
            cancellationToken);

        return Normalize(document, SanitizeName(id));
    }

    public async Task SaveAsync(HmiWidgetTemplateDocument template, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_directoryPath);

        var normalized = Normalize(template, SanitizeName(template.Id));
        var path = GetTemplatePath(normalized.Id);
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, normalized, SerializerOptions, cancellationToken);
    }

    private string GetTemplatePath(string id)
        => Path.Combine(_directoryPath, $"{SanitizeName(id)}.json");

    private static HmiWidgetTemplateDocument Normalize(HmiWidgetTemplateDocument? template, string fallbackId)
    {
        if (template is null)
        {
            return new HmiWidgetTemplateDocument(fallbackId, fallbackId, Array.Empty<HmiWidgetDefinition>());
        }

        var id = SanitizeName(string.IsNullOrWhiteSpace(template.Id) ? fallbackId : template.Id);
        var name = string.IsNullOrWhiteSpace(template.Name) ? id : template.Name.Trim();
        return template with
        {
            Id = id,
            Name = name,
            Widgets = template.Widgets ?? Array.Empty<HmiWidgetDefinition>()
        };
    }

    private static string SanitizeName(string name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "template" : name.Trim();
        var invalidCharacters = Path.GetInvalidFileNameChars();
        return string.Concat(trimmed.Select(character => invalidCharacters.Contains(character) ? '-' : character));
    }
}
