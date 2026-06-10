using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.Infrastructure.Communication;

public sealed class HttpCommunicationDriver : NetworkCommunicationDriverBase
{
    private HttpClient? _client;

    public HttpCommunicationDriver(CommunicationEndpoint endpoint, CommunicationOptions options)
        : base(endpoint, options)
    {
    }

    protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        _client = new HttpClient
        {
            BaseAddress = CreateBaseAddress(),
            Timeout = Options.ConnectTimeout
        };

        var heartbeatPath = GetParameter("heartbeatPath", string.Empty);
        if (!string.IsNullOrWhiteSpace(heartbeatPath))
        {
            using var response = await _client.GetAsync(heartbeatPath, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        _client?.Dispose();
        _client = null;
        return Task.CompletedTask;
    }

    protected override async Task<object?> ReadValueCoreAsync(TagDefinition tag, CancellationToken cancellationToken)
    {
        var client = GetClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(GetTagParameter(tag.Address, "method", GetParameter("readMethod", "GET"))),
            GetAddressPath(tag.Address));
        AddConfiguredHeaders(request);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadAsStringAsync(cancellationToken);
        var jsonPath = GetTagParameter(tag.Address, "jsonPath", GetParameter("jsonPath", string.Empty));
        return ConvertHttpPayload(payload, jsonPath, tag);
    }

