using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Persistence;

public static class PlotSnapshotMapper
{
    public static ProjectPlotSnapshot ToSnapshot(PlotObject plot)
    {
        ArgumentNullException.ThrowIfNull(plot);
        return new ProjectPlotSnapshot
        {
            Id = plot.Id,
            Name = plot.Name,
            PlotType = ToPlotKindKey(plot.PlotType),
            Data = new ProjectPlotDataBindingSnapshot
            {
                DataAssetId = plot.Data.DataAssetId,
                SourceRevision = plot.Data.SourceRevision,
                XColumnId = plot.Data.XColumnId,
                YColumnId = plot.Data.YColumnId,
                ValueColumnId = plot.Data.ValueColumnId,
                ErrorBars = plot.Data.ErrorBars is null
                    ? null
                    : new ProjectPlotErrorBarBindingSnapshot
                    {
                        Mode = plot.Data.ErrorBars.Mode == PlotErrorBarMode.Symmetric
                            ? "symmetric"
                            : "asymmetric",
                        SymmetricColumnId = plot.Data.ErrorBars.SymmetricColumnId,
                        LowerColumnId = plot.Data.ErrorBars.LowerColumnId,
                        UpperColumnId = plot.Data.ErrorBars.UpperColumnId,
                    },
            },
            XAxis = ToSnapshot(plot.XAxis),
            YAxis = ToSnapshot(plot.YAxis),
            Typography = new ProjectPlotTypographySnapshot
            {
                Axis = ToSnapshot(plot.Typography.Axis),
                Tick = ToSnapshot(plot.Typography.Tick),
                Legend = ToSnapshot(plot.Typography.Legend),
                Annotation = ToSnapshot(plot.Typography.Annotation),
            },
            Style = new ProjectPlotSeriesStyleSnapshot
            {
                LineColor = plot.Style.LineColor,
                LineWidthPt = plot.Style.LineWidthPt,
                LineStyle = ToLineStyleKey(plot.Style.LineStyle),
                MarkerShape = ToMarkerShapeKey(plot.Style.MarkerShape),
                MarkerSizePt = plot.Style.MarkerSizePt,
                MarkerFill = plot.Style.MarkerFill,
                MarkerStroke = plot.Style.MarkerStroke,
            },
            Filter = plot.Filter is null
                ? null
                : new ProjectPlotFilterSnapshot
                {
                    ColumnId = plot.Filter.ColumnId,
                    Operator = ToFilterOperatorKey(plot.Filter.Operator),
                    Operand = plot.Filter.Operand,
                    Expression = plot.Filter.Expression,
                    ExcludedRowCount = plot.Filter.ExcludedRowCount,
                },
            Transforms = plot.Transforms
                .Select(transform => new ProjectPlotTransformSnapshot
                {
                    ColumnId = transform.ColumnId,
                    Kind = ToTransformKindKey(transform.Kind),
                    Parameter = transform.Parameter,
                    WindowSize = transform.WindowSize,
                    Alignment = transform.Alignment is null
                        ? null
                        : transform.Alignment == PlotMovingAverageAlignment.Centered
                            ? "centered"
                            : "trailing",
                })
                .ToArray(),
        };
    }

