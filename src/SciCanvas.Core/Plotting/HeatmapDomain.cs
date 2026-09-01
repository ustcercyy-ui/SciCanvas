using System.Globalization;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Plotting;

public static class HeatmapQcCodes
{
    public const string NoValueColumn = "plot.heatmap.no-value-column";
    public const string DuplicateCell = "plot.heatmap.duplicate-cell";
    public const string GridIncomplete = "plot.heatmap.grid-incomplete";
    public const string IrregularGrid = "plot.heatmap.irregular-grid";
    public const string InvalidRange = "plot.heatmap.invalid-range";
    public const string LogNonpositive = "plot.heatmap.log-nonpositive";
    public const string ColorbarMismatch = "plot.heatmap.colorbar-mismatch";
}

public enum HeatmapGridKind { Auto, RegularGrid, IrregularGrid, PointCloud }
public enum HeatmapDuplicateCellPolicy { Error, Mean, Median, Min, Max }
public enum PlotColorScaleKind { Linear, Log10 }
public enum PlotColorClampMode { Clamp, Error }
public enum PlotColorbarBinding { Linked, Detached }
public enum PlotColorbarOrientation { Vertical, Horizontal }
public enum PlotColorbarPosition { Right, Left, Top, Bottom }
public enum HeatmapDomainIssueSeverity { Notice, Warning }

public sealed record HeatmapGridDefinition(
    HeatmapGridKind Kind = HeatmapGridKind.Auto,
    HeatmapDuplicateCellPolicy DuplicateCellPolicy = HeatmapDuplicateCellPolicy.Error)
{
    public static HeatmapGridDefinition Default { get; } = new();

    public HeatmapGridDefinition EnsureValid()
    {
        if (!Enum.IsDefined(Kind) || !Enum.IsDefined(DuplicateCellPolicy))
        {
            throw new InvalidDataException("Heatmap grid definition is invalid.");
        }

        return this;
    }
}

public sealed record PlotColorScale(
    string Colormap = "viridis",
    double? Minimum = null,
    double? Maximum = null,
    PlotColorScaleKind Scale = PlotColorScaleKind.Linear,
    PlotColorClampMode ClampMode = PlotColorClampMode.Clamp,
    string? NoDataColor = null,
    bool ShowColorbar = true)
{
    public static PlotColorScale Default { get; } = new();

    public PlotColorScale EnsureValid()
    {
        if (!ScientificColormap.IsSupported(Colormap) ||
            !Enum.IsDefined(Scale) || !Enum.IsDefined(ClampMode) ||
            Minimum is { } minimum && !double.IsFinite(minimum) ||
            Maximum is { } maximum && !double.IsFinite(maximum) ||
            Minimum is { } min && Maximum is { } max && min >= max ||
            Scale == PlotColorScaleKind.Log10 && (Minimum is <= 0 || Maximum is <= 0) ||
            !string.IsNullOrWhiteSpace(NoDataColor) && !ScientificStyleColor.ValidateColor(NoDataColor))
        {
            throw new HeatmapDomainException(
                Scale == PlotColorScaleKind.Log10 && (Minimum is <= 0 || Maximum is <= 0)
                    ? HeatmapQcCodes.LogNonpositive
                    : HeatmapQcCodes.InvalidRange,
                "Heatmap color scale is invalid.");
        }

        return this;
    }
}

public sealed record PlotColorbarDefinition(
    PlotColorbarBinding Binding = PlotColorbarBinding.Linked,
    PlotColorbarOrientation Orientation = PlotColorbarOrientation.Vertical,
    PlotColorbarPosition Position = PlotColorbarPosition.Right,
    double? Minimum = null,
    double? Maximum = null,
    string? Unit = null,
    IReadOnlyList<double>? Ticks = null,
    TextStyle? LabelStyle = null,
    IReadOnlyList<string>? TickLabels = null)
{
    public static PlotColorbarDefinition Default { get; } = new();

    public PlotColorbarDefinition EnsureValid()
    {
        bool incompatiblePlacement =
            Orientation == PlotColorbarOrientation.Vertical && Position is PlotColorbarPosition.Top or PlotColorbarPosition.Bottom ||
            Orientation == PlotColorbarOrientation.Horizontal && Position is PlotColorbarPosition.Left or PlotColorbarPosition.Right;
        if (!Enum.IsDefined(Binding) || !Enum.IsDefined(Orientation) || !Enum.IsDefined(Position) ||
            incompatiblePlacement ||
            Minimum is { } minimum && !double.IsFinite(minimum) ||
            Maximum is { } maximum && !double.IsFinite(maximum) ||
            Minimum is { } min && Maximum is { } max && min >= max ||
            Unit is { } unit && (string.IsNullOrWhiteSpace(unit) || unit.Trim().Length > 64) ||
            Ticks?.Any(tick => !double.IsFinite(tick)) == true ||
            Ticks is { } ticks && ticks.Distinct().Count() != ticks.Count ||
            TickLabels?.Any(label => string.IsNullOrWhiteSpace(label) || label.Trim().Length > 128) == true ||
            TickLabels is { Count: > 0 } labels && (Ticks is null || labels.Count != Ticks.Count))
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.ColorbarMismatch,
                "Heatmap colorbar definition or orientation/position combination is invalid.");
        }

        LabelStyle?.EnsureValid();
        return this;
    }
}

