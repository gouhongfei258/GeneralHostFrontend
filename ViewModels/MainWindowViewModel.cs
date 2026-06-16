using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Application;
using GeneralHostFrontend.Core.Logging;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Core.Tags;
using GeneralHostFrontend.ViewModels.Database;
using GeneralHostFrontend.ViewModels.Dashboard;
using GeneralHostFrontend.ViewModels.Devices;
using GeneralHostFrontend.ViewModels.Hmi;
using GeneralHostFrontend.ViewModels.Logic;
using GeneralHostFrontend.ViewModels.Tags;
using GeneralHostFrontend.Views.Database;
using GeneralHostFrontend.Views.Devices;
using GeneralHostFrontend.Views.Hmi;
using GeneralHostFrontend.Views.Logic;
using GeneralHostFrontend.Views.Tags;

namespace GeneralHostFrontend.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly ISettingsStore<HostSettings> _settingsStore;
    private readonly HostRuntime _runtime;
    private readonly ITagDataPipeline _pipeline;
    private readonly ILiveLogService _logs;
    private readonly Func<DatabaseViewerViewModel> _databaseViewerFactory;
    private readonly Func<DeviceEditorViewModel> _deviceEditorFactory;
    private readonly Func<LogicEditorViewModel> _logicEditorFactory;
    private readonly Func<TagEditorViewModel> _tagEditorFactory;
    private readonly Func<HmiEditorViewModel> _hmiEditorFactory;
    private readonly Func<HmiRuntimeViewModel> _hmiRuntimeFactory;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _subscriptions = new();
    private readonly Dictionary<string, TagValueViewModel> _tagIndex = new(StringComparer.OrdinalIgnoreCase);

    [ObservableProperty]
    private string _runtimeState = "Stopped";

    [ObservableProperty]
    private string _currentWorkspace = "Operator";

    [ObservableProperty]
    private string _selectedLogLevel = "Information";

    [ObservableProperty]
    private string? _logKeyword;

    public MainWindowViewModel()
    {
        _settingsStore = null!;
        _runtime = null!;
        _pipeline = null!;
        _logs = null!;
        _databaseViewerFactory = null!;
        _deviceEditorFactory = null!;
        _logicEditorFactory = null!;
        _tagEditorFactory = null!;
        _hmiEditorFactory = null!;
        _hmiRuntimeFactory = null!;
    }

    public MainWindowViewModel(
        ISettingsStore<HostSettings> settingsStore,
        HostRuntime runtime,
        ITagDataPipeline pipeline,
        ILiveLogService logs,
        Func<DatabaseViewerViewModel> databaseViewerFactory,
        Func<DeviceEditorViewModel> deviceEditorFactory,
        Func<LogicEditorViewModel> logicEditorFactory,
        Func<TagEditorViewModel> tagEditorFactory,
        Func<HmiEditorViewModel> hmiEditorFactory,
        Func<HmiRuntimeViewModel> hmiRuntimeFactory)
    {
        StartupTrace.Write("MainWindowViewModel constructor begin.");
        _settingsStore = settingsStore;
        _runtime = runtime;
        _pipeline = pipeline;
        _logs = logs;
        _databaseViewerFactory = databaseViewerFactory;
        _deviceEditorFactory = deviceEditorFactory;
        _logicEditorFactory = logicEditorFactory;
        _tagEditorFactory = tagEditorFactory;
        _hmiEditorFactory = hmiEditorFactory;
        _hmiRuntimeFactory = hmiRuntimeFactory;
        RuntimeState = _runtime.State.ToString();
        StartupTrace.Write("MainWindowViewModel dependencies assigned.");

        ApplyTags(_settingsStore.Current.Tags);
        StartupTrace.Write("MainWindowViewModel tags loaded.");

        foreach (var entry in _logs.Snapshot(new LogFilter(HostLogLevel.Information), 200))
        {
            Logs.Add(LogEntryViewModel.From(entry));
        }
        StartupTrace.Write("MainWindowViewModel log snapshot loaded.");

        _subscriptions.Add(ObserveTagsAsync(_stop.Token));
        StartupTrace.Write("MainWindowViewModel tag subscription started.");
        _subscriptions.Add(ObserveLogsAsync(_stop.Token));
        StartupTrace.Write("MainWindowViewModel log subscription started.");
        _subscriptions.Add(ObserveSettingsAsync(_stop.Token));
        StartupTrace.Write("MainWindowViewModel settings subscription started.");
        StartupTrace.Write("MainWindowViewModel constructor completed.");
    }

    public ObservableCollection<TagValueViewModel> Tags { get; } = new();

    public ObservableCollection<LogEntryViewModel> Logs { get; } = new();

    public IReadOnlyList<string> WorkspaceOptions { get; } = new[] { "Operator", "Maintenance", "Engineering", "Administration" };

    public IReadOnlyList<string> LogLevelOptions { get; } = new[] { "Trace", "Debug", "Information", "Warning", "Error", "Critical" };

    public bool CanForceIo => CurrentWorkspace is "Maintenance" or "Engineering" or "Administration";

    public bool CanEditSettings => CurrentWorkspace is "Engineering" or "Administration";

    [RelayCommand]
    private async Task StartAsync()
    {
        await _runtime.StartAsync();
        RuntimeState = _runtime.State.ToString();
    }

    [RelayCommand]
    private async Task StopAsync()
    {
        await _runtime.StopAsync();
        RuntimeState = _runtime.State.ToString();
    }

    [RelayCommand]
    private void OpenDatabaseViewer()
    {
        if (_databaseViewerFactory is null)
        {
            return;
        }

        var window = new DatabaseViewerWindow
        {
            DataContext = _databaseViewerFactory()
        };
        window.Show();
    }

    [RelayCommand]
    private void OpenDeviceEditor()
    {
        if (_deviceEditorFactory is null)
        {
            return;
        }

        var window = new DeviceEditorWindow
        {
            DataContext = _deviceEditorFactory()
        };
        window.Show();
    }

    [RelayCommand]
    private void OpenTagEditor()
    {
        if (_tagEditorFactory is null)
        {
            return;
        }

        var window = new TagEditorWindow
        {
            DataContext = _tagEditorFactory()
        };
        window.Show();
    }

    [RelayCommand]
    private void OpenLogicEditor()
    {
        if (_logicEditorFactory is null)
        {
            return;
        }

        var window = new LogicEditorWindow
        {
            DataContext = _logicEditorFactory()
        };
        window.Show();
    }

    [RelayCommand]
    private void OpenHmiEditor()
    {
        if (_hmiEditorFactory is null)
        {
            return;
        }

        var window = new HmiEditorWindow
        {
            DataContext = _hmiEditorFactory(),
            Width = 1180,
            Height = 720,
            MinWidth = 980,
            MinHeight = 620,
            WindowState = Avalonia.Controls.WindowState.Normal
        };
        window.Show();
    }

    [RelayCommand]
    private void OpenHmiRuntime()
    {
        if (_hmiRuntimeFactory is null)
        {
            return;
        }

        var window = new HmiRuntimeWindow
        {
            DataContext = _hmiRuntimeFactory(),
            Width = 1280,
            Height = 760,
            MinWidth = 900,
            MinHeight = 560,
            WindowState = Avalonia.Controls.WindowState.Normal
        };
        window.Show();
    }

    partial void OnCurrentWorkspaceChanged(string value)
    {
        OnPropertyChanged(nameof(CanForceIo));
        OnPropertyChanged(nameof(CanEditSettings));
    }

    private async Task ObserveTagsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_pipeline is null)
            {
                return;
            }

            await foreach (TagSampleBatch batch in _pipeline.SubscribeBatchesAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var sample in batch.Samples)
                    {
                        if (_tagIndex.TryGetValue(sample.TagName, out var item))
                        {
                            item.Update(sample);
                        }
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ObserveSettingsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_settingsStore is null)
            {
                return;
            }

            var isInitialSettings = true;
            await foreach (var settings in _settingsStore.WatchAsync(cancellationToken))
            {
                await _runtime.ApplySettingsAsync(settings, cancellationToken);

                if (isInitialSettings)
                {
                    isInitialSettings = false;
                    continue;
                }

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyTags(settings.Tags);
                    RuntimeState = _runtime.State.ToString();
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task ObserveLogsAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_logs is null)
            {
                return;
            }

            await foreach (var entry in _logs.WatchAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!MatchesLogFilter(entry))
                    {
                        return;
                    }

                    Logs.Add(LogEntryViewModel.From(entry));
                    while (Logs.Count > 1_000)
                    {
                        Logs.RemoveAt(0);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool MatchesLogFilter(LogEntry entry)
    {
        var level = Enum.TryParse<HostLogLevel>(SelectedLogLevel, out var parsedLevel)
            ? parsedLevel
            : HostLogLevel.Information;

        if (entry.Level < level)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(LogKeyword)
            || entry.Source.Contains(LogKeyword, StringComparison.OrdinalIgnoreCase)
            || entry.Message.Contains(LogKeyword, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyTags(IReadOnlyList<TagDefinition> definitions)
    {
        var nextNames = definitions
            .Select(tag => tag.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (var index = Tags.Count - 1; index >= 0; index--)
        {
            var item = Tags[index];
            if (!nextNames.Contains(item.Name))
            {
                _tagIndex.Remove(item.Name);
                Tags.RemoveAt(index);
            }
        }

        for (var index = 0; index < definitions.Count; index++)
        {
            var tag = definitions[index];
            if (!_tagIndex.TryGetValue(tag.Name, out var item))
            {
                item = new TagValueViewModel
                {
                    Name = tag.Name,
                    Unit = tag.EngineeringUnit,
                    Timestamp = DateTimeOffset.Now
                };
                _tagIndex[tag.Name] = item;
                Tags.Insert(Math.Min(index, Tags.Count), item);
            }
            else
            {
                item.Unit = tag.EngineeringUnit;
                item.Name = tag.Name;
                var currentIndex = Tags.IndexOf(item);
                if (currentIndex >= 0 && currentIndex != index)
                {
                    Tags.Move(currentIndex, Math.Min(index, Tags.Count - 1));
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        await WaitForSubscriptionsAsync();

        if (_runtime is not null)
        {
            await _runtime.StopAsync();
        }

        _stop.Dispose();
    }

    private async Task WaitForSubscriptionsAsync()
    {
        if (_subscriptions.Count == 0)
        {
            return;
        }

        try
        {
            var all = Task.WhenAll(_subscriptions);
            var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(1)));
            if (completed == all)
            {
                await all;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException)
        {
        }
    }
}
