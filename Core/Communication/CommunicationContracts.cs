using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Core.Communication;

public enum DriverKind
{
    ModbusTcp,
    ModbusUdp,
    ModbusRtu,
    SiemensS7,
    SiemensFetchWrite,
    EtherCat,
    OmronFins,
    OmronFinsUdp,
    OmronHostLinkOverTcp,
    OmronHostLinkCModeOverTcp,
    OmronCip,
    OmronConnectedCip,
    MelsecMc,
    MelsecMcUdp,
    MelsecMcAscii,
    MelsecMcAsciiUdp,
    MelsecMcR,
    MelsecA1E,
    MelsecA1EAscii,
    MelsecA3COverTcp,
    MelsecFxLinksOverTcp,
    MelsecFxSerialOverTcp,
    MelsecCip,
    KeyenceMc,
    KeyenceMcAscii,
    KeyenceNanoOverTcp,
    PanasonicMc,
    PanasonicMewtocolOverTcp,
    AllenBradleyCip,
    AllenBradleyConnectedCip,
    AllenBradleyPccc,
    AllenBradleySlc,
    BeckhoffAds,
    DeltaTcp,
    DeltaSerialOverTcp,
    DeltaSerialAsciiOverTcp,
    FatekProgramOverTcp,
    InovanceTcp,
    InovanceSerialOverTcp,
    InovanceEasy,
    InovanceConnectedCip,
    FujiSph,
    FujiSpbOverTcp,
    GeSrtp,
    LsFastEnet,
    LsCnetOverTcp,
    XinJeTcp,
    XinJeInternal,
    XinJeSerialOverTcp,
    YaskawaMemobusTcp,
    YaskawaMemobusUdp,
    MegMeetTcp,
    MegMeetSerialOverTcp,
    SiemensPpiOverTcp,
    Http,
    TcpServer,
    TcpClient,
    SerialPort,
    UsbHid,
    UsbBulk,
    Simulator
}

public enum DriverState
{
    Created,
    Connecting,
    Connected,
    Reconnecting,
    Disconnected,
    Faulted,
    Disposed
}

public sealed record CommunicationEndpoint(
    string DeviceId,
    DriverKind Kind,
    string Address,
    int Port = 0,
    IReadOnlyDictionary<string, string>? Parameters = null);

public sealed record CommunicationOptions(
    TimeSpan ConnectTimeout,
    TimeSpan HeartbeatPeriod,
    TimeSpan ReconnectDelay,
    int MaxConcurrentOperations,
    int MaxOperationsPerSecond)
{
    public static CommunicationOptions Default { get; } = new(
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(2),
        MaxConcurrentOperations: 8,
        MaxOperationsPerSecond: 200);
}

public sealed record DriverStatus(
    string DeviceId,
    DriverState State,
    string? Message,
    DateTimeOffset LastChangedAt,
    DateTimeOffset? LastHeartbeatAt);

public sealed record WriteTagCommand(TagDefinition Tag, object? Value);

public interface ICommunicationDriver : IAsyncDisposable
{
    string DeviceId { get; }

    DriverKind Kind { get; }

    DriverStatus Status { get; }

    IAsyncEnumerable<DriverStatus> WatchStatusAsync(CancellationToken cancellationToken = default);

    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task DisconnectAsync(CancellationToken cancellationToken = default);

    Task<TagValue> ReadAsync(TagDefinition tag, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagValue>> ReadBatchAsync(IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken = default);

    Task WriteAsync(WriteTagCommand command, CancellationToken cancellationToken = default);

    Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default);
}

public interface ICommunicationDriverFactory
{
    ICommunicationDriver Create(CommunicationEndpoint endpoint, CommunicationOptions options);
}

public interface ICommunicationConnectionPool : IAsyncDisposable
{
    Task<ICommunicationDriver> GetOrCreateAsync(
        CommunicationEndpoint endpoint,
        CommunicationOptions options,
        CancellationToken cancellationToken = default);

    IReadOnlyCollection<DriverStatus> GetStatuses();
}
