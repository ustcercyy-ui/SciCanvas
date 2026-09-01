using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Data;

public enum TabularDataType
{
    Numeric,
    Text,
    Boolean,
    DateTime,
}

public enum DataColumnRole
{
    X,
    Y,
    YError,
    Category,
    Label,
    Other,
}

public enum TabularDataFormat
{
    Csv,
    Tsv,
    Xlsx,
}

public sealed record DataColumn(
    Guid Id,
    string Name,
    TabularDataType DataType,
    string? Unit = null,
    DataColumnRole? Role = null)
{
    public DataColumn EnsureValid()
    {
        if (Id == Guid.Empty)
        {
            throw new InvalidDataException("数据列 ID 不能为空。");
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 256)
        {
            throw new InvalidDataException("数据列名称不能为空且不得超过 256 个字符。");
        }

        if (Unit is { } unit && (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 64))
        {
            throw new InvalidDataException("数据列单位不能为空白且不得超过 64 个字符。");
        }

        if (Role is DataColumnRole.X or DataColumnRole.Y or DataColumnRole.YError &&
            DataType != TabularDataType.Numeric)
        {
            throw new InvalidDataException("X、Y 与 YError 列必须是 Numeric 类型。");
        }

        return this;
    }
}

/// <summary>
/// Preserves the source token while exposing a single typed value. Missing
/// cells have every field set to null.
/// </summary>
public sealed record TabularDataValue(
    string? RawText,
    double? NumericValue = null,
    bool? BooleanValue = null,
    DateTimeOffset? DateTimeValue = null)
{
    public static TabularDataValue Missing { get; } = new((string?)null);

    public static TabularDataValue FromNumber(string rawText, double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), "数值单元格必须是有限数。");
        }

        return new TabularDataValue(RequireRawText(rawText), NumericValue: value);
    }

    public static TabularDataValue FromText(string value) =>
        new(RequireRawText(value));

    public static TabularDataValue FromBoolean(string rawText, bool value) =>
        new(RequireRawText(rawText), BooleanValue: value);

    public static TabularDataValue FromDateTime(string rawText, DateTimeOffset value) =>
        new(RequireRawText(rawText), DateTimeValue: value);

    public bool IsMissing =>
        RawText is null && NumericValue is null && BooleanValue is null && DateTimeValue is null;

    public void EnsureCompatible(TabularDataType dataType)
    {
        int typedValueCount =
            (NumericValue.HasValue ? 1 : 0) +
            (BooleanValue.HasValue ? 1 : 0) +
            (DateTimeValue.HasValue ? 1 : 0);
        if (IsMissing)
        {
            return;
        }

        if (string.IsNullOrEmpty(RawText) || typedValueCount > 1)
        {
            throw new InvalidDataException("数据单元格必须包含原始文本且只能有一个类型化值。");
        }

        bool compatible = dataType switch
        {
            TabularDataType.Numeric => NumericValue is { } number && double.IsFinite(number) &&
                BooleanValue is null && DateTimeValue is null,
            TabularDataType.Text => typedValueCount == 0,
            TabularDataType.Boolean => BooleanValue.HasValue &&
                NumericValue is null && DateTimeValue is null,
            TabularDataType.DateTime => DateTimeValue.HasValue &&
                NumericValue is null && BooleanValue is null,
            _ => false,
        };
        if (!compatible)
        {
            throw new InvalidDataException($"单元格值与列类型 {dataType} 不兼容。");
        }
    }

    private static string RequireRawText(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Length == 0
            ? throw new ArgumentException("非空单元格必须保留原始文本。", nameof(value))
            : value;
    }
}

public sealed record TabularDataRow(IReadOnlyList<TabularDataValue> Values);

public sealed record TabularImportMetadata
{
    public required TabularDataFormat Format { get; init; }

    public required DateTimeOffset ImportedAt { get; init; }

    public required string EncodingName { get; init; }

    public char? Delimiter { get; init; }

    public string? SheetName { get; init; }

    public string? SelectedRange { get; init; }

    /// <summary>One-based row index inside the selected text table or worksheet.</summary>
    public int HeaderRow { get; init; } = 1;

    public int DataRowCount { get; init; }

    public int InferenceRowCount { get; init; }

    public IReadOnlyList<string> OriginalHeaders { get; init; } = [];

    public TabularImportMetadata EnsureValid(int columnCount, int rowCount)
    {
        if (ImportedAt == default || string.IsNullOrWhiteSpace(EncodingName))
        {
            throw new InvalidDataException("数据导入元数据缺少时间或编码。");
        }

        if (HeaderRow < 1 || DataRowCount != rowCount || InferenceRowCount < 0 ||
            InferenceRowCount > rowCount || OriginalHeaders.Count != columnCount)
        {
            throw new InvalidDataException("数据导入元数据的行列计数无效。");
        }

        if (Format is TabularDataFormat.Csv or TabularDataFormat.Tsv && Delimiter is null)
        {
            throw new InvalidDataException("CSV/TSV 导入必须记录实际分隔符。");
        }

        if (Format == TabularDataFormat.Xlsx && string.IsNullOrWhiteSpace(SheetName))
        {
            throw new InvalidDataException("XLSX 导入必须记录工作表名称。");
        }

        return this;
    }
}

public sealed record TabularDataAsset(
    Guid Id,
    string Name,
    string? SourcePath,
    SourceFingerprint? Fingerprint,
    long SourceRevision,
    IReadOnlyList<DataColumn> Columns,
    IReadOnlyList<TabularDataRow> Rows,
    TabularImportMetadata ImportMetadata)
{
    public TabularDataAsset EnsureValid()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 256)
        {
            throw new InvalidDataException("TabularDataAsset 必须有有效 ID 和名称。");
        }

        bool hasPath = !string.IsNullOrWhiteSpace(SourcePath);
        if (hasPath != (Fingerprint is not null))
        {
            throw new InvalidDataException("外部表格来源路径与指纹必须同时存在或同时为空。");
        }

        if (SourceRevision < 1 || Columns.Count == 0 || Rows.Count == 0)
        {
            throw new InvalidDataException("数据修订必须大于 0，且资产必须包含列和数据行。");
        }

        if (Columns.Any(column => column is null) || Rows.Any(row => row is null))
        {
            throw new InvalidDataException("数据资产不能包含空列或空行。");
        }

        foreach (DataColumn column in Columns)
        {
            column.EnsureValid();
        }

        if (Columns.Select(column => column.Id).Distinct().Count() != Columns.Count ||
            Columns.Select(column => column.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != Columns.Count)
        {
            throw new InvalidDataException("数据列 ID 和名称必须唯一。");
        }

        foreach (TabularDataRow row in Rows)
        {
            if (row.Values.Count != Columns.Count)
            {
                throw new InvalidDataException("每个数据行的单元格数量必须与列数一致。");
            }

            for (int index = 0; index < Columns.Count; index++)
            {
                row.Values[index].EnsureCompatible(Columns[index].DataType);
            }
        }

        ImportMetadata.EnsureValid(Columns.Count, Rows.Count);
        return this;
    }
}
