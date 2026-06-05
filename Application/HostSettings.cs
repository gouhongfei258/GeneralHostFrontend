using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Application;

public sealed record HostSettings
{
    public CommunicationOptions Communication { get; init; } = CommunicationOptions.Default;

    public TagPipelineOptions Pipeline { get; init; } = TagPipelineOptions.Default;

    public IReadOnlyList<CommunicationEndpoint> Devices { get; init; } = new[]
    {
        new CommunicationEndpoint("SIM-PLC-01", DriverKind.Simulator, "sim://line-1")
    };

    public IReadOnlyList<TagDefinition> Tags { get; init; } = new[]
    {
        new TagDefinition("Line.Speed", "SIM-PLC-01", "D100", TagDataType.Float64, TagAccessMode.ReadWrite, TimeSpan.FromMilliseconds(250), "pcs/min", 0, 120),
        new TagDefinition("Line.Temperature", "SIM-PLC-01", "D102", TagDataType.Float64, TagAccessMode.ReadOnly, TimeSpan.FromMilliseconds(500), "degC", 0, 85),
        new TagDefinition("Line.Pressure", "SIM-PLC-01", "D104", TagDataType.Float64, TagAccessMode.ReadOnly, TimeSpan.FromMilliseconds(500), "kPa", 0, 800),
        new TagDefinition("Station.Ready", "SIM-PLC-01", "M10", TagDataType.Boolean, TagAccessMode.ReadOnly, TimeSpan.FromMilliseconds(200))
    };
}
