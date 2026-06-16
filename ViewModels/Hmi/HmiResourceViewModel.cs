using GeneralHostFrontend.Core.Hmi;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed class HmiResourceViewModel
{
    public HmiResourceViewModel(HmiResourceDescriptor descriptor)
    {
        Id = descriptor.Id;
        DisplayName = descriptor.DisplayName;
        Kind = descriptor.Kind;
        RelativePath = descriptor.RelativePath;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public string Kind { get; }

    public string RelativePath { get; }
}
