using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Core.Pipelines;

public sealed record TagPipelineOptions(
    int Capacity,
    int MaxBatchSize,
    TimeSpan MaxBatchLatency,
    TimeSpan UiPublishInterval)
{
    public static TagPipelineOptions Default { get; } = new(
        Capacity: 10_000,
        MaxBatchSize: 256,
        MaxBatchLatency: TimeSpan.FromMilliseconds(50),
        UiPublishInterval: TimeSpan.FromMilliseconds(150));
}

public sealed record TagSampleBatch(IReadOnlyList<TagValue> Samples, DateTimeOffset PublishedAt);

public interface ITagDataPipeline : IAsyncDisposable
{
    ValueTask PublishAsync(TagValue sample, CancellationToken cancellationToken = default);

    IAsyncEnumerable<TagSampleBatch> SubscribeBatchesAsync(CancellationToken cancellationToken = default);
}
