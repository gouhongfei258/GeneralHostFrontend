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

        foreach (var input in definition.Inputs)
        {
            Input.Add(new LogicConnectorViewModel(this, input));
        }

        foreach (var output in definition.Outputs)
        {
            Output.Add(new LogicConnectorViewModel(this, output));
        }

        Properties = new ObservableCollection<LogicNodePropertyViewModel>(
            definition.Properties
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
    }

    public LogicNodeDefinition ToDefinition()
    {
        return LogicNodeTemplate.Create(Kind, Id, Location.X, Location.Y) with
        {
            Title = Title,
            Properties = Properties.ToDictionary(
                property => property.Key.Trim(),
                property => property.Value.Trim(),
                StringComparer.OrdinalIgnoreCase)
        };
    }

    private void RefreshPropertyText()
    {
        PropertyText = string.Join(
            Environment.NewLine,
            Properties.Select(property => $"{property.Key}={property.Value}"));
    }

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
