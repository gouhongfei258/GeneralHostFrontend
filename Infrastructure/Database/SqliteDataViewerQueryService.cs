using System.Globalization;
using System.Text;
using GeneralHostFrontend.Core.Database;
using Microsoft.Data.Sqlite;

namespace GeneralHostFrontend.Infrastructure.Database;

public sealed class SqliteDataViewerQueryService : IDataViewerQueryService, IDatabaseHealthMonitor
{
    private const int ExportRowLimit = 10000;

    private readonly string _databasePath;
    private readonly string _connectionString;

    public SqliteDataViewerQueryService(string databasePath)
    {
        _databasePath = databasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        EnsureDatabase();
    }

    public async Task<IReadOnlyList<DatabaseTableInfo>> GetTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var tableNames = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                select name
                from sqlite_master
                where type = 'table'
                  and name not like 'sqlite_%'
                order by name;
                """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        var tables = new List<DatabaseTableInfo>();
        foreach (var tableName in tableNames)
        {
            tables.Add(await LoadTableInfoAsync(connection, tableName, cancellationToken));
        }

        return tables;
    }

    public async Task<PagedResult<IReadOnlyDictionary<string, object?>>> QueryAsync(PagedQuery query, CancellationToken cancellationToken = default)
    {
        var pageIndex = Math.Max(0, query.PageIndex);
        var pageSize = Math.Clamp(query.PageSize, 1, ExportRowLimit);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var tableInfo = await LoadTableInfoAsync(connection, query.Table, cancellationToken);
        var schema = TableSchema.From(tableInfo);

        var where = BuildWhere(schema, query.Filters);
        var orderBy = BuildOrderBy(schema, query.Sorts);

        var countCommand = connection.CreateCommand();
        countCommand.CommandText = $"select count(*) from {QuoteIdentifier(schema.TableName)}{where.Sql};";
        CopyParameters(where.Parameters, countCommand);
        var total = (long)(await countCommand.ExecuteScalarAsync(cancellationToken) ?? 0L);

        var dataCommand = connection.CreateCommand();
        dataCommand.CommandText = $"""
            select {string.Join(", ", schema.SelectColumns.Select(QuoteIdentifier))}
            from {QuoteIdentifier(schema.TableName)}
            {where.Sql}
            {orderBy}
            limit $limit offset $offset;
            """;
        CopyParameters(where.Parameters, dataCommand);
        dataCommand.Parameters.AddWithValue("$limit", pageSize);
        dataCommand.Parameters.AddWithValue("$offset", pageIndex * pageSize);

        var items = new List<IReadOnlyDictionary<string, object?>>();
        await using var reader = await dataCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in schema.SelectColumns)
            {
                row[column] = reader[column] is DBNull ? null : reader[column];
            }

            items.Add(row);
        }

        return new PagedResult<IReadOnlyDictionary<string, object?>>(items, pageIndex, pageSize, total);
    }

    public async Task ExportCsvAsync(PagedQuery query, Stream output, CancellationToken cancellationToken = default)
    {
        var (columns, rows) = await QueryExportRowsAsync(query, cancellationToken);

        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteLineAsync(string.Join(",", columns.Select(EscapeCsv)));

        foreach (var row in rows)
        {
            var line = string.Join(",", columns.Select(column => EscapeCsv(Format(row.GetValueOrDefault(column)))));
            await writer.WriteLineAsync(line);
        }
    }

    public async Task ExportExcelAsync(PagedQuery query, Stream output, CancellationToken cancellationToken = default)
    {
        var (columns, rows) = await QueryExportRowsAsync(query, cancellationToken);
        await ExcelWorkbookWriter.WriteAsync(columns, rows, output, cancellationToken);
    }

    public async Task<DatabaseHealth> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "pragma quick_check;";
            var result = Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);

            var file = new FileInfo(_databasePath);
            return new DatabaseHealth(
                "SQLite",
                _databasePath,
                file.Exists ? file.Length : 0,
                string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase),
                result,
                DateTimeOffset.Now);
        }
        catch (Exception ex)
        {
            return new DatabaseHealth("SQLite", _databasePath, 0, false, ex.Message, DateTimeOffset.Now);
        }
    }

    private void EnsureDatabase()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var create = connection.CreateCommand();
        create.CommandText = """
            create table if not exists AlarmHistory
            (
                Id integer primary key autoincrement,
                Time text not null,
                Level text not null,
                Code text not null,
                Message text not null,
                Confirmed integer not null default 0
            );

            create index if not exists IX_AlarmHistory_Time on AlarmHistory(Time desc);
            create index if not exists IX_AlarmHistory_Level on AlarmHistory(Level);

            create table if not exists Recipes
            (
                Id integer primary key autoincrement,
                Name text not null,
                ProductCode text not null,
                Version text not null,
                TargetSpeed real not null,
                TemperatureSetpoint real not null,
                UpdatedAt text not null
            );

