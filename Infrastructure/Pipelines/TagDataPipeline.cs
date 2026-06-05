using System.Threading.Channels;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Infrastructure.Pipelines;

public sealed class TagDataPipeline : ITagDataPipeline
{
    private readonly TagPipelineOptions _options;
    private readonly Channel<TagValue> _samples;

    public TagDataPipeline(TagPipelineOptions options)
    {
        _options = options;
        _samples = Channel.CreateBounded<TagValue>(new BoundedChannelOptions(options.Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public async ValueTask PublishAsync(TagValue sample, CancellationToken cancellationToken = default)
    {
        await _samples.Writer.WriteAsync(sample, cancellationToken);
    }

    public async IAsyncEnumerable<TagSampleBatch> SubscribeBatchesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new List<TagValue>(_options.MaxBatchSize);
        using var timer = new PeriodicTimer(_options.MaxBatchLatency);

        while (!cancellationToken.IsCancellationRequested)
        {
            while (buffer.Count < _options.MaxBatchSize && _samples.Reader.TryRead(out var sample))
            {
                buffer.Add(sample);
            }

            if (buffer.Count > 0)
            {
                yield return new TagSampleBatch(buffer.ToArray(), DateTimeOffset.Now);
                buffer.Clear();
            }

            if (!await timer.WaitForNextTickAsync(cancellationToken))
            {
                yield break;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _samples.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
