using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace GeneralHostFrontend.Infrastructure.Database;

internal static class ExcelWorkbookWriter
{
    public static async Task WriteAsync(
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        Stream output,
        CancellationToken cancellationToken)
    {
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        await WriteEntryAsync(archive, "[Content_Types].xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
              <Default Extension="xml" ContentType="application/xml"/>
              <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
              <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
              <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
            </Types>
            """, cancellationToken);

        await WriteEntryAsync(archive, "_rels/.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
            </Relationships>
            """, cancellationToken);

        await WriteEntryAsync(archive, "xl/_rels/workbook.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
              <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
            </Relationships>
            """, cancellationToken);

        await WriteEntryAsync(archive, "xl/workbook.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
              <sheets>
                <sheet name="Data" sheetId="1" r:id="rId1"/>
              </sheets>
            </workbook>
            """, cancellationToken);

        await WriteEntryAsync(archive, "xl/styles.xml", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
              <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
              <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
              <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
              <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
              <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
            </styleSheet>
            """, cancellationToken);

        await WriteWorksheetAsync(archive, columns, rows, cancellationToken);
    }

    private static async Task WriteWorksheetAsync(
        ZipArchive archive,
        IReadOnlyList<string> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));

        await writer.WriteAsync("""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
            <sheetData>
            """);

        await WriteRowAsync(writer, 1, columns.Select(column => (object?)column), header: true);

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = rows[rowIndex];
            await WriteRowAsync(writer, rowIndex + 2, columns.Select(column => row.GetValueOrDefault(column)), header: false);
        }

        await writer.WriteAsync("</sheetData><autoFilter ref=\"A1:");
        await writer.WriteAsync(GetCellReference(Math.Max(1, columns.Count), Math.Max(1, rows.Count + 1)));
        await writer.WriteAsync("\"/></worksheet>");
    }

    private static async Task WriteRowAsync(
        TextWriter writer,
        int rowNumber,
        IEnumerable<object?> values,
        bool header)
    {
        await writer.WriteAsync($"<row r=\"{rowNumber.ToString(CultureInfo.InvariantCulture)}\">");

        var columnNumber = 1;
        foreach (var value in values)
        {
            var cellReference = GetCellReference(columnNumber, rowNumber);
            var style = header ? " s=\"1\"" : string.Empty;
            await writer.WriteAsync($"<c r=\"{cellReference}\" t=\"inlineStr\"{style}><is><t>");
            await writer.WriteAsync(SecurityElement.Escape(Format(value)) ?? string.Empty);
            await writer.WriteAsync("</t></is></c>");
            columnNumber++;
        }

        await writer.WriteAsync("</row>");
    }

    private static async Task WriteEntryAsync(
        ZipArchive archive,
        string name,
        string content,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        await using var stream = entry.Open();
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }

    private static string GetCellReference(int columnNumber, int rowNumber)
    {
        var builder = new StringBuilder();
        while (columnNumber > 0)
        {
            columnNumber--;
            builder.Insert(0, (char)('A' + columnNumber % 26));
            columnNumber /= 26;
        }

        builder.Append(rowNumber.ToString(CultureInfo.InvariantCulture));
        return builder.ToString();
    }

    private static string Format(object? value)
        => value switch
        {
            null => string.Empty,
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
}
