using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Application;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.ViewModels.Tags;

public sealed partial class TagEditorViewModel : ViewModelBase
{
    private readonly ISettingsStore<HostSettings> _settingsStore;
    private HostSettings _settings;

    [ObservableProperty]
    private EditableTagViewModel? _selectedTag;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    public TagEditorViewModel()
    {
        _settingsStore = null!;
        _settings = new HostSettings();
    }

    public TagEditorViewModel(ISettingsStore<HostSettings> settingsStore)
    {
        _settingsStore = settingsStore;
        _settings = settingsStore.Current;
        LoadFromSettings(_settings);
    }

    public ObservableCollection<EditableTagViewModel> Tags { get; } = new();

    public IReadOnlyList<TagDataType> DataTypes { get; } = Enum.GetValues<TagDataType>();

    public IReadOnlyList<TagAccessMode> AccessModes { get; } = Enum.GetValues<TagAccessMode>();

    public IReadOnlyList<string> DeviceIds => _settings.Devices.Select(device => device.DeviceId).ToArray();

    [RelayCommand]
    private void Add()
    {
        var deviceId = DeviceIds.FirstOrDefault() ?? string.Empty;
        var tag = new EditableTagViewModel
        {
            Name = GenerateUniqueName("New.Tag"),
            DeviceId = deviceId,
            Address = "D0",
            DataType = TagDataType.Float64,
            Access = TagAccessMode.ReadOnly,
            ScanPeriodMs = 250
        };

        Tags.Add(tag);
        SelectedTag = tag;
        StatusMessage = "New tag added. Click Save to persist changes.";
    }

    [RelayCommand]
    private void Duplicate()
    {
        if (SelectedTag is null)
        {
            return;
        }

        var copy = SelectedTag.Clone();
        copy.Name = GenerateUniqueName($"{copy.Name}.Copy");
        Tags.Add(copy);
        SelectedTag = copy;
        StatusMessage = "Tag duplicated. Click Save to persist changes.";
    }

    [RelayCommand]
    private void Delete()
        => DeleteTag(SelectedTag);

    [RelayCommand]
    private void DeleteTag(EditableTagViewModel? tag)
    {
        if (tag is null)
        {
            return;
        }

        var index = Tags.IndexOf(tag);
        if (index < 0)
        {
            return;
        }

        Tags.Remove(tag);
        SelectedTag = Tags.Count == 0 ? null : Tags[Math.Clamp(index, 0, Tags.Count - 1)];
        StatusMessage = "Tag deleted. Click Save to persist changes.";
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
        StatusMessage = "Tags reloaded from configuration.";
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
            var tags = Tags.Select(tag => tag.ToDefinition()).ToArray();
            var next = _settings with { Tags = tags };
            await _settingsStore.SaveAsync(next);
            _settings = next;
            StatusMessage = "Tags saved. Runtime reload is applying now.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
    }

    private void LoadFromSettings(HostSettings settings)
    {
        Tags.Clear();
        foreach (var tag in settings.Tags)
        {
            Tags.Add(EditableTagViewModel.From(tag));
        }

        SelectedTag = Tags.FirstOrDefault();
    }

    private string GenerateUniqueName(string prefix)
    {
        var existing = Tags
            .Select(tag => tag.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var index = 1;
        var candidate = $"{prefix}.{index}";
        while (existing.Contains(candidate))
        {
            index++;
            candidate = $"{prefix}.{index}";
        }

        return candidate;
    }
}
