using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using System.Collections.Specialized;
using GeneralHostFrontend.ViewModels.Database;

namespace GeneralHostFrontend.Views.Database;

public partial class DatabaseViewerWindow : Window
{
    public DatabaseViewerWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachViewModel();
    }

    private DatabaseViewerViewModel? _viewModel;

    private void AttachViewModel()
    {
        if (_viewModel is not null)
        {
            _viewModel.Columns.CollectionChanged -= ColumnsChanged;
        }

        _viewModel = DataContext as DatabaseViewerViewModel;
        if (_viewModel is null)
        {
            RowsGrid.Columns.Clear();
            return;
        }

        _viewModel.Columns.CollectionChanged += ColumnsChanged;
        RebuildColumns();
    }

    private void ColumnsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildColumns();
    }

    private void RebuildColumns()
    {
        RowsGrid.Columns.Clear();
        if (_viewModel is null)
        {
            return;
        }

        foreach (var column in _viewModel.Columns)
        {
            RowsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = column,
                CellTemplate = new FuncDataTemplate<object>((_, _) => new TextBlock
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    [!TextBlock.TextProperty] = new Binding(".")
                    {
                        Converter = DataRowColumnValueConverter.Instance,
                        ConverterParameter = column
                    }
                }),
                Width = new DataGridLength(1, DataGridLengthUnitType.Auto),
                MinWidth = 110,
                MaxWidth = 360
            });
        }
    }
}
