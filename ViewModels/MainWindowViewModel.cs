using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Application;
using GeneralHostFrontend.Core.Logging;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.ViewModels.Database;
using GeneralHostFrontend.ViewModels.Dashboard;
using GeneralHostFrontend.ViewModels.Tags;
using GeneralHostFrontend.Views.Database;
using GeneralHostFrontend.Views.Tags;

namespace GeneralHostFrontend.ViewModels;

public partial class MainWindowViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly HostSettings _settings;
    private readonly HostRuntime _runtime;
    private readonly ITagDataPipeline _pipeline;
    private readonly ILiveLogService _logs;
    private readonly Func<DatabaseViewerViewModel> _databaseViewerFactory;
    private readonly Func<TagEditorViewModel> _tagEditorFactory;
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
        _settings = new HostSettings();
        _runtime = null!;
        _pipeline = null!;
        _logs = null!;
        _databaseViewerFactory = null!;
        _tagEditorFactory = null!;
    }

    public MainWindowViewModel(
        HostSettings settings,
        HostRuntime runtime,
        ITagDataPipeline pipeline,
        ILiveLogService logs,
        Func<DatabaseViewerViewModel> databaseViewerFactory,
        Func<TagEditorViewModel> tagEditorFactory)
    {
        StartupTrace.Write("MainWindowViewModel constructor begin.");
        _settings = settings;
        _runtime = runtime;
        _pipeline = pipeline;
        _logs = logs;
        _databaseViewerFactory = databaseViewerFactory;
        _tagEditorFactory = tagEditorFactory;
        RuntimeState = _runtime.State.ToString();
        StartupTrace.Write("MainWindowViewModel dependencies assigned.");

        foreach (var tag in _settings.Tags)
        {
            var item = new TagValueViewModel
            {
                Name = tag.Name,
                Unit = tag.EngineeringUnit,
                Timestamp = DateTimeOffset.Now
            };

            _tagIndex[tag.Name] = item;
            Tags.Add(item);
        }
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
