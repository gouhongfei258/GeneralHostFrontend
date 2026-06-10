using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Core.Logic;

public enum LogicNodeKind
{
    Timer,
    OnTagChanged,
    ReadTag,
    ReadTagCached,
    ReadTagDirect,
    ReadPlcStruct,
    Compare,
    Switch,
    WriteTag,
    PulseBit,
    Delay,
    Expression,
    Log
}

public enum LogicConnectorKind
{
    Flow,
    Value
}

public enum LogicValueType
{
    Any,
    Boolean,
    Number,
    String,
    TagValue,
    Struct,
    Object,
    Error
}

public enum LogicConnectorDirection
{
    Input,
    Output
}

public enum LogicTagReadMode
{
    Cached,
    Direct
}

public sealed record LogicConnectorDefinition(
    string Id,
    string Name,
    LogicConnectorKind Kind,
    LogicConnectorDirection Direction,
    LogicValueType ValueType = LogicValueType.Any);

public sealed record LogicPlcStructFieldDefinition(
    string Name,
    string Address,
    TagDataType DataType,
    int Length = 0,
    string? EngineeringUnit = null);

public sealed record LogicPlcStructDefinition(
    string Name,
    IReadOnlyList<LogicPlcStructFieldDefinition> Fields);

public sealed record LogicPlcStructReadRequest(
    string DeviceId,
    string SchemaName,
    string BaseAddress,
    LogicTagReadMode Mode);

public sealed record LogicNodeDefinition(
    string Id,
    LogicNodeKind Kind,
    string Title,
    double X,
    double Y,
    IReadOnlyDictionary<string, string> Properties,
    IReadOnlyList<LogicConnectorDefinition> Inputs,
    IReadOnlyList<LogicConnectorDefinition> Outputs);

public sealed record LogicConnectionDefinition(
    string Id,
    string SourceNodeId,
    string SourceConnectorId,
    string TargetNodeId,
    string TargetConnectorId);

public sealed record LogicGraphDocument(
    string Name,
    IReadOnlyList<LogicNodeDefinition> Nodes,
    IReadOnlyList<LogicConnectionDefinition> Connections,
    IReadOnlyList<LogicPlcStructDefinition>? PlcStructs = null);

public sealed record LogicBuildResult(
    string Code,
    bool IsValid,
    IReadOnlyList<string> Diagnostics,
    ICompiledHostLogic? CompiledLogic = null);

public interface ILogicGraphStore
{
    Task<LogicGraphDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LogicGraphDocument document, CancellationToken cancellationToken = default);
}

public interface ILogicCodeGenerator
{
    LogicBuildResult Generate(LogicGraphDocument document);
}

public interface IGeneratedHostLogic
{
    Task ExecuteAsync(IHostLogicContext context, CancellationToken cancellationToken);
}

public interface ICompiledHostLogic : IAsyncDisposable
{
    string AssemblyName { get; }

    IGeneratedHostLogic Instance { get; }
}

public interface IHostLogicContext
{
    Task<object?> ReadTagAsync(string tagName, CancellationToken cancellationToken);

    Task<TagValue?> ReadTagValueAsync(string tagName, LogicTagReadMode mode, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, object?>> ReadPlcStructAsync(LogicPlcStructReadRequest request, CancellationToken cancellationToken);

    Task WriteTagAsync(string tagName, object? value, CancellationToken cancellationToken);

    bool HasTagChanged(string tagName);

    void Log(string message);
}

public interface ILogicCompiler
{
    Task<LogicBuildResult> CompileAsync(string code, CancellationToken cancellationToken = default);
}

public static class LogicNodeTemplate
{
    public const int DefaultSwitchCaseCount = 3;

    public static IReadOnlyList<LogicPlcStructDefinition> CreateDefaultPlcStructs()
        => new[]
        {
            new LogicPlcStructDefinition(
                "RecipeHeader",
                new[]
                {
                    new LogicPlcStructFieldDefinition("RecipeId", "D200", TagDataType.Int16),
                    new LogicPlcStructFieldDefinition("Length", "D202", TagDataType.Int16),
                    new LogicPlcStructFieldDefinition("Checksum", "D204", TagDataType.UInt32),
                    new LogicPlcStructFieldDefinition("Version", "D208", TagDataType.Int16)
                })
        };

