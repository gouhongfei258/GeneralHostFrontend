using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Application;
using GeneralHostFrontend.Core.Communication;
using GeneralHostFrontend.Core.Settings;

namespace GeneralHostFrontend.ViewModels.Devices;

public sealed partial class DeviceEditorViewModel : ViewModelBase
{
    private readonly ISettingsStore<HostSettings> _settingsStore;
    private readonly ICommunicationDriverFactory _driverFactory;
    private HostSettings _settings;

    [ObservableProperty]
    private EditableDeviceViewModel? _selectedDevice;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public DeviceEditorViewModel()
    {
        _settingsStore = null!;
        _driverFactory = null!;
        _settings = new HostSettings();
    }

    public DeviceEditorViewModel(
        ISettingsStore<HostSettings> settingsStore,
        ICommunicationDriverFactory driverFactory)
    {
        _settingsStore = settingsStore;
        _driverFactory = driverFactory;
        _settings = settingsStore.Current;
        LoadFromSettings(_settings);
    }

    public ObservableCollection<EditableDeviceViewModel> Devices { get; } = new();

    public IReadOnlyList<DriverKind> DriverKinds { get; } = new[]
    {
        DriverKind.Simulator,
        DriverKind.ModbusTcp,
        DriverKind.ModbusUdp,
        DriverKind.ModbusRtu,
        DriverKind.SiemensS7,
        DriverKind.SiemensFetchWrite,
        DriverKind.SiemensPpiOverTcp,
        DriverKind.OmronFins,
        DriverKind.OmronFinsUdp,
        DriverKind.OmronHostLinkOverTcp,
        DriverKind.OmronHostLinkCModeOverTcp,
        DriverKind.OmronCip,
        DriverKind.OmronConnectedCip,
        DriverKind.MelsecMc,
        DriverKind.MelsecMcUdp,
        DriverKind.MelsecMcAscii,
        DriverKind.MelsecMcAsciiUdp,
        DriverKind.MelsecMcR,
        DriverKind.MelsecA1E,
        DriverKind.MelsecA1EAscii,
        DriverKind.MelsecA3COverTcp,
        DriverKind.MelsecFxLinksOverTcp,
        DriverKind.MelsecFxSerialOverTcp,
        DriverKind.MelsecCip,
        DriverKind.KeyenceMc,
        DriverKind.KeyenceMcAscii,
        DriverKind.KeyenceNanoOverTcp,
        DriverKind.PanasonicMc,
        DriverKind.PanasonicMewtocolOverTcp,
        DriverKind.AllenBradleyCip,
        DriverKind.AllenBradleyConnectedCip,
        DriverKind.AllenBradleyPccc,
        DriverKind.AllenBradleySlc,
        DriverKind.BeckhoffAds,
        DriverKind.DeltaTcp,
        DriverKind.DeltaSerialOverTcp,
        DriverKind.DeltaSerialAsciiOverTcp,
        DriverKind.FatekProgramOverTcp,
        DriverKind.InovanceTcp,
        DriverKind.InovanceSerialOverTcp,
        DriverKind.InovanceEasy,
        DriverKind.InovanceConnectedCip,
        DriverKind.FujiSph,
        DriverKind.FujiSpbOverTcp,
        DriverKind.GeSrtp,
        DriverKind.LsFastEnet,
        DriverKind.LsCnetOverTcp,
        DriverKind.XinJeTcp,
        DriverKind.XinJeInternal,
        DriverKind.XinJeSerialOverTcp,
        DriverKind.YaskawaMemobusTcp,
        DriverKind.YaskawaMemobusUdp,
        DriverKind.MegMeetTcp,
        DriverKind.MegMeetSerialOverTcp,
        DriverKind.Http,
        DriverKind.TcpServer,
        DriverKind.TcpClient
    };

