using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed class HmiToolboxItemViewModel
{
    public HmiToolboxItemViewModel(HmiWidgetDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public HmiWidgetDescriptor Descriptor { get; }

    public HmiWidgetKind Kind => Descriptor.Kind;

    public string DisplayName => Descriptor.DisplayName;

    public string Category => Descriptor.Category;
}
