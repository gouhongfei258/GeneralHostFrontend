using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Threading.Channels;
using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Tags;
using HslCommunication;
using HslCommunication.Core;
using HslCommunication.Core.Device;
using HslCommunication.Core.Net;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class HslCommunicationDriver : ICommunicationDriver
{
    private readonly CommunicationEndpoint _endpoint;
    private readonly CommunicationOptions _options;
    private readonly IReadWriteNet _client;
    private readonly Func<CancellationToken, Task<OperateResult>> _connect;
    private readonly Func<CancellationToken, Task<OperateResult>> _disconnect;
    private readonly Action _dispose;
    private readonly SemaphoreSlim _operationGate;
    private readonly Channel<DriverStatus> _statusChannel = Channel.CreateUnbounded<DriverStatus>();
    private DriverStatus _status;
    private bool _disposed;

    public HslCommunicationDriver(
        CommunicationEndpoint endpoint,
        CommunicationOptions options,
        IReadWriteNet client,
        Func<CancellationToken, Task<OperateResult>> connect,
        Func<CancellationToken, Task<OperateResult>> disconnect,
        Action dispose)
    {
        _endpoint = endpoint;
        _options = options;
        _client = client;
        _connect = connect;
        _disconnect = disconnect;
        _dispose = dispose;
        _operationGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentOperations));
        _status = new DriverStatus(endpoint.DeviceId, DriverState.Created, null, DateTimeOffset.Now, null);
    }

    public string DeviceId => _endpoint.DeviceId;

    public DriverKind Kind => _endpoint.Kind;

    public DriverStatus Status => _status;

    public async IAsyncEnumerable<DriverStatus> WatchStatusAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return _status;

        await foreach (var status in _statusChannel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return status;
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        SetStatus(DriverState.Connecting, $"Connecting {_endpoint.Kind} endpoint {_endpoint.Address}.");
        var result = await _connect(cancellationToken);
        if (result.IsSuccess)
        {
            SetStatus(DriverState.Connected, "Connected.");
            return;
        }

        SetStatus(DriverState.Faulted, FormatResultMessage(result));
        throw new InvalidOperationException(FormatResultMessage(result));
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var result = await _disconnect(cancellationToken);
        if (!result.IsSuccess)
        {
            SetStatus(DriverState.Faulted, FormatResultMessage(result));
            return;
        }

        SetStatus(DriverState.Disconnected, "Disconnected.");
    }

    public async Task<TagValue> ReadAsync(TagDefinition tag, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!tag.CanRead)
        {
            return ToValue(tag, null, TagQuality.AccessDenied);
        }

        if (_status.State is not DriverState.Connected)
        {
            return ToValue(tag, null, TagQuality.Disconnected);
        }

        return await RunLimitedAsync(async () =>
        {
            try
            {
                return await ReadByTypeAsync(tag);
            }
            catch (Exception ex)
            {
                SetStatus(DriverState.Faulted, ex.Message);
                return ToValue(tag, null, TagQuality.Bad);
            }
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<TagValue>> ReadBatchAsync(IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken = default)
    {
        var values = new List<TagValue>(tags.Count);
        foreach (var tag in tags)
        {
            values.Add(await ReadAsync(tag, cancellationToken));
        }

        return values;
    }

    public async Task WriteAsync(WriteTagCommand command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (!command.Tag.CanWrite)
        {
            throw new InvalidOperationException($"Tag '{command.Tag.Name}' is read-only.");
        }

        if (_status.State is not DriverState.Connected)
        {
            throw new InvalidOperationException($"Device '{DeviceId}' is not connected.");
        }

        var result = await RunLimitedAsync(
            () => WriteByTypeAsync(command.Tag, command.Value),
            cancellationToken);
        if (!result.IsSuccess)
        {
            SetStatus(DriverState.Faulted, FormatResultMessage(result));
            throw new InvalidOperationException(FormatResultMessage(result));
        }
    }

    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_status.State is not DriverState.Connected)
        {
            return false;
        }

        await Task.Delay(1, cancellationToken);
        _status = _status with { LastHeartbeatAt = DateTimeOffset.Now };
        _statusChannel.Writer.TryWrite(_status);
        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_status.State is DriverState.Connected)
            {
                await _disconnect(CancellationToken.None);
            }
        }
        catch
        {
        }

        _disposed = true;
        _operationGate.Dispose();
        _dispose();
        SetStatus(DriverState.Disposed, "Disposed.");
        _statusChannel.Writer.TryComplete();
    }

    private async Task<TagValue> ReadByTypeAsync(TagDefinition tag)
    {
        var address = GetHslAddress(tag.Address);
        return tag.DataType switch
        {
            TagDataType.Boolean => ToValue(tag, await ReadContentAsync(_client.ReadBoolAsync(address))),
            TagDataType.Int16 => ToValue(tag, await ReadContentAsync(_client.ReadInt16Async(address))),
            TagDataType.UInt16 => ToValue(tag, await ReadContentAsync(_client.ReadUInt16Async(address))),
            TagDataType.Int32 => ToValue(tag, await ReadContentAsync(_client.ReadInt32Async(address))),
            TagDataType.UInt32 => ToValue(tag, await ReadContentAsync(_client.ReadUInt32Async(address))),
            TagDataType.Int64 => ToValue(tag, await ReadContentAsync(_client.ReadInt64Async(address))),
            TagDataType.UInt64 => ToValue(tag, await ReadContentAsync(_client.ReadUInt64Async(address))),
            TagDataType.Float32 => ToValue(tag, await ReadContentAsync(_client.ReadFloatAsync(address))),
            TagDataType.Float64 => ToValue(tag, await ReadContentAsync(_client.ReadDoubleAsync(address))),
            TagDataType.String => ToValue(tag, await ReadContentAsync(_client.ReadStringAsync(address, GetStringLength(tag)))),
            TagDataType.Bytes => ToValue(tag, await ReadContentAsync(_client.ReadAsync(address, GetByteLength(tag)))),
            _ => ToValue(tag, null, TagQuality.Bad)
        };
    }

    private async Task<object?> ReadContentAsync<T>(Task<OperateResult<T>> readTask)
    {
        var result = await readTask;
        if (result.IsSuccess)
        {
            SetStatus(DriverState.Connected, "Connected.");
            return result.Content;
        }

        SetStatus(DriverState.Faulted, FormatResultMessage(result));
        return null;
    }

    private Task<OperateResult> WriteByTypeAsync(TagDefinition tag, object? value)
    {
        var address = GetHslAddress(tag.Address);
        return tag.DataType switch
        {
            TagDataType.Boolean => _client.WriteAsync(address, Convert.ToBoolean(value, CultureInfo.InvariantCulture)),
            TagDataType.Int16 => _client.WriteAsync(address, Convert.ToInt16(value, CultureInfo.InvariantCulture)),
            TagDataType.UInt16 => _client.WriteAsync(address, Convert.ToUInt16(value, CultureInfo.InvariantCulture)),
            TagDataType.Int32 => _client.WriteAsync(address, Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            TagDataType.UInt32 => _client.WriteAsync(address, Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            TagDataType.Int64 => _client.WriteAsync(address, Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            TagDataType.UInt64 => _client.WriteAsync(address, Convert.ToUInt64(value, CultureInfo.InvariantCulture)),
            TagDataType.Float32 => _client.WriteAsync(address, Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            TagDataType.Float64 => _client.WriteAsync(address, Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            TagDataType.String => _client.WriteAsync(address, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, Encoding.ASCII),
            TagDataType.Bytes => _client.WriteAsync(address, value as byte[] ?? Array.Empty<byte>()),
            _ => Task.FromResult(new OperateResult($"Unsupported tag data type '{tag.DataType}'."))
        };
    }

    private async Task<T> RunLimitedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var delay = _options.MaxOperationsPerSecond <= 0
                ? 1
                : Math.Max(1, 1000 / _options.MaxOperationsPerSecond);
            await Task.Delay(delay, cancellationToken);
            return await action();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private TagValue ToValue(TagDefinition tag, object? value)
    {
        var quality = value is null ? TagQuality.Bad : TagQuality.Good;
        return ToValue(tag, ApplyScaling(tag, value), quality);
    }

    private TagValue ToValue(TagDefinition tag, object? value, TagQuality quality)
        => new(tag.Name, value, quality, DateTimeOffset.Now, tag.EngineeringUnit, tag.LowerLimit, tag.UpperLimit);

    private static object? ApplyScaling(TagDefinition tag, object? value)
    {
        if (tag.Scaling is null || value is not IConvertible)
        {
            return value;
        }

        return tag.Scaling.Convert(Convert.ToDouble(value, CultureInfo.InvariantCulture));
    }

    private ushort GetStringLength(TagDefinition tag)
    {
        var value = TryGetTagAddressParameter(tag.Address, "length");
        return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (ushort)32;
    }

    private ushort GetByteLength(TagDefinition tag)
    {
        var value = TryGetTagAddressParameter(tag.Address, "length");
        return ushort.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : (ushort)1;
    }

    private static string GetHslAddress(string address)
    {
        var separatorIndex = address.IndexOf(';', StringComparison.Ordinal);
        return separatorIndex < 0 ? address : address[..separatorIndex];
    }

    private static string? TryGetTagAddressParameter(string address, string key)
    {
        foreach (var segment in address.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Skip(1))
        {
            var separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var name = segment[..separatorIndex];
            if (string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
            {
                return segment[(separatorIndex + 1)..];
            }
        }

        return null;
    }

    private void SetStatus(DriverState state, string? message)
    {
        _status = new DriverStatus(DeviceId, state, message, DateTimeOffset.Now, _status.LastHeartbeatAt);
        _statusChannel.Writer.TryWrite(_status);
    }

    private static string FormatResultMessage(OperateResult result)
        => string.IsNullOrWhiteSpace(result.Message)
            ? $"HSL operation failed with code {result.ErrorCode}."
            : $"HSL operation failed with code {result.ErrorCode}: {result.Message}";

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

public static class HslCommunicationDriverFactory
{
    public static ICommunicationDriver Create(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        return endpoint.Kind switch
        {
            DriverKind.ModbusTcp => CreateModbusTcp(endpoint, options),
            DriverKind.ModbusRtu => CreateModbusRtu(endpoint, options),
            DriverKind.SiemensS7 => CreateSiemensS7(endpoint, options),
            DriverKind.OmronFins => CreateOmronFins(endpoint, options),
            _ => throw new NotSupportedException($"Driver '{endpoint.Kind}' is not supported by the HSL adapter.")
        };
    }

    private static ICommunicationDriver CreateModbusTcp(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        var station = GetByte(endpoint, "station", 1);
        var client = new HslCommunication.ModBus.ModbusTcpNet(endpoint.Address, GetPort(endpoint, 502), station);
        ConfigureTcp(client, options);
        ConfigureModbus(client, endpoint);

        return new HslCommunicationDriver(
            endpoint,
            options,
            client,
            cancellationToken => ConnectTcpAsync(client, cancellationToken),
            cancellationToken => DisconnectTcpAsync(client, cancellationToken),
            client.Dispose);
    }

    private static ICommunicationDriver CreateModbusRtu(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        var station = GetByte(endpoint, "station", 1);
        var client = new HslCommunication.ModBus.ModbusRtu(station);
        client.SerialPortInni(
            endpoint.Address,
            GetInt(endpoint, "baudRate", 9600),
            GetInt(endpoint, "dataBits", 8),
            GetStopBits(endpoint, "stopBits", StopBits.One),
            GetParity(endpoint, "parity", Parity.None));
        ConfigureSerial(client, options);
        ConfigureModbus(client, endpoint);

        return new HslCommunicationDriver(
            endpoint,
            options,
            client,
            cancellationToken => OpenSerialAsync(client, cancellationToken),
            cancellationToken => CloseSerialAsync(client, cancellationToken),
            client.Dispose);
    }

    private static ICommunicationDriver CreateSiemensS7(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        var plcType = GetEnum(endpoint, "plcType", HslCommunication.Profinet.Siemens.SiemensPLCS.S1200);
        var client = new HslCommunication.Profinet.Siemens.SiemensS7Net(plcType, endpoint.Address)
        {
            Port = GetPort(endpoint, 102),
            Rack = GetByte(endpoint, "rack", 0),
            Slot = GetByte(endpoint, "slot", 0)
        };

        if (TryGetInt(endpoint, "connectionType", out var connectionType))
        {
            client.ConnectionType = (byte)connectionType;
        }

        if (TryGetInt(endpoint, "localTsap", out var localTsap))
        {
            client.LocalTSAP = localTsap;
        }

        if (TryGetInt(endpoint, "destTsap", out var destTsap))
        {
            client.DestTSAP = destTsap;
        }

        ConfigureTcp(client, options);

        return new HslCommunicationDriver(
            endpoint,
            options,
            client,
            cancellationToken => ConnectTcpAsync(client, cancellationToken),
            cancellationToken => DisconnectTcpAsync(client, cancellationToken),
            client.Dispose);
    }

    private static ICommunicationDriver CreateOmronFins(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        var client = new HslCommunication.Profinet.Omron.OmronFinsNet(endpoint.Address, GetPort(endpoint, 9600));
        ConfigureTcp(client, options);

        client.PlcType = GetEnum(endpoint, "plcType", HslCommunication.Profinet.Omron.OmronPlcType.CSCJ);
        client.ICF = GetByte(endpoint, "icf", client.ICF);
        client.GCT = GetByte(endpoint, "gct", client.GCT);
        client.DNA = GetByte(endpoint, "dna", client.DNA);
        client.DA1 = GetByte(endpoint, "da1", client.DA1);
        client.DA2 = GetByte(endpoint, "da2", client.DA2);
        client.SNA = GetByte(endpoint, "sna", client.SNA);
        client.SA1 = GetByte(endpoint, "sa1", client.SA1);
        client.SA2 = GetByte(endpoint, "sa2", client.SA2);
        client.SID = GetByte(endpoint, "sid", client.SID);
        client.ReadSplits = GetInt(endpoint, "readSplits", client.ReadSplits);
        client.ReceiveUntilEmpty = GetBool(endpoint, "receiveUntilEmpty", client.ReceiveUntilEmpty);

        return new HslCommunicationDriver(
            endpoint,
            options,
            client,
            cancellationToken => ConnectTcpAsync(client, cancellationToken),
            cancellationToken => DisconnectTcpAsync(client, cancellationToken),
            client.Dispose);
    }

    private static async Task<OperateResult> ConnectTcpAsync(DeviceTcpNet client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await client.ConnectServerAsync();
    }

    private static async Task<OperateResult> DisconnectTcpAsync(DeviceTcpNet client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await client.ConnectCloseAsync();
    }

    private static Task<OperateResult> OpenSerialAsync(DeviceSerialPort client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(client.Open());
    }

    private static Task<OperateResult> CloseSerialAsync(DeviceSerialPort client, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        client.Close();
        return Task.FromResult(new OperateResult { IsSuccess = true });
    }

    private static void ConfigureTcp(DeviceTcpNet client, CommunicationOptions options)
    {
        client.ConnectTimeOut = Math.Max(1, (int)options.ConnectTimeout.TotalMilliseconds);
    }

    private static void ConfigureSerial(DeviceSerialPort client, CommunicationOptions options)
    {
        if (client.CommunicationPipe is not null)
        {
            client.CommunicationPipe.ReceiveTimeOut = Math.Max(1, (int)options.ConnectTimeout.TotalMilliseconds);
        }
    }

    private static void ConfigureModbus(HslCommunication.ModBus.IModbus client, CommunicationEndpoint endpoint)
    {
        client.AddressStartWithZero = GetBool(endpoint, "addressStartWithZero", client.AddressStartWithZero);
        client.Station = GetByte(endpoint, "station", client.Station);
        client.DataFormat = GetEnum(endpoint, "dataFormat", client.DataFormat);
        client.IsStringReverse = GetBool(endpoint, "isStringReverse", client.IsStringReverse);
        client.EnableWriteMaskCode = GetBool(endpoint, "enableWriteMaskCode", client.EnableWriteMaskCode);
        client.BroadcastStation = GetInt(endpoint, "broadcastStation", client.BroadcastStation);
        client.WordReadBatchLength = GetInt(endpoint, "wordReadBatchLength", client.WordReadBatchLength);

        switch (client)
        {
            case HslCommunication.ModBus.ModbusTcpNet tcp:
                tcp.StationCheckMatch = GetBool(endpoint, "stationCheckMatch", tcp.StationCheckMatch);
                break;
            case HslCommunication.ModBus.ModbusRtu rtu:
                rtu.StationCheckMatch = GetBool(endpoint, "stationCheckMatch", rtu.StationCheckMatch);
                rtu.Crc16CheckEnable = GetBool(endpoint, "crc16CheckEnable", rtu.Crc16CheckEnable);
                break;
        }
    }

    private static int GetPort(CommunicationEndpoint endpoint, int defaultPort)
        => endpoint.Port > 0 ? endpoint.Port : defaultPort;

    private static bool GetBool(CommunicationEndpoint endpoint, string key, bool defaultValue)
        => TryGet(endpoint, key, out var value) && bool.TryParse(value, out var parsed)
            ? parsed
            : defaultValue;

    private static int GetInt(CommunicationEndpoint endpoint, string key, int defaultValue)
        => TryGetInt(endpoint, key, out var parsed) ? parsed : defaultValue;

    private static bool TryGetInt(CommunicationEndpoint endpoint, string key, out int value)
    {
        value = 0;
        return TryGet(endpoint, key, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static byte GetByte(CommunicationEndpoint endpoint, string key, byte defaultValue)
        => TryGet(endpoint, key, out var value) && byte.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;

    private static TEnum GetEnum<TEnum>(CommunicationEndpoint endpoint, string key, TEnum defaultValue)
        where TEnum : struct, Enum
        => TryGet(endpoint, key, out var value) && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
            ? parsed
            : defaultValue;

    private static StopBits GetStopBits(CommunicationEndpoint endpoint, string key, StopBits defaultValue)
        => GetEnum(endpoint, key, defaultValue);

    private static Parity GetParity(CommunicationEndpoint endpoint, string key, Parity defaultValue)
        => GetEnum(endpoint, key, defaultValue);

    private static bool TryGet(CommunicationEndpoint endpoint, string key, out string value)
    {
        value = string.Empty;
        if (endpoint.Parameters is null)
        {
            return false;
        }

        foreach (var parameter in endpoint.Parameters)
        {
            if (string.Equals(parameter.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = parameter.Value;
                return true;
            }
        }

        return false;
    }
}
