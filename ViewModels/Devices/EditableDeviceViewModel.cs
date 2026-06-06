using CommunityToolkit.Mvvm.ComponentModel;
using GeneralHostFrontend.Core.Communication;

namespace GeneralHostFrontend.ViewModels.Devices;

public sealed partial class EditableDeviceViewModel : ObservableObject
{
    [ObservableProperty]
    private string _deviceId = string.Empty;

    [ObservableProperty]
    private DriverKind _kind = DriverKind.ModbusTcp;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    private string _parametersText = string.Empty;

    public string Summary
    {
        get
        {
            var endpoint = string.IsNullOrWhiteSpace(Address) ? "No address" : Address.Trim();
            var port = Port > 0 ? $":{Port}" : string.Empty;
            return $"{Kind} - {endpoint}{port}";
        }
    }

    partial void OnKindChanged(DriverKind value)
        => OnPropertyChanged(nameof(Summary));

    partial void OnAddressChanged(string value)
        => OnPropertyChanged(nameof(Summary));

    partial void OnPortChanged(int value)
        => OnPropertyChanged(nameof(Summary));

    public static EditableDeviceViewModel From(CommunicationEndpoint endpoint)
    {
        return new EditableDeviceViewModel
        {
            DeviceId = endpoint.DeviceId,
            Kind = endpoint.Kind,
            Address = endpoint.Address,
            Port = endpoint.Port,
            ParametersText = FormatParameters(endpoint.Parameters)
        };
    }

    public CommunicationEndpoint ToEndpoint()
    {
        var parameters = ParseParameters(ParametersText);
        return new CommunicationEndpoint(
            DeviceId.Trim(),
            Kind,
            Address.Trim(),
            Math.Max(0, Port),
            parameters.Count == 0 ? null : parameters);
    }

    public EditableDeviceViewModel Clone()
        => From(ToEndpoint());

    public void ApplyDefaults()
    {
        switch (Kind)
        {
            case DriverKind.ModbusTcp:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.10" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("dataFormat", "ABCD");
                break;
            case DriverKind.ModbusRtu:
                Port = 0;
                Address = string.IsNullOrWhiteSpace(Address) ? "COM3" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("baudRate", "9600");
                EnsureParameter("dataBits", "8");
                EnsureParameter("parity", "None");
                EnsureParameter("stopBits", "One");
                break;
            case DriverKind.SiemensS7:
                Port = Port > 0 ? Port : 102;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.20" : Address;
                EnsureParameter("plcType", "S1200");
                EnsureParameter("rack", "0");
                EnsureParameter("slot", "1");
                break;
            case DriverKind.OmronFins:
                Port = Port > 0 ? Port : 9600;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.30" : Address;
                EnsureParameter("plcType", "CSCJ");
                break;
            case DriverKind.Simulator:
                Port = 0;
                Address = string.IsNullOrWhiteSpace(Address) ? "sim://line-1" : Address;
                break;
        }
    }

    private void EnsureParameter(string key, string value)
    {
        var parameters = ParseParameters(ParametersText);
        if (!parameters.Keys.Any(item => string.Equals(item, key, StringComparison.OrdinalIgnoreCase)))
        {
            parameters[key] = value;
            ParametersText = FormatParameters(parameters);
        }
    }

    private static string FormatParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(
            Environment.NewLine,
            parameters
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .Select(item => $"{item.Key}={item.Value}"));
    }

    private static Dictionary<string, string> ParseParameters(string? text)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return parameters;
        }

        foreach (var rawLine in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Length > 0)
            {
                parameters[key] = value;
            }
        }

        return parameters;
    }
}
