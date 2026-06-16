using System.Threading.Channels;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Infrastructure.Pipelines;

public sealed class TagDataPipeline : ITagDataPipeline
{
    private readonly TagPipelineOptions _options;
    private readonly object _subscribersSync = new();
    private readonly List<Channel<TagValue>> _subscribers = new();

    public TagDataPipeline(TagPipelineOptions options)
    {
        _options = options;
    }

    public ValueTask PublishAsync(TagValue sample, CancellationToken cancellationToken = default)
    {
        Channel<TagValue>[] subscribers;
        lock (_subscribersSync)
        {
            subscribers = _subscribers.ToArray();
        }

        foreach (var subscriber in subscribers)
        {
            subscriber.Writer.TryWrite(sample);
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<TagSampleBatch> SubscribeBatchesAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var samples = Channel.CreateBounded<TagValue>(new BoundedChannelOptions(_options.Capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        lock (_subscribersSync)
        {
            _subscribers.Add(samples);
        }

        var buffer = new List<TagValue>(_options.MaxBatchSize);
        using var timer = new PeriodicTimer(_options.MaxBatchLatency);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                while (buffer.Count < _options.MaxBatchSize && samples.Reader.TryRead(out var sample))
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
        finally
        {
            lock (_subscribersSync)
            {
                _subscribers.Remove(samples);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_subscribersSync)
        {
            foreach (var subscriber in _subscribers)
            {
                subscriber.Writer.TryComplete();
            }

            _subscribers.Clear();
        }

        return ValueTask.CompletedTask;
    }
}
