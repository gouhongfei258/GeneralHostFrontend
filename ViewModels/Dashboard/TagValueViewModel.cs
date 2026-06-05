using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.ViewModels.Dashboard;

public sealed partial class TagValueViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _value = "-";

    [ObservableProperty]
    private string? _unit;

    [ObservableProperty]
    private TagQuality _quality = TagQuality.Unknown;

    [ObservableProperty]
    private DateTimeOffset _timestamp;

    public void Update(TagValue tagValue)
    {
        Name = tagValue.TagName;
        Value = tagValue.DisplayValue;
        Unit = tagValue.EngineeringUnit;
        Quality = tagValue.Quality;
        Timestamp = tagValue.Timestamp;
    }
}
