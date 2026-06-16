using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Core.Hmi;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Tags;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed partial class HmiRuntimeViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IHmiPageStore _pageStore;
    private readonly ITagDataPipeline _pipeline;
    private readonly ITagWriteService _tagWriteService;
    private readonly IHmiResourceStore? _resourceStore;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, HmiPageDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TagValue> _latestTags = new(StringComparer.OrdinalIgnoreCase);
    private Task? _subscription;

    [ObservableProperty]
    private string _pageId = HmiPageDefaults.MainPageId;

    [ObservableProperty]
    private string _pageName = HmiPageDefaults.MainPageName;

    [ObservableProperty]
    private double _pageWidth = HmiPageDefaults.Width;

    [ObservableProperty]
    private double _pageHeight = HmiPageDefaults.Height;

    [ObservableProperty]
    private string _pageBackground = HmiPageDefaults.Background;

    [ObservableProperty]
    private string _statusMessage = "Runtime ready.";

    public HmiRuntimeViewModel()
    {
        _pageStore = null!;
        _pipeline = null!;
        _tagWriteService = null!;
        ApplyDocument(HmiPageDocument.CreateDefault());
    }

    public HmiRuntimeViewModel(
        IHmiPageStore pageStore,
        ITagDataPipeline pipeline,
        ITagWriteService tagWriteService,
        IHmiResourceStore resourceStore)
    {
        _pageStore = pageStore;
        _pipeline = pipeline;
        _tagWriteService = tagWriteService;
        _resourceStore = resourceStore;
        HmiWidgetViewModel.SetResourceResolver(resourceStore.ResolvePath);
    }

    public ObservableCollection<HmiWidgetViewModel> Widgets { get; } = new();

    public async Task LoadAsync(string pageId, CancellationToken cancellationToken = default)
    {
        if (_pageStore is null)
        {
            return;
        }

        await NavigateAsync(pageId, cancellationToken);
        _subscription ??= ObserveTagsAsync(_stop.Token);
    }

    public void LoadDocument(HmiPageDocument document)
    {
        _openDocuments[document.Id] = document;
        ApplyDocument(document);
        _subscription ??= ObserveTagsAsync(_stop.Token);
    }

    public async Task NavigateAsync(string pageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId) || _pageStore is null)
        {
            return;
        }

        var document = await LoadPageDocumentAsync(pageId.Trim(), cancellationToken);
        await Dispatcher.UIThread.InvokeAsync(() => ApplyDocument(document), DispatcherPriority.Normal, cancellationToken);
        StatusMessage = $"Opened {document.Name}.";
    }

    [RelayCommand]
    private async Task ExecuteWidgetAsync(HmiWidgetViewModel? widget)
    {
        if (widget is null)
        {
            return;
        }

        var definition = widget.ToDefinition();
        var evt = definition.Events?.FirstOrDefault(candidate =>
            string.Equals(candidate.EventName, HmiEventNames.Click, StringComparison.OrdinalIgnoreCase));
        if (evt is null || evt.Actions.Count == 0)
        {
            await ExecuteFallbackActionAsync(widget, _stop.Token);
            return;
        }

        foreach (var action in evt.Actions)
        {
            await ExecuteWidgetActionAsync(widget, action, _stop.Token);
        }
    }

    private async Task ExecuteWidgetActionAsync(
        HmiWidgetViewModel widget,
        HmiActionDefinition action,
        CancellationToken cancellationToken)
    {
        if (action.Kind is not HmiActionKind.WriteTag
            || action.Parameters.TryGetValue(HmiActionParameterNames.TagName, out var tagName) && !string.IsNullOrWhiteSpace(tagName)
            || string.IsNullOrWhiteSpace(widget.CommandTag))
        {
            await ExecuteAsync(action, cancellationToken);
            return;
        }

        var parameters = new Dictionary<string, string>(action.Parameters, StringComparer.OrdinalIgnoreCase)
        {
            [HmiActionParameterNames.TagName] = widget.CommandTag
        };

        await ExecuteAsync(action with { Parameters = parameters }, cancellationToken);
    }

    public async Task ExecuteAsync(HmiActionDefinition action, CancellationToken cancellationToken = default)
    {
        switch (action.Kind)
        {
            case HmiActionKind.WriteTag:
                await ExecuteWriteTagAsync(action, cancellationToken);
                break;
            case HmiActionKind.NavigatePage:
                if (TryGetParameter(action, HmiActionParameterNames.PageId, out var pageId))
                {
                    await NavigateAsync(pageId, cancellationToken);
                }
                break;
            case HmiActionKind.SetProperty:
                ExecuteSetProperty(action);
                break;
            case HmiActionKind.Delay:
                var delay = TryGetParameter(action, HmiActionParameterNames.DelayMilliseconds, out var text)
                    && int.TryParse(text, out var milliseconds)
                    ? Math.Max(0, milliseconds)
                    : 0;
                await Task.Delay(delay, cancellationToken);
                break;
        }
    }

    private async Task ExecuteFallbackActionAsync(HmiWidgetViewModel widget, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(widget.CommandTag))
        {
            StatusMessage = $"{widget.Text} has no action.";
            return;
        }

        var result = await _tagWriteService.WriteAsync(widget.CommandTag, true, cancellationToken);
        StatusMessage = result.Message;
    }

    private async Task ExecuteWriteTagAsync(HmiActionDefinition action, CancellationToken cancellationToken)
    {
        if (!TryGetParameter(action, HmiActionParameterNames.TagName, out var tagName))
        {
            StatusMessage = "WriteTag action is missing a tag name.";
            return;
        }

        TryGetParameter(action, HmiActionParameterNames.Value, out var value);
        var result = await _tagWriteService.WriteAsync(tagName, value, cancellationToken);
        StatusMessage = result.Message;
    }

    private void ExecuteSetProperty(HmiActionDefinition action)
    {
        if (!TryGetParameter(action, HmiActionParameterNames.TargetWidgetId, out var targetWidgetId)
            || !TryGetParameter(action, HmiActionParameterNames.PropertyName, out var propertyName)
            || !TryGetParameter(action, HmiActionParameterNames.Value, out var value))
        {
            StatusMessage = "SetProperty action is missing a parameter.";
            return;
        }

        var widget = Widgets.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, targetWidgetId, StringComparison.OrdinalIgnoreCase));
        if (widget is null)
        {
            StatusMessage = $"Widget '{targetWidgetId}' was not found.";
            return;
        }

        widget.SetPropertyValue(propertyName, value);
        StatusMessage = $"Updated {widget.Title}.";
    }

    private async Task<HmiPageDocument> LoadPageDocumentAsync(string pageId, CancellationToken cancellationToken)
    {
        if (_openDocuments.TryGetValue(pageId, out var document))
        {
            return document;
        }

        document = await _pageStore.LoadAsync(pageId, cancellationToken);
        _openDocuments[document.Id] = document;
        return document;
    }

    private async Task ObserveTagsAsync(CancellationToken cancellationToken)
    {
        if (_pipeline is null)
        {
            return;
        }

        try
        {
            await foreach (TagSampleBatch batch in _pipeline.SubscribeBatchesAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var sample in batch.Samples)
                    {
                        _latestTags[sample.TagName] = sample;
                        ApplyTagValue(sample);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyDocument(HmiPageDocument document)
    {
        PageId = document.Id;
        PageName = document.Name;
        PageWidth = document.Width;
        PageHeight = document.Height;
        PageBackground = document.Background;
        Widgets.Clear();

        foreach (var definition in document.Widgets.OrderBy(widget => widget.ZIndex))
        {
            var widget = new HmiWidgetViewModel(definition);
            foreach (var sample in _latestTags.Values)
            {
                if (widget.IsBoundTo(sample.TagName))
                {
                    widget.ApplyValue(sample.TagName, sample.DisplayValue, sample.Value);
                }
            }

            Widgets.Add(widget);
        }
    }

    private void ApplyTagValue(TagValue sample)
    {
        foreach (var widget in Widgets)
        {
            if (widget.IsBoundTo(sample.TagName))
            {
                widget.ApplyValue(sample.TagName, sample.DisplayValue, sample.Value);
            }
        }
    }

    private static bool TryGetParameter(HmiActionDefinition action, string name, out string value)
    {
        if (action.Parameters.TryGetValue(name, out value!)
            && !string.IsNullOrWhiteSpace(value))
        {
            value = value.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_stop.IsCancellationRequested)
        {
            await _stop.CancelAsync();
        }

        if (_subscription is not null)
        {
            try
            {
                await _subscription;
            }
            catch (OperationCanceledException)
            {
            }
        }

        _stop.Dispose();
    }
}
