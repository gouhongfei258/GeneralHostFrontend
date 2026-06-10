using System.Globalization;
using System.Text;
using GeneralHostFrontend.Core.Logic;

namespace GeneralHostFrontend.Infrastructure.Logic;

public sealed class CSharpLogicCodeGenerator : ILogicCodeGenerator
{
    public LogicBuildResult Generate(LogicGraphDocument document)
    {
        var diagnostics = Validate(document);
        var code = BuildCode(document);
        return new LogicBuildResult(code, diagnostics.Count == 0, diagnostics);
    }

    private static IReadOnlyList<string> Validate(LogicGraphDocument document)
    {
        var diagnostics = new List<string>();
        var nodeIds = document.Nodes.Select(node => node.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var structNames = (document.PlcStructs ?? Array.Empty<LogicPlcStructDefinition>())
            .Select(schema => schema.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var duplicate in document.Nodes
            .GroupBy(node => node.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add($"Node id '{duplicate.Key}' is duplicated.");
        }

        foreach (var duplicate in (document.PlcStructs ?? Array.Empty<LogicPlcStructDefinition>())
            .GroupBy(schema => schema.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            diagnostics.Add($"PLC struct schema '{duplicate.Key}' is duplicated.");
        }

        foreach (var schema in document.PlcStructs ?? Array.Empty<LogicPlcStructDefinition>())
        {
            if (string.IsNullOrWhiteSpace(schema.Name))
            {
                diagnostics.Add("PLC struct schema name cannot be empty.");
            }

            foreach (var field in schema.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.Name))
                {
                    diagnostics.Add($"PLC struct schema '{schema.Name}' has an empty field name.");
                }

                if (string.IsNullOrWhiteSpace(field.Address))
                {
                    diagnostics.Add($"PLC struct schema '{schema.Name}' field '{field.Name}' requires address.");
                }
            }
        }

        foreach (var connection in document.Connections)
        {
            if (!nodeIds.Contains(connection.SourceNodeId))
            {
                diagnostics.Add($"Connection '{connection.Id}' references missing source node '{connection.SourceNodeId}'.");
            }

            if (!nodeIds.Contains(connection.TargetNodeId))
            {
                diagnostics.Add($"Connection '{connection.Id}' references missing target node '{connection.TargetNodeId}'.");
            }
        }

        foreach (var node in document.Nodes)
        {
            switch (node.Kind)
            {
                case LogicNodeKind.Timer:
                    if (!TryGetInt(node, "intervalMs", out var intervalMs) || intervalMs < 20)
                    {
                        diagnostics.Add($"Timer node '{node.Title}' requires intervalMs >= 20.");
                    }

                    break;
                case LogicNodeKind.OnTagChanged:
                case LogicNodeKind.ReadTag:
                case LogicNodeKind.ReadTagCached:
                case LogicNodeKind.ReadTagDirect:
                    RequireProperty(diagnostics, node, "tagName");
                    break;
                case LogicNodeKind.ReadPlcStruct:
                    RequireProperty(diagnostics, node, "deviceId");
                    RequireProperty(diagnostics, node, "schemaName");
                    RequireProperty(diagnostics, node, "baseAddress");
                    var schemaName = GetProperty(node, "schemaName");
                    if (!string.IsNullOrWhiteSpace(schemaName) && structNames.Count > 0 && !structNames.Contains(schemaName))
                    {
                        diagnostics.Add($"Read PLC Struct node '{node.Title}' references missing schema '{schemaName}'.");
                    }

                    break;
                case LogicNodeKind.Compare:
                    if (!double.TryParse(GetProperty(node, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    {
                        diagnostics.Add($"Compare node '{node.Title}' requires numeric value.");
                    }

                    var op = GetProperty(node, "operator");
                    if (op is not (">" or ">=" or "<" or "<=" or "==" or "!="))
                    {
                        diagnostics.Add($"Compare node '{node.Title}' operator must be >, >=, <, <=, == or !=.");
                    }

                    break;
                case LogicNodeKind.Switch:
                    ValidateSwitchCases(diagnostics, node);
                    break;
                case LogicNodeKind.WriteTag:
                    RequireProperty(diagnostics, node, "tagName");
                    break;
                case LogicNodeKind.PulseBit:
                    RequireProperty(diagnostics, node, "tagName");
                    if (!TryGetInt(node, "durationMs", out var pulseMs) || pulseMs < 20)
                    {
                        diagnostics.Add($"Pulse Bit node '{node.Title}' requires durationMs >= 20.");
                    }

                    break;
                case LogicNodeKind.Delay:
                    if (!TryGetInt(node, "durationMs", out var delayMs) || delayMs < 1)
                    {
                        diagnostics.Add($"Delay node '{node.Title}' requires durationMs >= 1.");
                    }

                    break;
            }
        }

        return diagnostics;
    }

    private static void ValidateSwitchCases(ICollection<string> diagnostics, LogicNodeDefinition node)
    {
        foreach (var connectorId in GetSwitchCaseConnectorIds(node))
        {
            var value = GetProperty(node, connectorId);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var validationError = ValidateSwitchCaseRule(value);
            if (validationError is not null)
            {
                diagnostics.Add($"Switch node '{node.Title}' {connectorId} {validationError}");
            }
        }
    }

    private static string BuildCode(LogicGraphDocument document)
    {
        var builder = new StringBuilder();
        var nodes = document.Nodes.ToDictionary(node => node.Id, StringComparer.OrdinalIgnoreCase);
        var outgoing = document.Connections
            .GroupBy(connection => connection.SourceNodeId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        builder.AppendLine("using System;");
        builder.AppendLine("using System.Collections.Generic;");
        builder.AppendLine("using System.Threading;");
        builder.AppendLine("using System.Threading.Tasks;");
        builder.AppendLine("using GeneralHostFrontend.Core.Logic;");
        builder.AppendLine();
        builder.AppendLine("namespace GeneralHostFrontend.GeneratedLogic;");
        builder.AppendLine();
        builder.AppendLine("public sealed class GeneratedHostLogic : IGeneratedHostLogic");
        builder.AppendLine("{");
        builder.AppendLine("    public async Task ExecuteAsync(IHostLogicContext context, CancellationToken cancellationToken)");
        builder.AppendLine("    {");
        builder.AppendLine("        object? currentValue = null;");
        builder.AppendLine("        GeneralHostFrontend.Core.Tags.TagValue? currentTagValue = null;");
        builder.AppendLine("        IReadOnlyDictionary<string, object?>? currentStruct = null;");

        foreach (var trigger in document.Nodes.Where(IsTrigger))
        {
            AppendTrigger(builder, trigger, outgoing, nodes);
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendTrigger(
        StringBuilder builder,
        LogicNodeDefinition trigger,
        IReadOnlyDictionary<string, LogicConnectionDefinition[]> outgoing,
        IReadOnlyDictionary<string, LogicNodeDefinition> nodes)
    {
        switch (trigger.Kind)
        {
            case LogicNodeKind.Timer:
                var interval = Math.Max(20, TryGetInt(trigger, "intervalMs", out var parsedInterval) ? parsedInterval : 1000);
                builder.AppendLine($"        await Task.Delay({interval.ToString(CultureInfo.InvariantCulture)}, cancellationToken);");
                AppendTargets(builder, trigger, "then", outgoing, nodes, 2, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                break;
            case LogicNodeKind.OnTagChanged:
                builder.AppendLine($"        if (context.HasTagChanged({Quote(GetProperty(trigger, "tagName"))}))");
                builder.AppendLine("        {");
                builder.AppendLine($"            currentTagValue = await context.ReadTagValueAsync({Quote(GetProperty(trigger, "tagName"))}, LogicTagReadMode.Cached, cancellationToken);");
                builder.AppendLine("            currentValue = currentTagValue?.Value;");
                AppendTargets(builder, trigger, "then", outgoing, nodes, 3, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                builder.AppendLine("        }");
                break;
        }
    }

    private static void AppendNode(
        StringBuilder builder,
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, LogicConnectionDefinition[]> outgoing,
        IReadOnlyDictionary<string, LogicNodeDefinition> nodes,
        int indentLevel,
        HashSet<string> path)
    {
        if (!path.Add(node.Id))
        {
            AppendLine(builder, indentLevel, $"context.Log({Quote($"Loop skipped at node '{node.Title}'.")});");
            return;
        }

        switch (node.Kind)
        {
            case LogicNodeKind.ReadTag:
            case LogicNodeKind.ReadTagCached:
            case LogicNodeKind.ReadTagDirect:
                AppendReadTag(builder, node, indentLevel);
                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.ReadPlcStruct:
                AppendReadPlcStruct(builder, node, indentLevel);
                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.Compare:
                AppendCompare(builder, node, outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.Switch:
                AppendSwitch(builder, node, outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.WriteTag:
                AppendLine(builder, indentLevel, $"await context.WriteTagAsync({Quote(GetProperty(node, "tagName"))}, {ResolveNodeValue(node)}, cancellationToken);");
                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.PulseBit:
                var pulseMs = Math.Max(20, TryGetInt(node, "durationMs", out var parsedPulse) ? parsedPulse : 200);
                AppendLine(builder, indentLevel, $"await context.WriteTagAsync({Quote(GetProperty(node, "tagName"))}, true, cancellationToken);");
                AppendLine(builder, indentLevel, $"await Task.Delay({pulseMs.ToString(CultureInfo.InvariantCulture)}, cancellationToken);");
                AppendLine(builder, indentLevel, $"await context.WriteTagAsync({Quote(GetProperty(node, "tagName"))}, false, cancellationToken);");
                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.Delay:
                var delayMs = Math.Max(1, TryGetInt(node, "durationMs", out var parsedDelay) ? parsedDelay : 100);
                AppendLine(builder, indentLevel, $"await Task.Delay({delayMs.ToString(CultureInfo.InvariantCulture)}, cancellationToken);");
                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.Expression:
                AppendLine(builder, indentLevel, $"currentValue = {NormalizeExpression(GetProperty(node, "expression"))};");
                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
            case LogicNodeKind.Log:
                var message = GetProperty(node, "message");
                if (message.Contains("{value}", StringComparison.OrdinalIgnoreCase))
                {
                    AppendLine(builder, indentLevel, $"context.Log($\"{EscapeInterpolated(message).Replace("{value}", "{currentValue}", StringComparison.OrdinalIgnoreCase)}\");");
                }
                else
                {
                    AppendLine(builder, indentLevel, $"context.Log({Quote(message)});");
                }

                AppendTargets(builder, node, "then", outgoing, nodes, indentLevel, path);
                break;
        }
    }

    private static void AppendReadTag(StringBuilder builder, LogicNodeDefinition node, int indentLevel)
    {
        var mode = node.Kind switch
        {
            LogicNodeKind.ReadTagDirect => LogicTagReadMode.Direct,
            LogicNodeKind.ReadTagCached => LogicTagReadMode.Cached,
            _ => ParseReadMode(GetProperty(node, "mode"))
        };

        AppendLine(builder, indentLevel, $"currentTagValue = await context.ReadTagValueAsync({Quote(GetProperty(node, "tagName"))}, LogicTagReadMode.{mode}, cancellationToken);");
        AppendLine(builder, indentLevel, "currentValue = currentTagValue?.Value;");
    }

    private static void AppendReadPlcStruct(StringBuilder builder, LogicNodeDefinition node, int indentLevel)
    {
        var mode = ParseReadMode(GetProperty(node, "mode"));
        AppendLine(
            builder,
            indentLevel,
            "currentStruct = await context.ReadPlcStructAsync("
            + $"new LogicPlcStructReadRequest({Quote(GetProperty(node, "deviceId"))}, {Quote(GetProperty(node, "schemaName"))}, {Quote(GetProperty(node, "baseAddress"))}, LogicTagReadMode.{mode}), cancellationToken);");
        AppendLine(builder, indentLevel, "currentValue = currentStruct;");
    }

    private static void AppendCompare(
        StringBuilder builder,
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, LogicConnectionDefinition[]> outgoing,
        IReadOnlyDictionary<string, LogicNodeDefinition> nodes,
        int indentLevel,
        HashSet<string> path)
    {
        var op = NormalizeOperator(GetProperty(node, "operator"));
        var value = double.TryParse(GetProperty(node, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedValue)
            ? parsedValue
            : 0;

        AppendLine(builder, indentLevel, "var numericValue = Convert.ToDouble(currentValue ?? 0, System.Globalization.CultureInfo.InvariantCulture);");
        AppendLine(builder, indentLevel, $"if (numericValue {op} {value.ToString(CultureInfo.InvariantCulture)})");
        AppendLine(builder, indentLevel, "{");
        AppendTargets(builder, node, "true", outgoing, nodes, indentLevel + 1, path);
        AppendLine(builder, indentLevel, "}");
        var falseTargets = GetTargets(node, "false", outgoing, nodes).ToArray();
        if (falseTargets.Length > 0)
        {
            AppendLine(builder, indentLevel, "else");
            AppendLine(builder, indentLevel, "{");
            AppendTargets(builder, node, "false", outgoing, nodes, indentLevel + 1, path);
            AppendLine(builder, indentLevel, "}");
        }
    }

    private static void AppendSwitch(
        StringBuilder builder,
        LogicNodeDefinition node,
        IReadOnlyDictionary<string, LogicConnectionDefinition[]> outgoing,
        IReadOnlyDictionary<string, LogicNodeDefinition> nodes,
        int indentLevel,
        HashSet<string> path)
    {
        AppendLine(builder, indentLevel, "var switchValue = currentValue;");
        var hasCondition = false;
        foreach (var connectorId in GetSwitchCaseConnectorIds(node))
        {
            var rule = ParseSwitchCaseRule(GetProperty(node, connectorId));
            if (rule is null)
            {
                continue;
            }

            AppendLine(builder, indentLevel, hasCondition ? $"else if ({rule.Condition})" : $"if ({rule.Condition})");
            AppendLine(builder, indentLevel, "{");
            AppendTargets(builder, node, connectorId, outgoing, nodes, indentLevel + 1, path);
            AppendLine(builder, indentLevel, "}");
            hasCondition = true;
        }

        var defaultTargets = GetTargets(node, "default", outgoing, nodes).ToArray();
        if (defaultTargets.Length > 0)
        {
            AppendLine(builder, indentLevel, hasCondition ? "else" : "if (true)");
            AppendLine(builder, indentLevel, "{");
            foreach (var next in defaultTargets)
            {
                AppendNode(builder, next, outgoing, nodes, indentLevel + 1, new HashSet<string>(path, StringComparer.OrdinalIgnoreCase));
            }

            AppendLine(builder, indentLevel, "}");
        }
    }

    private static void AppendTargets(
        StringBuilder builder,
        LogicNodeDefinition source,
        string connectorId,
        IReadOnlyDictionary<string, LogicConnectionDefinition[]> outgoing,
        IReadOnlyDictionary<string, LogicNodeDefinition> nodes,
        int indentLevel,
        HashSet<string> path)
    {
        foreach (var next in GetTargets(source, connectorId, outgoing, nodes))
        {
            AppendNode(builder, next, outgoing, nodes, indentLevel, new HashSet<string>(path, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static IEnumerable<LogicNodeDefinition> GetTargets(
        LogicNodeDefinition source,
        string connectorId,
        IReadOnlyDictionary<string, LogicConnectionDefinition[]> outgoing,
        IReadOnlyDictionary<string, LogicNodeDefinition> nodes)
    {
        if (!outgoing.TryGetValue(source.Id, out var connections))
        {
            yield break;
        }

        foreach (var connection in connections.Where(item => string.Equals(item.SourceConnectorId, connectorId, StringComparison.OrdinalIgnoreCase)))
        {
            if (nodes.TryGetValue(connection.TargetNodeId, out var target))
            {
                yield return target;
            }
        }
    }

    private static bool IsTrigger(LogicNodeDefinition node)
        => node.Kind is LogicNodeKind.Timer or LogicNodeKind.OnTagChanged;

    private static IReadOnlyList<string> GetSwitchCaseConnectorIds(LogicNodeDefinition node)
        => node.Outputs
            .Where(output =>
                output.Kind is LogicConnectorKind.Flow
                && output.Id.StartsWith("case", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(output.Id[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .OrderBy(output => int.Parse(output.Id[4..], CultureInfo.InvariantCulture))
            .Select(output => output.Id)
            .ToArray();

    private static string ResolveNodeValue(LogicNodeDefinition node)
    {
        var value = GetProperty(node, "value");
        return string.IsNullOrWhiteSpace(value) ? "currentValue" : FormatLiteral(value);
    }

    private static string NormalizeExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return "currentValue";
        }

        return expression;
    }

    private static LogicTagReadMode ParseReadMode(string value)
        => Enum.TryParse<LogicTagReadMode>(value, ignoreCase: true, out var mode) ? mode : LogicTagReadMode.Cached;

    private static string NormalizeOperator(string? op)
        => op is ">" or ">=" or "<" or "<=" or "==" or "!=" ? op : ">";

    private static bool TryGetInt(LogicNodeDefinition node, string key, out int value)
    {
        value = 0;
        return int.TryParse(GetProperty(node, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void RequireProperty(ICollection<string> diagnostics, LogicNodeDefinition node, string key)
    {
        if (string.IsNullOrWhiteSpace(GetProperty(node, key)))
        {
            diagnostics.Add($"{node.Title} node '{node.Id}' requires {key}.");
        }
    }

    private static string GetProperty(LogicNodeDefinition node, string key)
        => node.Properties.TryGetValue(key, out var value) ? value : string.Empty;

    private static string FormatLiteral(string? value)
    {
        if (bool.TryParse(value, out var boolValue))
        {
            return boolValue ? "true" : "false";
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return Quote(value ?? string.Empty);
    }

    private static string Quote(string value)
        => SymbolDisplay.FormatLiteral(value, quote: true);

    private static SwitchCaseRule? ParseSwitchCaseRule(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOf(':', StringComparison.Ordinal);
        var kind = separator > 0 ? value[..separator].Trim() : "string";
        var body = separator > 0 ? value[(separator + 1)..].Trim() : value.Trim();

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        return kind.ToLowerInvariant() switch
        {
            "string" or "enum" => new SwitchCaseRule(
                $"string.Equals(Convert.ToString(switchValue, System.Globalization.CultureInfo.InvariantCulture), {Quote(body)}, StringComparison.OrdinalIgnoreCase)"),
            "number" or "num" => double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? new SwitchCaseRule(
                    "switchValue is IConvertible "
                    + $"&& Math.Abs(Convert.ToDouble(switchValue, System.Globalization.CultureInfo.InvariantCulture) - {number.ToString(CultureInfo.InvariantCulture)}) < 0.000001")
                : null,
            "bool" or "boolean" => bool.TryParse(body, out var boolValue)
                ? new SwitchCaseRule($"Equals(switchValue, {(boolValue ? "true" : "false")})")
                : null,
            "expr" or "expression" => new SwitchCaseRule(NormalizeSwitchExpression(body)),
            _ => new SwitchCaseRule(
                $"string.Equals(Convert.ToString(switchValue, System.Globalization.CultureInfo.InvariantCulture), {Quote(value)}, StringComparison.OrdinalIgnoreCase)")
        };
    }

    private static string? ValidateSwitchCaseRule(string value)
    {
        var separator = value.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var kind = value[..separator].Trim();
        var body = value[(separator + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(body))
        {
            return "requires a value after the case type prefix.";
        }

        return kind.ToLowerInvariant() switch
        {
            "string" or "enum" => null,
            "number" or "num" => double.TryParse(body, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
                ? null
                : "requires a numeric value for number case.",
            "bool" or "boolean" => bool.TryParse(body, out _)
                ? null
                : "requires true or false for bool case.",
            "expr" or "expression" => string.IsNullOrWhiteSpace(NormalizeSwitchExpression(body))
                ? "requires a boolean expression."
                : null,
            _ => "has an unknown case type prefix. Use string, number, bool, enum or expr."
        };
    }

    private static string NormalizeSwitchExpression(string expression)
    {
        var normalized = NormalizeExpression(expression);
        return string.IsNullOrWhiteSpace(normalized)
            ? "false"
            : normalized;
    }

    private sealed record SwitchCaseRule(string Condition);

    private static string EscapeInterpolated(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);

    private static void AppendLine(StringBuilder builder, int indentLevel, string line)
        => builder.Append(' ', indentLevel * 4).AppendLine(line);

    private static class SymbolDisplay
    {
        public static string FormatLiteral(string value, bool quote)
        {
            var escaped = value
                .Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace("\"", "\\\"", StringComparison.Ordinal)
                .Replace("\r", "\\r", StringComparison.Ordinal)
                .Replace("\n", "\\n", StringComparison.Ordinal);
            return quote ? $"\"{escaped}\"" : escaped;
        }
    }
}
