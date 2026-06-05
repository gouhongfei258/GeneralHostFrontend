using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeneralHostFrontend.Core.Database;

namespace GeneralHostFrontend.ViewModels.Database;

public sealed partial class DatabaseViewerViewModel : ViewModelBase
{
    private readonly IDataViewerQueryService _queryService;
    private readonly IDatabaseHealthMonitor _healthMonitor;
    private int _pageIndex;

    [ObservableProperty]
    private DatabaseTableViewModel? _selectedTable;

    [ObservableProperty]
    private string? _keyword;

    [ObservableProperty]
    private string _pageInfo = "Page 1";

    [ObservableProperty]
    private string _databaseStatus = "Unknown";

    [ObservableProperty]
    private string _databaseFile = string.Empty;

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    public DatabaseViewerViewModel()
    {
        _queryService = null!;
        _healthMonitor = null!;
    }

    public DatabaseViewerViewModel(IDataViewerQueryService queryService, IDatabaseHealthMonitor healthMonitor)
    {
        _queryService = queryService;
        _healthMonitor = healthMonitor;
        _ = InitializeAsync();
    }

    public ObservableCollection<DatabaseTableViewModel> Tables { get; } = new();

    public ObservableCollection<string> Columns { get; } = new();

    public ObservableCollection<DatabaseRowViewModel> Rows { get; } = new();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _pageIndex = 0;
        await LoadTablesAsync();
        await RefreshHealthAsync();
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (_pageIndex == 0)
        {
            return;
        }

        _pageIndex--;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        _pageIndex++;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task ExportCsvAsync()
    {
        if (_queryService is null || SelectedTable is null)
        {
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "Exports");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, $"{SelectedTable.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

        await using var stream = File.Create(filePath);
        await _queryService.ExportCsvAsync(CreateQuery(0, 500), stream);
        LastExportPath = filePath;
    }

    partial void OnSelectedTableChanged(DatabaseTableViewModel? value)
    {
        _pageIndex = 0;
        _ = LoadPageAsync();
    }

    private async Task InitializeAsync()
    {
        await LoadTablesAsync();
        await RefreshHealthAsync();
        await LoadPageAsync();
    }

    private async Task LoadTablesAsync()
    {
        if (_queryService is null)
        {
            return;
        }

        var tables = await _queryService.GetTablesAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            var selectedName = SelectedTable?.Name;
            Tables.Clear();
            foreach (var table in tables)
            {
                Tables.Add(new DatabaseTableViewModel(table));
            }

            SelectedTable = Tables.FirstOrDefault(table => table.Name == selectedName)
                ?? Tables.FirstOrDefault();
        });
    }

    private async Task RefreshHealthAsync()
    {
        if (_healthMonitor is null)
        {
            return;
        }

        var health = await _healthMonitor.CheckAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            DatabaseStatus = $"{health.Provider}: {(health.IsHealthy ? "Healthy" : "Faulted")}";
            DatabaseFile = health.FileSizeBytes <= 0
                ? health.FilePath
                : $"{health.FilePath} ({health.FileSizeBytes / 1024.0:0.0} KB)";
        });
    }

    private async Task LoadPageAsync()
    {
        if (_queryService is null || SelectedTable is null)
        {
            return;
        }

        var result = await _queryService.QueryAsync(CreateQuery(_pageIndex, 20));
        if (result.Items.Count == 0 && result.TotalCount > 0 && _pageIndex > 0)
        {
            _pageIndex--;
            result = await _queryService.QueryAsync(CreateQuery(_pageIndex, 20));
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Columns.Clear();
            foreach (var column in SelectedTable.Columns)
            {
                Columns.Add(column.Name);
            }

            Rows.Clear();
            foreach (var row in result.Items)
            {
                Rows.Add(new DatabaseRowViewModel(Columns.Select(column =>
                    new DatabaseCellViewModel(column, Format(row.GetValueOrDefault(column))))));
            }

            var totalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));
            PageInfo = $"Page {result.PageIndex + 1} / {totalPages} ({result.TotalCount} rows)";
        });
    }

    private PagedQuery CreateQuery(int pageIndex, int pageSize)
    {
        var filters = string.IsNullOrWhiteSpace(Keyword) || SelectedTable is null
            ? null
            : SelectedTable.Columns
                .Select(column => new FilterDescriptor(column.Name, "contains", Keyword))
                .ToArray();

        var defaultSort = SelectedTable?.Columns.FirstOrDefault(column => column.Name.Equals("Time", StringComparison.OrdinalIgnoreCase))?.Name
            ?? SelectedTable?.Columns.FirstOrDefault(column => column.IsPrimaryKey)?.Name
            ?? SelectedTable?.Columns.FirstOrDefault()?.Name
            ?? string.Empty;

        IReadOnlyList<SortDescriptor>? sorts = string.IsNullOrWhiteSpace(defaultSort)
            ? null
            : new[] { new SortDescriptor(defaultSort, Descending: true) };

        return new PagedQuery(SelectedTable?.Name ?? string.Empty, pageIndex, pageSize, filters, sorts);
    }

    private static string Format(object? value)
        => value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
            _ => value.ToString() ?? string.Empty
        };
}