public sealed class HeatmapDomainException : InvalidOperationException
{
    public HeatmapDomainException(string code, string message)
        : base($"{code}: {message}") => Code = code;

    public string Code { get; }
}

public sealed record HeatmapDomainIssue(
    string Code,
    HeatmapDomainIssueSeverity Severity,
    string Message);

public sealed record HeatmapDomainCell(
    double X,
    double Y,
    double Left,
    double Right,
    double Bottom,
    double Top,
    double? Value,
    bool IsNoData,
    double? NormalizedValue,
    string? Fill,
    int SourceCount);

public sealed record HeatmapColorbarDomain(
    PlotColorbarBinding Binding,
    PlotColorbarOrientation Orientation,
    PlotColorbarPosition Position,
    double Minimum,
    double Maximum,
    string? Unit,
    IReadOnlyList<double> Ticks,
    TextStyle? LabelStyle,
    IReadOnlyList<string> TickLabels);

public sealed record HeatmapDomain(
    HeatmapGridKind RequestedGridKind,
    HeatmapGridKind EffectiveGridKind,
    HeatmapDuplicateCellPolicy DuplicateCellPolicy,
    IReadOnlyList<double> XCoordinates,
    IReadOnlyList<double> YCoordinates,
    IReadOnlyList<HeatmapDomainCell> Cells,
    string Colormap,
    double Minimum,
    double Maximum,
    PlotColorScaleKind Scale,
    PlotColorClampMode ClampMode,
    string? NoDataColor,
    HeatmapColorbarDomain? Colorbar,
    IReadOnlyList<HeatmapDomainIssue> Issues)
{
    public double Normalize(double value)
    {
        double transformed = Scale == PlotColorScaleKind.Log10 ? Math.Log10(value) : value;
        double minimum = Scale == PlotColorScaleKind.Log10 ? Math.Log10(Minimum) : Minimum;
        double maximum = Scale == PlotColorScaleKind.Log10 ? Math.Log10(Maximum) : Maximum;
        if (maximum == minimum)
        {
            return 0.5;
        }

        double normalized = (transformed - minimum) / (maximum - minimum);
        return ClampMode == PlotColorClampMode.Clamp ? Math.Clamp(normalized, 0, 1) : normalized;
    }
}

