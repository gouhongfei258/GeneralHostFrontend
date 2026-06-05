using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.ViewModels.Tags;

public sealed partial class EditableTagViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _deviceId = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private TagDataType _dataType = TagDataType.Float64;

    [ObservableProperty]
    private TagAccessMode _access = TagAccessMode.ReadOnly;

    [ObservableProperty]
    private int _scanPeriodMs = 250;

    [ObservableProperty]
    private string? _engineeringUnit;

    [ObservableProperty]
    private double? _lowerLimit;

    [ObservableProperty]
    private double? _upperLimit;

    public static EditableTagViewModel From(TagDefinition tag)
    {
        return new EditableTagViewModel
        {
            Name = tag.Name,
            DeviceId = tag.DeviceId,
            Address = tag.Address,
            DataType = tag.DataType,
            Access = tag.Access,
            ScanPeriodMs = Math.Max(1, (int)tag.ScanPeriod.TotalMilliseconds),
            EngineeringUnit = tag.EngineeringUnit,
            LowerLimit = tag.LowerLimit,
            UpperLimit = tag.UpperLimit
        };
    }

    public TagDefinition ToDefinition()
    {
        return new TagDefinition(
            Name.Trim(),
            DeviceId.Trim(),
            Address.Trim(),
            DataType,
            Access,
            TimeSpan.FromMilliseconds(Math.Max(1, ScanPeriodMs)),
            string.IsNullOrWhiteSpace(EngineeringUnit) ? null : EngineeringUnit.Trim(),
            LowerLimit,
            UpperLimit);
    }

    public EditableTagViewModel Clone()
        => From(ToDefinition());
}