    protected override async Task WriteValueCoreAsync(WriteTagCommand command, CancellationToken cancellationToken)
    {
        var client = GetClient();
        using var request = new HttpRequestMessage(
            new HttpMethod(GetParameter("writeMethod", "POST")),
            GetAddressPath(command.Tag.Address));
        AddConfiguredHeaders(request);

        var contentType = GetParameter("contentType", "application/json");
        var body = GetParameter("writeBodyTemplate", string.Empty);
        if (string.IsNullOrWhiteSpace(body))
        {
            body = JsonSerializer.Serialize(new
            {
                tagName = command.Tag.Name,
                address = GetAddressPath(command.Tag.Address),
                value = command.Value
            });
        }
        else
        {
            body = ApplyTemplate(body, command.Tag, command.Value);
        }

        request.Content = new StringContent(body, Encoding.UTF8, contentType);

        using var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    protected override async Task<bool> HeartbeatCoreAsync(CancellationToken cancellationToken)
    {
        var heartbeatPath = GetParameter("heartbeatPath", string.Empty);
        if (string.IsNullOrWhiteSpace(heartbeatPath))
        {
            return _client is not null;
        }

        using var response = await GetClient().GetAsync(heartbeatPath, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private HttpClient GetClient()
        => _client ?? throw new InvalidOperationException($"Device '{DeviceId}' is not connected.");

    private Uri CreateBaseAddress()
    {
        var address = Endpoint.Address.Trim();
        var builder = address.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || address.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? new UriBuilder(address)
                : new UriBuilder("http", address);

        if (Endpoint.Port > 0)
        {
            builder.Port = Endpoint.Port;
        }

        return builder.Uri;
    }

    private void AddConfiguredHeaders(HttpRequestMessage request)
    {
        foreach (var parameter in Endpoint.Parameters ?? new Dictionary<string, string>())
        {
            if (!parameter.Key.StartsWith("header.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headerName = parameter.Key["header.".Length..];
            if (!string.IsNullOrWhiteSpace(headerName))
            {
                request.Headers.TryAddWithoutValidation(headerName, parameter.Value);
            }
        }
    }

    private object? ConvertHttpPayload(string payload, string jsonPath, TagDefinition tag)
    {
        if (tag.DataType is TagDataType.Bytes)
        {
            return ConvertToTagType(payload, tag);
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            var element = document.RootElement;
            if (!string.IsNullOrWhiteSpace(jsonPath) && TrySelectJsonElement(element, jsonPath, out var selected))
            {
                return ConvertToTagType(selected, tag);
            }

            if (element.ValueKind is JsonValueKind.Object && element.TryGetProperty("value", out var valueElement))
            {
                return ConvertToTagType(valueElement, tag);
            }

            return ConvertToTagType(element, tag);
        }
        catch (JsonException)
        {
            return ConvertToTagType(payload, tag);
        }
    }
}

public sealed class TcpClientCommunicationDriver : NetworkCommunicationDriverBase
{
    private readonly SemaphoreSlim _streamGate = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;

    public TcpClientCommunicationDriver(CommunicationEndpoint endpoint, CommunicationOptions options)
        : base(endpoint, options)
    {
    }

    protected override async Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        if (Endpoint.Port <= 0)
        {
            throw new InvalidOperationException("TCP client requires a remote port.");
        }

        _client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Options.ConnectTimeout);
        await _client.ConnectAsync(Endpoint.Address, Endpoint.Port, timeout.Token);
        _stream = _client.GetStream();
    }

    protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        _stream?.Dispose();
        _client?.Dispose();
        _stream = null;
        _client = null;
        return Task.CompletedTask;
    }

    protected override async Task<object?> ReadValueCoreAsync(TagDefinition tag, CancellationToken cancellationToken)
    {
        await _streamGate.WaitAsync(cancellationToken);
        try
        {
            var template = GetTagParameter(tag.Address, "readTemplate", GetParameter("readTemplate", "READ {address}"));
            var response = await SendAndReceiveAsync(ApplyTemplate(template, tag, null), cancellationToken);
            return ConvertTcpResponse(response, tag);
        }
        finally
        {
            _streamGate.Release();
        }
    }

    protected override async Task WriteValueCoreAsync(WriteTagCommand command, CancellationToken cancellationToken)
    {
        await _streamGate.WaitAsync(cancellationToken);
        try
        {
            var template = GetParameter("writeTemplate", "WRITE {address} {value}");
            await SendAsync(ApplyTemplate(template, command.Tag, command.Value), cancellationToken);

            if (GetBoolParameter("requireAck", false))
            {
                var response = await ReceiveAsync(cancellationToken);
                if (response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase)
                    || response.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(response);
                }
            }
        }
        finally
        {
            _streamGate.Release();
        }
    }

    protected override async Task<bool> HeartbeatCoreAsync(CancellationToken cancellationToken)
    {
        var command = GetParameter("heartbeatCommand", string.Empty);
        if (string.IsNullOrWhiteSpace(command))
        {
            return _client?.Connected is true;
        }

        await _streamGate.WaitAsync(cancellationToken);
        try
        {
            var response = await SendAndReceiveAsync(command, cancellationToken);
            return !response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase)
                && !response.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            _streamGate.Release();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        _streamGate.Dispose();
    }

    private async Task<string> SendAndReceiveAsync(string command, CancellationToken cancellationToken)
    {
        await SendAsync(command, cancellationToken);
        return await ReceiveAsync(cancellationToken);
    }

    private async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        var stream = GetStream();
        var encoding = GetEncoding();
        var terminator = DecodeEscapes(GetParameter("terminator", "\\n"));
        var bytes = encoding.GetBytes(command + terminator);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private async Task<string> ReceiveAsync(CancellationToken cancellationToken)
    {
        var stream = GetStream();
        var encoding = GetEncoding();
        var terminator = encoding.GetBytes(DecodeEscapes(GetParameter("responseTerminator", "\\n")));
        var maxBytes = Math.Max(1, GetIntParameter("maxResponseBytes", 65536));
        var buffer = new byte[1024];
        using var memory = new MemoryStream();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Options.ConnectTimeout);

        while (memory.Length < maxBytes)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
            {
                throw new IOException("Remote TCP endpoint closed the connection.");
            }

            memory.Write(buffer, 0, read);
            if (EndsWith(memory, terminator))
            {
                break;
            }
        }

        var data = memory.ToArray();
        if (terminator.Length > 0 && data.Length >= terminator.Length && EndsWith(data, terminator))
        {
            data = data[..^terminator.Length];
        }

        return encoding.GetString(data).Trim();
    }

    private NetworkStream GetStream()
        => _stream ?? throw new InvalidOperationException($"Device '{DeviceId}' is not connected.");
}

public sealed class TcpServerCommunicationDriver : NetworkCommunicationDriverBase
{
    private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();
    private readonly ConcurrentDictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private TcpListener? _listener;
    private CancellationTokenSource? _serverCancellation;

    public TcpServerCommunicationDriver(CommunicationEndpoint endpoint, CommunicationOptions options)
        : base(endpoint, options)
    {
    }

