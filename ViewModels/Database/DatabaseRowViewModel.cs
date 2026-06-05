using System.Collections.ObjectModel;

namespace GeneralHostFrontend.ViewModels.Database;

public sealed class DatabaseRowViewModel
{
    public DatabaseRowViewModel(IEnumerable<DatabaseCellViewModel> cells)
    {
        Cells = new ObservableCollection<DatabaseCellViewModel>(cells);
    }

    public ObservableCollection<DatabaseCellViewModel> Cells { get; }
}
