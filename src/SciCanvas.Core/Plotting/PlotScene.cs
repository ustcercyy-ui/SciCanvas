using System.Diagnostics;
using System.Globalization;
using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Plotting;

public enum PlotTextAnchor { Start, Middle, End }

public readonly record struct PlotPoint(double X, double Y);

public readonly record struct PlotRect(double X, double Y, double Width, double Height)
{
    public double Left => X;
    public double Top => Y;
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public abstract record PlotPrimitive;
public sealed record PlotLine(PlotPoint A, PlotPoint B, string Stroke, double Width, PlotLineStyle Dash = PlotLineStyle.Solid) : PlotPrimitive;
public sealed record PlotPolyline(IReadOnlyList<PlotPoint> Points, string Stroke, double Width, PlotLineStyle Dash = PlotLineStyle.Solid) : PlotPrimitive;
public sealed record PlotRectangle(PlotRect Bounds, string? Fill, string? Stroke = null, double Width = 0, PlotLineStyle Dash = PlotLineStyle.Solid) : PlotPrimitive;
public sealed record PlotEllipse(PlotRect Bounds, string? Fill, string? Stroke = null, double Width = 0) : PlotPrimitive;
public sealed record PlotPolygon(IReadOnlyList<PlotPoint> Points, string? Fill, string? Stroke = null, double Width = 0) : PlotPrimitive;
public sealed record PlotText(string Value, double X, double Y, TextStyle Style, double FontPixels, PlotTextAnchor Anchor = PlotTextAnchor.Start) : PlotPrimitive;
public sealed record PlotHeatmapCell(PlotRect Bounds, string Fill) : PlotPrimitive;
public sealed record PlotClipRegion(PlotRect Bounds, IReadOnlyList<PlotPrimitive> Primitives) : PlotPrimitive;
public readonly record struct PlotAxisBounds(double XMinimum, double XMaximum, double YMinimum, double YMaximum);
public sealed record PlotScene(IReadOnlyList<PlotPrimitive> Primitives, PlotRect Chart, PlotAxisBounds AxisBounds);
public readonly record struct PlotSceneBuildTimings(TimeSpan Bounds, TimeSpan AxisGeneration, TimeSpan HeatmapGeometry);
public sealed record PlotSceneBuildResult(PlotScene Scene, PlotSceneBuildTimings Timings);

public static class PlotSceneBuilder
{
    public static PlotScene Build(FigurePlotPanelExportItem panel, FigureGlobalStyle figureStyle, int dpi)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(figureStyle);
        panel.EnsureValid();
        return Build(
            panel.Plot,
            panel.Projection,
            panel.ResolveTypography(figureStyle).Value,
            new PlotRect(
                panel.DestinationRect.X,
                panel.DestinationRect.Y,
                panel.DestinationRect.Width,
                panel.DestinationRect.Height),
            dpi);
    }

    public static PlotScene Build(
        PlotObject plot,
        PlotDataProjection projection,
        PlotTypography typography,
        PlotRect destination,
        int dpi) => BuildWithDiagnostics(plot, projection, typography, destination, dpi).Scene;

    public static PlotSceneBuildResult BuildWithDiagnostics(
        PlotObject plot,
        PlotDataProjection projection,
        PlotTypography typography,
        PlotRect destination,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(typography);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        if (!Enum.IsDefined(plot.PlotType) || string.IsNullOrWhiteSpace(plot.Name))
        {
            throw new InvalidDataException("Plot scene requires a known Plot kind and non-empty name.");
        }
        ArgumentNullException.ThrowIfNull(plot.XAxis);
        ArgumentNullException.ThrowIfNull(plot.YAxis);
        ArgumentNullException.ThrowIfNull(plot.Style);
        ArgumentNullException.ThrowIfNull(projection.Rows);
        ArgumentNullException.ThrowIfNull(projection.AppliedTransforms);
        plot.XAxis.EnsureValid();
        plot.YAxis.EnsureValid();
        plot.Style.EnsureValid();
        if (projection.SourceRowCount < 0 || projection.IncludedRowCount < 0 ||
            projection.ExcludedRowCount < 0 || projection.UnplottableRowCount < 0 ||
            projection.SourceRowCount != projection.IncludedRowCount + projection.ExcludedRowCount ||
            projection.Rows.Count != projection.IncludedRowCount ||
            projection.UnplottableRowCount > projection.IncludedRowCount)
        {
            throw new InvalidDataException("Plot scene projection row counts are inconsistent.");
        }
        if (!double.IsFinite(destination.X) || !double.IsFinite(destination.Y) ||
            !double.IsFinite(destination.Width) || !double.IsFinite(destination.Height) ||
            destination.Width <= 0 || destination.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Plot scene destination must have finite positive dimensions.");
        }
        typography.EnsureValid();
        double axisFont = Px(typography.Axis.FontSizePt, dpi);
        double tickFont = Px(typography.Tick.FontSizePt, dpi);
        double legendFont = Px(typography.Legend.FontSizePt, dpi);
        double annotationFont = Px(typography.Annotation.FontSizePt, dpi);
        double left = Math.Max(38, tickFont * 5.8);
        double right = Math.Max(12, tickFont * 1.2);
        double top = Math.Max(25, legendFont * 1.8);
        double bottom = Math.Max(46, tickFont * 1.6 + axisFont * 1.6 + annotationFont * 1.4);
        var chart = new PlotRect(
            destination.Left + left,
            destination.Top + top,
            Math.Max(20, destination.Width - left - right),
            Math.Max(20, destination.Height - top - bottom));
        var output = new List<PlotPrimitive> { new PlotRectangle(destination, "#FFFFFFFF") };
        Series series = BuildSeries(projection, plot.PlotType);
        long boundsStarted = Stopwatch.GetTimestamp();
        Bounds bounds = ResolveBounds(plot, series);
        TimeSpan boundsElapsed = Stopwatch.GetElapsedTime(boundsStarted);
        long axisStarted = Stopwatch.GetTimestamp();
        AddAxes(output, chart, plot, typography, bounds, dpi);
        TimeSpan axisElapsed = Stopwatch.GetElapsedTime(axisStarted);
        TimeSpan heatmapElapsed = TimeSpan.Zero;
        switch (plot.PlotType)
        {
            case PlotKind.Line:
                AddXy(output, chart, plot, bounds, series.Samples, true, false, dpi);
                break;
            case PlotKind.Scatter:
                AddXy(output, chart, plot, bounds, series.Samples, false, true, dpi);
                break;
            case PlotKind.LineAndSymbol:
                AddXy(output, chart, plot, bounds, series.Samples, true, true, dpi);
                break;
            case PlotKind.ErrorBar:
                AddErrors(output, chart, plot, bounds, series.Samples, dpi);
                AddXy(output, chart, plot, bounds, series.Samples, true, true, dpi);
                break;
            case PlotKind.Histogram:
                AddHistogram(output, chart, plot, bounds, series.Bins, dpi);
                break;
            case PlotKind.BoxPlot:
                AddBoxes(output, chart, plot, typography, bounds, series.Groups, dpi);
                break;
            case PlotKind.Heatmap:
                long heatmapStarted = Stopwatch.GetTimestamp();
                AddHeatmap(output, chart, plot, bounds, series.Samples);
                heatmapElapsed = Stopwatch.GetElapsedTime(heatmapStarted);
                break;
            default:
                throw new InvalidDataException("未知 Plot 类型。");
        }

        output.Add(new PlotText(
            plot.Name,
            chart.Left,
            destination.Top + Math.Max(2, legendFont * 0.12),
            typography.Legend,
            legendFont));
        output.Add(new PlotText(
            $"n={series.Projection.IncludedRowCount}/{series.Projection.SourceRowCount}; " +
            $"excluded={series.Projection.ExcludedRowCount}; " +
            $"unplottable={series.Projection.UnplottableRowCount}; " +
            $"transforms={series.Projection.AppliedTransforms.Count}",
            chart.Right,
            destination.Bottom - annotationFont * 1.15,
            typography.Annotation,
            annotationFont,
            PlotTextAnchor.End));
        return new PlotSceneBuildResult(
            new PlotScene(output, chart, new PlotAxisBounds(bounds.X0, bounds.X1, bounds.Y0, bounds.Y1)),
            new PlotSceneBuildTimings(boundsElapsed, axisElapsed, heatmapElapsed));
    }

    private static Series BuildSeries(PlotDataProjection projection, PlotKind kind)
    {
        if (kind == PlotKind.Histogram)
        {
            double[] values = projection.Rows.Where(row => row.Y.HasValue).Select(row => row.Y!.Value).ToArray();
            return new([], CreateHistogram(values), [], projection);
        }
        if (kind == PlotKind.BoxPlot)
        {
            Group[] groups = projection.Rows
                .Where(row => row.Y.HasValue)
                .GroupBy(row => row.Category ?? "All", StringComparer.Ordinal)
                .Select((group, index) => new Group(index, group.Key, group.Select(row => row.Y!.Value).Order().ToArray()))
                .ToArray();
            return new([], [], groups, projection);
        }
        Sample[] samples = projection.Rows
            .Where(row => row.X.HasValue && row.Y.HasValue)
            .Select(row => new Sample(row.X!.Value, row.Y!.Value, row.ErrorLower, row.ErrorUpper, row.Value))
            .ToArray();
        return new(samples, [], [], projection);
    }

    private static Bounds ResolveBounds(PlotObject plot, Series series)
    {
        double x0;
        double x1;
        double y0;
        double y1;
        if (plot.PlotType == PlotKind.Histogram)
        {
            if (series.Bins.Count == 0) throw new InvalidDataException("Histogram 没有可绘制数值。");
            x0 = series.Bins[0].Lower;
            x1 = series.Bins[^1].Upper;
            y0 = plot.YAxis.Scale == PlotAxisScale.Log10 ? 1 : 0;
            y1 = series.Bins.Max(bin => bin.Count);
        }
        else if (plot.PlotType == PlotKind.BoxPlot)
        {
            if (series.Groups.Count == 0) throw new InvalidDataException("Box Plot 没有可绘制数值。");
            x0 = -0.5;
            x1 = series.Groups.Count - 0.5;
            y0 = series.Groups.Min(group => group.Values[0]);
            y1 = series.Groups.Max(group => group.Values[^1]);
        }
        else
        {
            if (series.Samples.Count == 0) throw new InvalidDataException("Plot 没有成对的可绘制数值。");
            x0 = series.Samples.Min(sample => sample.X);
            x1 = series.Samples.Max(sample => sample.X);
            y0 = series.Samples.Min(sample => sample.Low is { } error ? sample.Y - error : sample.Y);
            y1 = series.Samples.Max(sample => sample.High is { } error ? sample.Y + error : sample.Y);
        }
        (x0, x1) = Expand(x0, x1);
        (y0, y1) = Expand(y0, y1);
        x0 = plot.XAxis.Minimum ?? x0;
        x1 = plot.XAxis.Maximum ?? x1;
        y0 = plot.YAxis.Minimum ?? y0;
        y1 = plot.YAxis.Maximum ?? y1;
        if (plot.XAxis.Scale == PlotAxisScale.Log10 && (x0 <= 0 || x1 <= 0) ||
            plot.YAxis.Scale == PlotAxisScale.Log10 && (y0 <= 0 || y1 <= 0))
        {
            throw new InvalidDataException("对数轴包含非正数；不会静默移除数据点。");
        }
        return new(x0, x1, y0, y1);
    }

    private static void AddAxes(List<PlotPrimitive> output, PlotRect chart, PlotObject plot, PlotTypography typography, Bounds bounds, int dpi)
    {
        double width = Math.Max(0.5, Px(0.75, dpi));
        output.Add(new PlotLine(new(chart.Left, chart.Bottom), new(chart.Right, chart.Bottom), "#FF20262E", width));
        output.Add(new PlotLine(new(chart.Left, chart.Bottom), new(chart.Left, chart.Top), "#FF20262E", width));
        if (plot.PlotType != PlotKind.BoxPlot)
        {
            AddTicks(output, chart, plot.XAxis, bounds.X0, bounds.X1, true, typography.Tick, dpi);
        }
        AddTicks(output, chart, plot.YAxis, bounds.Y0, bounds.Y1, false, typography.Tick, dpi);
        double axisFont = Px(typography.Axis.FontSizePt, dpi);
        output.Add(new PlotText(
            AxisTitle(plot.XAxis),
            chart.Left + chart.Width / 2,
            chart.Bottom + Px(typography.Tick.FontSizePt, dpi) * 1.55,
            typography.Axis,
            axisFont,
            PlotTextAnchor.Middle));
        output.Add(new PlotText(AxisTitle(plot.YAxis), chart.Left, chart.Top - axisFont * 1.25, typography.Axis, axisFont));
    }

    private static void AddTicks(List<PlotPrimitive> output, PlotRect chart, PlotAxisDefinition axis, double minimum, double maximum, bool isX, TextStyle style, int dpi)
    {
        IReadOnlyList<double> ticks = MajorTicks(axis, minimum, maximum);
        double width = Math.Max(0.4, Px(0.6, dpi));
        double length = Math.Max(3, Px(3.5, dpi));
        double font = Px(style.FontSizePt, dpi);
        for (int index = 0; index < ticks.Count; index++)
        {
            double value = ticks[index];
            double fraction = Fraction(value, minimum, maximum, axis.Scale);
            if (fraction is < -1e-9 or > 1.000000001) continue;
            if (isX)
            {
                double x = chart.Left + fraction * chart.Width;
                output.Add(new PlotLine(new(x, chart.Bottom), new(x, chart.Bottom + length), "#FF303945", width));
                output.Add(new PlotText(Tick(value), x, chart.Bottom + length + 2, style, font, PlotTextAnchor.Middle));
            }
            else
            {
                double y = chart.Bottom - fraction * chart.Height;
                output.Add(new PlotLine(new(chart.Left - length, y), new(chart.Left, y), "#FF303945", width));
                output.Add(new PlotText(Tick(value), chart.Left - length - 3, y - font * 0.55, style, font, PlotTextAnchor.End));
            }
            if (index >= ticks.Count - 1 || axis.MinorTickCount == 0) continue;
            double next = ticks[index + 1];
            for (int minor = 1; minor <= axis.MinorTickCount; minor++)
            {
                double minorValue = axis.Scale == PlotAxisScale.Log10
                    ? Math.Pow(10, Math.Log10(value) + (Math.Log10(next) - Math.Log10(value)) * minor / (axis.MinorTickCount + 1))
                    : value + (next - value) * minor / (axis.MinorTickCount + 1);
                double f = Fraction(minorValue, minimum, maximum, axis.Scale);
                if (isX)
                {
                    double x = chart.Left + f * chart.Width;
                    output.Add(new PlotLine(new(x, chart.Bottom), new(x, chart.Bottom + length / 2), "#FF303945", width));
                }
                else
                {
                    double y = chart.Bottom - f * chart.Height;
                    output.Add(new PlotLine(new(chart.Left - length / 2, y), new(chart.Left, y), "#FF303945", width));
                }
            }
        }
    }

    private static void AddXy(List<PlotPrimitive> output, PlotRect chart, PlotObject plot, Bounds bounds, IReadOnlyList<Sample> samples, bool line, bool markers, int dpi)
    {
        double width = Px(plot.Style.LineWidthPt, dpi);
        PlotPoint? previous = null;
        foreach (Sample sample in samples)
        {
            PlotPoint point = Map(chart, plot, bounds, sample.X, sample.Y);
            if (line && previous is { } prior && ClipLine(prior, point, chart, out PlotPoint a, out PlotPoint b))
            {
                output.Add(new PlotLine(a, b, plot.Style.LineColor, width, plot.Style.LineStyle));
            }
            if (markers && Contains(chart, point) && plot.Style.MarkerShape != PlotMarkerShape.None)
            {
                AddMarker(output, point, plot.Style, dpi);
            }
            previous = point;
        }
    }

    private static void AddErrors(List<PlotPrimitive> output, PlotRect chart, PlotObject plot, Bounds bounds, IReadOnlyList<Sample> samples, int dpi)
    {
        double width = Px(plot.Style.LineWidthPt, dpi);
        double cap = Math.Max(3, Px(3, dpi));
        foreach (Sample sample in samples)
        {
            if (sample.Low is not { } low || sample.High is not { } high) continue;
            PlotPoint a = Map(chart, plot, bounds, sample.X, sample.Y - low);
            PlotPoint b = Map(chart, plot, bounds, sample.X, sample.Y + high);
            if (!ClipLine(a, b, chart, out PlotPoint clippedA, out PlotPoint clippedB)) continue;
            output.Add(new PlotLine(clippedA, clippedB, plot.Style.LineColor, width, plot.Style.LineStyle));
            output.Add(new PlotLine(new(clippedA.X - cap, clippedA.Y), new(clippedA.X + cap, clippedA.Y), plot.Style.LineColor, width));
            output.Add(new PlotLine(new(clippedB.X - cap, clippedB.Y), new(clippedB.X + cap, clippedB.Y), plot.Style.LineColor, width));
        }
    }

    private static void AddHistogram(List<PlotPrimitive> output, PlotRect chart, PlotObject plot, Bounds bounds, IReadOnlyList<Bin> bins, int dpi)
    {
        double baseline = plot.YAxis.Scale == PlotAxisScale.Log10 ? bounds.Y0 : 0;
        foreach (Bin bin in bins)
        {
            if (plot.YAxis.Scale == PlotAxisScale.Log10 && bin.Count == 0) continue;
            PlotPoint lower = Map(chart, plot, bounds, bin.Lower, baseline);
            PlotPoint upper = Map(chart, plot, bounds, bin.Upper, bin.Count);
            PlotRect rect = Intersect(
                new(lower.X + 0.5, Math.Min(lower.Y, upper.Y), Math.Max(0.5, upper.X - lower.X - 1), Math.Abs(lower.Y - upper.Y)),
                chart);
            if (rect.Width > 0 && rect.Height > 0)
            {
                output.Add(new PlotRectangle(rect, plot.Style.MarkerFill, plot.Style.LineColor, Px(plot.Style.LineWidthPt, dpi), plot.Style.LineStyle));
            }
        }
    }

    private static void AddBoxes(List<PlotPrimitive> output, PlotRect chart, PlotObject plot, PlotTypography typography, Bounds bounds, IReadOnlyList<Group> groups, int dpi)
    {
        double stroke = Px(plot.Style.LineWidthPt, dpi);
        double labelFont = Px(typography.Tick.FontSizePt, dpi);
        foreach (Group group in groups)
        {
            double min = group.Values[0];
            double q1 = Quantile(group.Values, 0.25);
            double median = Quantile(group.Values, 0.5);
            double q3 = Quantile(group.Values, 0.75);
            double max = group.Values[^1];
            PlotPoint minP = Map(chart, plot, bounds, group.Index, min);
            PlotPoint maxP = Map(chart, plot, bounds, group.Index, max);
            PlotPoint q1P = Map(chart, plot, bounds, group.Index, q1);
            PlotPoint q3P = Map(chart, plot, bounds, group.Index, q3);
            PlotPoint medianP = Map(chart, plot, bounds, group.Index, median);
            double boxWidth = Math.Min(Px(18, dpi), chart.Width / Math.Max(2, groups.Count * 2));
            output.Add(new PlotLine(minP, maxP, plot.Style.LineColor, stroke, plot.Style.LineStyle));
            output.Add(new PlotLine(new(minP.X - boxWidth / 3, minP.Y), new(minP.X + boxWidth / 3, minP.Y), plot.Style.LineColor, stroke));
            output.Add(new PlotLine(new(maxP.X - boxWidth / 3, maxP.Y), new(maxP.X + boxWidth / 3, maxP.Y), plot.Style.LineColor, stroke));
            output.Add(new PlotRectangle(new(q1P.X - boxWidth / 2, q3P.Y, boxWidth, Math.Max(0.5, q1P.Y - q3P.Y)), plot.Style.MarkerFill, plot.Style.LineColor, stroke, plot.Style.LineStyle));
            output.Add(new PlotLine(new(medianP.X - boxWidth / 2, medianP.Y), new(medianP.X + boxWidth / 2, medianP.Y), plot.Style.LineColor, stroke));
            output.Add(new PlotText(group.Label, minP.X, chart.Bottom + 3, typography.Tick, labelFont, PlotTextAnchor.Middle));
        }
    }

    private static void AddHeatmap(List<PlotPrimitive> output, PlotRect chart, PlotObject plot, Bounds bounds, IReadOnlyList<Sample> samples)
    {
        double[] values = samples.Where(sample => sample.Value.HasValue).Select(sample => sample.Value!.Value).ToArray();
        if (values.Length == 0) throw new InvalidDataException("Heatmap value 列没有可绘制数值。");
        double minimum = values.Min();
        double maximum = values.Max();
        double width = Math.Clamp(chart.Width / Math.Max(8, Math.Sqrt(values.Length) * 2), 3, 18);
        double height = Math.Clamp(chart.Height / Math.Max(8, Math.Sqrt(values.Length) * 2), 3, 18);
        foreach (Sample sample in samples.Where(sample => sample.Value.HasValue))
        {
            PlotPoint point = Map(chart, plot, bounds, sample.X, sample.Y);
            double f = maximum > minimum ? (sample.Value!.Value - minimum) / (maximum - minimum) : 0.5;
            byte red = (byte)(30 + 220 * f);
            byte green = (byte)(65 + 110 * (1 - Math.Abs(f - 0.5) * 2));
            byte blue = (byte)(220 - 190 * f);
            PlotRect cell = Intersect(new(point.X - width / 2, point.Y - height / 2, width, height), chart);
            if (cell.Width > 0 && cell.Height > 0)
            {
                output.Add(new PlotHeatmapCell(cell, $"#FF{red:X2}{green:X2}{blue:X2}"));
            }
        }
    }

    private static void AddMarker(List<PlotPrimitive> output, PlotPoint point, PlotSeriesStyle style, int dpi)
    {
        double radius = Px(style.MarkerSizePt, dpi) / 2;
        double stroke = Math.Max(0.5, Px(style.LineWidthPt, dpi));
        var bounds = new PlotRect(point.X - radius, point.Y - radius, radius * 2, radius * 2);
        switch (style.MarkerShape)
        {
            case PlotMarkerShape.Circle:
                output.Add(new PlotEllipse(bounds, style.MarkerFill, style.MarkerStroke, stroke));
                break;
            case PlotMarkerShape.Square:
                output.Add(new PlotRectangle(bounds, style.MarkerFill, style.MarkerStroke, stroke));
                break;
            case PlotMarkerShape.Triangle:
                output.Add(new PlotPolygon([new(point.X, point.Y - radius), new(point.X + radius, point.Y + radius), new(point.X - radius, point.Y + radius)], style.MarkerFill, style.MarkerStroke, stroke));
                break;
            case PlotMarkerShape.Diamond:
                output.Add(new PlotPolygon([new(point.X, point.Y - radius), new(point.X + radius, point.Y), new(point.X, point.Y + radius), new(point.X - radius, point.Y)], style.MarkerFill, style.MarkerStroke, stroke));
                break;
            case PlotMarkerShape.Cross:
                output.Add(new PlotLine(new(point.X - radius, point.Y - radius), new(point.X + radius, point.Y + radius), style.MarkerStroke, stroke));
                output.Add(new PlotLine(new(point.X - radius, point.Y + radius), new(point.X + radius, point.Y - radius), style.MarkerStroke, stroke));
                break;
        }
    }

    private static PlotPoint Map(PlotRect chart, PlotObject plot, Bounds bounds, double x, double y) => new(
        chart.Left + Fraction(x, bounds.X0, bounds.X1, plot.XAxis.Scale) * chart.Width,
        chart.Bottom - Fraction(y, bounds.Y0, bounds.Y1, plot.YAxis.Scale) * chart.Height);

    private static double Fraction(double value, double minimum, double maximum, PlotAxisScale scale)
    {
        if (scale == PlotAxisScale.Log10)
        {
            if (value <= 0) throw new InvalidDataException("对数轴包含非正数；不会静默移除该数据。");
            value = Math.Log10(value);
            minimum = Math.Log10(minimum);
            maximum = Math.Log10(maximum);
        }
        return (value - minimum) / (maximum - minimum);
    }

    private static IReadOnlyList<double> MajorTicks(PlotAxisDefinition axis, double minimum, double maximum)
    {
        if (axis.MajorTickInterval is { } interval)
        {
            var ticks = new List<double>();
            double start = Math.Ceiling(minimum / interval) * interval;
            for (double value = start; value <= maximum && ticks.Count < 200; value += interval) ticks.Add(value);
            if (ticks.Count > 0) return ticks;
        }
        if (axis.Scale == PlotAxisScale.Log10)
        {
            int start = (int)Math.Ceiling(Math.Log10(minimum));
            int end = (int)Math.Floor(Math.Log10(maximum));
            double[] decades = Enumerable.Range(start, Math.Max(0, end - start + 1)).Select(power => Math.Pow(10, power)).ToArray();
            if (decades.Length > 0) return decades;
        }
        return Enumerable.Range(0, 5).Select(index => minimum + (maximum - minimum) * index / 4).ToArray();
    }

    private static IReadOnlyList<Bin> CreateHistogram(double[] values)
    {
        if (values.Length == 0) return [];
        double minimum = values.Min();
        double maximum = values.Max();
        (minimum, maximum) = Expand(minimum, maximum);
        int count = Math.Clamp((int)Math.Ceiling(Math.Sqrt(values.Length)), 5, 50);
        double width = (maximum - minimum) / count;
        int[] bins = new int[count];
        foreach (double value in values)
        {
            int index = Math.Min(count - 1, (int)((value - minimum) / width));
            bins[Math.Max(0, index)]++;
        }
        return Enumerable.Range(0, count).Select(index => new Bin(minimum + index * width, minimum + (index + 1) * width, bins[index])).ToArray();
    }

    private static double Quantile(IReadOnlyList<double> values, double probability)
    {
        if (values.Count == 1) return values[0];
        double position = probability * (values.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        return values[lower] + (values[upper] - values[lower]) * (position - lower);
    }

    private static (double, double) Expand(double minimum, double maximum)
    {
        if (minimum != maximum) return (minimum, maximum);
        double padding = Math.Abs(minimum) > 1e-12 ? Math.Abs(minimum) * 0.05 : 0.5;
        return (minimum - padding, maximum + padding);
    }

    private static bool Contains(PlotRect rect, PlotPoint point) =>
        point.X >= rect.Left && point.X <= rect.Right && point.Y >= rect.Top && point.Y <= rect.Bottom;

    private static PlotRect Intersect(PlotRect value, PlotRect clip)
    {
        double left = Math.Max(value.Left, clip.Left);
        double top = Math.Max(value.Top, clip.Top);
        double right = Math.Min(value.Right, clip.Right);
        double bottom = Math.Min(value.Bottom, clip.Bottom);
        return new(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static bool ClipLine(PlotPoint start, PlotPoint end, PlotRect clip, out PlotPoint clippedStart, out PlotPoint clippedEnd)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double t0 = 0;
        double t1 = 1;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < 1e-12) return q >= 0;
            double ratio = q / p;
            if (p < 0)
            {
                if (ratio > t1) return false;
                if (ratio > t0) t0 = ratio;
            }
            else
            {
                if (ratio < t0) return false;
                if (ratio < t1) t1 = ratio;
            }
            return true;
        }
        bool visible = Clip(-dx, start.X - clip.Left) && Clip(dx, clip.Right - start.X) &&
            Clip(-dy, start.Y - clip.Top) && Clip(dy, clip.Bottom - start.Y);
        clippedStart = new(start.X + t0 * dx, start.Y + t0 * dy);
        clippedEnd = new(start.X + t1 * dx, start.Y + t1 * dy);
        return visible;
    }

    private static string AxisTitle(PlotAxisDefinition axis) =>
        string.IsNullOrWhiteSpace(axis.Unit) ? axis.Title : $"{axis.Title} ({axis.Unit})";

    private static string Tick(double value) =>
        value == 0 || Math.Abs(value) is >= 0.001 and < 10000
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : value.ToString("0.##E+0", CultureInfo.InvariantCulture);

    private static double Px(double points, int dpi) => points / 72.0 * dpi;

    private sealed record Series(IReadOnlyList<Sample> Samples, IReadOnlyList<Bin> Bins, IReadOnlyList<Group> Groups, PlotDataProjection Projection);
    private sealed record Sample(double X, double Y, double? Low, double? High, double? Value);
    private sealed record Bin(double Lower, double Upper, int Count);
    private sealed record Group(int Index, string Label, IReadOnlyList<double> Values);
    private readonly record struct Bounds(double X0, double X1, double Y0, double Y1);
}
