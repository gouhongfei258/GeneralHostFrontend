using System.Collections.ObjectModel;
using System.Data;
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
    private string? _filterColumn;

    [ObservableProperty]
    private string _filterOperator = "contains";

    [ObservableProperty]
    private string? _filterValue;

    [ObservableProperty]
    private int _pageNumber = 1;

    [ObservableProperty]
    private int _pageSize = 20;

    [ObservableProperty]
    private string _pageInfo = "Page 1";

    [ObservableProperty]
    private string _databaseStatus = "Unknown";

    [ObservableProperty]
    private string _databaseFile = string.Empty;

    [ObservableProperty]
    private string _lastExportPath = string.Empty;

    [ObservableProperty]
    private DataView? _gridRows;

    private bool _isSyncingPageNumber;

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

    public ObservableCollection<string> FilterColumns { get; } = new();

    public ObservableCollection<FilterOperatorViewModel> FilterOperators { get; } = new(new[]
    {
        new FilterOperatorViewModel("contains", "Contains"),
        new FilterOperatorViewModel("equals", "Equals"),
        new FilterOperatorViewModel("startsWith", "Starts with"),
        new FilterOperatorViewModel("endsWith", "Ends with")
    });

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _pageIndex = 0;
        PageNumber = 1;
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
        SyncPageNumber();
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        _pageIndex++;
        SyncPageNumber();
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
        await _queryService.ExportCsvAsync(CreateQuery(0, 10000), stream);
        LastExportPath = filePath;
    }

    [RelayCommand]
    private async Task ExportExcelAsync()
    {
        if (_queryService is null || SelectedTable is null)
        {
            return;
        }

        var directory = Path.Combine(AppContext.BaseDirectory, "Exports");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, $"{SelectedTable.Name}-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx");

        await using var stream = File.Create(filePath);
        await _queryService.ExportExcelAsync(CreateQuery(0, 10000), stream);
        LastExportPath = filePath;
    }

    [RelayCommand]
    private async Task ApplyFiltersAsync()
    {
        _pageIndex = 0;
        PageNumber = 1;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task ClearFiltersAsync()
    {
        Keyword = string.Empty;
        FilterColumn = FilterColumns.FirstOrDefault();
        FilterOperator = "contains";
        FilterValue = string.Empty;
        _pageIndex = 0;
        PageNumber = 1;
        await LoadPageAsync();
    }

    [RelayCommand]
    private async Task GoToPageAsync()
    {
        _pageIndex = Math.Max(0, PageNumber - 1);
        await LoadPageAsync();
    }

    partial void OnSelectedTableChanged(DatabaseTableViewModel? value)
    {
        _pageIndex = 0;
        PageNumber = 1;
        ResetFilterColumns(value);
        _ = LoadPageAsync();
    }

    partial void OnPageSizeChanged(int value)
    {
        if (value < 1)
        {
            PageSize = 1;
            return;
        }

        _pageIndex = 0;
        PageNumber = 1;
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

        var result = await _queryService.QueryAsync(CreateQuery(_pageIndex, PageSize));
        if (result.Items.Count == 0 && result.TotalCount > 0 && _pageIndex > 0)
        {
            var lastPageIndex = Math.Max(0, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize) - 1);
            _pageIndex = Math.Min(_pageIndex - 1, lastPageIndex);
            SyncPageNumber();
            result = await _queryService.QueryAsync(CreateQuery(_pageIndex, PageSize));
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            Columns.Clear();
            foreach (var column in SelectedTable.Columns)
            {
                Columns.Add(column.Name);
            }

            var dataTable = new DataTable(SelectedTable.Name);
            foreach (var row in result.Items)
            {
                if (dataTable.Columns.Count == 0)
                {
                    foreach (var column in Columns)
                    {
                        dataTable.Columns.Add(column, typeof(string));
                    }
                }

                var dataRow = dataTable.NewRow();
                foreach (var column in Columns)
                {
                    dataRow[column] = Format(row.GetValueOrDefault(column));
                }

                dataTable.Rows.Add(dataRow);
            }

            if (dataTable.Columns.Count == 0)
            {
                foreach (var column in Columns)
                {
                    dataTable.Columns.Add(column, typeof(string));
                }
            }

            GridRows = dataTable.DefaultView;

            var totalPages = Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)result.PageSize));
            _pageIndex = Math.Clamp(result.PageIndex, 0, totalPages - 1);
            SyncPageNumber();
            PageInfo = $"Page {_pageIndex + 1} / {totalPages} ({result.TotalCount} rows)";
        });
    }

    private PagedQuery CreateQuery(int pageIndex, int pageSize)
    {
        var filters = new List<FilterDescriptor>();
        if (!string.IsNullOrWhiteSpace(Keyword))
        {
            filters.Add(new FilterDescriptor("*", "contains", Keyword));
        }

        if (!string.IsNullOrWhiteSpace(FilterValue)
            && !string.IsNullOrWhiteSpace(FilterColumn)
            && FilterColumn != AllColumnsFilter)
        {
            filters.Add(new FilterDescriptor(FilterColumn, FilterOperator, FilterValue));
        }

        var defaultSort = SelectedTable?.Columns.FirstOrDefault(column => column.Name.Equals("Time", StringComparison.OrdinalIgnoreCase))?.Name
            ?? SelectedTable?.Columns.FirstOrDefault(column => column.IsPrimaryKey)?.Name
            ?? SelectedTable?.Columns.FirstOrDefault()?.Name
            ?? string.Empty;

        IReadOnlyList<SortDescriptor>? sorts = string.IsNullOrWhiteSpace(defaultSort)
            ? null
            : new[] { new SortDescriptor(defaultSort, Descending: true) };

        return new PagedQuery(SelectedTable?.Name ?? string.Empty, pageIndex, pageSize, filters.Count == 0 ? null : filters, sorts);
    }

    private void ResetFilterColumns(DatabaseTableViewModel? table)
    {
        FilterColumns.Clear();
        FilterColumns.Add(AllColumnsFilter);
        if (table is not null)
        {
            foreach (var column in table.Columns)
            {
                FilterColumns.Add(column.Name);
            }
        }

        if (string.IsNullOrWhiteSpace(FilterColumn) || !FilterColumns.Contains(FilterColumn))
        {
            FilterColumn = FilterColumns.FirstOrDefault();
        }
    }

    private void SyncPageNumber()
    {
        _isSyncingPageNumber = true;
        PageNumber = _pageIndex + 1;
        _isSyncingPageNumber = false;
    }

    partial void OnPageNumberChanged(int value)
    {
        if (_isSyncingPageNumber || value >= 1)
        {
            return;
        }

        PageNumber = 1;
    }

    private static string Format(object? value)
        => value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss"),
            _ => value.ToString() ?? string.Empty
        };

    private const string AllColumnsFilter = "All columns";
}
