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
                or Core.Communication.DriverKind.ModbusRtu
                or Core.Communication.DriverKind.SiemensS7
                or Core.Communication.DriverKind.OmronFins))
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
