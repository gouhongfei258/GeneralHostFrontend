using System.Globalization;

namespace GeneralHostFrontend.Core.Tags;

public enum TagDataType
{
    Boolean,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float32,
    Float64,
    String,
    Bytes
}

public enum TagAccessMode
{
    ReadOnly,
    WriteOnly,
    ReadWrite
}

public enum TagQuality
{
    Unknown,
    Good,
    Bad,
    Uncertain,
    Timeout,
    Disconnected,
    AccessDenied
}

public sealed record LinearScalingRule(
    double RawMin,
    double RawMax,
    double EngineeringMin,
    double EngineeringMax)
{
    public double Convert(double raw)
    {
        if (Math.Abs(RawMax - RawMin) < double.Epsilon)
        {
            return EngineeringMin;
        }

        var ratio = (raw - RawMin) / (RawMax - RawMin);
        return EngineeringMin + ratio * (EngineeringMax - EngineeringMin);
    }
}

public sealed record TagDefinition(
    string Name,
    string DeviceId,
    string Address,
    TagDataType DataType,
    TagAccessMode Access,
    TimeSpan ScanPeriod,
    string? EngineeringUnit = null,
    double? LowerLimit = null,
    double? UpperLimit = null,
    LinearScalingRule? Scaling = null)
{
    public bool CanRead => Access is TagAccessMode.ReadOnly or TagAccessMode.ReadWrite;

    public bool CanWrite => Access is TagAccessMode.WriteOnly or TagAccessMode.ReadWrite;
}

public sealed record TagValue(
    string TagName,
    object? Value,
    TagQuality Quality,
    DateTimeOffset Timestamp,
    string? EngineeringUnit = null,
    double? LowerLimit = null,
    double? UpperLimit = null)
{
    public string DisplayValue
    {
        get
        {
            if (Value is null)
            {
                return "-";
            }

            return Value switch
            {
                double d => d.ToString("0.###", CultureInfo.InvariantCulture),
                float f => f.ToString("0.###", CultureInfo.InvariantCulture),
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => Value.ToString() ?? "-"
            };
        }
    }
}