    protected override Task ConnectCoreAsync(CancellationToken cancellationToken)
    {
        if (Endpoint.Port <= 0)
        {
            throw new InvalidOperationException("TCP server requires a listen port.");
        }

        var bindAddress = GetParameter("bindAddress", Endpoint.Address);
        if (string.IsNullOrWhiteSpace(bindAddress) || !IPAddress.TryParse(bindAddress, out var ipAddress))
        {
            ipAddress = IPAddress.Any;
        }

        _serverCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _listener = new TcpListener(ipAddress, Endpoint.Port);
        _listener.Start();
        _ = Task.Run(() => AcceptLoopAsync(_serverCancellation.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected override Task DisconnectCoreAsync(CancellationToken cancellationToken)
    {
        _serverCancellation?.Cancel();
        _listener?.Stop();
        foreach (var client in _clients.Keys)
        {
            CloseClient(client);
        }

        _serverCancellation?.Dispose();
        _serverCancellation = null;
        _listener = null;
        return Task.CompletedTask;
    }

    protected override Task<object?> ReadValueCoreAsync(TagDefinition tag, CancellationToken cancellationToken)
    {
        var address = GetAddressPath(tag.Address);
        return Task.FromResult(_values.TryGetValue(address, out var value)
            ? ConvertToTagType(value, tag)
            : null);
    }

    protected override async Task WriteValueCoreAsync(WriteTagCommand command, CancellationToken cancellationToken)
    {
        var address = GetAddressPath(command.Tag.Address);
        _values[address] = Convert.ToString(command.Value, CultureInfo.InvariantCulture) ?? string.Empty;

        var template = GetParameter("writeTemplate", "WRITE {address} {value}");
        var line = ApplyTemplate(template, command.Tag, command.Value);
        var encoding = GetEncoding();
        var writeTerminator = GetParameter("writeTerminator", GetParameter("terminator", "\\n"));
        var bytes = encoding.GetBytes(line + DecodeEscapes(writeTerminator));

        foreach (var client in _clients.Keys)
        {
            try
            {
                var stream = client.GetStream();
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch
            {
                CloseClient(client);
            }
        }
    }

    protected override Task<bool> HeartbeatCoreAsync(CancellationToken cancellationToken)
        => Task.FromResult(_listener is not null);

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _listener is not null)
        {
            try
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _clients[client] = 0;
                _ = Task.Run(() => ReceiveLoopAsync(client, cancellationToken), CancellationToken.None);
                SetStatus(DriverState.Connected, $"TCP server listening. Clients: {_clients.Count}.");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                SetStatus(DriverState.Faulted, ex.Message);
            }
        }
    }

    private async Task ReceiveLoopAsync(TcpClient client, CancellationToken cancellationToken)
    {
        var stream = client.GetStream();
        var encoding = GetEncoding();
        var terminator = DecodeEscapes(GetParameter("terminator", "\\n"));
        var buffer = new byte[1024];
        var text = new StringBuilder();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, cancellationToken);
                if (read == 0)
                {
                    break;
                }

                text.Append(encoding.GetString(buffer, 0, read));
                DrainReceivedLines(text, terminator);
            }
        }
        catch
        {
        }
        finally
        {
            CloseClient(client);
            SetStatus(DriverState.Connected, $"TCP server listening. Clients: {_clients.Count}.");
        }
    }

    private void DrainReceivedLines(StringBuilder text, string terminator)
    {
        while (true)
        {
            var content = text.ToString();
            var index = content.IndexOf(terminator, StringComparison.Ordinal);
            if (index < 0)
            {
                return;
            }

            var line = content[..index].Trim();
            text.Remove(0, index + terminator.Length);
            CaptureLine(line);
        }
    }

    private void CaptureLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind is JsonValueKind.Object
                && document.RootElement.TryGetProperty("address", out var address)
                && document.RootElement.TryGetProperty("value", out var value))
            {
                _values[address.GetString() ?? string.Empty] = JsonElementToString(value);
                return;
            }
        }
        catch (JsonException)
        {
        }

        var separator = line.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0)
        {
            separator = line.IndexOf(' ', StringComparison.Ordinal);
        }

        if (separator > 0)
        {
            var address = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (address.Length > 0)
            {
                _values[address] = value;
            }
        }
    }

    private void CloseClient(TcpClient client)
    {
        _clients.TryRemove(client, out _);
        try
        {
            client.Dispose();
        }
        catch
        {
        }
    }
}

public abstract class NetworkCommunicationDriverBase : ICommunicationDriver
{
    private readonly Channel<DriverStatus> _statusChannel = Channel.CreateUnbounded<DriverStatus>();
    private readonly SemaphoreSlim _operationGate;
    private DriverStatus _status;
    private bool _disposed;