    public string ParameterHint
        => SelectedDevice?.Kind switch
        {
            DriverKind.ModbusTcp => "station=1, dataFormat=ABCD, addressStartWithZero=true",
            DriverKind.ModbusUdp => "station=1, dataFormat=ABCD, addressStartWithZero=true",
            DriverKind.ModbusRtu => "station=1, baudRate=9600, dataBits=8, parity=None, stopBits=One",
            DriverKind.SiemensS7 => "plcType=S1200, rack=0, slot=1, connectionType=1",
            DriverKind.SiemensFetchWrite => "No extra driver parameters. Configure PLC address and tag addresses.",
            DriverKind.SiemensPpiOverTcp => "station=2",
            DriverKind.OmronFins or DriverKind.OmronFinsUdp => "plcType=CSCJ, da1=10, sa1=20, readSplits=500",
            DriverKind.OmronHostLinkOverTcp => "plcType=CSCJ, unitNumber=0, da2=0, sa2=0, responseWaitTime=0",
            DriverKind.OmronHostLinkCModeOverTcp => "unitNumber=0",
            DriverKind.OmronCip => "No extra driver parameters. CIP tag names are used as addresses.",
            DriverKind.OmronConnectedCip => "connectionTimeoutMultiplier=1",
            DriverKind.AllenBradleyCip
                or DriverKind.AllenBradleyPccc
                or DriverKind.AllenBradleySlc
                or DriverKind.MelsecCip
                or DriverKind.InovanceConnectedCip => "No extra driver parameters. CIP/PCCC tag names are used as addresses.",
            DriverKind.AllenBradleyConnectedCip => "No extra driver parameters. Connected CIP tag names are used as addresses.",
            DriverKind.MelsecMc
                or DriverKind.MelsecMcUdp
                or DriverKind.MelsecMcAscii
                or DriverKind.MelsecMcAsciiUdp
                or DriverKind.MelsecMcR => "networkNumber=0, networkStationNumber=0, plcNumber=255, targetIOStation=1023",
            DriverKind.MelsecA1E
                or DriverKind.MelsecA1EAscii => "plcNumber=0",
            DriverKind.MelsecA3COverTcp => "station=0, format=1, sumCheck=true",
            DriverKind.MelsecFxLinksOverTcp => "station=0, format=1, sumCheck=true, waittingTime=0",
            DriverKind.MelsecFxSerialOverTcp => "isNewVersion=true, useGot=false",
            DriverKind.KeyenceMc
                or DriverKind.KeyenceMcAscii
                or DriverKind.PanasonicMc => "No extra driver parameters. MC protocol addresses such as D100, M100.",
            DriverKind.KeyenceNanoOverTcp => "station=0, useStation=false",
            DriverKind.PanasonicMewtocolOverTcp => "station=238",
            DriverKind.DeltaTcp
                or DriverKind.DeltaSerialOverTcp
                or DriverKind.DeltaSerialAsciiOverTcp => "station=1, series=Dvp, dataFormat=ABCD, addressStartWithZero=true",
            DriverKind.InovanceTcp
                or DriverKind.InovanceSerialOverTcp => "station=1, series=AM, dataFormat=ABCD, addressStartWithZero=true",
            DriverKind.XinJeTcp
                or DriverKind.XinJeSerialOverTcp
                or DriverKind.MegMeetTcp
                or DriverKind.MegMeetSerialOverTcp => "station=1, dataFormat=ABCD, addressStartWithZero=true",
            DriverKind.XinJeInternal => "station=1, dataFormat=ABCD, isStringReverse=false",
            DriverKind.BeckhoffAds => "amsPort=851, useAutoAmsNetId=true, useTagCache=true",
            DriverKind.FatekProgramOverTcp => "No extra driver parameters. Use addresses supported by the selected HSL driver.",
            DriverKind.FujiSph => "connectionId=0",
            DriverKind.FujiSpbOverTcp => "station=1",
            DriverKind.LsFastEnet => "companyId=LSIS-XGT, baseNo=0, slotNo=0",
            DriverKind.LsCnetOverTcp => "station=0",
            DriverKind.YaskawaMemobusTcp or DriverKind.YaskawaMemobusUdp => "cpuFrom=2, cpuTo=1",
            DriverKind.GeSrtp => "No extra driver parameters. Use addresses supported by the selected HSL driver.",
            DriverKind.Http => "readMethod=GET, writeMethod=POST, heartbeatPath=/health",
            DriverKind.TcpClient => "encoding=utf-8, terminator=\\n, responseTerminator=\\n",
            DriverKind.TcpServer => "encoding=utf-8, terminator=\\n, writeTerminator=\\n",
            DriverKind.Simulator => "No parameters required.",
            _ => "The selected protocol is not active."
        };

    public bool IsTcpEndpoint
        => SelectedDevice?.Kind is DriverKind.ModbusTcp
            or DriverKind.ModbusUdp
            or DriverKind.SiemensS7
            or DriverKind.SiemensFetchWrite
            or DriverKind.SiemensPpiOverTcp
            or DriverKind.OmronFins
            or DriverKind.OmronFinsUdp
            or DriverKind.OmronHostLinkOverTcp
            or DriverKind.OmronHostLinkCModeOverTcp
            or DriverKind.OmronCip
            or DriverKind.OmronConnectedCip
            or DriverKind.MelsecMc
            or DriverKind.MelsecMcUdp
            or DriverKind.MelsecMcAscii
            or DriverKind.MelsecMcAsciiUdp
            or DriverKind.MelsecMcR
            or DriverKind.MelsecA1E
            or DriverKind.MelsecA1EAscii
            or DriverKind.MelsecA3COverTcp
            or DriverKind.MelsecFxLinksOverTcp
            or DriverKind.MelsecFxSerialOverTcp
            or DriverKind.MelsecCip
            or DriverKind.KeyenceMc
            or DriverKind.KeyenceMcAscii
            or DriverKind.KeyenceNanoOverTcp
            or DriverKind.PanasonicMc
            or DriverKind.PanasonicMewtocolOverTcp
            or DriverKind.AllenBradleyCip
            or DriverKind.AllenBradleyConnectedCip
            or DriverKind.AllenBradleyPccc
            or DriverKind.AllenBradleySlc
            or DriverKind.BeckhoffAds
            or DriverKind.DeltaTcp
            or DriverKind.DeltaSerialOverTcp
            or DriverKind.DeltaSerialAsciiOverTcp
            or DriverKind.FatekProgramOverTcp
            or DriverKind.InovanceTcp
            or DriverKind.InovanceSerialOverTcp
            or DriverKind.InovanceEasy
            or DriverKind.InovanceConnectedCip
            or DriverKind.FujiSph
            or DriverKind.FujiSpbOverTcp
            or DriverKind.GeSrtp
            or DriverKind.LsFastEnet
            or DriverKind.LsCnetOverTcp
            or DriverKind.XinJeTcp
            or DriverKind.XinJeInternal
            or DriverKind.XinJeSerialOverTcp
            or DriverKind.YaskawaMemobusTcp
            or DriverKind.YaskawaMemobusUdp
            or DriverKind.MegMeetTcp
            or DriverKind.MegMeetSerialOverTcp
            or DriverKind.Http
            or DriverKind.TcpServer
            or DriverKind.TcpClient;

