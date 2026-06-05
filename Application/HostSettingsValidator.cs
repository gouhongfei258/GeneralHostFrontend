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
