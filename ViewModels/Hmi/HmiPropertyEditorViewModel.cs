using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed partial class HmiPropertyEditorViewModel : ObservableObject
{
    private readonly HmiWidgetViewModel _widget;

    public HmiPropertyEditorViewModel(HmiWidgetViewModel widget, HmiPropertyDescriptor descriptor)
    {
        _widget = widget;
        Descriptor = descriptor;
    }

    public HmiPropertyDescriptor Descriptor { get; }

    public string Name => Descriptor.Name;

    public string DisplayName => Descriptor.DisplayName;

    public string Group => Descriptor.Group;

    public HmiPropertyEditorKind Editor => Descriptor.Editor;

    public IReadOnlyList<string> Options => Descriptor.Options;

    public bool IsTextEditor => Editor is HmiPropertyEditorKind.Text or HmiPropertyEditorKind.Color or HmiPropertyEditorKind.Resource;

    public bool IsNumberEditor => Editor is HmiPropertyEditorKind.Number;

    public bool IsBooleanEditor => Editor is HmiPropertyEditorKind.Boolean;

    public bool IsEnumEditor => Editor is HmiPropertyEditorKind.Enum;

    public string Value
    {
        get => _widget.GetPropertyValue(Name, Descriptor.DefaultValue ?? string.Empty);
        set
        {
            _widget.SetPropertyValue(Name, value);
            OnPropertyChanged();
        }
    }

    public double NumericValue
    {
        get => double.TryParse(Value, out var parsed) ? parsed : 0;
        set => Value = value.ToString("0.###");
    }

    public bool BooleanValue
    {
        get => bool.TryParse(Value, out var parsed) && parsed;
        set => Value = value.ToString();
    }
}
