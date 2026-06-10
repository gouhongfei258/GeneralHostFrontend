using GeneralHostFrontend.Core.Settings;

namespace GeneralHostFrontend.Application;

public sealed class HostSettingsValidator : ISettingsValidator<HostSettings>
{
    public SettingsValidationResult Validate(HostSettings settings)
    {
        var messages = new List<ValidationMessage>();

        if (settings.Communication.MaxConcurrentOperations <= 0)
        {
            messages.Add(new ValidationMessage(nameof(settings.Communication.MaxConcurrentOperations), "Max concurrent operations must be greater than zero."));
        }

        if (settings.Communication.MaxOperationsPerSecond <= 0)
        {
            messages.Add(new ValidationMessage(nameof(settings.Communication.MaxOperationsPerSecond), "Max operations per second must be greater than zero."));
        }

        var deviceIds = settings.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .Select(device => device.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (settings.Devices.Count == 0)
        {
            messages.Add(new ValidationMessage(nameof(settings.Devices), "At least one device must be configured."));
        }

        foreach (var duplicate in settings.Devices
            .Where(device => !string.IsNullOrWhiteSpace(device.DeviceId))
            .GroupBy(device => device.DeviceId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            messages.Add(new ValidationMessage("Devices.DeviceId", $"Device id '{duplicate.Key}' is duplicated."));
        }

        foreach (var device in settings.Devices)
        {
            if (string.IsNullOrWhiteSpace(device.DeviceId))
            {
                messages.Add(new ValidationMessage("Devices.DeviceId", "Device id cannot be empty."));
            }

            if (string.IsNullOrWhiteSpace(device.Address))
            {
                messages.Add(new ValidationMessage(device.DeviceId, "Device address cannot be empty."));
            }

            if (device.Port is < 0 or > 65535)
            {
                messages.Add(new ValidationMessage(device.DeviceId, "TCP port must be between 0 and 65535."));
            }

            if (device.Kind is not (
                Core.Communication.DriverKind.Simulator
                or Core.Communication.DriverKind.ModbusTcp
                or Core.Communication.DriverKind.ModbusUdp
                or Core.Communication.DriverKind.ModbusRtu
                or Core.Communication.DriverKind.SiemensS7
                or Core.Communication.DriverKind.SiemensFetchWrite
                or Core.Communication.DriverKind.OmronFins
                or Core.Communication.DriverKind.OmronFinsUdp
                or Core.Communication.DriverKind.OmronHostLinkOverTcp
                or Core.Communication.DriverKind.OmronHostLinkCModeOverTcp
                or Core.Communication.DriverKind.OmronCip
                or Core.Communication.DriverKind.OmronConnectedCip
                or Core.Communication.DriverKind.MelsecMc
                or Core.Communication.DriverKind.MelsecMcUdp
                or Core.Communication.DriverKind.MelsecMcAscii
                or Core.Communication.DriverKind.MelsecMcAsciiUdp
                or Core.Communication.DriverKind.MelsecMcR
                or Core.Communication.DriverKind.MelsecA1E
                or Core.Communication.DriverKind.MelsecA1EAscii
                or Core.Communication.DriverKind.MelsecA3COverTcp
                or Core.Communication.DriverKind.MelsecFxLinksOverTcp
                or Core.Communication.DriverKind.MelsecFxSerialOverTcp
                or Core.Communication.DriverKind.MelsecCip
                or Core.Communication.DriverKind.KeyenceMc
                or Core.Communication.DriverKind.KeyenceMcAscii
                or Core.Communication.DriverKind.KeyenceNanoOverTcp
                or Core.Communication.DriverKind.PanasonicMc
                or Core.Communication.DriverKind.PanasonicMewtocolOverTcp
                or Core.Communication.DriverKind.AllenBradleyCip
                or Core.Communication.DriverKind.AllenBradleyConnectedCip
                or Core.Communication.DriverKind.AllenBradleyPccc
                or Core.Communication.DriverKind.AllenBradleySlc
                or Core.Communication.DriverKind.BeckhoffAds
                or Core.Communication.DriverKind.DeltaTcp
                or Core.Communication.DriverKind.DeltaSerialOverTcp
                or Core.Communication.DriverKind.DeltaSerialAsciiOverTcp
                or Core.Communication.DriverKind.FatekProgramOverTcp
                or Core.Communication.DriverKind.InovanceTcp
                or Core.Communication.DriverKind.InovanceSerialOverTcp
                or Core.Communication.DriverKind.InovanceEasy
                or Core.Communication.DriverKind.InovanceConnectedCip
                or Core.Communication.DriverKind.FujiSph
                or Core.Communication.DriverKind.FujiSpbOverTcp
                or Core.Communication.DriverKind.GeSrtp
                or Core.Communication.DriverKind.LsFastEnet
                or Core.Communication.DriverKind.LsCnetOverTcp
                or Core.Communication.DriverKind.XinJeTcp
                or Core.Communication.DriverKind.XinJeInternal
                or Core.Communication.DriverKind.XinJeSerialOverTcp
                or Core.Communication.DriverKind.YaskawaMemobusTcp
                or Core.Communication.DriverKind.YaskawaMemobusUdp
                or Core.Communication.DriverKind.MegMeetTcp
                or Core.Communication.DriverKind.MegMeetSerialOverTcp
                or Core.Communication.DriverKind.SiemensPpiOverTcp
                or Core.Communication.DriverKind.Http
                or Core.Communication.DriverKind.TcpServer
                or Core.Communication.DriverKind.TcpClient))
            {
                messages.Add(new ValidationMessage(device.DeviceId, $"Driver '{device.Kind}' is not implemented yet."));
            }
        }

        foreach (var duplicate in settings.Tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => tag.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1))
        {
            messages.Add(new ValidationMessage("Tags.Name", $"Tag name '{duplicate.Key}' is duplicated."));
        }

        foreach (var tag in settings.Tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                messages.Add(new ValidationMessage("Tags.Name", "Tag name cannot be empty."));
            }

            if (string.IsNullOrWhiteSpace(tag.DeviceId))
            {
                messages.Add(new ValidationMessage(tag.Name, "Device id cannot be empty."));
            }
            else if (!deviceIds.Contains(tag.DeviceId))
            {
                messages.Add(new ValidationMessage(tag.Name, $"Device id '{tag.DeviceId}' is not configured."));
            }

            if (string.IsNullOrWhiteSpace(tag.Address))
            {
                messages.Add(new ValidationMessage(tag.Name, "Address cannot be empty."));
            }

            if (tag.ScanPeriod < TimeSpan.FromMilliseconds(20))
            {
                messages.Add(new ValidationMessage(tag.Name, "Scan period below 20 ms is blocked to protect field devices."));
            }

            if (tag.LowerLimit.HasValue && tag.UpperLimit.HasValue && tag.LowerLimit > tag.UpperLimit)
            {
                messages.Add(new ValidationMessage(tag.Name, "Lower limit cannot be greater than upper limit."));
            }
        }

        return messages.Count == 0
            ? SettingsValidationResult.Success
            : new SettingsValidationResult(false, messages);
    }
}
