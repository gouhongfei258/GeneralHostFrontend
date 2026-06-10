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
                EnsureParameter("addressStartWithZero", "true");
                break;
            case DriverKind.ModbusUdp:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.10" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("dataFormat", "ABCD");
                EnsureParameter("addressStartWithZero", "true");
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
                EnsureParameter("connectionType", "1");
                break;
            case DriverKind.SiemensFetchWrite:
                Port = Port > 0 ? Port : 102;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.21" : Address;
                break;
            case DriverKind.SiemensPpiOverTcp:
                Port = Port > 0 ? Port : 102;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.21" : Address;
                EnsureParameter("station", "2");
                break;
            case DriverKind.OmronFins:
            case DriverKind.OmronFinsUdp:
                Port = Port > 0 ? Port : 9600;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.30" : Address;
                EnsureParameter("plcType", "CSCJ");
                EnsureParameter("da1", "10");
                EnsureParameter("sa1", "20");
                EnsureParameter("readSplits", "500");
                break;
            case DriverKind.OmronHostLinkOverTcp:
                Port = Port > 0 ? Port : 9600;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.31" : Address;
                EnsureParameter("plcType", "CSCJ");
                EnsureParameter("unitNumber", "0");
                EnsureParameter("da2", "0");
                EnsureParameter("sa2", "0");
                EnsureParameter("responseWaitTime", "0");
                break;
            case DriverKind.OmronHostLinkCModeOverTcp:
                Port = Port > 0 ? Port : 9600;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.31" : Address;
                EnsureParameter("unitNumber", "0");
                break;
            case DriverKind.OmronCip:
            case DriverKind.AllenBradleyCip:
            case DriverKind.AllenBradleyPccc:
            case DriverKind.AllenBradleySlc:
            case DriverKind.MelsecCip:
            case DriverKind.InovanceConnectedCip:
                Port = Port > 0 ? Port : 44818;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.40" : Address;
                break;
            case DriverKind.OmronConnectedCip:
                Port = Port > 0 ? Port : 44818;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.40" : Address;
                EnsureParameter("connectionTimeoutMultiplier", "1");
                break;
            case DriverKind.AllenBradleyConnectedCip:
                Port = Port > 0 ? Port : 44818;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.40" : Address;
                break;
            case DriverKind.MelsecMc:
            case DriverKind.MelsecMcUdp:
            case DriverKind.MelsecMcAscii:
            case DriverKind.MelsecMcAsciiUdp:
            case DriverKind.MelsecMcR:
                Port = Port > 0 ? Port : 6000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.50" : Address;
                EnsureParameter("networkNumber", "0");
                EnsureParameter("networkStationNumber", "0");
                EnsureParameter("plcNumber", "255");
                EnsureParameter("targetIOStation", "1023");
                break;
            case DriverKind.MelsecA3COverTcp:
                Port = Port > 0 ? Port : 6000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.50" : Address;
                EnsureParameter("station", "0");
                EnsureParameter("format", "1");
                EnsureParameter("sumCheck", "true");
                break;
            case DriverKind.MelsecFxLinksOverTcp:
                Port = Port > 0 ? Port : 6000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.50" : Address;
                EnsureParameter("station", "0");
                EnsureParameter("format", "1");
                EnsureParameter("sumCheck", "true");
                EnsureParameter("waittingTime", "0");
                break;
            case DriverKind.MelsecFxSerialOverTcp:
                Port = Port > 0 ? Port : 6000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.50" : Address;
                EnsureParameter("isNewVersion", "true");
                EnsureParameter("useGot", "false");
                break;
            case DriverKind.MelsecA1E:
            case DriverKind.MelsecA1EAscii:
                Port = Port > 0 ? Port : 5000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.51" : Address;
                EnsureParameter("plcNumber", "0");
                break;
            case DriverKind.KeyenceMc:
            case DriverKind.KeyenceMcAscii:
                Port = Port > 0 ? Port : 5000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.51" : Address;
                break;
            case DriverKind.KeyenceNanoOverTcp:
                Port = Port > 0 ? Port : 8501;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.52" : Address;
                EnsureParameter("station", "0");
                EnsureParameter("useStation", "false");
                break;
            case DriverKind.PanasonicMc:
                Port = Port > 0 ? Port : 6000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.50" : Address;
                break;
            case DriverKind.PanasonicMewtocolOverTcp:
                Port = Port > 0 ? Port : 9094;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.53" : Address;
                EnsureParameter("station", "238");
                break;
            case DriverKind.BeckhoffAds:
                Port = Port > 0 ? Port : 48898;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.60" : Address;
                EnsureParameter("amsPort", "851");
                EnsureParameter("useAutoAmsNetId", "true");
                EnsureParameter("useTagCache", "true");
                break;
            case DriverKind.DeltaTcp:
            case DriverKind.DeltaSerialOverTcp:
            case DriverKind.DeltaSerialAsciiOverTcp:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.70" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("series", "Dvp");
                EnsureParameter("dataFormat", "ABCD");
                EnsureParameter("addressStartWithZero", "true");
                break;
            case DriverKind.InovanceTcp:
            case DriverKind.InovanceSerialOverTcp:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.70" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("series", "AM");
                EnsureParameter("dataFormat", "ABCD");
                EnsureParameter("addressStartWithZero", "true");
                break;
            case DriverKind.XinJeTcp:
            case DriverKind.XinJeSerialOverTcp:
            case DriverKind.MegMeetTcp:
            case DriverKind.MegMeetSerialOverTcp:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.70" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("dataFormat", "ABCD");
                EnsureParameter("addressStartWithZero", "true");
                break;
            case DriverKind.XinJeInternal:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.70" : Address;
                EnsureParameter("station", "1");
                EnsureParameter("dataFormat", "ABCD");
                EnsureParameter("isStringReverse", "false");
                break;
            case DriverKind.FatekProgramOverTcp:
                Port = Port > 0 ? Port : 500;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.72" : Address;
                break;
            case DriverKind.InovanceEasy:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.71" : Address;
                break;
            case DriverKind.FujiSph:
                Port = Port > 0 ? Port : 18245;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.80" : Address;
                EnsureParameter("connectionId", "0");
                break;
            case DriverKind.FujiSpbOverTcp:
                Port = Port > 0 ? Port : 18245;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.80" : Address;
                EnsureParameter("station", "1");
                break;
            case DriverKind.GeSrtp:
                Port = Port > 0 ? Port : 18245;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.80" : Address;
                break;
            case DriverKind.LsFastEnet:
                Port = Port > 0 ? Port : 2004;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.90" : Address;
                EnsureParameter("companyId", "LSIS-XGT");
                EnsureParameter("baseNo", "0");
                EnsureParameter("slotNo", "0");
                break;
            case DriverKind.LsCnetOverTcp:
                Port = Port > 0 ? Port : 2004;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.90" : Address;
                EnsureParameter("station", "0");
                break;
            case DriverKind.YaskawaMemobusTcp:
            case DriverKind.YaskawaMemobusUdp:
                Port = Port > 0 ? Port : 502;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.100" : Address;
                EnsureParameter("cpuFrom", "2");
                EnsureParameter("cpuTo", "1");
                break;
            case DriverKind.Http:
                Port = Port > 0 ? Port : 80;
                Address = string.IsNullOrWhiteSpace(Address) ? "http://192.168.1.40" : Address;
                EnsureParameter("readMethod", "GET");
                EnsureParameter("writeMethod", "POST");
                EnsureParameter("heartbeatPath", "/health");
                break;
            case DriverKind.TcpClient:
                Port = Port > 0 ? Port : 9000;
                Address = string.IsNullOrWhiteSpace(Address) ? "192.168.1.50" : Address;
                EnsureParameter("encoding", "utf-8");
                EnsureParameter("terminator", "\\n");
                EnsureParameter("responseTerminator", "\\n");
                break;
            case DriverKind.TcpServer:
                Port = Port > 0 ? Port : 9000;
                Address = string.IsNullOrWhiteSpace(Address) ? "0.0.0.0" : Address;
                EnsureParameter("encoding", "utf-8");
                EnsureParameter("terminator", "\\n");
                EnsureParameter("writeTerminator", "\\n");
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