    protected NetworkCommunicationDriverBase(CommunicationEndpoint endpoint, CommunicationOptions options)
    {
        Endpoint = endpoint;
        Options = options;
        _operationGate = new SemaphoreSlim(Math.Max(1, options.MaxConcurrentOperations));
        _status = new DriverStatus(endpoint.DeviceId, DriverState.Created, null, DateTimeOffset.Now, null);
    }

    protected CommunicationEndpoint Endpoint { get; }

    protected CommunicationOptions Options { get; }

    public string DeviceId => Endpoint.DeviceId;

    public DriverKind Kind => Endpoint.Kind;

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
        SetStatus(DriverState.Connecting, $"Connecting {Kind} endpoint {Endpoint.Address}.");
        try
        {
            await ConnectCoreAsync(cancellationToken);
            SetStatus(DriverState.Connected, "Connected.");
        }
        catch (Exception ex)
        {
            SetStatus(DriverState.Faulted, ex.Message);
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await DisconnectCoreAsync(cancellationToken);
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
                var value = await ReadValueCoreAsync(tag, cancellationToken);
                return ToValue(tag, value, value is null ? TagQuality.Bad : TagQuality.Good);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return ToValue(tag, null, TagQuality.Timeout);
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

        await RunLimitedAsync(async () =>
        {
            await WriteValueCoreAsync(command, cancellationToken);
            return true;
        }, cancellationToken);
    }

    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (_status.State is not DriverState.Connected)
        {
            return false;
        }

