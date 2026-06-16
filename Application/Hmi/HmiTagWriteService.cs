using GeneralHostFrontend.Core.Hmi;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Application.Hmi;

public sealed class HmiTagWriteService : ITagWriteService
{
    private readonly ISettingsStore<HostSettings> _settingsStore;
    private readonly ITagDataPipeline _pipeline;

    public HmiTagWriteService(
        ISettingsStore<HostSettings> settingsStore,
        ITagDataPipeline pipeline)
    {
        _settingsStore = settingsStore;
        _pipeline = pipeline;
    }

    public async Task<TagWriteResult> WriteAsync(string tagName, object? value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return TagWriteResult.Failure("Tag name is empty.");
        }

        var tag = _settingsStore.Current.Tags.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, tagName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (tag is null)
        {
            return TagWriteResult.Failure($"Tag '{tagName}' was not found.");
        }

        if (!tag.CanWrite)
        {
            return TagWriteResult.Failure($"Tag '{tag.Name}' is read-only.");
        }

        var converted = ConvertValue(value, tag.DataType);
        var sample = new TagValue(
            tag.Name,
            converted,
            TagQuality.Good,
            DateTimeOffset.Now,
            tag.EngineeringUnit,
            tag.LowerLimit,
            tag.UpperLimit);

        await _pipeline.PublishAsync(sample, cancellationToken);
        return TagWriteResult.Success($"Wrote {sample.DisplayValue} to {tag.Name}.");
    }

    private static object? ConvertValue(object? value, TagDataType dataType)
    {
        if (value is null)
        {
            return null;
        }

        var text = value.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return dataType switch
        {
            TagDataType.Boolean => bool.TryParse(text, out var boolean)
                ? boolean
                : double.TryParse(text, out var number) && Math.Abs(number) > double.Epsilon,
            TagDataType.Int16 => short.TryParse(text, out var int16) ? int16 : value,
            TagDataType.UInt16 => ushort.TryParse(text, out var uint16) ? uint16 : value,
            TagDataType.Int32 => int.TryParse(text, out var int32) ? int32 : value,
            TagDataType.UInt32 => uint.TryParse(text, out var uint32) ? uint32 : value,
            TagDataType.Int64 => long.TryParse(text, out var int64) ? int64 : value,
            TagDataType.UInt64 => ulong.TryParse(text, out var uint64) ? uint64 : value,
            TagDataType.Float32 => float.TryParse(text, out var float32) ? float32 : value,
            TagDataType.Float64 => double.TryParse(text, out var float64) ? float64 : value,
            _ => text
        };
    }
}