            create table if not exists SystemConfig
            (
                Key text primary key,
                Value text not null,
                Category text not null,
                UpdatedAt text not null
            );
            """;
        create.ExecuteNonQuery();
    }

    private static SqlWhere BuildWhere(TableSchema schema, IReadOnlyList<FilterDescriptor>? filters)
    {
        if (filters is null || filters.Count == 0)
        {
            return new SqlWhere(string.Empty, Array.Empty<SqlParameterValue>());
        }

        var clauses = new List<string>();
        var parameters = new List<SqlParameterValue>();

        for (var index = 0; index < filters.Count; index++)
        {
            var filter = filters[index];
            if (string.IsNullOrWhiteSpace(filter.Value))
            {
                continue;
            }

            if (filter.Field == "*")
            {
                var parameterName = $"$filter{index}";
                var value = NormalizeFilterValue(filter.Operator, filter.Value);
                var globalClauses = schema.FilterableColumns
                    .Select(column => BuildPredicate(column, filter.Operator, parameterName))
                    .ToArray();

                clauses.Add("(" + string.Join(" or ", globalClauses) + ")");
                parameters.Add(new SqlParameterValue(parameterName, value));
                continue;
            }

            if (!schema.FilterableColumns.Contains(filter.Field))
            {
                continue;
            }

            var fieldParameterName = $"$filter{index}";
            clauses.Add(BuildPredicate(filter.Field, filter.Operator, fieldParameterName));
            parameters.Add(new SqlParameterValue(fieldParameterName, NormalizeFilterValue(filter.Operator, filter.Value)));
        }

        return clauses.Count == 0
            ? new SqlWhere(string.Empty, Array.Empty<SqlParameterValue>())
            : new SqlWhere(" where " + string.Join(" and ", clauses), parameters);
    }

    private static string BuildPredicate(string field, string @operator, string parameterName)
    {
        var column = QuoteIdentifier(field);
        return @operator switch
        {
            "equals" => $"cast({column} as text) = {parameterName}",
            "startsWith" => $"cast({column} as text) like {parameterName}",
            "endsWith" => $"cast({column} as text) like {parameterName}",
            _ => $"cast({column} as text) like {parameterName}"
        };
    }

    private static string NormalizeFilterValue(string @operator, string value)
    {
        return @operator switch
        {
            "equals" => value,
            "startsWith" => value + "%",
            "endsWith" => "%" + value,
            _ => "%" + value + "%"
        };
    }

    private static string BuildOrderBy(TableSchema schema, IReadOnlyList<SortDescriptor>? sorts)
    {
        var sort = sorts?.FirstOrDefault(item => schema.SortableColumns.Contains(item.Field));
        if (sort is null)
        {
            var defaultColumn = schema.SortableColumns.Contains("Time")
                ? "Time"
                : schema.SortableColumns.FirstOrDefault();

            return defaultColumn is null
                ? string.Empty
                : $"order by {QuoteIdentifier(defaultColumn)} desc";
        }

        return $"order by {QuoteIdentifier(sort.Field)} {(sort.Descending ? "desc" : "asc")}";
    }

    private static void CopyParameters(IEnumerable<SqlParameterValue> parameters, SqliteCommand command)
    {
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }
    }

    private static string Format(object? value)
    {
        return value switch
        {
            null => string.Empty,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static string EscapeCsv(string value)
    {
        return value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private async Task<(IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows)> QueryExportRowsAsync(
        PagedQuery query,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var tableInfo = await LoadTableInfoAsync(connection, query.Table, cancellationToken);
        var columns = tableInfo.Columns.Select(column => column.Name).ToArray();
        var result = await QueryAsync(query with { PageIndex = 0, PageSize = ExportRowLimit }, cancellationToken);
        return (columns, result.Items);
    }

    private async Task<DatabaseTableInfo> LoadTableInfoAsync(SqliteConnection connection, string tableName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tableName))
        {
            throw new InvalidOperationException("Table name cannot be empty.");
        }

        var exists = false;
        await using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = """
                select count(*)
                from sqlite_master
                where type = 'table'
                  and name = $name
                  and name not like 'sqlite_%';
                """;
            existsCommand.Parameters.AddWithValue("$name", tableName);
            exists = Convert.ToInt32(await existsCommand.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) > 0;
        }

        if (!exists)
        {
            throw new InvalidOperationException($"Table '{tableName}' does not exist or is not visible.");
        }

        var columns = new List<DatabaseColumnInfo>();
        await using var command = connection.CreateCommand();
        command.CommandText = $"pragma table_info({QuoteIdentifier(tableName)});";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            columns.Add(new DatabaseColumnInfo(
                reader["name"].ToString() ?? string.Empty,
                reader["type"].ToString() ?? string.Empty,
                Convert.ToInt32(reader["notnull"], CultureInfo.InvariantCulture) == 0,
                Convert.ToInt32(reader["pk"], CultureInfo.InvariantCulture) > 0));
        }

        return new DatabaseTableInfo(tableName, columns);
    }

    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record TableSchema(
        string TableName,
        IReadOnlyList<string> SelectColumns,
        IReadOnlySet<string> FilterableColumns)
    {
        public IReadOnlySet<string> SortableColumns { get; } = FilterableColumns;

        public static TableSchema From(DatabaseTableInfo table)
        {
            var columns = table.Columns.Select(column => column.Name).ToArray();
            return new TableSchema(
                table.Name,
                columns,
                new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase));
        }
    }

    private sealed record SqlWhere(string Sql, IReadOnlyList<SqlParameterValue> Parameters);

    private sealed record SqlParameterValue(string Name, object Value);
}
