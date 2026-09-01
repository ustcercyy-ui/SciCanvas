using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Data;

public sealed record TabularDataImportOptions
{
    public string? SheetName { get; init; }

    /// <summary>Optional A1 range for XLSX, for example B3:F200.</summary>
    public string? SelectedRange { get; init; }

    /// <summary>One-based row within the selected table/range that contains headers.</summary>
    public int HeaderRow { get; init; } = 1;

    public char? Delimiter { get; init; }

    public int PreviewRowCount { get; init; } = 20;

    public int InferenceRowCount { get; init; } = 1000;

    public TabularDataImportOptions EnsureValid()
    {
        if (HeaderRow < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(HeaderRow));
        }

        if (PreviewRowCount is < 1 or > 500 || InferenceRowCount is < 1 or > 100_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(PreviewRowCount),
                "预览行数必须为 1–500，类型推断行数必须为 1–100000。");
        }

        if (Delimiter is '\r' or '\n' or '"' or '\0')
        {
            throw new ArgumentException("分隔符不能是换行、双引号或 NUL。", nameof(Delimiter));
        }

        if (SheetName is { } sheet && string.IsNullOrWhiteSpace(sheet))
        {
            throw new ArgumentException("工作表名称不能为空白。", nameof(SheetName));
        }

        if (SelectedRange is { } range && string.IsNullOrWhiteSpace(range))
        {
            throw new ArgumentException("选择范围不能为空白。", nameof(SelectedRange));
        }

        return this;
    }
}

public sealed record TabularDataImportPreview(
    string SourcePath,
    SourceFingerprint Fingerprint,
    TabularDataFormat Format,
    string EncodingName,
    char? Delimiter,
    IReadOnlyList<string> AvailableSheets,
    string? SelectedSheetName,
    string? SelectedRange,
    int HeaderRow,
    IReadOnlyList<DataColumn> SuggestedColumns,
    IReadOnlyList<TabularDataRow> FirstRows,
    int TotalDataRowCount,
    int InferenceRowCount,
    IReadOnlyList<string> OriginalHeaders)
{
    public TabularDataImportPreview EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || SuggestedColumns.Count == 0 ||
            TotalDataRowCount < FirstRows.Count || TotalDataRowCount < 1 ||
            InferenceRowCount is < 1 || InferenceRowCount > TotalDataRowCount ||
            HeaderRow < 1 || OriginalHeaders.Count != SuggestedColumns.Count)
        {
            throw new InvalidDataException("表格导入预览缺少有效的来源、列或行信息。");
        }

        foreach (DataColumn column in SuggestedColumns)
        {
            column.EnsureValid();
        }

        foreach (TabularDataRow row in FirstRows)
        {
            if (row.Values.Count != SuggestedColumns.Count)
            {
                throw new InvalidDataException("预览行宽度与建议列不一致。");
            }

            for (int index = 0; index < SuggestedColumns.Count; index++)
            {
                row.Values[index].EnsureCompatible(SuggestedColumns[index].DataType);
            }
        }

        if (Format is TabularDataFormat.Csv or TabularDataFormat.Tsv && Delimiter is null)
        {
            throw new InvalidDataException("CSV/TSV 预览必须包含实际分隔符。");
        }

        if (Format == TabularDataFormat.Xlsx &&
            (AvailableSheets.Count == 0 || string.IsNullOrWhiteSpace(SelectedSheetName)))
        {
            throw new InvalidDataException("XLSX 预览必须包含可用和已选工作表。");
        }

        return this;
    }
}

public sealed record TabularDataImportConfirmation(
    string Name,
    IReadOnlyList<DataColumn>? Columns = null,
    Guid? AssetId = null);

public interface ITabularDataImporter
{
    Task<IReadOnlyList<string>> DiscoverSheetsAsync(
        string sourcePath,
        CancellationToken cancellationToken = default);

    Task<TabularDataImportPreview> PreviewAsync(
        string sourcePath,
        TabularDataImportOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<TabularDataAsset> ImportAsync(
        TabularDataImportPreview preview,
        TabularDataImportConfirmation confirmation,
        CancellationToken cancellationToken = default);
}
