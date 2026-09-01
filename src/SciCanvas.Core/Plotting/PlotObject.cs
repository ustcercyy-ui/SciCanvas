using SciCanvas.Core.Data;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Plotting;

public enum PlotKind
{
    Line,
    Scatter,
    LineAndSymbol,
    ErrorBar,
    Histogram,
    BoxPlot,
    Heatmap,
}

public enum PlotAxisScale
{
    Linear,
    Log10,
}

public enum PlotLineStyle
{
    Solid,
    Dash,
    Dot,
    DashDot,
}

public enum PlotMarkerShape
{
    None,
    Circle,
    Square,
    Triangle,
    Diamond,
    Cross,
}

public enum PlotErrorBarMode
{
    Symmetric,
    Asymmetric,
}

public sealed record PlotAxisDefinition(
    string Title,
    string? Unit,
    PlotAxisScale Scale,
    double? Minimum,
    double? Maximum,
    double? MajorTickInterval,
    int MinorTickCount)
{
    public static PlotAxisDefinition DefaultX { get; } =
        new("X", null, PlotAxisScale.Linear, null, null, null, 4);

    public static PlotAxisDefinition DefaultY { get; } =
        new("Y", null, PlotAxisScale.Linear, null, null, null, 4);

    public PlotAxisDefinition EnsureValid()
    {
        if (Title is null || Title.Trim().Length > 256 ||
            Unit is { } unit && (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 64))
        {
            throw new InvalidDataException("坐标轴标题或单位无效。");
        }

        if (Minimum is { } minimum && !double.IsFinite(minimum) ||
            Maximum is { } maximum && !double.IsFinite(maximum) ||
            MajorTickInterval is { } interval && (!double.IsFinite(interval) || interval <= 0))
        {
            throw new InvalidDataException("坐标轴范围与主刻度必须是有效有限数值。");
        }

        if (Minimum is { } min && Maximum is { } max && min >= max)
        {
            throw new InvalidDataException("坐标轴最小值必须小于最大值。");
        }

        if (Scale == PlotAxisScale.Log10 &&
            (Minimum is <= 0 || Maximum is <= 0))
        {
            throw new InvalidDataException("对数坐标轴的显式范围必须大于 0。");
        }

        if (MinorTickCount is < 0 or > 20)
        {
            throw new InvalidDataException("次刻度数量必须在 0–20 之间。");
        }

        return this;
    }
}

public sealed record PlotTypography(
    TextStyle Axis,
    TextStyle Tick,
    TextStyle Legend,
    TextStyle Annotation)
{
    public static PlotTypography Default { get; } = new(
        new TextStyle("Arial", 9, false, "#FF111111"),
        new TextStyle("Arial", 8, false, "#FF111111"),
        new TextStyle("Arial", 8, false, "#FF111111"),
        new TextStyle("Arial", 8, false, "#FF111111"));

    public PlotTypography EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Axis);
        ArgumentNullException.ThrowIfNull(Tick);
        ArgumentNullException.ThrowIfNull(Legend);
        ArgumentNullException.ThrowIfNull(Annotation);
        Axis.EnsureValid();
        Tick.EnsureValid();
        Legend.EnsureValid();
        Annotation.EnsureValid();
        return this;
    }
}

public sealed record PlotSeriesStyle(
    string LineColor,
    double LineWidthPt,
    PlotLineStyle LineStyle,
    PlotMarkerShape MarkerShape,
    double MarkerSizePt,
    string MarkerFill,
    string MarkerStroke)
{
    public static PlotSeriesStyle Default { get; } = new(
        "#FF2563EB",
        1.25,
        PlotLineStyle.Solid,
        PlotMarkerShape.Circle,
        5,
        "#FFFFFFFF",
        "#FF2563EB");

    public PlotSeriesStyle EnsureValid()
    {
        if (!ScientificStyleColor.ValidateColor(LineColor) ||
            !ScientificStyleColor.ValidateColor(MarkerFill) ||
            !ScientificStyleColor.ValidateColor(MarkerStroke) ||
            !double.IsFinite(LineWidthPt) || LineWidthPt is < 0.25 or > 10 ||
            !double.IsFinite(MarkerSizePt) || MarkerSizePt is < 1 or > 72)
        {
            throw new InvalidDataException("Plot series 的线条或 marker 样式无效。");
        }

        return this;
    }
}

