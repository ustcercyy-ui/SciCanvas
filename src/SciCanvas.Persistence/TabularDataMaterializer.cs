using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SciCanvas.Core.Data;

namespace SciCanvas.Persistence;

internal static partial class TabularDataMaterializer
{
    public static IReadOnlyList<DataColumn> SuggestColumns(
        RawTabularTable table,
        string fingerprintSha256,
        int inferenceRowCount)
    {
        int sampledRows = Math.Min(inferenceRowCount, table.Rows.Count);
        var columns = new DataColumn[table.Headers.Count];
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
        {
            string header = string.IsNullOrWhiteSpace(table.Headers[columnIndex])
                ? $"Column {columnIndex + 1}"
                : table.Headers[columnIndex].Trim();
            (string parsedName, string? unit) = ParseHeader(header);
            string uniqueName = parsedName;
            int suffix = 2;
            while (!usedNames.Add(uniqueName))
            {
                uniqueName = $"{parsedName} ({suffix++})";
            }

            TabularDataType dataType = InferDataType(
                table.Rows.Take(sampledRows).Select(row => row[columnIndex]));
            columns[columnIndex] = new DataColumn(
                CreateStableColumnId(fingerprintSha256, columnIndex, table.Headers[columnIndex]),
                uniqueName,
                dataType,
                unit);
        }

        return columns;
    }

    public static IReadOnlyList<TabularDataRow> MaterializeRows(
        IReadOnlyList<IReadOnlyList<string>> rawRows,
        IReadOnlyList<DataColumn> columns,
        int? maximumRows = null)
    {
        int rowCount = maximumRows is int maximum
            ? Math.Min(maximum, rawRows.Count)
            : rawRows.Count;
        var rows = new TabularDataRow[rowCount];
        for (int rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            IReadOnlyList<string> rawRow = rawRows[rowIndex];
            if (rawRow.Count != columns.Count)
            {
                throw new InvalidDataException("解析后的表格行宽度与列数不一致。");
            }

            var values = new TabularDataValue[columns.Count];
            for (int columnIndex = 0; columnIndex < columns.Count; columnIndex++)
            {
                values[columnIndex] = ParseValue(
                    rawRow[columnIndex],
                    columns[columnIndex],
                    rowIndex + 1);
            }

            rows[rowIndex] = new TabularDataRow(values);
        }

        return rows;
    }

    private static TabularDataValue ParseValue(
        string rawText,
        DataColumn column,
        int dataRowNumber)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return TabularDataValue.Missing;
        }

        string trimmed = rawText.Trim();
        switch (column.DataType)
        {
            case TabularDataType.Numeric:
                if (double.TryParse(
                        trimmed,
                        NumberStyles.Float | NumberStyles.AllowThousands,
                        CultureInfo.InvariantCulture,
                        out double number) &&
                    double.IsFinite(number))
                {
                    return TabularDataValue.FromNumber(rawText, number);
                }

                break;
            case TabularDataType.Boolean:
                if (bool.TryParse(trimmed, out bool boolean))
                {
                    return TabularDataValue.FromBoolean(rawText, boolean);
                }

                break;
            case TabularDataType.DateTime:
                if (DateTimeOffset.TryParse(
                    trimmed,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces,
                    out DateTimeOffset dateTime))
                {
                    return TabularDataValue.FromDateTime(rawText, dateTime);
                }

                break;
            case TabularDataType.Text:
                return TabularDataValue.FromText(rawText);
        }

        throw new InvalidDataException(
            $"数据第 {dataRowNumber} 行、列“{column.Name}”无法按 {column.DataType} 解析；请在预览中修正列类型或源数据。");
    }

    private static TabularDataType InferDataType(IEnumerable<string> values)
    {
        string[] present = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToArray();
        if (present.Length == 0)
        {
            return TabularDataType.Text;
        }

        if (present.All(value =>
                double.TryParse(
                    value,
                    NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture,
                    out double number) &&
                double.IsFinite(number)))
        {
            return TabularDataType.Numeric;
        }

        if (present.All(value => bool.TryParse(value, out _)))
        {
            return TabularDataType.Boolean;
        }

        if (present.All(value => DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out _)))
        {
            return TabularDataType.DateTime;
        }

        return TabularDataType.Text;
    }

    private static (string Name, string? Unit) ParseHeader(string header)
    {
        Match match = HeaderUnitPattern().Match(header);
        if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
        {
            return (header, null);
        }

        string unit = match.Groups[2].Value.Trim();
        return unit.Length is > 0 and <= 64
            ? (match.Groups[1].Value.Trim(), unit)
            : (header, null);
    }

    private static Guid CreateStableColumnId(string sha256, int index, string header)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{sha256}:{index}:{header}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    [GeneratedRegex(@"^(.+?)\s*[\(\[]\s*([^\)\]]+)\s*[\)\]]\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex HeaderUnitPattern();
}
