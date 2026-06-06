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
        DriverKind.ModbusRtu,
        DriverKind.SiemensS7,
        DriverKind.OmronFins
    };

    public string ParameterHint
        => SelectedDevice?.Kind switch
        {
            DriverKind.ModbusTcp => "station=1, dataFormat=ABCD, addressStartWithZero=true",
            DriverKind.ModbusRtu => "station=1, baudRate=9600, dataBits=8, parity=None, stopBits=One",
            DriverKind.SiemensS7 => "plcType=S1200, rack=0, slot=1",
            DriverKind.OmronFins => "plcType=CSCJ, da1=10, sa1=20",
            DriverKind.Simulator => "No parameters required.",
            _ => "Only Simulator, Modbus TCP/RTU, Siemens S7 and Omron FINS are active."
        };

    public bool IsTcpEndpoint
        => SelectedDevice?.Kind is DriverKind.ModbusTcp or DriverKind.SiemensS7 or DriverKind.OmronFins;

    public string AddressLabel
        => SelectedDevice?.Kind is DriverKind.ModbusRtu
            ? "Serial Port"
            : "IP / Address";

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
