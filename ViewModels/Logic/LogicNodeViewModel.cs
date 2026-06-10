using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Logic;
using NodifyM.Avalonia.ViewModelBase;

namespace GeneralHostFrontend.ViewModels.Logic;

public sealed partial class LogicNodeViewModel : NodeViewModelBase
{
    [ObservableProperty]
    private string _propertyText = string.Empty;

    public LogicNodeViewModel(LogicNodeDefinition definition)
    {
        Id = definition.Id;
        Kind = definition.Kind;
        Title = definition.Title;
        Location = new Point(definition.X, definition.Y);

        var template = LogicNodeTemplate.Create(definition.Kind, definition.Id, definition.X, definition.Y);
        var inputs = MergeConnectors(definition.Inputs, template.Inputs);
        var properties = NormalizeProperties(definition.Properties, template.Properties, definition.Kind);
        var outputs = CreateOutputs(definition.Outputs, template.Outputs, properties, definition.Kind);

        foreach (var input in inputs)
        {
            Input.Add(new LogicConnectorViewModel(this, input));
        }

        foreach (var output in outputs)
        {
            Output.Add(new LogicConnectorViewModel(this, output));
        }

        Properties = new ObservableCollection<LogicNodePropertyViewModel>(
            properties
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => new LogicNodePropertyViewModel(item.Key, item.Value)));
        RefreshPropertyText();
    }

    public string Id { get; }

    public LogicNodeKind Kind { get; }

    public ObservableCollection<LogicNodePropertyViewModel> Properties { get; }

    partial void OnPropertyTextChanged(string value)
    {
        Properties.Clear();
        foreach (var property in ParseProperties(value))
        {
            Properties.Add(new LogicNodePropertyViewModel(property.Key, property.Value));
        }

        RefreshDynamicConnectors();
    }

    public LogicNodeDefinition ToDefinition()
    {
        var properties = Properties.ToDictionary(
            property => property.Key.Trim(),
            property => property.Value.Trim(),
            StringComparer.OrdinalIgnoreCase);
        var template = LogicNodeTemplate.Create(Kind, Id, Location.X, Location.Y);

        return template with
        {
            Title = Title,
            Properties = properties,
            Outputs = Kind is LogicNodeKind.Switch
                ? LogicNodeTemplate.CreateSwitchOutputs(properties)
                : template.Outputs
        };
    }

    private void RefreshPropertyText()
    {
        PropertyText = string.Join(
            Environment.NewLine,
            Properties.Select(property => $"{property.Key}={property.Value}"));
    }

    private static IReadOnlyList<LogicConnectorDefinition> MergeConnectors(
        IReadOnlyList<LogicConnectorDefinition> current,
        IReadOnlyList<LogicConnectorDefinition> template)
    {
        var merged = new List<LogicConnectorDefinition>(current);
        var currentIds = current
            .Select(connector => connector.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var connector in template)
        {
            if (currentIds.Add(connector.Id))
            {
                merged.Add(connector);
            }
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> MergeProperties(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> template)
    {
        var merged = new Dictionary<string, string>(template, StringComparer.OrdinalIgnoreCase);
        foreach (var property in current)
        {
            merged[property.Key] = property.Value;
        }

        return merged;
    }

    private static IReadOnlyDictionary<string, string> NormalizeProperties(
        IReadOnlyDictionary<string, string> current,
        IReadOnlyDictionary<string, string> template,
        LogicNodeKind kind)
    {
        var merged = MergeProperties(current, template);
        if (kind is not LogicNodeKind.Switch)
        {
            return merged;
        }

        return merged
            .Where(property => !IsSwitchCaseKey(property.Key) || !string.IsNullOrWhiteSpace(property.Value) || template.ContainsKey(property.Key))
            .ToDictionary(property => property.Key, property => property.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<LogicConnectorDefinition> CreateOutputs(
        IReadOnlyList<LogicConnectorDefinition> current,
        IReadOnlyList<LogicConnectorDefinition> template,
        IReadOnlyDictionary<string, string> properties,
        LogicNodeKind kind)
        => kind is LogicNodeKind.Switch
            ? LogicNodeTemplate.CreateSwitchOutputs(properties)
            : MergeConnectors(current, template);

    private void RefreshDynamicConnectors()
    {
        if (Kind is not LogicNodeKind.Switch)
        {
            return;
        }

        var desired = LogicNodeTemplate.CreateSwitchOutputs(Properties.ToDictionary(
            property => property.Key.Trim(),
            property => property.Value.Trim(),
            StringComparer.OrdinalIgnoreCase));
        var desiredIds = desired
            .Select(connector => connector.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = Output.Count - 1; index >= 0; index--)
        {
            if (!desiredIds.Contains(((LogicConnectorViewModel)Output[index]).Id))
            {
                Output.RemoveAt(index);
            }
        }

        var currentIds = Output
            .OfType<LogicConnectorViewModel>()
            .Select(connector => connector.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var connector in desired)
        {
            if (currentIds.Add(connector.Id))
            {
                Output.Add(new LogicConnectorViewModel(this, connector));
            }
        }

        for (var targetIndex = 0; targetIndex < desired.Count; targetIndex++)
        {
            var connectorId = desired[targetIndex].Id;
            var currentIndex = Output
                .OfType<LogicConnectorViewModel>()
                .Select((connector, index) => new { connector, index })
                .FirstOrDefault(item => string.Equals(item.connector.Id, connectorId, StringComparison.OrdinalIgnoreCase))
                ?.index;
            if (currentIndex is not null && currentIndex.Value != targetIndex)
            {
                Output.Move(currentIndex.Value, targetIndex);
            }
        }
    }

    private static bool IsSwitchCaseKey(string key)
        => key.StartsWith("case", StringComparison.OrdinalIgnoreCase)
           && int.TryParse(key[4..], out _);

    private static IEnumerable<KeyValuePair<string, string>> ParseProperties(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            yield return new KeyValuePair<string, string>(
                key,
                line[(separator + 1)..].Trim());
        }
    }
}

public sealed partial class LogicNodePropertyViewModel : ObservableObject
{
    [ObservableProperty]
    private string _key;

    [ObservableProperty]
    private string _value;

    public LogicNodePropertyViewModel(string key, string value)
    {
        _key = key;
        _value = value;
    }
}