/// <summary>
/// Stores source column identities rather than copied error values so error bars
/// remain traceable to the imported table.
/// </summary>
public sealed record PlotErrorBarBinding(
    PlotErrorBarMode Mode,
    Guid? SymmetricColumnId = null,
    Guid? LowerColumnId = null,
    Guid? UpperColumnId = null)
{
    public IReadOnlyList<Guid> ColumnIds => Mode switch
    {
        PlotErrorBarMode.Symmetric when SymmetricColumnId.HasValue =>
            [SymmetricColumnId.Value],
        PlotErrorBarMode.Asymmetric when LowerColumnId.HasValue && UpperColumnId.HasValue =>
            [LowerColumnId.Value, UpperColumnId.Value],
        _ => [],
    };

    public PlotErrorBarBinding EnsureValid()
    {
        bool valid = Mode switch
        {
            PlotErrorBarMode.Symmetric =>
                SymmetricColumnId is { } symmetric && symmetric != Guid.Empty &&
                LowerColumnId is null && UpperColumnId is null,
            PlotErrorBarMode.Asymmetric =>
                SymmetricColumnId is null &&
                LowerColumnId is { } lower && lower != Guid.Empty &&
                UpperColumnId is { } upper && upper != Guid.Empty &&
                lower != upper,
            _ => false,
        };
        if (!valid)
        {
            throw new InvalidDataException("误差列绑定必须明确为一个对称列或两个不同的非对称列。");
        }

        return this;
    }
}

public sealed record PlotDataBinding(
    Guid DataAssetId,
    long SourceRevision,
    Guid? XColumnId,
    Guid YColumnId,
    PlotErrorBarBinding? ErrorBars = null,
    Guid? ValueColumnId = null)
{
    public PlotDataBinding EnsureValid(PlotKind plotType, TabularDataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        asset.EnsureValid();
        if (DataAssetId == Guid.Empty || DataAssetId != asset.Id ||
            SourceRevision < 1 || SourceRevision != asset.SourceRevision)
        {
            throw new InvalidDataException("Plot 数据绑定必须匹配 DataAsset ID 与 SourceRevision。");
        }

        DataColumn y = RequireColumn(asset, YColumnId, "Y");
        switch (plotType)
        {
            case PlotKind.Line:
            case PlotKind.Scatter:
            case PlotKind.LineAndSymbol:
                RequireNumeric(asset, XColumnId, "X");
                RequireNumeric(y, "Y");
                RequireAbsent(ErrorBars, ValueColumnId);
                break;

            case PlotKind.ErrorBar:
                RequireNumeric(asset, XColumnId, "X");
                RequireNumeric(y, "Y");
                if (ErrorBars is null)
                {
                    throw new InvalidDataException("Error Bar plot 必须绑定原始误差列。");
                }

                ErrorBars.EnsureValid();
                foreach (Guid errorColumnId in ErrorBars.ColumnIds)
                {
                    RequireNumeric(RequireColumn(asset, errorColumnId, "Error"), "Error");
                }

                if (ValueColumnId is not null)
                {
                    throw new InvalidDataException("Error Bar plot 不使用 heatmap value 列。");
                }

                break;

            case PlotKind.Histogram:
                RequireNumeric(y, "Y");
                if (XColumnId is not null || ErrorBars is not null || ValueColumnId is not null)
                {
                    throw new InvalidDataException("Histogram 只绑定一个数值列。");
                }

                break;

            case PlotKind.BoxPlot:
                RequireNumeric(y, "Y");
                if (XColumnId is { } categoryId)
                {
                    _ = RequireColumn(asset, categoryId, "Category");
                }

                RequireAbsent(ErrorBars, ValueColumnId);
                break;

            case PlotKind.Heatmap:
                RequireNumeric(asset, XColumnId, "X");
                RequireNumeric(y, "Y");
                RequireNumeric(asset, ValueColumnId, "Value");
                if (ErrorBars is not null)
                {
                    throw new InvalidDataException("Heatmap 不支持误差列绑定。");
                }

                break;

            default:
                throw new InvalidDataException("未知 Plot 类型。");
        }

        return this;
    }

    private static void RequireAbsent(
        PlotErrorBarBinding? errorBars,
        Guid? valueColumnId)
    {
        if (errorBars is not null || valueColumnId is not null)
        {
            throw new InvalidDataException("当前 Plot 类型包含不适用的数据列绑定。");
        }
    }

    private static DataColumn RequireNumeric(
        TabularDataAsset asset,
        Guid? columnId,
        string role) =>
        RequireNumeric(
            columnId is { } id && id != Guid.Empty
                ? RequireColumn(asset, id, role)
                : throw new InvalidDataException($"{role} 数值列不能为空。"),
            role);

    private static DataColumn RequireNumeric(DataColumn column, string role)
    {
        if (column.DataType != TabularDataType.Numeric)
        {
            throw new InvalidDataException($"{role} 列必须是 Numeric 类型。");
        }

        return column;
    }

    private static DataColumn RequireColumn(
        TabularDataAsset asset,
        Guid columnId,
        string role)
    {
        if (columnId == Guid.Empty)
        {
            throw new InvalidDataException($"{role} 列 ID 不能为空。");
        }

        return asset.Columns.FirstOrDefault(column => column.Id == columnId) ??
            throw new InvalidDataException($"{role} 列不属于绑定的 DataAsset。");
    }
}

