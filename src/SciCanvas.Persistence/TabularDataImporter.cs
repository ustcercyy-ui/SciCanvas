using System.Security.Cryptography;
using SciCanvas.Core.Data;
using SciCanvas.Core.Sources;

namespace SciCanvas.Persistence;

/// <summary>
/// Implements the mandatory preview-then-confirm import boundary. Every read
/// uses FileAccess.Read and confirmation revalidates the exact source
/// fingerprint observed by the preview.
/// </summary>
public sealed class TabularDataImporter : ITabularDataImporter
{
    public async Task<IReadOnlyList<string>> DiscoverSheetsAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        string fullPath = NormalizeSourcePath(sourcePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        SourceFingerprint before = await CreateFingerprintAsync(fullPath, cancellationToken);
        IReadOnlyList<string> sheets = XlsxTableReader.DiscoverSheetNames(
            fullPath,
            cancellationToken);
        SourceFingerprint after = await CreateFingerprintAsync(fullPath, cancellationToken);
        EnsureSameFingerprint(before, after, "读取工作表列表期间来源发生变化，请重试。");
        return sheets.Count > 0
            ? sheets
            : throw new InvalidDataException("XLSX 未包含可读取的工作表。");
    }

    public async Task<TabularDataImportPreview> PreviewAsync(
        string sourcePath,
        TabularDataImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        string fullPath = NormalizeSourcePath(sourcePath);
        TabularDataImportOptions actualOptions = (options ?? new TabularDataImportOptions())
            .EnsureValid();
        IRawTabularTableReader reader = CreateReader(fullPath);
        SourceFingerprint before = await CreateFingerprintAsync(fullPath, cancellationToken);
        RawTabularTable table = await reader.ReadAsync(
            fullPath,
            actualOptions,
            cancellationToken);
        SourceFingerprint after = await CreateFingerprintAsync(fullPath, cancellationToken);
        EnsureSameFingerprint(before, after, "预览期间源表格发生变化，请重新预览。");

        int inferenceRows = Math.Min(actualOptions.InferenceRowCount, table.Rows.Count);
        IReadOnlyList<DataColumn> columns = TabularDataMaterializer.SuggestColumns(
            table,
            after.Sha256,
            inferenceRows);
        IReadOnlyList<TabularDataRow> firstRows = TabularDataMaterializer.MaterializeRows(
            table.Rows,
            columns,
            actualOptions.PreviewRowCount);
        return new TabularDataImportPreview(
            fullPath,
            after,
            table.Format,
            table.EncodingName,
            table.Delimiter,
            table.AvailableSheets,
            table.SelectedSheetName,
            table.SelectedRange,
            table.HeaderRow,
            columns,
            firstRows,
            table.Rows.Count,
            inferenceRows,
            table.Headers).EnsureValid();
    }

    public async Task<TabularDataAsset> ImportAsync(
        TabularDataImportPreview preview,
        TabularDataImportConfirmation confirmation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(confirmation);
        preview.EnsureValid();
        if (string.IsNullOrWhiteSpace(confirmation.Name) || confirmation.Name.Trim().Length > 256)
        {
            throw new InvalidDataException("确认导入时必须提供不超过 256 个字符的数据资产名称。");
        }

        string fullPath = NormalizeSourcePath(preview.SourcePath);
        SourceFingerprint before = await CreateFingerprintAsync(fullPath, cancellationToken);
        EnsureSameFingerprint(
            preview.Fingerprint,
            before,
            "源表格自预览后已改变；为避免导入与预览不一致，请重新预览。");
        var options = new TabularDataImportOptions
        {
            SheetName = preview.SelectedSheetName,
            SelectedRange = preview.SelectedRange,
            HeaderRow = preview.HeaderRow,
            Delimiter = preview.Delimiter,
            PreviewRowCount = Math.Max(1, preview.FirstRows.Count),
            InferenceRowCount = preview.InferenceRowCount,
        };
        RawTabularTable table = await CreateReader(fullPath).ReadAsync(
            fullPath,
            options,
            cancellationToken);
        SourceFingerprint after = await CreateFingerprintAsync(fullPath, cancellationToken);
        EnsureSameFingerprint(
            before,
            after,
            "导入期间源表格发生变化；未创建数据资产，请重新预览。");
        if (!table.Headers.SequenceEqual(preview.OriginalHeaders, StringComparer.Ordinal) ||
            table.Rows.Count != preview.TotalDataRowCount)
        {
            throw new InvalidDataException("重新读取的表格结构与预览不一致；未创建数据资产。");
        }

        IReadOnlyList<DataColumn> columns = confirmation.Columns ?? preview.SuggestedColumns;
        if (columns.Count != table.Headers.Count)
        {
            throw new InvalidDataException("确认列数量与预览不一致。");
        }

        foreach (DataColumn column in columns)
        {
            column.EnsureValid();
        }

        IReadOnlyList<TabularDataRow> rows = TabularDataMaterializer.MaterializeRows(
            table.Rows,
            columns);
        var metadata = new TabularImportMetadata
        {
            Format = table.Format,
            ImportedAt = DateTimeOffset.UtcNow,
            EncodingName = table.EncodingName,
            Delimiter = table.Delimiter,
            SheetName = table.SelectedSheetName,
            SelectedRange = table.SelectedRange,
            HeaderRow = table.HeaderRow,
            DataRowCount = rows.Count,
            InferenceRowCount = preview.InferenceRowCount,
            OriginalHeaders = table.Headers,
        };
        return new TabularDataAsset(
            confirmation.AssetId ?? Guid.NewGuid(),
            confirmation.Name.Trim(),
            fullPath,
            after,
            1,
            columns.ToArray(),
            rows,
            metadata).EnsureValid();
    }

    private static IRawTabularTableReader CreateReader(string fullPath) =>
        Path.GetExtension(fullPath).ToLowerInvariant() switch
        {
            ".csv" => new DelimitedTextTableReader(TabularDataFormat.Csv),
            ".tsv" => new DelimitedTextTableReader(TabularDataFormat.Tsv),
            ".xlsx" => new XlsxTableReader(),
            _ => throw new NotSupportedException("表格导入仅支持 CSV、TSV 与 XLSX。"),
        };

    private static string NormalizeSourcePath(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        string fullPath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("表格来源文件不存在。", fullPath);
        }

        _ = CreateReader(fullPath);
        return fullPath;
    }

    private static async Task<SourceFingerprint> CreateFingerprintAsync(
        string fullPath,
        CancellationToken cancellationToken)
    {
        var information = new FileInfo(fullPath);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        information.Refresh();
        return new SourceFingerprint(
            information.Length,
            information.LastWriteTimeUtc,
            Convert.ToHexString(hash),
            null);
    }

    private static void EnsureSameFingerprint(
        SourceFingerprint expected,
        SourceFingerprint actual,
        string message)
    {
        if (expected.ByteLength != actual.ByteLength ||
            expected.LastWriteTimeUtc != actual.LastWriteTimeUtc ||
            !string.Equals(expected.Sha256, actual.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(message);
        }
    }
}
