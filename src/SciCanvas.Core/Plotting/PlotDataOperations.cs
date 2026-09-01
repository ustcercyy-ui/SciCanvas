using System.Globalization;
using SciCanvas.Core.Data;

namespace SciCanvas.Core.Plotting;

public enum PlotFilterOperator
{
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    IsMissing,
    IsNotMissing,
}

public enum PlotTransformKind
{
    NormalizeMinMax,
    Offset,
    Log10,
    MovingAverage,
}

public enum PlotMovingAverageAlignment
{
    Centered,
    Trailing,
}

public sealed record PlotDataFilter(
    Guid ColumnId,
    PlotFilterOperator Operator,
    string? Operand,
    string Expression,
    int ExcludedRowCount)
{
    public static PlotDataFilter Create(
        TabularDataAsset asset,
        Guid columnId,
        PlotFilterOperator filterOperator,
        string? operand)
    {
        ArgumentNullException.ThrowIfNull(asset);
        DataColumn column = RequireColumn(asset, columnId);
        string? normalizedOperand = NormalizeOperand(column, filterOperator, operand);
        string expression = BuildExpression(column, filterOperator, normalizedOperand);
        int excluded = asset.Rows.Count(row =>
            !Evaluate(row, asset, columnId, filterOperator, normalizedOperand));
        return new PlotDataFilter(
            columnId,
            filterOperator,
            normalizedOperand,
            expression,
            excluded);
    }

    public PlotDataFilter EnsureValid(TabularDataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(asset);
        DataColumn column = RequireColumn(asset, ColumnId);
        string? normalizedOperand = NormalizeOperand(column, Operator, Operand);
        string expectedExpression = BuildExpression(column, Operator, normalizedOperand);
        if (!string.Equals(Expression, expectedExpression, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Filter expression 与结构化列/操作符/操作数不一致。");
        }

        int actualExcluded = asset.Rows.Count(row =>
            !Evaluate(row, asset, ColumnId, Operator, normalizedOperand));
        if (ExcludedRowCount < 0 ||
            ExcludedRowCount > asset.Rows.Count ||
            ExcludedRowCount != actualExcluded)
        {
            throw new InvalidDataException(
                "Filter excluded row count 与原始 DataAsset 重算结果不一致。");
        }

        return this;
    }

    public bool Includes(TabularDataRow row, TabularDataAsset asset)
    {
        EnsureValid(asset);
        return Evaluate(row, asset, ColumnId, Operator, Operand);
    }

    internal bool IncludesValidated(TabularDataRow row, TabularDataAsset asset) =>
        Evaluate(row, asset, ColumnId, Operator, Operand);

    private static bool Evaluate(
        TabularDataRow row,
        TabularDataAsset asset,
        Guid columnId,
        PlotFilterOperator filterOperator,
        string? operand)
    {
        int index = FindColumnIndex(asset, columnId);
        TabularDataValue value = row.Values[index];
        if (filterOperator == PlotFilterOperator.IsMissing)
        {
            return value.IsMissing;
        }

        if (filterOperator == PlotFilterOperator.IsNotMissing)
        {
            return !value.IsMissing;
        }

        if (value.IsMissing)
        {
            return false;
        }

        DataColumn column = asset.Columns[index];
        int comparison = column.DataType switch
        {
            TabularDataType.Numeric => value.NumericValue!.Value.CompareTo(
                double.Parse(operand!, NumberStyles.Float, CultureInfo.InvariantCulture)),
            TabularDataType.Text => string.Compare(
                value.RawText,
                operand,
                StringComparison.Ordinal),
            TabularDataType.Boolean => value.BooleanValue!.Value.CompareTo(
                bool.Parse(operand!)),
            TabularDataType.DateTime => value.DateTimeValue!.Value.CompareTo(
                DateTimeOffset.Parse(
                    operand!,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind)),
            _ => throw new InvalidDataException("未知 Filter 列类型。"),
        };
        return filterOperator switch
        {
            PlotFilterOperator.Equal => comparison == 0,
            PlotFilterOperator.NotEqual => comparison != 0,
            PlotFilterOperator.LessThan => comparison < 0,
            PlotFilterOperator.LessThanOrEqual => comparison <= 0,
            PlotFilterOperator.GreaterThan => comparison > 0,
            PlotFilterOperator.GreaterThanOrEqual => comparison >= 0,
            _ => throw new InvalidDataException("未知 Filter 操作符。"),
        };
    }

    private static string? NormalizeOperand(
        DataColumn column,
        PlotFilterOperator filterOperator,
        string? operand)
    {
        if (filterOperator is PlotFilterOperator.IsMissing or PlotFilterOperator.IsNotMissing)
        {
            if (!string.IsNullOrWhiteSpace(operand))
            {
                throw new InvalidDataException("missing Filter 不接受操作数。");
            }

            return null;
        }

        string text = operand?.Trim() ??
            throw new InvalidDataException("Filter 缺少操作数。");
        if (text.Length == 0 || text.Length > 512)
        {
            throw new InvalidDataException("Filter 操作数不能为空且不得超过 512 字符。");
        }

        bool equalityOnly = column.DataType is TabularDataType.Text or TabularDataType.Boolean;
        if (equalityOnly &&
            filterOperator is not (PlotFilterOperator.Equal or PlotFilterOperator.NotEqual))
        {
            throw new InvalidDataException("Text/Boolean Filter 只支持 Equal 与 NotEqual。");
        }

        return column.DataType switch
        {
            TabularDataType.Numeric => double.TryParse(
                    text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double number) &&
                double.IsFinite(number)
                    ? number.ToString("R", CultureInfo.InvariantCulture)
                    : throw new InvalidDataException("Numeric Filter 必须使用 invariant finite number。"),
            TabularDataType.Boolean => bool.TryParse(text, out bool boolean)
                ? boolean.ToString().ToLowerInvariant()
                : throw new InvalidDataException("Boolean Filter 操作数必须是 true 或 false。"),
            TabularDataType.DateTime => DateTimeOffset.TryParse(
                    text,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out DateTimeOffset timestamp)
                ? timestamp.ToString("O", CultureInfo.InvariantCulture)
                : throw new InvalidDataException("DateTime Filter 必须是可解析的 ISO 时间。"),
            _ => text,
        };
    }

    private static string BuildExpression(
        DataColumn column,
        PlotFilterOperator filterOperator,
        string? operand)
    {
        string columnLabel = EscapeExpressionText(column.Name);
        string left = $"column(\"{columnLabel}\", {column.Id:D})";
        return filterOperator switch
        {
            PlotFilterOperator.IsMissing => $"{left} is missing",
            PlotFilterOperator.IsNotMissing => $"{left} is not missing",
            _ => $"{left} {OperatorToken(filterOperator)} {QuoteOperand(column, operand!)}",
        };
    }

    private static string QuoteOperand(DataColumn column, string operand) =>
        column.DataType == TabularDataType.Text
            ? $"\"{EscapeExpressionText(operand)}\""
            : operand;

    private static string EscapeExpressionText(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string OperatorToken(PlotFilterOperator filterOperator) =>
        filterOperator switch
        {
            PlotFilterOperator.Equal => "==",
            PlotFilterOperator.NotEqual => "!=",
            PlotFilterOperator.LessThan => "<",
            PlotFilterOperator.LessThanOrEqual => "<=",
            PlotFilterOperator.GreaterThan => ">",
            PlotFilterOperator.GreaterThanOrEqual => ">=",
            _ => throw new InvalidDataException("未知 Filter 操作符。"),
        };

    private static DataColumn RequireColumn(TabularDataAsset asset, Guid columnId)
    {
        if (columnId == Guid.Empty)
        {
            throw new InvalidDataException("Filter column ID 不能为空。");
        }

        return asset.Columns.FirstOrDefault(column => column.Id == columnId) ??
            throw new InvalidDataException("Filter column 不属于绑定 DataAsset。");
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

        throw new InvalidDataException("Filter column 不属于绑定 DataAsset。");
    }
}

public sealed record PlotDataTransform(
    Guid ColumnId,
    PlotTransformKind Kind,
    double? Parameter = null,
    int? WindowSize = null,
    PlotMovingAverageAlignment? Alignment = null)
{
    public PlotDataTransform EnsureValid(PlotObject plot, TabularDataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(asset);
        DataColumn column = asset.Columns.FirstOrDefault(item => item.Id == ColumnId) ??
            throw new InvalidDataException("Transform column 不属于绑定 DataAsset。");
        if (column.DataType != TabularDataType.Numeric)
        {
            throw new InvalidDataException("Transform 只能绑定 Numeric 列。");
        }

        HashSet<Guid> transformableColumns =
        [
            plot.Data.YColumnId,
            .. plot.Data.XColumnId is { } x ? [x] : Array.Empty<Guid>(),
            .. plot.Data.ValueColumnId is { } value ? [value] : Array.Empty<Guid>(),
        ];
        if (!transformableColumns.Contains(ColumnId))
        {
            throw new InvalidDataException("Transform 只能作用于 Plot 的 X、Y 或 value 列。");
        }

        bool validParameters = Kind switch
        {
            PlotTransformKind.NormalizeMinMax =>
                Parameter is null && WindowSize is null && Alignment is null,
            PlotTransformKind.Offset =>
                Parameter is { } offset && double.IsFinite(offset) &&
                WindowSize is null && Alignment is null,
            PlotTransformKind.Log10 =>
                Parameter is null && WindowSize is null && Alignment is null,
            PlotTransformKind.MovingAverage =>
                Parameter is null &&
                WindowSize is >= 2 and <= 10_000 &&
                Alignment.HasValue,
            _ => false,
        };
        if (!validParameters)
        {
            throw new InvalidDataException("Transform 参数与 kind 不一致。");
        }

        if (plot.PlotType == PlotKind.ErrorBar &&
            ColumnId == plot.Data.YColumnId &&
            Kind != PlotTransformKind.Offset)
        {
            throw new InvalidDataException(
                "Error Bar 的 Y 列仅允许 offset；其他变换需要显式误差传播，当前不会猜测。");
        }

        return this;
    }

    public string ToAuditText(DataColumn column) => Kind switch
    {
        PlotTransformKind.NormalizeMinMax =>
            $"normalize-minmax({column.Name}, columnId={ColumnId:D})",
        PlotTransformKind.Offset =>
            $"offset({column.Name}, {Parameter!.Value.ToString("R", CultureInfo.InvariantCulture)}, columnId={ColumnId:D})",
        PlotTransformKind.Log10 =>
            $"log10({column.Name}, columnId={ColumnId:D})",
        PlotTransformKind.MovingAverage =>
            $"moving-average({column.Name}, window={WindowSize}, alignment={Alignment}, columnId={ColumnId:D})",
        _ => throw new InvalidDataException("未知 Transform kind。"),
    };
}

public sealed record ProjectedPlotRow(
    int SourceRowIndex,
    double? OriginalX,
    double? OriginalY,
    double? OriginalValue,
    double? X,
    double? Y,
    double? Value,
    double? ErrorLower,
    double? ErrorUpper,
    string? Category);

public sealed record PlotDataProjection(
    IReadOnlyList<ProjectedPlotRow> Rows,
    int SourceRowCount,
    int ExcludedRowCount,
    int IncludedRowCount,
    int UnplottableRowCount,
    IReadOnlyList<string> AppliedTransforms);

public static class PlotDataProjector
{
    public static PlotDataProjection Project(
        PlotObject plot,
        TabularDataAsset asset)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(asset);
        plot.EnsureValidForProjection(asset);
        return ProjectValidated(plot, asset);
    }

    internal static void ValidateOperations(
        PlotObject plot,
        TabularDataAsset asset)
    {
        PlotDataProjection projection = ProjectValidated(plot, asset);
        if (plot.PlotType == PlotKind.Heatmap)
        {
            _ = HeatmapDomainBuilder.Build(plot, projection);
        }
    }

    private static PlotDataProjection ProjectValidated(
        PlotObject plot,
        TabularDataAsset asset)
    {
        int[] includedIndexes = asset.Rows
            .Select((row, index) => (row, index))
            .Where(item => plot.Filter?.IncludesValidated(item.row, asset) ?? true)
            .Select(item => item.index)
            .ToArray();
        if (includedIndexes.Length == 0)
        {
            throw new InvalidDataException(
                "Filter excluded every source row；不能创建空 Plot。");
        }

        Guid[] numericColumnIds = GetProjectionNumericColumnIds(plot).Distinct().ToArray();
        var valuesByColumn = numericColumnIds.ToDictionary(
            columnId => columnId,
            columnId =>
            {
                int columnIndex = FindColumnIndex(asset, columnId);
                return includedIndexes
                    .Select(rowIndex => asset.Rows[rowIndex].Values[columnIndex].NumericValue)
                    .ToArray();
            });
        var appliedTransforms = new List<string>(plot.Transforms.Count);
        foreach (PlotDataTransform transform in plot.Transforms)
        {
            DataColumn column = asset.Columns[FindColumnIndex(asset, transform.ColumnId)];
            valuesByColumn[transform.ColumnId] = ApplyTransform(
                valuesByColumn[transform.ColumnId],
                transform);
            appliedTransforms.Add(transform.ToAuditText(column));
        }

        ValidateProjectedAxes(plot, asset, includedIndexes, valuesByColumn);

        int? xIndex = plot.Data.XColumnId is { } xId ? FindColumnIndex(asset, xId) : null;
        int yIndex = FindColumnIndex(asset, plot.Data.YColumnId);
        int? valueIndex = plot.Data.ValueColumnId is { } valueId
            ? FindColumnIndex(asset, valueId)
            : null;
        int? symmetricIndex = plot.Data.ErrorBars?.SymmetricColumnId is { } symmetricId
            ? FindColumnIndex(asset, symmetricId)
            : null;
        int? lowerIndex = plot.Data.ErrorBars?.LowerColumnId is { } lowerId
            ? FindColumnIndex(asset, lowerId)
            : null;
        int? upperIndex = plot.Data.ErrorBars?.UpperColumnId is { } upperId
            ? FindColumnIndex(asset, upperId)
            : null;
        var projectedRows = new List<ProjectedPlotRow>(includedIndexes.Length);
        int unplottable = 0;
        for (int projectionIndex = 0; projectionIndex < includedIndexes.Length; projectionIndex++)
        {
            int sourceRowIndex = includedIndexes[projectionIndex];
            TabularDataRow sourceRow = asset.Rows[sourceRowIndex];
            double? originalX = xIndex.HasValue
                ? sourceRow.Values[xIndex.Value].NumericValue
                : null;
            double? originalY = sourceRow.Values[yIndex].NumericValue;
            double? originalValue = valueIndex.HasValue
                ? sourceRow.Values[valueIndex.Value].NumericValue
                : null;
            double? x = plot.Data.XColumnId is { } xColumnId
                ? valuesByColumn[xColumnId][projectionIndex]
                : null;
            double? y = valuesByColumn[plot.Data.YColumnId][projectionIndex];
            double? value = plot.Data.ValueColumnId is { } valueColumnId
                ? valuesByColumn[valueColumnId][projectionIndex]
                : null;
            double? lower = symmetricIndex.HasValue
                ? sourceRow.Values[symmetricIndex.Value].NumericValue
                : lowerIndex.HasValue
                    ? sourceRow.Values[lowerIndex.Value].NumericValue
                    : null;
            double? upper = symmetricIndex.HasValue
                ? sourceRow.Values[symmetricIndex.Value].NumericValue
                : upperIndex.HasValue
                    ? sourceRow.Values[upperIndex.Value].NumericValue
                    : null;
            string? category = plot.PlotType == PlotKind.BoxPlot && xIndex.HasValue
                ? sourceRow.Values[xIndex.Value].RawText
                : null;
            var row = new ProjectedPlotRow(
                sourceRowIndex,
                originalX,
                originalY,
                originalValue,
                x,
                y,
                value,
                lower,
                upper,
                category);
            if (!IsPlottable(plot.PlotType, row))
            {
                unplottable++;
            }

            projectedRows.Add(row);
        }

        return new PlotDataProjection(
            projectedRows,
            asset.Rows.Count,
            asset.Rows.Count - includedIndexes.Length,
            includedIndexes.Length,
            unplottable,
            appliedTransforms);
    }

    private static IReadOnlyList<Guid> GetProjectionNumericColumnIds(PlotObject plot)
    {
        var ids = new List<Guid> { plot.Data.YColumnId };
        if (plot.Data.XColumnId is { } x)
        {
            ids.Add(x);
        }

        if (plot.Data.ValueColumnId is { } value)
        {
            ids.Add(value);
        }

        return ids;
    }

    private static double?[] ApplyTransform(
        IReadOnlyList<double?> values,
        PlotDataTransform transform)
    {
        return transform.Kind switch
        {
            PlotTransformKind.NormalizeMinMax => Normalize(values),
            PlotTransformKind.Offset => values
                .Select(value => value + transform.Parameter)
                .ToArray(),
            PlotTransformKind.Log10 => Log10(values),
            PlotTransformKind.MovingAverage => MovingAverage(
                values,
                transform.WindowSize!.Value,
                transform.Alignment!.Value),
            _ => throw new InvalidDataException("未知 Transform kind。"),
        };
    }

    private static void ValidateProjectedAxes(
        PlotObject plot,
        TabularDataAsset asset,
        IReadOnlyList<int> includedIndexes,
        IReadOnlyDictionary<Guid, double?[]> valuesByColumn)
    {
        Guid? xAxisColumnId = plot.PlotType switch
        {
            PlotKind.Histogram => plot.Data.YColumnId,
            PlotKind.BoxPlot => null,
            _ => plot.Data.XColumnId,
        };
        if (plot.XAxis.Scale == PlotAxisScale.Log10 &&
            xAxisColumnId is { } xColumnId)
        {
            EnsureProjectedPositive(valuesByColumn[xColumnId], "X");
        }

        if (plot.YAxis.Scale == PlotAxisScale.Log10 &&
            plot.PlotType != PlotKind.Histogram)
        {
            double?[] projectedY = valuesByColumn[plot.Data.YColumnId];
            EnsureProjectedPositive(projectedY, "Y");
            if (plot.Data.ErrorBars is { } errorBars)
            {
                int[] errorIndexes = errorBars.ColumnIds
                    .Select(columnId => FindColumnIndex(asset, columnId))
                    .ToArray();
                for (int index = 0; index < includedIndexes.Count; index++)
                {
                    if (projectedY[index] is not { } y)
                    {
                        continue;
                    }

                    TabularDataRow row = asset.Rows[includedIndexes[index]];
                    if (errorIndexes
                        .Select(errorIndex => row.Values[errorIndex].NumericValue)
                        .Where(value => value.HasValue)
                        .Any(error => y - error!.Value <= 0))
                    {
                        throw new InvalidDataException(
                            "Transform 后的 log Y 误差范围触及非正数；不会静默裁掉误差条。");
                    }
                }
            }
        }
    }

    private static void EnsureProjectedPositive(
        IReadOnlyList<double?> values,
        string axisName)
    {
        if (values.Any(value => value is <= 0))
        {
            throw new InvalidDataException(
                $"Transform 后的 {axisName} log 轴包含非正数；不会静默移除数据点。");
        }
    }

    private static double?[] Normalize(IReadOnlyList<double?> values)
    {
        double[] present = values
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (present.Length == 0)
        {
            throw new InvalidDataException("normalize 列没有可变换数值。");
        }

        double minimum = present.Min();
        double maximum = present.Max();
        double range = maximum - minimum;
        return values
            .Select(value => value.HasValue
                ? range == 0 ? 0 : (value.Value - minimum) / range
                : (double?)null)
            .ToArray();
    }

    private static double?[] Log10(IReadOnlyList<double?> values)
    {
        if (values.Any(value => value is <= 0))
        {
            throw new InvalidDataException(
                "log10 transform 遇到非正数；SciCanvas 不会静默移除该行。");
        }

        return values
            .Select(value => value.HasValue ? Math.Log10(value.Value) : (double?)null)
            .ToArray();
    }

    private static double?[] MovingAverage(
        IReadOnlyList<double?> values,
        int windowSize,
        PlotMovingAverageAlignment alignment)
    {
        var result = new double?[values.Count];
        for (int index = 0; index < values.Count; index++)
        {
            int start;
            int end;
            if (alignment == PlotMovingAverageAlignment.Trailing)
            {
                start = Math.Max(0, index - windowSize + 1);
                end = index;
            }
            else
            {
                int before = (windowSize - 1) / 2;
                int after = windowSize - 1 - before;
                start = Math.Max(0, index - before);
                end = Math.Min(values.Count - 1, index + after);
            }

            double[] present = values
                .Skip(start)
                .Take(end - start + 1)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            result[index] = present.Length == 0 ? null : present.Average();
        }

        return result;
    }

    private static bool IsPlottable(PlotKind kind, ProjectedPlotRow row) => kind switch
    {
        PlotKind.Histogram or PlotKind.BoxPlot => row.Y.HasValue,
        PlotKind.Heatmap => row.X.HasValue && row.Y.HasValue && row.Value.HasValue,
        PlotKind.ErrorBar => row.X.HasValue && row.Y.HasValue &&
            row.ErrorLower.HasValue && row.ErrorUpper.HasValue,
        _ => row.X.HasValue && row.Y.HasValue,
    };

    private static int FindColumnIndex(TabularDataAsset asset, Guid columnId)
    {
        for (int index = 0; index < asset.Columns.Count; index++)
        {
            if (asset.Columns[index].Id == columnId)
            {
                return index;
            }
        }

        throw new InvalidDataException("Plot operation 引用了不存在的数据列。");
    }
}

public sealed record PlotScientificProvenance(
    Guid PlotId,
    Guid DataAssetId,
    long SourceRevision,
    Guid? XColumnId,
    Guid YColumnId,
    IReadOnlyList<Guid> ErrorColumnIds,
    Guid? ValueColumnId,
    string? FilterExpression,
    int ExcludedRowCount,
    IReadOnlyList<string> Transforms,
    PlotKind PlotType,
    PlotSeriesStyle Style,
    int SourceRowCount,
    int IncludedRowCount,
    int UnplottableRowCount,
    HeatmapScientificProvenance? Heatmap = null);

public sealed record HeatmapColorbarScientificProvenance(
    string Binding,
    string Orientation,
    string Position,
    double Minimum,
    double Maximum,
    string? Unit,
    IReadOnlyList<double> Ticks,
    IReadOnlyList<string> TickLabels);

public sealed record HeatmapScientificProvenance(
    string RequestedGridKind,
    string EffectiveGridKind,
    string DuplicateCellPolicy,
    bool DuplicateAggregationApplied,
    string Colormap,
    double Minimum,
    double Maximum,
    string Scale,
    string ClampMode,
    string? NoDataColor,
    HeatmapColorbarScientificProvenance? Colorbar,
    IReadOnlyList<string> DomainIssueCodes);

public static class HeatmapScientificProvenanceBuilder
{
    public static HeatmapScientificProvenance Create(HeatmapDomain domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return new HeatmapScientificProvenance(
            domain.RequestedGridKind.ToString(),
            domain.EffectiveGridKind.ToString(),
            domain.DuplicateCellPolicy.ToString(),
            domain.Issues.Any(issue => issue.Code == HeatmapQcCodes.DuplicateCell),
            domain.Colormap,
            domain.Minimum,
            domain.Maximum,
            domain.Scale.ToString(),
            domain.ClampMode.ToString(),
            domain.NoDataColor,
            domain.Colorbar is null
                ? null
                : new HeatmapColorbarScientificProvenance(
                    domain.Colorbar.Binding.ToString(),
                    domain.Colorbar.Orientation.ToString(),
                    domain.Colorbar.Position.ToString(),
                    domain.Colorbar.Minimum,
                    domain.Colorbar.Maximum,
                    domain.Colorbar.Unit,
                    domain.Colorbar.Ticks,
                    domain.Colorbar.TickLabels),
            domain.Issues.Select(issue => issue.Code).Distinct(StringComparer.Ordinal).ToArray());
    }
}

public static class PlotScientificProvenanceBuilder
{
    public static PlotScientificProvenance Create(
        PlotObject plot,
        TabularDataAsset asset)
    {
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);
        HeatmapScientificProvenance? heatmap = plot.PlotType == PlotKind.Heatmap
            ? HeatmapScientificProvenanceBuilder.Create(HeatmapDomainBuilder.Build(plot, projection))
            : null;
        return new PlotScientificProvenance(
            plot.Id,
            plot.Data.DataAssetId,
            plot.Data.SourceRevision,
            plot.Data.XColumnId,
            plot.Data.YColumnId,
            plot.Data.ErrorBars?.ColumnIds ?? [],
            plot.Data.ValueColumnId,
            plot.Filter?.Expression,
            projection.ExcludedRowCount,
            projection.AppliedTransforms,
            plot.PlotType,
            plot.Style,
            projection.SourceRowCount,
            projection.IncludedRowCount,
            projection.UnplottableRowCount,
            heatmap);
    }
}
