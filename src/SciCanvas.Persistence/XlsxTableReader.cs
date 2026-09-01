using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SciCanvas.Core.Data;

namespace SciCanvas.Persistence;

internal sealed partial class XlsxTableReader : IRawTabularTableReader
{
    private const long MaximumImportedCells = 10_000_000;
    private static readonly XNamespace Spreadsheet =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace OfficeRelationships =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PackageRelationships =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyList<string> DiscoverSheetNames(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XDocument workbook = LoadRequiredXml(archive, "xl/workbook.xml");
        return workbook
            .Descendants(Spreadsheet + "sheet")
            .Select(element => (string?)element.Attribute("name") ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToArray();
    }

    public Task<RawTabularTable> ReadAsync(
        string sourcePath,
        TabularDataImportOptions options,
        CancellationToken cancellationToken)
    {
        if (options.Delimiter is not null)
        {
            throw new InvalidDataException("XLSX 不使用文本分隔符。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        XDocument workbook = LoadRequiredXml(archive, "xl/workbook.xml");
        XDocument relationships = LoadRequiredXml(
            archive,
            "xl/_rels/workbook.xml.rels");
        Dictionary<string, string> relationshipTargets = relationships
            .Descendants(PackageRelationships + "Relationship")
            .Where(element => !string.Equals(
                (string?)element.Attribute("TargetMode"),
                "External",
                StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                element => (string?)element.Attribute("Id") ?? string.Empty,
                element => NormalizeWorkbookTarget(
                    (string?)element.Attribute("Target") ?? string.Empty),
                StringComparer.Ordinal);
        SheetReference[] sheets = workbook
            .Descendants(Spreadsheet + "sheet")
            .Select(element => new SheetReference(
                (string?)element.Attribute("name") ?? string.Empty,
                (string?)element.Attribute(OfficeRelationships + "id") ?? string.Empty))
            .Where(sheet => sheet.Name.Length > 0 && sheet.RelationshipId.Length > 0)
            .ToArray();
        if (sheets.Length == 0)
        {
            throw new InvalidDataException("XLSX 未包含可读取的工作表。");
        }

        SheetReference selected = options.SheetName is null
            ? sheets[0]
            : sheets.FirstOrDefault(sheet => string.Equals(
                    sheet.Name,
                    options.SheetName,
                    StringComparison.OrdinalIgnoreCase)) ??
                throw new InvalidDataException($"XLSX 不包含工作表“{options.SheetName}”。");
        if (!relationshipTargets.TryGetValue(selected.RelationshipId, out string? worksheetPath))
        {
            throw new InvalidDataException("XLSX 工作表关系缺失或指向外部资源。");
        }

        IReadOnlyList<string> sharedStrings = ReadSharedStrings(archive);
        XDocument worksheet = LoadRequiredXml(archive, worksheetPath);
        Dictionary<CellCoordinate, string> cells = ReadCells(
            worksheet,
            sharedStrings,
            cancellationToken);
        if (cells.Count == 0)
        {
            throw new InvalidDataException($"工作表“{selected.Name}”不包含可导入单元格。");
        }

        CellRange range = options.SelectedRange is null
            ? CellRange.FromCells(cells.Keys)
            : ParseRange(options.SelectedRange);
        int headerWorksheetRow = checked(range.StartRow + options.HeaderRow - 1);
        if (headerWorksheetRow > range.EndRow)
        {
            throw new InvalidDataException("指定表头行超出 XLSX 选择范围。");
        }

        int width = checked(range.EndColumn - range.StartColumn + 1);
        int dataRowCount = range.EndRow - headerWorksheetRow;
        if (dataRowCount < 1)
        {
            throw new InvalidDataException("XLSX 选择范围必须在表头之后包含至少一行数据。");
        }

        if ((long)width * dataRowCount > MaximumImportedCells)
        {
            throw new InvalidDataException(
                $"XLSX 选择范围超过 {MaximumImportedCells:N0} 个单元格；请缩小 A1 范围。");
        }

        string[] headers = ReadRow(cells, headerWorksheetRow, range.StartColumn, range.EndColumn);
        var rows = new IReadOnlyList<string>[dataRowCount];
        for (int index = 0; index < dataRowCount; index++)
        {
            rows[index] = ReadRow(
                cells,
                headerWorksheetRow + index + 1,
                range.StartColumn,
                range.EndColumn);
        }

        var table = new RawTabularTable(
            TabularDataFormat.Xlsx,
            "OOXML",
            null,
            sheets.Select(sheet => sheet.Name).ToArray(),
            selected.Name,
            range.ToString(),
            options.HeaderRow,
            headers,
            rows);
        return Task.FromResult(table);
    }

    private static Dictionary<CellCoordinate, string> ReadCells(
        XDocument worksheet,
        IReadOnlyList<string> sharedStrings,
        CancellationToken cancellationToken)
    {
        var cells = new Dictionary<CellCoordinate, string>();
        int fallbackRow = 0;
        foreach (XElement row in worksheet.Descendants(Spreadsheet + "row"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            int rowNumber = ParsePositiveInt((string?)row.Attribute("r")) ?? ++fallbackRow;
            fallbackRow = Math.Max(fallbackRow, rowNumber);
            int fallbackColumn = 0;
            foreach (XElement cell in row.Elements(Spreadsheet + "c"))
            {
                string? reference = (string?)cell.Attribute("r");
                CellCoordinate coordinate = reference is null
                    ? new CellCoordinate(++fallbackColumn, rowNumber)
                    : ParseCellReference(reference);
                fallbackColumn = Math.Max(fallbackColumn, coordinate.Column);
                string value = ReadCellValue(cell, sharedStrings);
                if (value.Length > 0)
                {
                    cells[coordinate] = value;
                }
            }
        }

        return cells;
    }

    private static string ReadCellValue(
        XElement cell,
        IReadOnlyList<string> sharedStrings)
    {
        string? type = (string?)cell.Attribute("t");
        if (string.Equals(type, "inlineStr", StringComparison.Ordinal))
        {
            return string.Concat(cell.Descendants(Spreadsheet + "t").Select(text => text.Value));
        }

        string value = cell.Element(Spreadsheet + "v")?.Value ?? string.Empty;
        if (string.Equals(type, "s", StringComparison.Ordinal))
        {
            if (!int.TryParse(value, out int index) || index < 0 || index >= sharedStrings.Count)
            {
                throw new InvalidDataException("XLSX shared string 索引无效。");
            }

            return sharedStrings[index];
        }

        return string.Equals(type, "b", StringComparison.Ordinal)
            ? value == "1" ? "TRUE" : "FALSE"
            : value;
    }

    private static IReadOnlyList<string> ReadSharedStrings(ZipArchive archive)
    {
        ZipArchiveEntry? entry = archive.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
        {
            return [];
        }

        using Stream stream = entry.Open();
        XDocument document = XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        return document
            .Descendants(Spreadsheet + "si")
            .Select(item => string.Concat(
                item.Descendants(Spreadsheet + "t").Select(text => text.Value)))
            .ToArray();
    }

    private static XDocument LoadRequiredXml(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = archive.GetEntry(path) ??
            throw new InvalidDataException($"XLSX 缺少 {path}。");
        using Stream stream = entry.Open();
        try
        {
            return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new InvalidDataException($"XLSX 内部 XML {path} 无效。", exception);
        }
    }

    private static string NormalizeWorkbookTarget(string target)
    {
        string normalized = Uri.UnescapeDataString(target).Replace('\\', '/');
        string combined = normalized.StartsWith('/')
            ? normalized.TrimStart('/')
            : "xl/" + normalized;
        var segments = new List<string>();
        foreach (string segment in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                if (segments.Count == 0)
                {
                    throw new InvalidDataException("XLSX 工作表路径越界。");
                }

                segments.RemoveAt(segments.Count - 1);
            }
            else
            {
                segments.Add(segment);
            }
        }

        string path = string.Join('/', segments);
        return path.StartsWith("xl/", StringComparison.Ordinal)
            ? path
            : throw new InvalidDataException("XLSX 工作表路径不在 xl 目录中。");
    }

    private static CellRange ParseRange(string value)
    {
        Match match = A1RangePattern().Match(value.Trim());
        if (!match.Success)
        {
            throw new InvalidDataException("XLSX 范围必须使用 A1 或 A1:D200 格式。");
        }

        CellCoordinate start = ParseCellReference(match.Groups[1].Value);
        CellCoordinate end = match.Groups[2].Success
            ? ParseCellReference(match.Groups[2].Value)
            : start;
        if (end.Column < start.Column || end.Row < start.Row)
        {
            throw new InvalidDataException("XLSX 范围终点不得位于起点之前。");
        }

        return new CellRange(start.Column, start.Row, end.Column, end.Row);
    }

    private static CellCoordinate ParseCellReference(string value)
    {
        Match match = A1CellPattern().Match(value.Trim());
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out int row))
        {
            throw new InvalidDataException($"XLSX 单元格引用“{value}”无效。");
        }

        int column = 0;
        foreach (char character in match.Groups[1].Value.ToUpperInvariant())
        {
            column = checked(column * 26 + character - 'A' + 1);
        }

        return new CellCoordinate(column, row);
    }

    private static int? ParsePositiveInt(string? value) =>
        int.TryParse(value, out int parsed) && parsed > 0 ? parsed : null;

    private static string[] ReadRow(
        IReadOnlyDictionary<CellCoordinate, string> cells,
        int row,
        int startColumn,
        int endColumn)
    {
        var values = new string[endColumn - startColumn + 1];
        for (int column = startColumn; column <= endColumn; column++)
        {
            values[column - startColumn] = cells.GetValueOrDefault(
                new CellCoordinate(column, row),
                string.Empty);
        }

        return values;
    }

    private static string ColumnName(int column)
    {
        var characters = new Stack<char>();
        while (column > 0)
        {
            column--;
            characters.Push((char)('A' + column % 26));
            column /= 26;
        }

        return new string(characters.ToArray());
    }

    private sealed record SheetReference(string Name, string RelationshipId);

    private readonly record struct CellCoordinate(int Column, int Row);

    private readonly record struct CellRange(
        int StartColumn,
        int StartRow,
        int EndColumn,
        int EndRow)
    {
        public static CellRange FromCells(IEnumerable<CellCoordinate> cells)
        {
            CellCoordinate[] values = cells.ToArray();
            return new CellRange(
                values.Min(cell => cell.Column),
                values.Min(cell => cell.Row),
                values.Max(cell => cell.Column),
                values.Max(cell => cell.Row));
        }

        public override string ToString() =>
            $"{ColumnName(StartColumn)}{StartRow}:{ColumnName(EndColumn)}{EndRow}";
    }

    [GeneratedRegex(@"^([A-Za-z]+[1-9][0-9]*)(?::([A-Za-z]+[1-9][0-9]*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex A1RangePattern();

    [GeneratedRegex(@"^([A-Za-z]+)([1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex A1CellPattern();
}
