using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed class HmiTemplateViewModel
{
    public HmiTemplateViewModel(HmiWidgetTemplateDescriptor descriptor)
    {
        Id = descriptor.Id;
        Name = descriptor.Name;
        WidgetCount = descriptor.WidgetCount;
    }

    public string Id { get; }

    public string Name { get; }

    public int WidgetCount { get; }

    public string Summary => WidgetCount == 1 ? "1 widget" : $"{WidgetCount} widgets";
}
