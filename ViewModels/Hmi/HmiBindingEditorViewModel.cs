using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed partial class HmiBindingEditorViewModel : ObservableObject
{
    private readonly HmiWidgetViewModel _widget;

    public HmiBindingEditorViewModel(
        HmiWidgetViewModel widget,
        HmiBindingSlotDescriptor descriptor,
        IReadOnlyList<string> tagOptions)
    {
        _widget = widget;
        Descriptor = descriptor;
        TagOptions = tagOptions;
    }

    public HmiBindingSlotDescriptor Descriptor { get; }

    public string Name => Descriptor.Name;

    public string DisplayName => Descriptor.DisplayName;

    public string Group => Descriptor.Group;

    public IReadOnlyList<string> TagOptions { get; }

    public string TagName
    {
        get => _widget.GetBindingValue(Name);
        set
        {
            _widget.SetBindingValue(Name, value);
            OnPropertyChanged();
        }
    }
}