public static class HeatmapDomainBuilder
{
    public static HeatmapDomain Build(PlotObject plot, PlotDataProjection projection)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(projection);
        if (plot.PlotType != PlotKind.Heatmap || plot.Data.ValueColumnId is null)
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.NoValueColumn,
                "Heatmap requires a numeric value column.");
        }

        HeatmapGridDefinition grid = (plot.HeatmapGrid ?? HeatmapGridDefinition.Default).EnsureValid();
        PlotColorScale colorScale = (plot.ColorScale ?? PlotColorScale.Default).EnsureValid();
        PlotColorbarDefinition colorbar = (plot.Colorbar ?? PlotColorbarDefinition.Default).EnsureValid();
        ProjectedPlotRow[] coordinateRows = projection.Rows
            .Where(row => row.X.HasValue && row.Y.HasValue)
            .ToArray();
        if (coordinateRows.Length == 0)
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.NoValueColumn,
                "Heatmap has no rows with usable X and Y coordinates.");
        }

        var issues = new List<HeatmapDomainIssue>();
        CoordinateValue[] coordinates = coordinateRows
            .GroupBy(row => (X: row.X!.Value, Y: row.Y!.Value))
            .Select(group => Aggregate(group.Key.X, group.Key.Y, group.ToArray(), grid, issues))
            .OrderBy(value => value.Y)
            .ThenBy(value => value.X)
            .ToArray();
        double[] values = coordinates
            .Where(value => value.Value.HasValue)
            .Select(value => value.Value!.Value)
            .ToArray();
        if (values.Length == 0)
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.NoValueColumn,
                "Heatmap value column has no usable scalar values.");
        }

        double[] xCoordinates = coordinates.Select(value => value.X).Distinct().Order().ToArray();
        double[] yCoordinates = coordinates.Select(value => value.Y).Distinct().Order().ToArray();
        bool complete = coordinates.Length == checked(xCoordinates.Length * yCoordinates.Length);
        bool irregular = !IsUniform(xCoordinates) || !IsUniform(yCoordinates);
        HeatmapGridKind effectiveKind = ResolveGridKind(grid.Kind, complete, irregular);
        if (!complete)
        {
            issues.Add(new HeatmapDomainIssue(
                HeatmapQcCodes.GridIncomplete,
                HeatmapDomainIssueSeverity.Warning,
                effectiveKind == HeatmapGridKind.PointCloud
                    ? "Incomplete coordinate combinations are rendered as colored points without interpolation."
                    : "Incomplete coordinate combinations are represented by explicit NoData cells."));
        }
        if (irregular && effectiveKind != HeatmapGridKind.PointCloud)
        {
            issues.Add(new HeatmapDomainIssue(
                HeatmapQcCodes.IrregularGrid,
                HeatmapDomainIssueSeverity.Notice,
                "Heatmap cell boundaries use the actual nonuniform coordinate spacing."));
        }

        (double minimum, double maximum) = ResolveRange(values, colorScale);
        if (colorScale.Scale == PlotColorScaleKind.Log10 &&
            (minimum <= 0 || maximum <= 0 || values.Any(value => value <= 0)))
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.LogNonpositive,
                "Log10 heatmap scale contains a nonpositive value.");
        }
        if (colorScale.ClampMode == PlotColorClampMode.Error &&
            values.Any(value => value < minimum || value > maximum))
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.InvalidRange,
                "Heatmap value lies outside the configured range while clamp mode is Error.");
        }

        string colormap = ScientificColormap.Normalize(colorScale.Colormap);
        string? noDataColor = string.IsNullOrWhiteSpace(colorScale.NoDataColor)
            ? null
            : ScientificStyleColor.NormalizeColor(colorScale.NoDataColor);
        var normalizer = new HeatmapNormalizer(minimum, maximum, colorScale.Scale, colorScale.ClampMode);
        IReadOnlyList<HeatmapDomainCell> cells = effectiveKind == HeatmapGridKind.PointCloud
            ? BuildPoints(coordinates, normalizer, colormap, noDataColor)
            : BuildGrid(coordinates, xCoordinates, yCoordinates, normalizer, colormap, noDataColor);
        HeatmapColorbarDomain? resolvedColorbar = colorScale.ShowColorbar
            ? ResolveColorbar(colorbar, minimum, maximum, colorScale.Scale)
            : null;
        return new HeatmapDomain(
            grid.Kind,
            effectiveKind,
            grid.DuplicateCellPolicy,
            xCoordinates,
            yCoordinates,
            cells,
            colormap,
            minimum,
            maximum,
            colorScale.Scale,
            colorScale.ClampMode,
            noDataColor,
            resolvedColorbar,
            issues);
    }

    private static CoordinateValue Aggregate(
        double x,
        double y,
        IReadOnlyList<ProjectedPlotRow> rows,
        HeatmapGridDefinition grid,
        ICollection<HeatmapDomainIssue> issues)
    {
        if (rows.Count > 1 && grid.DuplicateCellPolicy == HeatmapDuplicateCellPolicy.Error)
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.DuplicateCell,
                $"Coordinate ({Format(x)}, {Format(y)}) occurs {rows.Count} times.");
        }

        double[] values = rows.Where(row => row.Value.HasValue).Select(row => row.Value!.Value).Order().ToArray();
        if (rows.Count > 1)
        {
            issues.Add(new HeatmapDomainIssue(
                HeatmapQcCodes.DuplicateCell,
                HeatmapDomainIssueSeverity.Notice,
                $"{rows.Count} values at ({Format(x)}, {Format(y)}) were aggregated using {grid.DuplicateCellPolicy}."));
        }
        double? value = values.Length == 0 ? null : grid.DuplicateCellPolicy switch
        {
            HeatmapDuplicateCellPolicy.Mean => values.Average(),
            HeatmapDuplicateCellPolicy.Median => values.Length % 2 == 1
                ? values[values.Length / 2]
                : (values[values.Length / 2 - 1] + values[values.Length / 2]) / 2,
            HeatmapDuplicateCellPolicy.Min => values[0],
            HeatmapDuplicateCellPolicy.Max => values[^1],
            _ => values[0],
        };
        return new CoordinateValue(x, y, value, rows.Count);
    }

    private static HeatmapGridKind ResolveGridKind(
        HeatmapGridKind requested,
        bool complete,
        bool irregular) => requested switch
        {
            HeatmapGridKind.Auto when !complete => HeatmapGridKind.PointCloud,
            HeatmapGridKind.Auto when irregular => HeatmapGridKind.IrregularGrid,
            HeatmapGridKind.Auto => HeatmapGridKind.RegularGrid,
            HeatmapGridKind.RegularGrid when irregular => HeatmapGridKind.IrregularGrid,
            _ => requested,
        };

    private static (double Minimum, double Maximum) ResolveRange(
        IReadOnlyList<double> values,
        PlotColorScale scale)
    {
        double minimum = scale.Minimum ?? values.Min();
        double maximum = scale.Maximum ?? values.Max();
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || minimum > maximum ||
            minimum == maximum && (scale.Minimum.HasValue || scale.Maximum.HasValue))
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.InvalidRange,
                "Heatmap color range must have a finite minimum smaller than its maximum.");
        }
        return (minimum, maximum);
    }

    private static IReadOnlyList<HeatmapDomainCell> BuildPoints(
        IReadOnlyList<CoordinateValue> coordinates,
        HeatmapNormalizer normalizer,
        string colormap,
        string? noDataColor) => coordinates
        .Select(value => CreateCell(value, value.X, value.X, value.Y, value.Y, normalizer, colormap, noDataColor))
        .ToArray();

    private static IReadOnlyList<HeatmapDomainCell> BuildGrid(
        IReadOnlyList<CoordinateValue> coordinates,
        IReadOnlyList<double> xs,
        IReadOnlyList<double> ys,
        HeatmapNormalizer normalizer,
        string colormap,
        string? noDataColor)
    {
        IReadOnlyDictionary<(double X, double Y), CoordinateValue> lookup =
            coordinates.ToDictionary(value => (value.X, value.Y));
        (double Left, double Right)[] xBounds = Boundaries(xs);
        (double Left, double Right)[] yBounds = Boundaries(ys);
        var cells = new List<HeatmapDomainCell>(checked(xs.Count * ys.Count));
        for (int yIndex = 0; yIndex < ys.Count; yIndex++)
        {
            for (int xIndex = 0; xIndex < xs.Count; xIndex++)
            {
                CoordinateValue value = lookup.TryGetValue((xs[xIndex], ys[yIndex]), out CoordinateValue? found)
                    ? found
                    : new CoordinateValue(xs[xIndex], ys[yIndex], null, 0);
                cells.Add(CreateCell(
                    value,
                    xBounds[xIndex].Left,
                    xBounds[xIndex].Right,
                    yBounds[yIndex].Left,
                    yBounds[yIndex].Right,
                    normalizer,
                    colormap,
                    noDataColor));
            }
        }
        return cells;
    }

    private static HeatmapDomainCell CreateCell(
        CoordinateValue value,
        double left,
        double right,
        double bottom,
        double top,
        HeatmapNormalizer normalizer,
        string colormap,
        string? noDataColor)
    {
        double? normalized = value.Value.HasValue ? normalizer.Normalize(value.Value.Value) : null;
        string? fill = normalized.HasValue
            ? ScientificColormap.Sample(colormap, normalized.Value).ToHex()
            : noDataColor;
        return new HeatmapDomainCell(
            value.X, value.Y, left, right, bottom, top, value.Value,
            !value.Value.HasValue, normalized, fill, value.SourceCount);
    }

    private static (double Left, double Right)[] Boundaries(IReadOnlyList<double> coordinates)
    {
        if (coordinates.Count == 1)
        {
            return [(coordinates[0] - 0.5, coordinates[0] + 0.5)];
        }
        var result = new (double Left, double Right)[coordinates.Count];
        for (int index = 0; index < coordinates.Count; index++)
        {
            double left = index == 0
                ? coordinates[0] - (coordinates[1] - coordinates[0]) / 2
                : (coordinates[index - 1] + coordinates[index]) / 2;
            double right = index == coordinates.Count - 1
                ? coordinates[^1] + (coordinates[^1] - coordinates[^2]) / 2
                : (coordinates[index] + coordinates[index + 1]) / 2;
            result[index] = (left, right);
        }
        return result;
    }

    private static bool IsUniform(IReadOnlyList<double> coordinates)
    {
        if (coordinates.Count < 3) return true;
        double spacing = coordinates[1] - coordinates[0];
        double tolerance = Math.Max(1e-12, Math.Abs(spacing) * 1e-9);
        return coordinates.Skip(2).Select((value, index) => value - coordinates[index + 1])
            .All(delta => Math.Abs(delta - spacing) <= tolerance);
    }

    private static HeatmapColorbarDomain ResolveColorbar(
        PlotColorbarDefinition definition,
        double heatmapMinimum,
        double heatmapMaximum,
        PlotColorScaleKind scale)
    {
        double minimum;
        double maximum;
        if (definition.Binding == PlotColorbarBinding.Linked)
        {
            if (definition.Minimum is { } configuredMinimum && configuredMinimum != heatmapMinimum ||
                definition.Maximum is { } configuredMaximum && configuredMaximum != heatmapMaximum)
            {
                throw new HeatmapDomainException(
                    HeatmapQcCodes.ColorbarMismatch,
                    "A linked colorbar cannot use a range different from its heatmap.");
            }
            minimum = heatmapMinimum;
            maximum = heatmapMaximum;
        }
        else
        {
            minimum = definition.Minimum ?? heatmapMinimum;
            maximum = definition.Maximum ?? heatmapMaximum;
            if (minimum >= maximum)
            {
                throw new HeatmapDomainException(
                    HeatmapQcCodes.ColorbarMismatch,
                    "Detached colorbar minimum must be smaller than maximum.");
            }
        }

        if (scale == PlotColorScaleKind.Log10 && (minimum <= 0 || maximum <= 0))
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.LogNonpositive,
                "A Log10 colorbar range must be strictly positive.");
        }

        double[] ticks;
        string[] tickLabels;
        if (definition.Ticks is { Count: > 0 } configuredTicks)
        {
            var ordered = configuredTicks
                .Select((tick, index) => (
                    Tick: tick,
                    Label: definition.TickLabels is { Count: > 0 }
                        ? definition.TickLabels[index].Trim()
                        : FormatTick(tick)))
                .OrderBy(item => item.Tick)
                .ToArray();
            ticks = ordered.Select(item => item.Tick).ToArray();
            tickLabels = ordered.Select(item => item.Label).ToArray();
        }
        else
        {
            ticks = AutoTicks(minimum, maximum, scale);
            tickLabels = ticks.Select(FormatTick).ToArray();
        }
        if (ticks.Any(tick => tick < minimum || tick > maximum))
        {
            throw new HeatmapDomainException(
                HeatmapQcCodes.ColorbarMismatch,
                "Colorbar ticks must lie within the colorbar range.");
        }
        return new HeatmapColorbarDomain(
            definition.Binding,
            definition.Orientation,
            definition.Position,
            minimum,
            maximum,
            string.IsNullOrWhiteSpace(definition.Unit) ? null : definition.Unit.Trim(),
            ticks,
            definition.LabelStyle,
            tickLabels);
    }

    private static double[] AutoTicks(
        double minimum,
        double maximum,
        PlotColorScaleKind scale)
    {
        if (minimum == maximum) return [minimum];
        if (scale == PlotColorScaleKind.Log10)
        {
            double logMinimum = Math.Log10(minimum);
            double logMaximum = Math.Log10(maximum);
            return Enumerable.Range(0, 5)
                .Select(index => Math.Pow(10, logMinimum + (logMaximum - logMinimum) * index / 4))
                .ToArray();
        }
        return Enumerable.Range(0, 5)
            .Select(index => minimum + (maximum - minimum) * index / 4)
            .ToArray();
    }

    private static string Format(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static string FormatTick(double value) =>
        value == 0 || Math.Abs(value) is >= 0.001 and < 10000
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : value.ToString("0.##E+0", CultureInfo.InvariantCulture);

    private sealed record CoordinateValue(double X, double Y, double? Value, int SourceCount);

    private readonly record struct HeatmapNormalizer(
        double Minimum,
        double Maximum,
        PlotColorScaleKind Scale,
        PlotColorClampMode ClampMode)
    {
        public double Normalize(double value)
        {
            double transformed = Scale == PlotColorScaleKind.Log10 ? Math.Log10(value) : value;
            double minimum = Scale == PlotColorScaleKind.Log10 ? Math.Log10(Minimum) : Minimum;
            double maximum = Scale == PlotColorScaleKind.Log10 ? Math.Log10(Maximum) : Maximum;
            double normalized = maximum == minimum ? 0.5 : (transformed - minimum) / (maximum - minimum);
            return ClampMode == PlotColorClampMode.Clamp ? Math.Clamp(normalized, 0, 1) : normalized;
        }
    }
}
