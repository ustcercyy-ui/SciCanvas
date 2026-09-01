using System.Text;
using SciCanvas.Core.Data;

namespace SciCanvas.Persistence;

internal sealed class DelimitedTextTableReader(TabularDataFormat format) : IRawTabularTableReader
{
    private const long MaximumImportedCells = 10_000_000;

    public async Task<RawTabularTable> ReadAsync(
        string sourcePath,
        TabularDataImportOptions options,
        CancellationToken cancellationToken)
    {
        if (format is not (TabularDataFormat.Csv or TabularDataFormat.Tsv))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if (options.SheetName is not null || options.SelectedRange is not null)
        {
            throw new InvalidDataException("工作表与 A1 范围选择仅适用于 XLSX。");
        }

        (string text, string encodingName) = await ReadUtf8TextAsync(
            sourcePath,
            cancellationToken);
        char delimiter = options.Delimiter ?? DetectDelimiter(text, format);
        List<List<string>> parsedRows = ParseDelimited(text, delimiter);
        while (parsedRows.Count > 0 && IsBlank(parsedRows[^1]))
        {
            parsedRows.RemoveAt(parsedRows.Count - 1);
        }

        if (options.HeaderRow > parsedRows.Count)
        {
            throw new InvalidDataException("指定的表头行超出文本表格范围。");
        }

        int headerIndex = options.HeaderRow - 1;
        int width = parsedRows
            .Skip(headerIndex)
            .Select(row => row.Count)
            .DefaultIfEmpty(0)
            .Max();
        if (width == 0)
        {
            throw new InvalidDataException("表格未包含可导入的列。");
        }

        int dataRowCount = parsedRows.Count - options.HeaderRow;
        if (dataRowCount < 1)
        {
            throw new InvalidDataException("表格必须在表头之后包含至少一行数据。");
        }

        if ((long)width * dataRowCount > MaximumImportedCells)
        {
            throw new InvalidDataException(
                $"表格包含超过 {MaximumImportedCells:N0} 个单元格；请先选择较小的数据范围。");
        }

        IReadOnlyList<string> headers = PadRow(parsedRows[headerIndex], width);
        IReadOnlyList<IReadOnlyList<string>> rows = parsedRows
            .Skip(headerIndex + 1)
            .Select(row => (IReadOnlyList<string>)PadRow(row, width))
            .ToArray();
        return new RawTabularTable(
            format,
            encodingName,
            delimiter,
            [],
            null,
            null,
            options.HeaderRow,
            headers,
            rows);
    }

    private static async Task<(string Text, string EncodingName)> ReadUtf8TextAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] prefix = new byte[4];
        int prefixLength = await stream.ReadAsync(prefix, cancellationToken);
        bool utf8Bom = prefixLength >= 3 &&
            prefix[0] == 0xEF && prefix[1] == 0xBB && prefix[2] == 0xBF;
        bool unsupportedBom =
            prefixLength >= 2 &&
            ((prefix[0] == 0xFF && prefix[1] == 0xFE) ||
             (prefix[0] == 0xFE && prefix[1] == 0xFF)) ||
            prefixLength >= 4 &&
            ((prefix[0] == 0x00 && prefix[1] == 0x00 && prefix[2] == 0xFE && prefix[3] == 0xFF) ||
             (prefix[0] == 0xFF && prefix[1] == 0xFE && prefix[2] == 0x00 && prefix[3] == 0x00));
        if (unsupportedBom)
        {
            throw new InvalidDataException("CSV/TSV 仅支持 UTF-8 或 UTF-8 BOM 编码。");
        }

        stream.Position = 0;
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: true);
        try
        {
            string text = await reader.ReadToEndAsync(cancellationToken);
            return (text, utf8Bom ? "UTF-8 BOM" : "UTF-8");
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("CSV/TSV 包含无效 UTF-8 字节。", exception);
        }
    }

    private static char DetectDelimiter(string text, TabularDataFormat format)
    {
        char preferred = format == TabularDataFormat.Tsv ? '\t' : ',';
        char[] candidates = [preferred, ',', '\t', ';', '|'];
        int bestScore = int.MinValue;
        char best = preferred;
        foreach (char candidate in candidates.Distinct())
        {
            try
            {
                List<List<string>> rows = ParseDelimited(text, candidate);
                int[] widths = rows
                    .Where(row => !IsBlank(row))
                    .Take(30)
                    .Select(row => row.Count)
                    .ToArray();
                if (widths.Length == 0)
                {
                    continue;
                }

                int modeWidth = widths
                    .GroupBy(width => width)
                    .OrderByDescending(group => group.Count())
                    .ThenByDescending(group => group.Key)
                    .First().Key;
                int consistentRows = widths.Count(width => width == modeWidth);
                int score = modeWidth > 1
                    ? consistentRows * 100 + modeWidth * 5 - widths.Sum(width => Math.Abs(width - modeWidth))
                    : consistentRows;
                if (candidate == preferred)
                {
                    score++;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }
            catch (InvalidDataException)
            {
                // Try the next candidate; the selected delimiter is parsed strictly below.
            }
        }

        return best;
    }

    private static List<List<string>> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;
        bool fieldStartedWithQuote = false;
        for (int index = 0; index < text.Length; index++)
        {
            char current = text[index];
            if (inQuotes)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        field.Append('"');
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(current);
                }

                continue;
            }

            if (current == '"')
            {
                if (field.Length != 0 || fieldStartedWithQuote)
                {
                    throw new InvalidDataException("CSV/TSV 包含未正确转义的双引号。");
                }

                inQuotes = true;
                fieldStartedWithQuote = true;
            }
            else if (current == delimiter)
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStartedWithQuote = false;
            }
            else if (current is '\r' or '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                fieldStartedWithQuote = false;
                rows.Add(row);
                row = [];
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }
            }
            else
            {
                if (fieldStartedWithQuote)
                {
                    throw new InvalidDataException("CSV/TSV 引号字段结束后存在非法字符。");
                }

                field.Append(current);
            }
        }

        if (inQuotes)
        {
            throw new InvalidDataException("CSV/TSV 包含未闭合的引号字段。");
        }

        if (field.Length > 0 || fieldStartedWithQuote || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static bool IsBlank(IReadOnlyList<string> row) =>
        row.All(string.IsNullOrWhiteSpace);

    private static string[] PadRow(IReadOnlyList<string> row, int width)
    {
        var padded = new string[width];
        for (int index = 0; index < width; index++)
        {
            padded[index] = index < row.Count ? row[index] : string.Empty;
        }

        return padded;
    }
}