public sealed record PlotObject
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required PlotKind PlotType { get; init; }

    public required PlotDataBinding Data { get; init; }

    public required PlotAxisDefinition XAxis { get; init; }

    public required PlotAxisDefinition YAxis { get; init; }

    public required PlotTypography Typography { get; init; }

    public required PlotSeriesStyle Style { get; init; }

    public PlotDataFilter? Filter { get; init; }

    public IReadOnlyList<PlotDataTransform> Transforms { get; init; } = [];

    public PlotObject EnsureValid(TabularDataAsset asset) =>
        EnsureValidCore(asset, validateOperations: true);

    internal PlotObject EnsureValidForProjection(TabularDataAsset asset) =>
        EnsureValidCore(asset, validateOperations: false);

    private PlotObject EnsureValidCore(
        TabularDataAsset asset,
        bool validateOperations)
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 256)
        {
            throw new InvalidDataException("PlotObject 必须有有效 ID 和名称。");
        }

        ArgumentNullException.ThrowIfNull(Data);
        ArgumentNullException.ThrowIfNull(XAxis);
        ArgumentNullException.ThrowIfNull(YAxis);
        ArgumentNullException.ThrowIfNull(Typography);
        ArgumentNullException.ThrowIfNull(Style);
        Data.EnsureValid(PlotType, asset);
        XAxis.EnsureValid();
        YAxis.EnsureValid();
        Typography.EnsureValid();
        Style.EnsureValid();
        Filter?.EnsureValid(asset);
        EnsureAxisDataCompatibility(asset);
        if (Transforms.Any(transform => transform is null))
        {
            throw new InvalidDataException("Plot transform 列表不能包含空项。");
        }

        foreach (PlotDataTransform transform in Transforms)
        {
            transform.EnsureValid(this, asset);
        }

        if (validateOperations)
        {
            PlotDataProjector.ValidateOperations(this, asset);
        }

        return this;
    }

    private void EnsureAxisDataCompatibility(TabularDataAsset asset)
    {
        if (PlotType == PlotKind.BoxPlot && XAxis.Scale == PlotAxisScale.Log10)
        {
            throw new InvalidDataException("Box Plot 的 category 轴不能使用 log scale。");
        }

        TabularDataRow[] includedRows = asset.Rows
            .Where(row => Filter?.IncludesValidated(row, asset) ?? true)
            .ToArray();
        Guid? xValueColumnId = PlotType switch
        {
            PlotKind.Histogram => Data.YColumnId,
            PlotKind.BoxPlot => null,
            _ => Data.XColumnId,
        };
        if (XAxis.Scale == PlotAxisScale.Log10 && xValueColumnId is { } xColumnId)
        {
            EnsurePositiveValues(asset, includedRows, xColumnId, "X");
        }

        bool yAxisUsesSourceValues = PlotType != PlotKind.Histogram;
        if (YAxis.Scale == PlotAxisScale.Log10 && yAxisUsesSourceValues)
        {
            EnsurePositiveValues(asset, includedRows, Data.YColumnId, "Y");
        }

        if (Data.ErrorBars is not { } errorBars)
        {
            return;
        }

        int yIndex = FindColumnIndex(asset, Data.YColumnId);
        int[] errorIndexes = errorBars.ColumnIds
            .Select(columnId => FindColumnIndex(asset, columnId))
            .ToArray();
        foreach (TabularDataRow row in includedRows)
        {
            double[] errors = errorIndexes
                .Select(index => row.Values[index].NumericValue)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            if (errors.Any(error => error < 0))
            {
                throw new InvalidDataException("Error Bar 的误差列不能包含负值。");
            }

            if (YAxis.Scale == PlotAxisScale.Log10 &&
                row.Values[yIndex].NumericValue is { } y &&
                errors.Any(error => y - error <= 0))
            {
                throw new InvalidDataException(
                    "对数 Y 轴的误差范围触及非正数；SciCanvas 不会静默裁掉误差条。");
            }
        }
    }

    private static void EnsurePositiveValues(
        TabularDataAsset asset,
        IReadOnlyList<TabularDataRow> rows,
        Guid columnId,
        string axisName)
    {
        int index = FindColumnIndex(asset, columnId);
        if (rows.Any(row => row.Values[index].NumericValue is <= 0))
        {
            throw new InvalidDataException(
                $"{axisName} 对数轴绑定列包含非正数；SciCanvas 不会静默移除数据点。");
        }
    }

    private static int FindColumnIndex(TabularDataAsset asset, Guid columnId)
    {
        for (int index = 0; index < asset.Columns.Count; index++)
        {
            if (asset.Columns[index].Id == columnId)
            {
                return index;
            }
        }

        throw new InvalidDataException("Plot 引用的数据列不存在。");
    }
}