        try
        {
            var ok = await RunLimitedAsync(() => HeartbeatCoreAsync(cancellationToken), cancellationToken);
            if (ok)
            {
                _status = _status with { LastHeartbeatAt = DateTimeOffset.Now };
                _statusChannel.Writer.TryWrite(_status);
            }

            return ok;
        }
        catch (Exception ex)
        {
            SetStatus(DriverState.Faulted, ex.Message);
            return false;
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_status.State is DriverState.Connected)
            {
                await DisconnectCoreAsync(CancellationToken.None);
            }
        }
        catch
        {
        }

        _disposed = true;
        _operationGate.Dispose();
        SetStatus(DriverState.Disposed, "Disposed.");
        _statusChannel.Writer.TryComplete();
    }

    protected abstract Task ConnectCoreAsync(CancellationToken cancellationToken);

    protected abstract Task DisconnectCoreAsync(CancellationToken cancellationToken);

    protected abstract Task<object?> ReadValueCoreAsync(TagDefinition tag, CancellationToken cancellationToken);

    protected abstract Task WriteValueCoreAsync(WriteTagCommand command, CancellationToken cancellationToken);

    protected abstract Task<bool> HeartbeatCoreAsync(CancellationToken cancellationToken);

    protected async Task<T> RunLimitedAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            var delay = Options.MaxOperationsPerSecond <= 0
                ? 1
                : Math.Max(1, 1000 / Options.MaxOperationsPerSecond);
            await Task.Delay(delay, cancellationToken);
            return await action();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    protected string GetParameter(string key, string defaultValue)
    {
        if (Endpoint.Parameters is null)
        {
            return defaultValue;
        }

        foreach (var parameter in Endpoint.Parameters)
        {
            if (string.Equals(parameter.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return parameter.Value;
            }
        }

        return defaultValue;
    }

    protected int GetIntParameter(string key, int defaultValue)
        => int.TryParse(GetParameter(key, string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : defaultValue;

    protected bool GetBoolParameter(string key, bool defaultValue)
        => bool.TryParse(GetParameter(key, string.Empty), out var value)
            ? value
            : defaultValue;

    protected Encoding GetEncoding()
    {
        try
        {
            return Encoding.GetEncoding(GetParameter("encoding", "utf-8"));
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    protected static string GetAddressPath(string address)
    {
        var separatorIndex = address.IndexOf(';', StringComparison.Ordinal);
        return separatorIndex < 0 ? address.Trim() : address[..separatorIndex].Trim();
    }

    protected static string GetTagParameter(string address, string key, string defaultValue)
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

        return defaultValue;
    }

    protected string ApplyTemplate(string template, TagDefinition tag, object? value)
    {
        var address = GetAddressPath(tag.Address);
        var textValue = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return template
            .Replace("{tagName}", tag.Name, StringComparison.OrdinalIgnoreCase)
            .Replace("{address}", address, StringComparison.OrdinalIgnoreCase)
            .Replace("{value}", textValue, StringComparison.OrdinalIgnoreCase);
    }

    protected object? ConvertTcpResponse(string response, TagDefinition tag)
    {
        var address = GetAddressPath(tag.Address);
        var text = response.Trim();
        var separator = text.IndexOf('=', StringComparison.Ordinal);
        if (separator > 0)
        {
            var responseAddress = text[..separator].Trim();
            if (string.Equals(responseAddress, address, StringComparison.OrdinalIgnoreCase)
                || string.Equals(responseAddress, tag.Name, StringComparison.OrdinalIgnoreCase))
            {
                text = text[(separator + 1)..].Trim();
            }
        }
        else if (text.StartsWith("OK ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[3..].Trim();
        }

        return ConvertToTagType(text, tag);
    }

    protected static object? ConvertToTagType(JsonElement element, TagDefinition tag)
    {
        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (tag.DataType is TagDataType.String)
        {
            return element.ValueKind is JsonValueKind.String ? element.GetString() : element.GetRawText();
        }

        if (tag.DataType is TagDataType.Bytes)
        {
            return ConvertToTagType(element.ValueKind is JsonValueKind.String ? element.GetString() : element.GetRawText(), tag);
        }

        if (element.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return ConvertToTagType(element.GetBoolean().ToString(CultureInfo.InvariantCulture), tag);
        }

        if (element.ValueKind is JsonValueKind.Number)
        {
            return ConvertToTagType(element.GetRawText(), tag);
        }

        if (element.ValueKind is JsonValueKind.String)
        {
            return ConvertToTagType(element.GetString(), tag);
        }

        return ConvertToTagType(element.GetRawText(), tag);
    }

    protected static object? ConvertToTagType(string? text, TagDefinition tag)
    {
        if (text is null)
        {
            return null;
        }

        var value = text.Trim();
        try
        {
            return tag.DataType switch
            {
                TagDataType.Boolean => ParseBoolean(value),
                TagDataType.Int16 => short.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.UInt16 => ushort.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.Int32 => int.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.UInt32 => uint.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.Int64 => long.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.UInt64 => ulong.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.Float32 => float.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.Float64 => double.Parse(value, CultureInfo.InvariantCulture),
                TagDataType.String => text,
                TagDataType.Bytes => ConvertToBytes(value),
                _ => value
            };
        }
        catch (Exception) when (tag.DataType is not TagDataType.String)
        {
            return null;
        }
    }

    protected static bool TrySelectJsonElement(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (value.ValueKind is JsonValueKind.Object && value.TryGetProperty(segment, out var property))
            {
                value = property;
                continue;
            }

            if (value.ValueKind is JsonValueKind.Array
                && int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < value.GetArrayLength())
            {
                value = value[index];
                continue;
            }

            return false;
        }

        return true;
    }

    protected static string DecodeEscapes(string value)
        => value
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal);

    protected static bool EndsWith(MemoryStream stream, byte[] suffix)
        => EndsWith(stream.ToArray(), suffix);

    protected static bool EndsWith(byte[] value, byte[] suffix)
    {
        if (suffix.Length == 0 || value.Length < suffix.Length)
        {
            return false;
        }

        for (var i = 0; i < suffix.Length; i++)
        {
            if (value[value.Length - suffix.Length + i] != suffix[i])
            {
                return false;
            }
        }

        return true;
    }

    protected static string JsonElementToString(JsonElement element)
        => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            JsonValueKind.Number => element.GetRawText(),
            _ => element.GetRawText()
        };

    protected void SetStatus(DriverState state, string? message)
    {
        _status = new DriverStatus(DeviceId, state, message, DateTimeOffset.Now, _status.LastHeartbeatAt);
        _statusChannel.Writer.TryWrite(_status);
    }

    private TagValue ToValue(TagDefinition tag, object? value, TagQuality quality)
        => new(tag.Name, ApplyScaling(tag, value), quality, DateTimeOffset.Now, tag.EngineeringUnit, tag.LowerLimit, tag.UpperLimit);

    private static object? ApplyScaling(TagDefinition tag, object? value)
    {
        if (tag.Scaling is null || value is not IConvertible)
        {
            return value;
        }

        return tag.Scaling.Convert(Convert.ToDouble(value, CultureInfo.InvariantCulture));
    }

    private static byte[] ConvertToBytes(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            return Encoding.UTF8.GetBytes(value);
        }
    }

    private static bool ParseBoolean(string value)
    {
        if (string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bool.Parse(value);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