    public string AddressLabel
        => SelectedDevice?.Kind switch
        {
            DriverKind.ModbusRtu => "Serial Port",
            DriverKind.Http => "Base URL",
            DriverKind.TcpServer => "Bind Address",
            DriverKind.TcpClient => "Remote Address",
            _ => "IP / Address"
        };

    partial void OnSelectedDeviceChanged(EditableDeviceViewModel? oldValue, EditableDeviceViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnSelectedDevicePropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnSelectedDevicePropertyChanged;
        }

        OnPropertyChanged(nameof(ParameterHint));
        OnPropertyChanged(nameof(IsTcpEndpoint));
        OnPropertyChanged(nameof(AddressLabel));
    }

    private void OnSelectedDevicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditableDeviceViewModel.Kind))
        {
            OnPropertyChanged(nameof(ParameterHint));
            OnPropertyChanged(nameof(IsTcpEndpoint));
            OnPropertyChanged(nameof(AddressLabel));
        }
    }

    [RelayCommand]
    private void Add()
    {
        var device = new EditableDeviceViewModel
        {
            DeviceId = GenerateUniqueDeviceId("PLC"),
            Kind = DriverKind.ModbusTcp
        };
        device.ApplyDefaults();

        Devices.Add(device);
        SelectedDevice = device;
        StatusMessage = "New device added. Click Save Changes to persist.";
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var copy = SelectedDevice.Clone();
        copy.DeviceId = GenerateUniqueDeviceId($"{copy.DeviceId}.Copy");
        Devices.Add(copy);
        SelectedDevice = copy;
        StatusMessage = "Device duplicated. Click Save Changes to persist.";
    }

    [RelayCommand]
    private void Delete()
        => DeleteDevice(SelectedDevice);

    [RelayCommand]
    private void DeleteDevice(EditableDeviceViewModel? device)
    {
        if (device is null)
        {
            return;
        }

        var index = Devices.IndexOf(device);
        if (index < 0)
        {
            return;
        }

        Devices.Remove(device);
        SelectedDevice = Devices.Count == 0 ? null : Devices[Math.Clamp(index, 0, Devices.Count - 1)];
        StatusMessage = "Device deleted. Click Save Changes to persist.";
    }

    [RelayCommand]
    private void ApplyDefaults()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        SelectedDevice.ApplyDefaults();
        OnPropertyChanged(nameof(ParameterHint));
        OnPropertyChanged(nameof(IsTcpEndpoint));
        OnPropertyChanged(nameof(AddressLabel));
        StatusMessage = "Default parameters applied for the selected protocol.";
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (_driverFactory is null || SelectedDevice is null)
        {
            return;
        }

        ICommunicationDriver? driver = null;
        try
        {
            var endpoint = SelectedDevice.ToEndpoint();
            driver = _driverFactory.Create(endpoint, _settings.Communication);
            await driver.ConnectAsync();
            StatusMessage = $"Connection test succeeded for {endpoint.DeviceId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Connection test failed: {ex.Message}";
        }
        finally
        {
            if (driver is not null)
            {
                await driver.DisposeAsync();
            }
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        _settings = await _settingsStore.LoadAsync();
        LoadFromSettings(_settings);
        StatusMessage = "Devices reloaded from configuration.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_settingsStore is null)
        {
            return;
        }

        try
        {
            var devices = Devices.Select(device => device.ToEndpoint()).ToArray();
            var next = _settings with { Devices = devices };
            await _settingsStore.SaveAsync(next);
            _settings = next;
            LoadFromSettings(_settings);
            StatusMessage = "Devices saved. Runtime reload is applying now.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void LoadFromSettings(HostSettings settings)
    {
        Devices.Clear();
        foreach (var device in settings.Devices)
        {
            Devices.Add(EditableDeviceViewModel.From(device));
        }

        SelectedDevice = Devices.FirstOrDefault();
    }

    private string GenerateUniqueDeviceId(string prefix)
    {
        var existing = Devices
            .Select(device => device.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = 1;
        var candidate = $"{prefix}-{index:00}";
        while (existing.Contains(candidate))
        {
            index++;
            candidate = $"{prefix}-{index:00}";
        }

        return candidate;
    }
}
