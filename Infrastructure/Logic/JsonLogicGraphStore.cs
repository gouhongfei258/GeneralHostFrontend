using System.Text.Json;
using System.Text.Json.Serialization;
using GeneralHostFrontend.Core.Logic;

namespace GeneralHostFrontend.Infrastructure.Logic;

public sealed class JsonLogicGraphStore : ILogicGraphStore
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonLogicGraphStore(string filePath)
    {
        _filePath = filePath;
    }

    public async Task<LogicGraphDocument> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(_filePath))
        {
            var document = CreateDefaultDocument();
            await SaveAsync(document, cancellationToken);
            return document;
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<LogicGraphDocument>(stream, _jsonOptions, cancellationToken)
            ?? CreateDefaultDocument();
    }

    public async Task SaveAsync(LogicGraphDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, document, _jsonOptions, cancellationToken);
    }

    private static LogicGraphDocument CreateDefaultDocument()
    {
        var timer = LogicNodeTemplate.Create(LogicNodeKind.Timer, "timer-1", 60, 80);
        var readTag = LogicNodeTemplate.Create(LogicNodeKind.ReadTagCached, "read-1", 300, 80) with
        {
            Properties = new Dictionary<string, string>
            {
                ["tagName"] = "Line.Speed"
            }
        };
        var compare = LogicNodeTemplate.Create(LogicNodeKind.Compare, "compare-1", 540, 80) with
        {
            Properties = new Dictionary<string, string>
            {
                ["operator"] = ">",
                ["value"] = "10"
            }
        };
        var pulseBit = LogicNodeTemplate.Create(LogicNodeKind.PulseBit, "pulse-1", 780, 80) with
        {
            Properties = new Dictionary<string, string>
            {
                ["tagName"] = "Station.Ready",
                ["durationMs"] = "200"
            }
        };
        var tagChanged = LogicNodeTemplate.Create(LogicNodeKind.OnTagChanged, "change-1", 60, 280) with
        {
            Properties = new Dictionary<string, string>
            {
                ["tagName"] = "Line.Temperature"
            }
        };
        var readStruct = LogicNodeTemplate.Create(LogicNodeKind.ReadPlcStruct, "struct-1", 300, 280) with
        {
            Properties = new Dictionary<string, string>
            {
                ["deviceId"] = "SIM-PLC-01",
                ["schemaName"] = "RecipeHeader",
                ["baseAddress"] = "D200",
                ["mode"] = LogicTagReadMode.Cached.ToString()
            }
        };
        var logStruct = LogicNodeTemplate.Create(LogicNodeKind.Log, "log-1", 560, 280) with
        {
            Properties = new Dictionary<string, string>
            {
                ["message"] = "Recipe header changed: {value}"
            }
        };

        return new LogicGraphDocument(
            "Main Logic",
            new[] { timer, readTag, compare, pulseBit, tagChanged, readStruct, logStruct },
            new[]
            {
                Connect("c1", timer, "then", readTag, "in"),
                Connect("c2", readTag, "then", compare, "in"),
                Connect("c3", compare, "true", pulseBit, "in"),
                Connect("c4", tagChanged, "then", readStruct, "in"),
                Connect("c5", readStruct, "then", logStruct, "in")
            },
            LogicNodeTemplate.CreateDefaultPlcStructs());
    }

    private static LogicConnectionDefinition Connect(
        string id,
        LogicNodeDefinition source,
        string sourceConnector,
        LogicNodeDefinition target,
        string targetConnector)
        => new(id, source.Id, sourceConnector, target.Id, targetConnector);
}