    public static LogicNodeDefinition Create(LogicNodeKind kind, string id, double x, double y)
    {
        return kind switch
        {
            LogicNodeKind.Timer => new LogicNodeDefinition(
                id,
                kind,
                "Timer",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["intervalMs"] = "1000"
                },
                Array.Empty<LogicConnectorDefinition>(),
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output)
                }),
            LogicNodeKind.OnTagChanged => new LogicNodeDefinition(
                id,
                kind,
                "On Tag Changed",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["tagName"] = string.Empty
                },
                Array.Empty<LogicConnectorDefinition>(),
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Output, LogicValueType.TagValue)
                }),
            LogicNodeKind.ReadTag => new LogicNodeDefinition(
                id,
                kind,
                "Read Tag",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["tagName"] = string.Empty,
                    ["mode"] = LogicTagReadMode.Cached.ToString()
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Output, LogicValueType.TagValue)
                }),
            LogicNodeKind.ReadTagCached => new LogicNodeDefinition(
                id,
                kind,
                "Read Tag Cached",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["tagName"] = string.Empty
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Output, LogicValueType.TagValue)
                }),
            LogicNodeKind.ReadTagDirect => new LogicNodeDefinition(
                id,
                kind,
                "Read Tag Direct",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["tagName"] = string.Empty
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Output, LogicValueType.TagValue)
                }),
            LogicNodeKind.ReadPlcStruct => new LogicNodeDefinition(
                id,
                kind,
                "Read PLC Struct",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["deviceId"] = string.Empty,
                    ["schemaName"] = "RecipeHeader",
                    ["baseAddress"] = string.Empty,
                    ["mode"] = LogicTagReadMode.Cached.ToString()
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("struct", "Struct", LogicConnectorKind.Value, LogicConnectorDirection.Output, LogicValueType.Struct)
                }),
            LogicNodeKind.Compare => new LogicNodeDefinition(
                id,
                kind,
                "Compare",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["operator"] = ">",
                    ["value"] = "0"
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Input, LogicValueType.Any)
                },
                new[]
                {
                    new LogicConnectorDefinition("true", "True", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("false", "False", LogicConnectorKind.Flow, LogicConnectorDirection.Output)
                }),
            LogicNodeKind.Switch => new LogicNodeDefinition(
                id,
                kind,
                "Switch",
                x,
                y,
                CreateSwitchProperties(),
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Input, LogicValueType.Any)
                },
                CreateSwitchOutputs(CreateSwitchProperties())),
            LogicNodeKind.WriteTag => new LogicNodeDefinition(
                id,
                kind,
                "Write Tag",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["tagName"] = string.Empty,
                    ["value"] = "true"
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Input, LogicValueType.Any)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output)
                }),
            LogicNodeKind.PulseBit => new LogicNodeDefinition(
                id,
                kind,
                "Pulse Bit",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["tagName"] = string.Empty,
                    ["durationMs"] = "200"
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output)
                }),
            LogicNodeKind.Delay => new LogicNodeDefinition(
                id,
                kind,
                "Delay",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["durationMs"] = "100"
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output)
                }),
            LogicNodeKind.Expression => new LogicNodeDefinition(
                id,
                kind,
                "Expression",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["expression"] = "currentValue"
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Input, LogicValueType.Any)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output),
                    new LogicConnectorDefinition("result", "Result", LogicConnectorKind.Value, LogicConnectorDirection.Output, LogicValueType.Any)
                }),
            LogicNodeKind.Log => new LogicNodeDefinition(
                id,
                kind,
                "Log",
                x,
                y,
                new Dictionary<string, string>
                {
                    ["message"] = "Logic event"
                },
                new[]
                {
                    new LogicConnectorDefinition("in", "In", LogicConnectorKind.Flow, LogicConnectorDirection.Input),
                    new LogicConnectorDefinition("value", "Value", LogicConnectorKind.Value, LogicConnectorDirection.Input, LogicValueType.Any)
                },
                new[]
                {
                    new LogicConnectorDefinition("then", "Then", LogicConnectorKind.Flow, LogicConnectorDirection.Output)
                }),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported node kind.")
        };
    }

    private static IReadOnlyDictionary<string, string> CreateSwitchProperties()
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["case1"] = "string:Auto",
            ["case2"] = "string:Manual"
        };

        for (var index = 3; index <= DefaultSwitchCaseCount; index++)
        {
            properties[$"case{index}"] = string.Empty;
        }

        return properties;
    }

    public static IReadOnlyList<LogicConnectorDefinition> CreateSwitchOutputs(IReadOnlyDictionary<string, string> properties)
    {
        var caseIndexes = properties.Keys
            .Select(ParseSwitchCaseIndex)
            .Where(index => index > 0)
            .OrderBy(index => index)
            .Distinct()
            .ToArray();

        var outputs = new List<LogicConnectorDefinition>(caseIndexes.Length + 1);
        foreach (var index in caseIndexes)
        {
            outputs.Add(new LogicConnectorDefinition(
                $"case{index}",
                $"Case {index}",
                LogicConnectorKind.Flow,
                LogicConnectorDirection.Output));
        }

        outputs.Add(new LogicConnectorDefinition("default", "Default", LogicConnectorKind.Flow, LogicConnectorDirection.Output));
        return outputs;
    }

    private static int ParseSwitchCaseIndex(string key)
    {
        if (!key.StartsWith("case", StringComparison.OrdinalIgnoreCase)
            || !int.TryParse(key[4..], out var index))
        {
            return 0;
        }

        return index;
    }
}