    public static PlotObject ToModel(
        ProjectPlotSnapshot snapshot,
        TabularDataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(asset);
        ProjectPlotDataBindingSnapshot data = snapshot.Data;
        ProjectPlotTypographySnapshot typography = snapshot.Typography;
        ProjectPlotSeriesStyleSnapshot style = snapshot.Style;
        var plot = new PlotObject
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            PlotType = ParsePlotKind(snapshot.PlotType),
            Data = new PlotDataBinding(
                data.DataAssetId,
                data.SourceRevision,
                data.XColumnId,
                data.YColumnId,
                data.ErrorBars is null
                    ? null
                    : new PlotErrorBarBinding(
                        ParseErrorMode(data.ErrorBars.Mode),
                        data.ErrorBars.SymmetricColumnId,
                        data.ErrorBars.LowerColumnId,
                        data.ErrorBars.UpperColumnId),
                data.ValueColumnId),
            XAxis = ToModel(snapshot.XAxis),
            YAxis = ToModel(snapshot.YAxis),
            Typography = new PlotTypography(
                ToModel(typography.Axis),
                ToModel(typography.Tick),
                ToModel(typography.Legend),
                ToModel(typography.Annotation)),
            Style = new PlotSeriesStyle(
                style.LineColor,
                style.LineWidthPt,
                ParseLineStyle(style.LineStyle),
                ParseMarkerShape(style.MarkerShape),
                style.MarkerSizePt,
                style.MarkerFill,
                style.MarkerStroke),
            Filter = snapshot.Filter is null
                ? null
                : new PlotDataFilter(
                    snapshot.Filter.ColumnId,
                    ParseFilterOperator(snapshot.Filter.Operator),
                    snapshot.Filter.Operand,
                    snapshot.Filter.Expression,
                    snapshot.Filter.ExcludedRowCount),
            Transforms = snapshot.Transforms
                .Select(transform => new PlotDataTransform(
                    transform.ColumnId,
                    ParseTransformKind(transform.Kind),
                    transform.Parameter,
                    transform.WindowSize,
                    transform.Alignment is null
                        ? null
                        : ParseMovingAverageAlignment(transform.Alignment)))
                .ToArray(),
        };
        return plot.EnsureValid(asset);
    }

    private static ProjectPlotAxisSnapshot ToSnapshot(PlotAxisDefinition axis) => new()
    {
        Title = axis.Title,
        Unit = axis.Unit,
        Scale = axis.Scale == PlotAxisScale.Linear ? "linear" : "log10",
        Minimum = axis.Minimum,
        Maximum = axis.Maximum,
        MajorTickInterval = axis.MajorTickInterval,
        MinorTickCount = axis.MinorTickCount,
    };

    private static PlotAxisDefinition ToModel(ProjectPlotAxisSnapshot axis) => new(
        axis.Title,
        axis.Unit,
        axis.Scale.ToLowerInvariant() switch
        {
            "linear" => PlotAxisScale.Linear,
            "log10" => PlotAxisScale.Log10,
            _ => throw new InvalidDataException($"未知 Plot axis scale：{axis.Scale}"),
        },
        axis.Minimum,
        axis.Maximum,
        axis.MajorTickInterval,
        axis.MinorTickCount);

    private static ProjectTextStyleSnapshot ToSnapshot(TextStyle style) => new()
    {
        FontFamily = style.FontFamily,
        FontSizePt = style.FontSizePt,
        IsBold = style.IsBold,
        Color = style.Color,
    };

    private static TextStyle ToModel(ProjectTextStyleSnapshot style) =>
        new(style.FontFamily, style.FontSizePt, style.IsBold, style.Color);

    private static string ToPlotKindKey(PlotKind kind) => kind switch
    {
        PlotKind.Line => "line",
        PlotKind.Scatter => "scatter",
        PlotKind.LineAndSymbol => "lineAndSymbol",
        PlotKind.ErrorBar => "errorBar",
        PlotKind.Histogram => "histogram",
        PlotKind.BoxPlot => "boxPlot",
        PlotKind.Heatmap => "heatmap",
        _ => throw new InvalidDataException("未知 Plot 类型。"),
    };

    private static PlotKind ParsePlotKind(string value) => value.ToLowerInvariant() switch
    {
        "line" => PlotKind.Line,
        "scatter" => PlotKind.Scatter,
        "lineandsymbol" => PlotKind.LineAndSymbol,
        "errorbar" => PlotKind.ErrorBar,
        "histogram" => PlotKind.Histogram,
        "boxplot" => PlotKind.BoxPlot,
        "heatmap" => PlotKind.Heatmap,
        _ => throw new InvalidDataException($"未知 Plot 类型：{value}"),
    };

    private static string ToLineStyleKey(PlotLineStyle style) => style switch
    {
        PlotLineStyle.Solid => "solid",
        PlotLineStyle.Dash => "dash",
        PlotLineStyle.Dot => "dot",
        PlotLineStyle.DashDot => "dashDot",
        _ => throw new InvalidDataException("未知 Plot line style。"),
    };

    private static PlotLineStyle ParseLineStyle(string value) => value.ToLowerInvariant() switch
    {
        "solid" => PlotLineStyle.Solid,
        "dash" => PlotLineStyle.Dash,
        "dot" => PlotLineStyle.Dot,
        "dashdot" => PlotLineStyle.DashDot,
        _ => throw new InvalidDataException($"未知 Plot line style：{value}"),
    };

    private static string ToMarkerShapeKey(PlotMarkerShape shape) => shape switch
    {
        PlotMarkerShape.None => "none",
        PlotMarkerShape.Circle => "circle",
        PlotMarkerShape.Square => "square",
        PlotMarkerShape.Triangle => "triangle",
        PlotMarkerShape.Diamond => "diamond",
        PlotMarkerShape.Cross => "cross",
        _ => throw new InvalidDataException("未知 Plot marker shape。"),
    };

    private static PlotMarkerShape ParseMarkerShape(string value) => value.ToLowerInvariant() switch
    {
        "none" => PlotMarkerShape.None,
        "circle" => PlotMarkerShape.Circle,
        "square" => PlotMarkerShape.Square,
        "triangle" => PlotMarkerShape.Triangle,
        "diamond" => PlotMarkerShape.Diamond,
        "cross" => PlotMarkerShape.Cross,
        _ => throw new InvalidDataException($"未知 Plot marker shape：{value}"),
    };

    private static PlotErrorBarMode ParseErrorMode(string value) => value.ToLowerInvariant() switch
    {
        "symmetric" => PlotErrorBarMode.Symmetric,
        "asymmetric" => PlotErrorBarMode.Asymmetric,
        _ => throw new InvalidDataException($"未知 error bar mode：{value}"),
    };

    private static string ToFilterOperatorKey(PlotFilterOperator value) => value switch
    {
        PlotFilterOperator.Equal => "equal",
        PlotFilterOperator.NotEqual => "notEqual",
        PlotFilterOperator.LessThan => "lessThan",
        PlotFilterOperator.LessThanOrEqual => "lessThanOrEqual",
        PlotFilterOperator.GreaterThan => "greaterThan",
        PlotFilterOperator.GreaterThanOrEqual => "greaterThanOrEqual",
        PlotFilterOperator.IsMissing => "isMissing",
        PlotFilterOperator.IsNotMissing => "isNotMissing",
        _ => throw new InvalidDataException("未知 Plot filter operator。"),
    };

    private static PlotFilterOperator ParseFilterOperator(string value) =>
        value.ToLowerInvariant() switch
        {
            "equal" => PlotFilterOperator.Equal,
            "notequal" => PlotFilterOperator.NotEqual,
            "lessthan" => PlotFilterOperator.LessThan,
            "lessthanorequal" => PlotFilterOperator.LessThanOrEqual,
            "greaterthan" => PlotFilterOperator.GreaterThan,
            "greaterthanorequal" => PlotFilterOperator.GreaterThanOrEqual,
            "ismissing" => PlotFilterOperator.IsMissing,
            "isnotmissing" => PlotFilterOperator.IsNotMissing,
            _ => throw new InvalidDataException($"未知 Plot filter operator：{value}"),
        };

    private static string ToTransformKindKey(PlotTransformKind value) => value switch
    {
        PlotTransformKind.NormalizeMinMax => "normalizeMinMax",
        PlotTransformKind.Offset => "offset",
        PlotTransformKind.Log10 => "log10",
        PlotTransformKind.MovingAverage => "movingAverage",
        _ => throw new InvalidDataException("未知 Plot transform kind。"),
    };

    private static PlotTransformKind ParseTransformKind(string value) =>
        value.ToLowerInvariant() switch
        {
            "normalizeminmax" => PlotTransformKind.NormalizeMinMax,
            "offset" => PlotTransformKind.Offset,
            "log10" => PlotTransformKind.Log10,
            "movingaverage" => PlotTransformKind.MovingAverage,
            _ => throw new InvalidDataException($"未知 Plot transform kind：{value}"),
        };

    private static PlotMovingAverageAlignment ParseMovingAverageAlignment(string value) =>
        value.ToLowerInvariant() switch
        {
            "centered" => PlotMovingAverageAlignment.Centered,
            "trailing" => PlotMovingAverageAlignment.Trailing,
            _ => throw new InvalidDataException($"未知 moving-average alignment：{value}"),
        };
}
