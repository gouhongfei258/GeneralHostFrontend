using System.Data;
using System.Globalization;
using Avalonia.Data.Converters;

namespace GeneralHostFrontend.Views.Database;

public sealed class DataRowColumnValueConverter : IValueConverter
{
    public static DataRowColumnValueConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DataRowView row || parameter is not string column)
        {
            return string.Empty;
        }

        var table = row.DataView.Table;
        if (table is null || !table.Columns.Contains(column))
        {
            return string.Empty;
        }

        return row[column]?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
