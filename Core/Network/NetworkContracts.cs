namespace GeneralHostFrontend.Core.Network;

public enum PayloadFormat
{
    Json,
    Protobuf,
    Bytes
}

public sealed record MessageEnvelope(
    string Topic,
    ReadOnlyMemory<byte> Payload,
    PayloadFormat Format,
    IReadOnlyDictionary<string, string>? Headers = null,
    string? CorrelationId = null,
    DateTimeOffset? ExpiresAt = null);

public interface IPubSubChannel
{
    Task PublishAsync(MessageEnvelope message, CancellationToken cancellationToken = default);

    IAsyncEnumerable<MessageEnvelope> SubscribeAsync(string topicFilter, CancellationToken cancellationToken = default);
}

public interface IRequestResponseChannel
{
    Task<MessageEnvelope> SendAsync(MessageEnvelope request, CancellationToken cancellationToken = default);
}

public interface IPayloadSerializer
{
    PayloadFormat Format { get; }

    ReadOnlyMemory<byte> Serialize<T>(T value);

    T? Deserialize<T>(ReadOnlyMemory<byte> payload);
}
