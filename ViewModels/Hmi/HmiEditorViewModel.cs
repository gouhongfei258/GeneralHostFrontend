using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Core.Hmi;
using GeneralHostFrontend.Core.Pipelines;
using GeneralHostFrontend.Core.Settings;
using GeneralHostFrontend.Core.Tags;
using GeneralHostFrontend.Application;

namespace GeneralHostFrontend.ViewModels.Hmi;

public sealed partial class HmiEditorViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly IHmiPageStore _pageStore;
    private readonly ITagDataPipeline _pipeline;
    private readonly IHmiWidgetCatalog _catalog;
    private readonly ITagWriteService _tagWriteService;
    private readonly IHmiResourceStore? _resourceStore;
    private readonly IHmiTemplateStore? _templateStore;
    private readonly ISettingsStore<HostSettings>? _settingsStore;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task? _subscription;
    private readonly Dictionary<string, HmiPageDocument> _openDocuments = new(StringComparer.OrdinalIgnoreCase);
    private List<HmiWidgetDefinition> _clipboard = new();

    [ObservableProperty]
    private string _pageName = HmiPageDefaults.MainPageName;

    [ObservableProperty]
    private string _pageId = HmiPageDefaults.MainPageId;

    [ObservableProperty]
    private string? _selectedPageId = HmiPageDefaults.MainPageId;

    [ObservableProperty]
    private double _pageWidth = HmiPageDefaults.Width;

    [ObservableProperty]
    private double _pageHeight = HmiPageDefaults.Height;

    [ObservableProperty]
    private string _pageBackground = HmiPageDefaults.Background;

    [ObservableProperty]
    private bool _isGridVisible = true;

    [ObservableProperty]
    private bool _snapToGrid = true;

    [ObservableProperty]
    private double _gridSize = 20;

    [ObservableProperty]
    private HmiWidgetViewModel? _selectedWidget;

    [ObservableProperty]
    private bool _isRunMode;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private bool _isPageOperationInProgress;

    public bool CanRunPageOperation => !IsPageOperationInProgress;

    public HmiEditorViewModel()
    {
        _pageStore = null!;
        _pipeline = null!;
        _catalog = new DefaultHmiWidgetCatalog();
        _tagWriteService = null!;
        _resourceStore = null;
        _templateStore = null;
        RuntimePreview = new HmiRuntimeViewModel();
        LoadCatalog();
        AddDefaultDesignItems();
    }

    public HmiEditorViewModel(
        IHmiPageStore pageStore,
        ITagDataPipeline pipeline,
        IHmiWidgetCatalog catalog,
        ITagWriteService tagWriteService,
        IHmiResourceStore resourceStore,
        IHmiTemplateStore templateStore,
        ISettingsStore<HostSettings> settingsStore)
    {
        _pageStore = pageStore;
        _pipeline = pipeline;
        _catalog = catalog;
        _tagWriteService = tagWriteService;
        _resourceStore = resourceStore;
        _templateStore = templateStore;
        _settingsStore = settingsStore;
        HmiWidgetViewModel.SetResourceResolver(resourceStore.ResolvePath);
        RuntimePreview = new HmiRuntimeViewModel(pageStore, pipeline, tagWriteService, resourceStore);
        LoadCatalog();
        LoadTagOptions();
        _ = LoadAsync();
        _ = LoadResourcesAsync();
        _ = LoadTemplatesAsync();
        _subscription = ObserveTagsAsync(_stop.Token);
    }

    public ObservableCollection<HmiWidgetViewModel> Widgets { get; } = new();

    public ObservableCollection<string> PageIds { get; } = new();

    public ObservableCollection<HmiWidgetViewModel> SelectedWidgets { get; } = new();

    public ObservableCollection<HmiGridLineViewModel> GridLines { get; } = new();

    public ObservableCollection<HmiToolboxItemViewModel> ToolboxItems { get; } = new();

    public ObservableCollection<HmiPropertyEditorViewModel> PropertyEditors { get; } = new();

    public ObservableCollection<HmiBindingEditorViewModel> BindingEditors { get; } = new();

    public ObservableCollection<string> TagOptions { get; } = new();

    public ObservableCollection<HmiResourceViewModel> Resources { get; } = new();

    public ObservableCollection<HmiTemplateViewModel> Templates { get; } = new();

    public IReadOnlyList<HmiWidgetKind> WidgetKinds { get; } = Enum.GetValues<HmiWidgetKind>();

    public HmiRuntimeViewModel RuntimePreview { get; }

    [ObservableProperty]
    private HmiWidgetKind _selectedWidgetKind = HmiWidgetKind.ValueText;

    [ObservableProperty]
    private HmiToolboxItemViewModel? _selectedToolboxItem;

    [ObservableProperty]
    private HmiResourceViewModel? _selectedResource;

    [ObservableProperty]
    private HmiTemplateViewModel? _selectedTemplate;

    public bool HasSelectedWidget => SelectedWidget is not null;

    public bool HasMultipleSelectedWidgets => SelectedWidgets.Count > 1;

    public bool IsEditMode => !IsRunMode;

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_pageStore is null)
        {
            return;
        }

        await RefreshPageListAsync();
        var document = await _pageStore.LoadAsync(PageId);
        _openDocuments[document.Id] = document;
        await Dispatcher.UIThread.InvokeAsync(() => ApplyDocument(document));
        StatusMessage = $"Loaded {document.Name}.";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_pageStore is null)
        {
            return;
        }

        var document = ToDocument();
        await _pageStore.SaveAsync(document);
        _openDocuments[document.Id] = document;
        await RefreshPageListAsync();
        StatusMessage = $"Saved {document.Name}.";
    }

    [RelayCommand]
    private async Task SwitchPageAsync(string? pageId)
    {
        if (_pageStore is null
            || IsPageOperationInProgress
            || string.IsNullOrWhiteSpace(pageId)
            || string.Equals(pageId, PageId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            IsPageOperationInProgress = true;
            CacheCurrentDocument();
            var document = await LoadPageDocumentAsync(pageId);
            await Dispatcher.UIThread.InvokeAsync(() => ApplyDocument(document));
            StatusMessage = $"Switched to {document.Name}.";
        }
        finally
        {
            IsPageOperationInProgress = false;
        }
    }

    partial void OnSelectedPageIdChanged(string? value)
    {
        if (!IsPageOperationInProgress
            && !string.IsNullOrWhiteSpace(value)
            && !string.Equals(value, PageId, StringComparison.OrdinalIgnoreCase))
        {
            SwitchPageCommand.Execute(value);
        }
    }

    [RelayCommand]
    private async Task NewPageAsync()
    {
        if (_pageStore is null)
        {
            return;
        }

        try
        {
            IsPageOperationInProgress = true;
            CacheCurrentDocument();
            var baseId = "page";
            var index = 1;
            var existing = (await _pageStore.ListAsync(_stop.Token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var id = $"{baseId}-{index}";
            while (existing.Contains(id))
            {
                index++;
                id = $"{baseId}-{index}";
            }

            var document = HmiPageDocument.CreateDefault(id, $"Page {index}") with
            {
                Widgets = Array.Empty<HmiWidgetDefinition>()
            };

            await _pageStore.SaveAsync(document);
            _openDocuments[document.Id] = document;
            await Dispatcher.UIThread.InvokeAsync(() => ApplyDocument(document));
            await RefreshPageListAsync();
            StatusMessage = $"Created {document.Name}.";
        }
        finally
        {
            IsPageOperationInProgress = false;
        }
    }

    [RelayCommand]
    private async Task DeletePageAsync()
    {
        if (_pageStore is null)
        {
            return;
        }

        try
        {
            IsPageOperationInProgress = true;
            var deletedPageId = PageId;
            _openDocuments.Remove(deletedPageId);
            await _pageStore.DeleteAsync(deletedPageId, _stop.Token);

            var remainingPageIds = (await _pageStore.ListAsync(_stop.Token))
                .Where(pageId => !string.Equals(pageId, deletedPageId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            HmiPageDocument nextDocument;
            if (remainingPageIds.Length == 0)
            {
                nextDocument = HmiPageDocument.CreateDefault();
                await _pageStore.SaveAsync(nextDocument, _stop.Token);
            }
            else
            {
                nextDocument = await LoadPageDocumentAsync(remainingPageIds[0]);
            }

            await Dispatcher.UIThread.InvokeAsync(() => ApplyDocument(nextDocument));
            await RefreshPageListAsync();
            StatusMessage = $"Deleted {deletedPageId}.";
        }
        finally
        {
            IsPageOperationInProgress = false;
        }
    }

    [RelayCommand]
    private void AddWidget()
    {
        var index = Widgets.Count + 1;
        var kind = SelectedToolboxItem?.Kind ?? SelectedWidgetKind;
        var definition = CreateWidgetDefinition(kind, index);
        if (kind is HmiWidgetKind.Container)
        {
            definition = definition with
            {
                ZIndex = Widgets.Count == 0 ? -100 : Widgets.Min(widget => widget.ZIndex) - 1
            };
        }

        var widget = new HmiWidgetViewModel(definition);
        Widgets.Add(widget);
        SelectWidget(widget);
        StatusMessage = $"{widget.KindText} added.";
    }

    [RelayCommand]
    private void DeleteSelectedWidget()
    {
        if (SelectedWidgets.Count == 0)
        {
            return;
        }

        var deleted = SelectedWidgets.ToArray();
        foreach (var widget in deleted)
        {
            Widgets.Remove(widget);
        }

        ClearSelection();
        StatusMessage = deleted.Length == 1 ? "Widget deleted." : $"{deleted.Length} widgets deleted.";
    }

    [RelayCommand]
    private void DuplicateSelectedWidget()
    {
        CopySelectedWidgets();
        PasteWidgets();
    }

    [RelayCommand]
    private void CopySelectedWidgets()
    {
        if (SelectedWidgets.Count == 0)
        {
            return;
        }

        _clipboard = SelectedWidgets
            .OrderBy(widget => widget.ZIndex)
            .Select(widget => widget.ToDefinition())
            .ToList();
        StatusMessage = _clipboard.Count == 1 ? "Widget copied." : $"{_clipboard.Count} widgets copied.";
    }

    [RelayCommand]
    private void PasteWidgets()
    {
        if (_clipboard.Count == 0)
        {
            return;
        }

        ClearSelection();
        foreach (var definition in _clipboard)
        {
            var clone = definition with
            {
                Id = $"widget-{Guid.NewGuid():N}",
                Title = $"{definition.Title} Copy",
                X = SnapCoordinate(definition.X + GridSize),
                Y = SnapCoordinate(definition.Y + GridSize),
                ZIndex = Widgets.Count
            };
            var widget = new HmiWidgetViewModel(clone);
            Widgets.Add(widget);
            AddToSelection(widget);
        }

        StatusMessage = _clipboard.Count == 1 ? "Widget pasted." : $"{_clipboard.Count} widgets pasted.";
    }

    [RelayCommand]
    private void ApplySelectedResource()
    {
        if (SelectedWidget is null || SelectedResource is null)
        {
            return;
        }

        SelectedWidget.SetPropertyValue(HmiPropertyNames.Source, SelectedResource.Id);
        StatusMessage = $"Applied resource {SelectedResource.DisplayName}.";
    }

    [RelayCommand]
    private async Task SaveSelectionAsTemplateAsync()
    {
        if (_templateStore is null || SelectedWidgets.Count == 0)
        {
            return;
        }

        var ordered = SelectedWidgets.OrderBy(widget => widget.ZIndex).ToArray();
        var left = ordered.Min(widget => widget.X);
        var top = ordered.Min(widget => widget.Y);
        var widgets = ordered
            .Select(widget => widget.ToDefinition() with
            {
                X = widget.X - left,
                Y = widget.Y - top
            })
            .ToArray();
        var id = $"template-{DateTimeOffset.Now:yyyyMMddHHmmss}";
        var template = new HmiWidgetTemplateDocument(id, $"Template {Templates.Count + 1}", widgets);
        await _templateStore.SaveAsync(template, _stop.Token);
        await LoadTemplatesAsync();
        StatusMessage = $"Saved template {template.Name}.";
    }

    [RelayCommand]
    private async Task InsertSelectedTemplateAsync()
    {
        if (_templateStore is null || SelectedTemplate is null)
        {
            return;
        }

        var template = await _templateStore.LoadAsync(SelectedTemplate.Id, _stop.Token);
        if (template.Widgets.Count == 0)
        {
            return;
        }

        ClearSelection();
        var offset = GridSize <= 0 ? 20 : GridSize;
        foreach (var definition in template.Widgets)
        {
            var clone = definition with
            {
                Id = $"widget-{Guid.NewGuid():N}",
                X = SnapCoordinate(definition.X + 72 + offset),
                Y = SnapCoordinate(definition.Y + 72 + offset),
                ZIndex = Widgets.Count == 0 ? definition.ZIndex : Widgets.Max(widget => widget.ZIndex) + 1
            };
            var widget = new HmiWidgetViewModel(clone);
            Widgets.Add(widget);
            AddToSelection(widget);
        }

        StatusMessage = $"Inserted template {template.Name}.";
    }

    [RelayCommand]
    private void BringForward()
    {
        if (SelectedWidget is null)
        {
            return;
        }

        SelectedWidget.ZIndex++;
    }

    [RelayCommand]
    private void SendBackward()
    {
        if (SelectedWidget is null)
        {
            return;
        }

        SelectedWidget.ZIndex--;
    }

    [RelayCommand]
    private void BringToFront()
    {
        if (SelectedWidget is null)
        {
            return;
        }

        SelectedWidget.ZIndex = Widgets.Count == 0 ? 0 : Widgets.Max(widget => widget.ZIndex) + 1;
    }

    [RelayCommand]
    private void SendToBack()
    {
        if (SelectedWidget is null)
        {
            return;
        }

        SelectedWidget.ZIndex = Widgets.Count == 0 ? 0 : Widgets.Min(widget => widget.ZIndex) - 1;
    }

    [RelayCommand]
    private void AlignLeft()
    {
        if (SelectedWidgets.Count < 2)
        {
            return;
        }

        var x = SelectedWidgets.Min(widget => widget.X);
        foreach (var widget in SelectedWidgets)
        {
            widget.X = x;
        }
    }

    [RelayCommand]
    private void AlignTop()
    {
        if (SelectedWidgets.Count < 2)
        {
            return;
        }

        var y = SelectedWidgets.Min(widget => widget.Y);
        foreach (var widget in SelectedWidgets)
        {
            widget.Y = y;
        }
    }

    [RelayCommand]
    private void AlignRight()
    {
        if (SelectedWidgets.Count < 2)
        {
            return;
        }

        var right = SelectedWidgets.Max(widget => widget.X + widget.Width);
        foreach (var widget in SelectedWidgets)
        {
            widget.X = right - widget.Width;
        }
    }

    [RelayCommand]
    private void AlignBottom()
    {
        if (SelectedWidgets.Count < 2)
        {
            return;
        }

        var bottom = SelectedWidgets.Max(widget => widget.Y + widget.Height);
        foreach (var widget in SelectedWidgets)
        {
            widget.Y = bottom - widget.Height;
        }
    }

    [RelayCommand]
    private void MoveSelection(string? direction)
    {
        var step = string.Equals(direction, "LargeLeft", StringComparison.OrdinalIgnoreCase)
            || string.Equals(direction, "LargeRight", StringComparison.OrdinalIgnoreCase)
            || string.Equals(direction, "LargeUp", StringComparison.OrdinalIgnoreCase)
            || string.Equals(direction, "LargeDown", StringComparison.OrdinalIgnoreCase)
            ? GridSize
            : 1;

        var normalized = direction?.Replace("Large", string.Empty, StringComparison.OrdinalIgnoreCase);
        var (dx, dy) = normalized switch
        {
            "Left" => (-step, 0d),
            "Right" => (step, 0d),
            "Up" => (0d, -step),
            "Down" => (0d, step),
            _ => (0d, 0d)
        };

        MoveSelectedWidgets(dx, dy);
    }

    [RelayCommand]
    private void ToggleRunMode()
    {
        IsRunMode = !IsRunMode;
        if (IsRunMode)
        {
            ClearSelection();
            RuntimePreview.LoadDocument(ToDocument());
        }

        StatusMessage = IsRunMode ? "Run preview enabled." : "Edit mode enabled.";
    }

    partial void OnIsRunModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEditMode));
    }

    public void SelectWidget(HmiWidgetViewModel? widget)
    {
        ClearSelection();
        if (widget is not null)
        {
            AddToSelection(widget);
        }
    }

    public void ToggleWidgetSelection(HmiWidgetViewModel widget)
    {
        if (SelectedWidgets.Contains(widget))
        {
            RemoveFromSelection(widget);
            return;
        }

        AddToSelection(widget);
    }

    public void SelectWidgetsInRect(double left, double top, double right, double bottom)
    {
        ClearSelection();
        var minX = Math.Min(left, right);
        var maxX = Math.Max(left, right);
        var minY = Math.Min(top, bottom);
        var maxY = Math.Max(top, bottom);

        foreach (var widget in Widgets.Where(widget =>
                     widget.X < maxX
                     && widget.X + widget.Width > minX
                     && widget.Y < maxY
                     && widget.Y + widget.Height > minY))
        {
            AddToSelection(widget);
        }
    }

    public void MoveSelectedWidgets(double deltaX, double deltaY)
    {
        IEnumerable<HmiWidgetViewModel> targets = SelectedWidgets.Count > 0
            ? SelectedWidgets
            : SelectedWidget is null ? Array.Empty<HmiWidgetViewModel>() : new[] { SelectedWidget };
        foreach (var widget in targets)
        {
            SetWidgetPosition(widget, widget.X + deltaX, widget.Y + deltaY);
        }
    }

    public void SetWidgetPosition(HmiWidgetViewModel widget, double x, double y)
    {
        widget.X = Math.Max(0, SnapCoordinate(x));
        widget.Y = Math.Max(0, SnapCoordinate(y));
    }

    public void ResizeWidget(HmiWidgetViewModel widget, double width, double height)
    {
        widget.Width = Math.Max(24, SnapCoordinate(width));
        widget.Height = Math.Max(24, SnapCoordinate(height));
    }

    private void AddToSelection(HmiWidgetViewModel widget)
    {
        if (SelectedWidget is not null)
        {
            SelectedWidget.IsPrimarySelected = false;
        }

        SelectedWidget = widget;
        SelectedWidget.IsPrimarySelected = true;

        if (!SelectedWidgets.Contains(widget))
        {
            SelectedWidgets.Add(widget);
            widget.IsSelected = true;
        }
    }

    private void RemoveFromSelection(HmiWidgetViewModel widget)
    {
        widget.IsSelected = false;
        widget.IsPrimarySelected = false;
        SelectedWidgets.Remove(widget);
        SelectedWidget = SelectedWidgets.LastOrDefault();
        if (SelectedWidget is not null)
        {
            SelectedWidget.IsPrimarySelected = true;
        }
    }

    private void ClearSelection()
    {
        foreach (var widget in SelectedWidgets)
        {
            widget.IsSelected = false;
            widget.IsPrimarySelected = false;
        }

        SelectedWidgets.Clear();
        SelectedWidget = null;
    }

    partial void OnSelectedWidgetChanged(HmiWidgetViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedWidget));
        OnPropertyChanged(nameof(HasMultipleSelectedWidgets));
        DeleteSelectedWidgetCommand.NotifyCanExecuteChanged();
        DuplicateSelectedWidgetCommand.NotifyCanExecuteChanged();
        RebuildEditorPanels();
    }

    private void ApplyDocument(HmiPageDocument document)
    {
        PageId = document.Id;
        SelectedPageId = document.Id;
        PageName = document.Name;
        PageWidth = document.Width;
        PageHeight = document.Height;
        PageBackground = document.Background;
        IsGridVisible = document.Grid.IsVisible;
        SnapToGrid = document.Grid.SnapToGrid;
        GridSize = document.Grid.Size;
        Widgets.Clear();
        SelectedWidgets.Clear();

        foreach (var widget in document.Widgets.OrderBy(widget => widget.ZIndex))
        {
            Widgets.Add(new HmiWidgetViewModel(widget));
        }

        RebuildGridLines();
        SelectWidget(Widgets.FirstOrDefault());
    }

    private HmiPageDocument ToDocument()
        => new(
            HmiPageDocument.CurrentSchemaVersion,
            PageId,
            PageName,
            PageWidth,
            PageHeight,
            PageBackground,
            new HmiGridDefinition(IsGridVisible, SnapToGrid, GridSize),
            Widgets
                .OrderBy(widget => widget.ZIndex)
                .Select(widget => widget.ToDefinition())
                .ToArray());

    private void CacheCurrentDocument()
    {
        if (string.IsNullOrWhiteSpace(PageId))
        {
            return;
        }

        var document = ToDocument();
        _openDocuments[document.Id] = document;
    }

    private async Task<HmiPageDocument> LoadPageDocumentAsync(string pageId)
    {
        if (_openDocuments.TryGetValue(pageId, out var cachedDocument))
        {
            return cachedDocument;
        }

        var document = await _pageStore.LoadAsync(pageId, _stop.Token);
        _openDocuments[document.Id] = document;
        return document;
    }

    private async Task RefreshPageListAsync(bool includeCurrentPage = true)
    {
        if (_pageStore is null)
        {
            return;
        }

        var pageIds = await _pageStore.ListAsync(_stop.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            PageIds.Clear();
            foreach (var pageId in pageIds)
            {
                PageIds.Add(pageId);
            }

            if (includeCurrentPage && !PageIds.Contains(PageId))
            {
                PageIds.Add(PageId);
            }
        });
    }

    private async Task ObserveTagsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (TagSampleBatch batch in _pipeline.SubscribeBatchesAsync(cancellationToken))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var sample in batch.Samples)
                    {
                        ApplyTagValue(sample);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
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

    private HmiWidgetDefinition CreateWidgetDefinition(HmiWidgetKind kind, int index)
    {
        var x = 72 + index * 20;
        var y = 72 + index * 20;
        var descriptor = _catalog.Get(kind);
        var bindings = descriptor.Bindings.ToDictionary(
            binding => binding.Name,
            _ => string.Empty,
            StringComparer.OrdinalIgnoreCase);
        var properties = descriptor.Properties
            .Where(property => property.DefaultValue is not null)
            .ToDictionary(
                property => property.Name,
                property => property.DefaultValue!,
                StringComparer.OrdinalIgnoreCase);

        return kind switch
        {
            HmiWidgetKind.StateIndicator => HmiWidgetDefinition.Create(
                kind,
                $"State {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(properties, $"State {index}")),
            HmiWidgetKind.CommandButton => HmiWidgetDefinition.Create(
                kind,
                $"Command {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(properties, $"Command {index}")) with
                {
                    Events = CreateDefaultButtonEvents(bindings)
                },
            HmiWidgetKind.Image => HmiWidgetDefinition.Create(
                kind,
                $"Image {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(WithDefaultImage(properties), $"Image {index}")),
            HmiWidgetKind.ProgressBar => HmiWidgetDefinition.Create(
                kind,
                $"Progress {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(properties, $"Progress {index}")),
            HmiWidgetKind.TrendChart => HmiWidgetDefinition.Create(
                kind,
                $"Trend {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(properties, $"Trend {index}")),
            HmiWidgetKind.AlarmList => HmiWidgetDefinition.Create(
                kind,
                $"Alarms {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(properties, $"Alarms {index}")),
            _ => HmiWidgetDefinition.Create(
                kind,
                $"{descriptor.DisplayName} {index}",
                x,
                y,
                descriptor.DefaultWidth,
                descriptor.DefaultHeight,
                bindings,
                WithText(properties, $"{descriptor.DisplayName} {index}"))
        };
    }

    private static Dictionary<string, string> WithText(Dictionary<string, string> properties, string text)
    {
        var copy = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase)
        {
            [HmiPropertyNames.Text] = text
        };

        return copy;
    }

    private Dictionary<string, string> WithDefaultImage(Dictionary<string, string> properties)
    {
        var copy = new Dictionary<string, string>(properties, StringComparer.OrdinalIgnoreCase);
        if (SelectedResource is not null)
        {
            copy[HmiPropertyNames.Source] = SelectedResource.Id;
        }

        return copy;
    }

    private static IReadOnlyList<HmiEventDefinition> CreateDefaultButtonEvents(IReadOnlyDictionary<string, string> bindings)
    {
        bindings.TryGetValue(HmiBindingNames.Command, out var tagName);
        return new[]
        {
            new HmiEventDefinition(
                HmiEventNames.Click,
                new[]
                {
                    new HmiActionDefinition(
                        HmiActionKind.WriteTag,
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [HmiActionParameterNames.TagName] = tagName ?? string.Empty,
                            [HmiActionParameterNames.Value] = "true"
                        })
                })
        };
    }

    private void AddDefaultDesignItems()
    {
        ApplyDocument(HmiPageDocument.CreateDefault(PageId, PageName));
    }

    private void LoadCatalog()
    {
        ToolboxItems.Clear();
        foreach (var descriptor in _catalog.List())
        {
            ToolboxItems.Add(new HmiToolboxItemViewModel(descriptor));
        }

        SelectedToolboxItem = ToolboxItems.FirstOrDefault();
    }

    private void LoadTagOptions()
    {
        TagOptions.Clear();
        if (_settingsStore is null)
        {
            return;
        }

        foreach (var tag in _settingsStore.Current.Tags.Select(tag => tag.Name).OrderBy(name => name))
        {
            TagOptions.Add(tag);
        }
    }

    private async Task LoadResourcesAsync()
    {
        if (_resourceStore is null)
        {
            return;
        }

        var resources = await _resourceStore.ListAsync(_stop.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Resources.Clear();
            foreach (var resource in resources)
            {
                Resources.Add(new HmiResourceViewModel(resource));
            }

            SelectedResource = Resources.FirstOrDefault();
        });
    }

    private async Task LoadTemplatesAsync()
    {
        if (_templateStore is null)
        {
            return;
        }

        var templates = await _templateStore.ListAsync(_stop.Token);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Templates.Clear();
            foreach (var template in templates)
            {
                Templates.Add(new HmiTemplateViewModel(template));
            }

            SelectedTemplate = Templates.FirstOrDefault();
        });
    }

    partial void OnIsPageOperationInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(CanRunPageOperation));
    }

    private void RebuildEditorPanels()
    {
        PropertyEditors.Clear();
        BindingEditors.Clear();

        if (SelectedWidget is null)
        {
            return;
        }

        var descriptor = _catalog.Get(SelectedWidget.Kind);
        foreach (var property in descriptor.Properties)
        {
            PropertyEditors.Add(new HmiPropertyEditorViewModel(SelectedWidget, property));
        }

        foreach (var binding in descriptor.Bindings)
        {
            BindingEditors.Add(new HmiBindingEditorViewModel(SelectedWidget, binding, TagOptions));
        }
    }

    private double SnapCoordinate(double value)
    {
        if (!SnapToGrid || GridSize <= 1)
        {
            return value;
        }

        return Math.Round(value / GridSize) * GridSize;
    }

    partial void OnPageWidthChanged(double value)
    {
        RebuildGridLines();
    }

    partial void OnPageHeightChanged(double value)
    {
        RebuildGridLines();
    }

    partial void OnGridSizeChanged(double value)
    {
        if (value < 4)
        {
            GridSize = 4;
            return;
        }

        RebuildGridLines();
    }

    private void RebuildGridLines()
    {
        GridLines.Clear();
        if (GridSize <= 1 || PageWidth <= 0 || PageHeight <= 0)
        {
            return;
        }

        for (var x = GridSize; x < PageWidth; x += GridSize)
        {
            GridLines.Add(new HmiGridLineViewModel(x, 0, 1, PageHeight));
        }

        for (var y = GridSize; y < PageHeight; y += GridSize)
        {
            GridLines.Add(new HmiGridLineViewModel(0, y, PageWidth, 1));
        }
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
