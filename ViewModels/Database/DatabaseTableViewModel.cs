using GeneralHostFrontend.Core.Database;

namespace GeneralHostFrontend.ViewModels.Database;

public sealed class DatabaseTableViewModel
{
    public DatabaseTableViewModel(DatabaseTableInfo table)
    {
        Name = table.Name;
        Columns = table.Columns;
    }

    public string Name { get; }

    public IReadOnlyList<DatabaseColumnInfo> Columns { get; }

    public string Summary => $"{Columns.Count} columns";
}
