using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.App;

public sealed class PlotPreviewControl : FrameworkElement
{
    public static readonly DependencyProperty PlotProperty = DependencyProperty.Register(
        nameof(Plot),
        typeof(PlotObject),
        typeof(PlotPreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DataAssetProperty = DependencyProperty.Register(
        nameof(DataAsset),
        typeof(TabularDataAsset),
        typeof(PlotPreviewControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public PlotObject? Plot
    {
        get => (PlotObject?)GetValue(PlotProperty);
        set => SetValue(PlotProperty, value);
    }

    public TabularDataAsset? DataAsset
    {
        get => (TabularDataAsset?)GetValue(DataAssetProperty);
        set => SetValue(DataAssetProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var fullBounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawRectangle(Brushes.White, null, fullBounds);
        if (ActualWidth < 160 || ActualHeight < 120)
        {
            return;
        }

        if (Plot is not { } plot || DataAsset is not { } asset)
        {
            DrawCenteredMessage(drawingContext, "保存或选择 Plot 后显示矢量预览");
            return;
        }

        try
        {
            plot.EnsureValid(asset);
            RenderPlot(drawingContext, plot, asset);
        }
        catch (Exception exception) when (exception is
            InvalidDataException or InvalidOperationException or ArgumentException)
        {
            DrawCenteredMessage(drawingContext, $"Plot 不可预览\n{exception.Message}");
        }
    }

    private void RenderPlot(
        DrawingContext drawingContext,
        PlotObject plot,
        TabularDataAsset asset)
    {
        var chart = new Rect(58, 28, Math.Max(40, ActualWidth - 82), Math.Max(40, ActualHeight - 80));
        PlotRenderSeries renderSeries = BuildRenderSeries(plot, asset);
        PlotBounds bounds = ResolveBounds(plot, renderSeries);
        DrawAxes(drawingContext, chart, plot, bounds);

        switch (plot.PlotType)
        {
            case PlotKind.Line:
                DrawXySeries(drawingContext, chart, plot, bounds, renderSeries.Samples, drawLine: true, drawMarkers: false);
                break;
            case PlotKind.Scatter:
                DrawXySeries(drawingContext, chart, plot, bounds, renderSeries.Samples, drawLine: false, drawMarkers: true);
                break;
            case PlotKind.LineAndSymbol:
                DrawXySeries(drawingContext, chart, plot, bounds, renderSeries.Samples, drawLine: true, drawMarkers: true);
                break;
            case PlotKind.ErrorBar:
                DrawXySeries(drawingContext, chart, plot, bounds, renderSeries.Samples, drawLine: true, drawMarkers: true);
                DrawErrorBars(drawingContext, chart, plot, bounds, renderSeries.Samples);
                break;
            case PlotKind.Histogram:
                DrawHistogram(drawingContext, chart, plot, bounds, renderSeries.HistogramBins);
                break;
            case PlotKind.BoxPlot:
                DrawBoxPlot(drawingContext, chart, plot, bounds, renderSeries.BoxGroups);
                break;
            case PlotKind.Heatmap:
                DrawHeatmap(drawingContext, chart, plot, bounds, renderSeries.Samples);
                break;
        }

        DrawText(
            drawingContext,
            plot.Name,
            plot.Typography.Legend,
            new Point(chart.Left, 5),
            TextAlignment.Left);
        DrawText(
            drawingContext,
            $"DataAsset {asset.Name} · revision {plot.Data.SourceRevision} · " +
            $"included {renderSeries.Projection.IncludedRowCount}/{renderSeries.Projection.SourceRowCount} · " +
            $"excluded {renderSeries.Projection.ExcludedRowCount} · " +
            $"unplottable {renderSeries.Projection.UnplottableRowCount} · " +
            $"transforms {renderSeries.Projection.AppliedTransforms.Count}",
            plot.Typography.Annotation,
            new Point(chart.Right, ActualHeight - 18),
            TextAlignment.Right);
    }

    private static PlotRenderSeries BuildRenderSeries(
        PlotObject plot,
        TabularDataAsset asset)
    {
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);

        if (plot.PlotType == PlotKind.Histogram)
        {
            double[] values = projection.Rows
                .Select(row => row.Y)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .ToArray();
            return new PlotRenderSeries([], CreateHistogram(values), [], projection);
        }

        if (plot.PlotType == PlotKind.BoxPlot)
        {
            PlotBoxGroup[] groups = projection.Rows
                .Where(row => row.Y.HasValue)
                .GroupBy(row => row.Category ?? "All", StringComparer.Ordinal)
                .Select((group, index) => new PlotBoxGroup(
                    index,
                    group.Key,
                    group.Select(row => row.Y!.Value).Order().ToArray()))
                .ToArray();
            return new PlotRenderSeries([], [], groups, projection);
        }

        PlotSample[] samples = projection.Rows
            .Where(row => row.X.HasValue && row.Y.HasValue)
            .Select(row => new PlotSample(
                row.X!.Value,
                row.Y!.Value,
                row.ErrorLower,
                row.ErrorUpper,
                row.Value))
            .ToArray();
        return new PlotRenderSeries(samples, [], [], projection);
    }

    private static PlotBounds ResolveBounds(
        PlotObject plot,
        PlotRenderSeries series)
    {
        double autoXMin;
        double autoXMax;
        double autoYMin;
        double autoYMax;
        if (plot.PlotType == PlotKind.Histogram)
        {
            if (series.HistogramBins.Count == 0)
            {
                throw new InvalidDataException("Histogram 没有可绘制数值。");
            }

            autoXMin = series.HistogramBins[0].Lower;
            autoXMax = series.HistogramBins[^1].Upper;
            autoYMin = plot.YAxis.Scale == PlotAxisScale.Log10 ? 1 : 0;
            autoYMax = series.HistogramBins.Max(bin => bin.Count);
        }
        else if (plot.PlotType == PlotKind.BoxPlot)
        {
            if (series.BoxGroups.Count == 0)
            {
                throw new InvalidDataException("Box Plot 没有可绘制数值。");
            }

            autoXMin = -0.5;
            autoXMax = series.BoxGroups.Count - 0.5;
            autoYMin = series.BoxGroups.Min(group => group.Values[0]);
            autoYMax = series.BoxGroups.Max(group => group.Values[^1]);
        }
        else
        {
            if (series.Samples.Count == 0)
            {
                throw new InvalidDataException("绑定列没有成对的可绘制数值。");
            }

            autoXMin = series.Samples.Min(sample => sample.X);
            autoXMax = series.Samples.Max(sample => sample.X);
            autoYMin = series.Samples.Min(sample => sample.LowerError is { } error
                ? sample.Y - error
                : sample.Y);
            autoYMax = series.Samples.Max(sample => sample.UpperError is { } error
                ? sample.Y + error
                : sample.Y);
        }

        (autoXMin, autoXMax) = ExpandEqualRange(autoXMin, autoXMax);
        (autoYMin, autoYMax) = ExpandEqualRange(autoYMin, autoYMax);
        double xMin = plot.XAxis.Minimum ?? autoXMin;
        double xMax = plot.XAxis.Maximum ?? autoXMax;
        double yMin = plot.YAxis.Minimum ?? autoYMin;
        double yMax = plot.YAxis.Maximum ?? autoYMax;
        if (plot.XAxis.Scale == PlotAxisScale.Log10 && (xMin <= 0 || xMax <= 0) ||
            plot.YAxis.Scale == PlotAxisScale.Log10 && (yMin <= 0 || yMax <= 0))
        {
            throw new InvalidDataException("对数轴包含非正数；SciCanvas 不会隐藏这些数据点。");
        }

        return new PlotBounds(xMin, xMax, yMin, yMax);
    }

    private static void DrawAxes(
        DrawingContext drawingContext,
        Rect chart,
        PlotObject plot,
        PlotBounds bounds)
    {
        var axisPen = new Pen(ToBrush("#FF20262E"), 1);
        axisPen.Freeze();
        drawingContext.DrawLine(axisPen, chart.BottomLeft, chart.BottomRight);
        drawingContext.DrawLine(axisPen, chart.BottomLeft, chart.TopLeft);
        DrawTicks(drawingContext, chart, plot.XAxis, bounds.XMin, bounds.XMax, isX: true, plot.Typography.Tick);
        DrawTicks(drawingContext, chart, plot.YAxis, bounds.YMin, bounds.YMax, isX: false, plot.Typography.Tick);

        string xTitle = FormatAxisTitle(plot.XAxis);
        DrawText(
            drawingContext,
            xTitle,
            plot.Typography.Axis,
            new Point(chart.Left + chart.Width / 2, chart.Bottom + 28),
            TextAlignment.Center);

        string yTitle = FormatAxisTitle(plot.YAxis);
        drawingContext.PushTransform(new RotateTransform(
            -90,
            chart.Left - 43,
            chart.Top + chart.Height / 2));
        DrawText(
            drawingContext,
            yTitle,
            plot.Typography.Axis,
            new Point(chart.Left - 43, chart.Top + chart.Height / 2),
            TextAlignment.Center);
        drawingContext.Pop();
    }

    private static void DrawTicks(
        DrawingContext drawingContext,
        Rect chart,
        PlotAxisDefinition axis,
        double minimum,
        double maximum,
        bool isX,
        TextStyle tickStyle)
    {
        IReadOnlyList<double> majorTicks = CreateMajorTicks(axis, minimum, maximum);
        var tickPen = new Pen(ToBrush("#FF303945"), 0.8);
        tickPen.Freeze();
        for (int index = 0; index < majorTicks.Count; index++)
        {
            double value = majorTicks[index];
            double fraction = AxisFraction(value, minimum, maximum, axis.Scale);
            if (isX)
            {
                double x = chart.Left + fraction * chart.Width;
                drawingContext.DrawLine(
                    tickPen,
                    new Point(x, chart.Bottom),
                    new Point(x, chart.Bottom + 5));
                DrawText(
                    drawingContext,
                    FormatTick(value),
                    tickStyle,
                    new Point(x, chart.Bottom + 7),
                    TextAlignment.Center);
            }
            else
            {
                double y = chart.Bottom - fraction * chart.Height;
                drawingContext.DrawLine(
                    tickPen,
                    new Point(chart.Left - 5, y),
                    new Point(chart.Left, y));
                DrawText(
                    drawingContext,
                    FormatTick(value),
                    tickStyle,
                    new Point(chart.Left - 8, y - 6),
                    TextAlignment.Right);
            }

            if (index >= majorTicks.Count - 1 || axis.MinorTickCount == 0)
            {
                continue;
            }

            double next = majorTicks[index + 1];
            for (int minor = 1; minor <= axis.MinorTickCount; minor++)
            {
                double minorValue = axis.Scale == PlotAxisScale.Log10
                    ? Math.Pow(
                        10,
                        Math.Log10(value) +
                        (Math.Log10(next) - Math.Log10(value)) *
                        minor / (axis.MinorTickCount + 1))
                    : value + (next - value) * minor / (axis.MinorTickCount + 1);
                double minorFraction = AxisFraction(
                    minorValue,
                    minimum,
                    maximum,
                    axis.Scale);
                if (isX)
                {
                    double x = chart.Left + minorFraction * chart.Width;
                    drawingContext.DrawLine(
                        tickPen,
                        new Point(x, chart.Bottom),
                        new Point(x, chart.Bottom + 2.5));
                }
                else
                {
                    double y = chart.Bottom - minorFraction * chart.Height;
                    drawingContext.DrawLine(
                        tickPen,
                        new Point(chart.Left - 2.5, y),
                        new Point(chart.Left, y));
                }
            }
        }
    }

    private static void DrawXySeries(
        DrawingContext drawingContext,
        Rect chart,
        PlotObject plot,
        PlotBounds bounds,
        IReadOnlyList<PlotSample> samples,
        bool drawLine,
        bool drawMarkers)
    {
        Pen linePen = CreateSeriesPen(plot.Style);
        Point? previous = null;
        foreach (PlotSample sample in samples)
        {
            Point point = MapPoint(chart, plot, bounds, sample.X, sample.Y);
            if (drawLine && previous is { } prior)
            {
                drawingContext.DrawLine(linePen, prior, point);
            }

            if (drawMarkers && plot.Style.MarkerShape != PlotMarkerShape.None)
            {
                DrawMarker(drawingContext, point, plot.Style);
            }

            previous = point;
        }
    }

    private static void DrawErrorBars(
        DrawingContext drawingContext,
        Rect chart,
        PlotObject plot,
        PlotBounds bounds,
        IReadOnlyList<PlotSample> samples)
    {
        Pen pen = CreateSeriesPen(plot.Style);
        foreach (PlotSample sample in samples)
        {
            if (sample.LowerError is not { } lower ||
                sample.UpperError is not { } upper)
            {
                continue;
            }

            Point low = MapPoint(chart, plot, bounds, sample.X, sample.Y - lower);
            Point high = MapPoint(chart, plot, bounds, sample.X, sample.Y + upper);
            drawingContext.DrawLine(pen, low, high);
            drawingContext.DrawLine(pen, new Point(low.X - 4, low.Y), new Point(low.X + 4, low.Y));
            drawingContext.DrawLine(pen, new Point(high.X - 4, high.Y), new Point(high.X + 4, high.Y));
        }
    }

    private static void DrawHistogram(
        DrawingContext drawingContext,
        Rect chart,
        PlotObject plot,
        PlotBounds bounds,
        IReadOnlyList<PlotHistogramBin> bins)
    {
        Brush fill = ToBrush(plot.Style.MarkerFill);
        Pen stroke = CreateSeriesPen(plot.Style);
        foreach (PlotHistogramBin bin in bins)
        {
            if (plot.YAxis.Scale == PlotAxisScale.Log10 && bin.Count == 0)
            {
                continue;
            }

            double baseline = plot.YAxis.Scale == PlotAxisScale.Log10
                ? bounds.YMin
                : 0;
            Point lower = MapPoint(chart, plot, bounds, bin.Lower, baseline);
            Point upper = MapPoint(chart, plot, bounds, bin.Upper, bin.Count);
            var rect = new Rect(
                new Point(lower.X + 0.5, upper.Y),
                new Point(Math.Max(lower.X + 1, upper.X - 0.5), lower.Y));
            drawingContext.DrawRectangle(fill, stroke, rect);
        }
    }

    private static void DrawBoxPlot(
        DrawingContext drawingContext,
        Rect chart,
        PlotObject plot,
        PlotBounds bounds,
        IReadOnlyList<PlotBoxGroup> groups)
    {
        Pen pen = CreateSeriesPen(plot.Style);
        Brush fill = ToBrush(plot.Style.MarkerFill);
        foreach (PlotBoxGroup group in groups)
        {
            double min = group.Values[0];
            double q1 = Quantile(group.Values, 0.25);
            double median = Quantile(group.Values, 0.5);
            double q3 = Quantile(group.Values, 0.75);
            double max = group.Values[^1];
            Point minPoint = MapPoint(chart, plot, bounds, group.Index, min);
            Point maxPoint = MapPoint(chart, plot, bounds, group.Index, max);
            Point q1Point = MapPoint(chart, plot, bounds, group.Index, q1);
            Point q3Point = MapPoint(chart, plot, bounds, group.Index, q3);
            Point medianPoint = MapPoint(chart, plot, bounds, group.Index, median);
            double width = Math.Min(24, chart.Width / Math.Max(2, groups.Count * 2));
            drawingContext.DrawLine(pen, minPoint, maxPoint);
            drawingContext.DrawLine(pen, new Point(minPoint.X - width / 3, minPoint.Y), new Point(minPoint.X + width / 3, minPoint.Y));
            drawingContext.DrawLine(pen, new Point(maxPoint.X - width / 3, maxPoint.Y), new Point(maxPoint.X + width / 3, maxPoint.Y));
            drawingContext.DrawRectangle(
                fill,
                pen,
                new Rect(
                    new Point(q1Point.X - width / 2, q3Point.Y),
                    new Point(q1Point.X + width / 2, q1Point.Y)));
            drawingContext.DrawLine(
                pen,
                new Point(medianPoint.X - width / 2, medianPoint.Y),
                new Point(medianPoint.X + width / 2, medianPoint.Y));
            DrawText(
                drawingContext,
                group.Label,
                plot.Typography.Tick,
                new Point(minPoint.X, chart.Bottom + 7),
                TextAlignment.Center);
        }
    }

    private static void DrawHeatmap(
        DrawingContext drawingContext,
        Rect chart,
        PlotObject plot,
        PlotBounds bounds,
        IReadOnlyList<PlotSample> samples)
    {
        double[] values = samples
            .Where(sample => sample.Value.HasValue)
            .Select(sample => sample.Value!.Value)
            .ToArray();
        if (values.Length == 0)
        {
            throw new InvalidDataException("Heatmap value 列没有可绘制数值。");
        }

        double min = values.Min();
        double max = values.Max();
        double cellWidth = Math.Clamp(chart.Width / Math.Max(8, Math.Sqrt(values.Length) * 2), 3, 18);
        double cellHeight = Math.Clamp(chart.Height / Math.Max(8, Math.Sqrt(values.Length) * 2), 3, 18);
        foreach (PlotSample sample in samples.Where(sample => sample.Value.HasValue))
        {
            Point point = MapPoint(chart, plot, bounds, sample.X, sample.Y);
            double fraction = max > min ? (sample.Value!.Value - min) / (max - min) : 0.5;
            Color color = Color.FromRgb(
                (byte)(30 + 220 * fraction),
                (byte)(65 + 110 * (1 - Math.Abs(fraction - 0.5) * 2)),
                (byte)(220 - 190 * fraction));
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            drawingContext.DrawRectangle(
                brush,
                null,
                new Rect(
                    point.X - cellWidth / 2,
                    point.Y - cellHeight / 2,
                    cellWidth,
                    cellHeight));
        }
    }

    private static Point MapPoint(
        Rect chart,
        PlotObject plot,
        PlotBounds bounds,
        double x,
        double y) => new(
            chart.Left + AxisFraction(x, bounds.XMin, bounds.XMax, plot.XAxis.Scale) * chart.Width,
            chart.Bottom - AxisFraction(y, bounds.YMin, bounds.YMax, plot.YAxis.Scale) * chart.Height);

    private static double AxisFraction(
        double value,
        double minimum,
        double maximum,
        PlotAxisScale scale)
    {
        if (scale == PlotAxisScale.Log10)
        {
            if (value <= 0)
            {
                throw new InvalidDataException("对数轴包含非正数；SciCanvas 不会静默移除该数据。");
            }

            value = Math.Log10(value);
            minimum = Math.Log10(minimum);
            maximum = Math.Log10(maximum);
        }

        return (value - minimum) / (maximum - minimum);
    }

    private static IReadOnlyList<double> CreateMajorTicks(
        PlotAxisDefinition axis,
        double minimum,
        double maximum)
    {
        if (axis.MajorTickInterval is { } interval)
        {
            var ticks = new List<double>();
            double start = Math.Ceiling(minimum / interval) * interval;
            for (double value = start; value <= maximum && ticks.Count < 200; value += interval)
            {
                ticks.Add(value);
            }

            if (ticks.Count > 0)
            {
                return ticks;
            }
        }

        if (axis.Scale == PlotAxisScale.Log10)
        {
            int startExponent = (int)Math.Ceiling(Math.Log10(minimum));
            int endExponent = (int)Math.Floor(Math.Log10(maximum));
            double[] decades = Enumerable.Range(
                    startExponent,
                    Math.Max(0, endExponent - startExponent + 1))
                .Select(exponent => Math.Pow(10, exponent))
                .ToArray();
            if (decades.Length > 0)
            {
                return decades;
            }
        }

        return Enumerable.Range(0, 5)
            .Select(index => minimum + (maximum - minimum) * index / 4)
            .ToArray();
    }

    private static IReadOnlyList<PlotHistogramBin> CreateHistogram(double[] values)
    {
        if (values.Length == 0)
        {
            return [];
        }

        double min = values.Min();
        double max = values.Max();
        (min, max) = ExpandEqualRange(min, max);
        int binCount = Math.Clamp((int)Math.Ceiling(Math.Sqrt(values.Length)), 5, 50);
        double width = (max - min) / binCount;
        int[] counts = new int[binCount];
        foreach (double value in values)
        {
            int index = Math.Min(binCount - 1, (int)((value - min) / width));
            counts[Math.Max(0, index)]++;
        }

        return Enumerable.Range(0, binCount)
            .Select(index => new PlotHistogramBin(
                min + index * width,
                min + (index + 1) * width,
                counts[index]))
            .ToArray();
    }

    private static double Quantile(IReadOnlyList<double> sortedValues, double probability)
    {
        if (sortedValues.Count == 1)
        {
            return sortedValues[0];
        }

        double position = probability * (sortedValues.Count - 1);
        int lower = (int)Math.Floor(position);
        int upper = (int)Math.Ceiling(position);
        double fraction = position - lower;
        return sortedValues[lower] +
            (sortedValues[upper] - sortedValues[lower]) * fraction;
    }

    private static (double Minimum, double Maximum) ExpandEqualRange(
        double minimum,
        double maximum)
    {
        if (minimum != maximum)
        {
            return (minimum, maximum);
        }

        double padding = Math.Abs(minimum) > 1e-12 ? Math.Abs(minimum) * 0.05 : 0.5;
        return (minimum - padding, maximum + padding);
    }

    private static Pen CreateSeriesPen(PlotSeriesStyle style)
    {
        var pen = new Pen(ToBrush(style.LineColor), style.LineWidthPt * 96.0 / 72.0)
        {
            DashStyle = style.LineStyle switch
            {
                PlotLineStyle.Dash => DashStyles.Dash,
                PlotLineStyle.Dot => DashStyles.Dot,
                PlotLineStyle.DashDot => DashStyles.DashDot,
                _ => DashStyles.Solid,
            },
        };
        pen.Freeze();
        return pen;
    }

    private static void DrawMarker(
        DrawingContext drawingContext,
        Point point,
        PlotSeriesStyle style)
    {
        double radius = style.MarkerSizePt * 96.0 / 72.0 / 2;
        Brush fill = ToBrush(style.MarkerFill);
        var pen = new Pen(ToBrush(style.MarkerStroke), Math.Max(0.75, style.LineWidthPt * 96.0 / 72.0));
        pen.Freeze();
        switch (style.MarkerShape)
        {
            case PlotMarkerShape.Circle:
                drawingContext.DrawEllipse(fill, pen, point, radius, radius);
                break;
            case PlotMarkerShape.Square:
                drawingContext.DrawRectangle(
                    fill,
                    pen,
                    new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2));
                break;
            case PlotMarkerShape.Triangle:
                DrawPolygon(
                    drawingContext,
                    fill,
                    pen,
                    [
                        new Point(point.X, point.Y - radius),
                        new Point(point.X + radius, point.Y + radius),
                        new Point(point.X - radius, point.Y + radius),
                    ]);
                break;
            case PlotMarkerShape.Diamond:
                DrawPolygon(
                    drawingContext,
                    fill,
                    pen,
                    [
                        new Point(point.X, point.Y - radius),
                        new Point(point.X + radius, point.Y),
                        new Point(point.X, point.Y + radius),
                        new Point(point.X - radius, point.Y),
                    ]);
                break;
            case PlotMarkerShape.Cross:
                drawingContext.DrawLine(
                    pen,
                    new Point(point.X - radius, point.Y - radius),
                    new Point(point.X + radius, point.Y + radius));
                drawingContext.DrawLine(
                    pen,
                    new Point(point.X - radius, point.Y + radius),
                    new Point(point.X + radius, point.Y - radius));
                break;
        }
    }

    private static void DrawPolygon(
        DrawingContext drawingContext,
        Brush fill,
        Pen pen,
        IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(points[0], true, true);
            context.PolyLineTo(points.Skip(1).ToArray(), true, false);
        }

        geometry.Freeze();
        drawingContext.DrawGeometry(fill, pen, geometry);
    }

    private static void DrawText(
        DrawingContext drawingContext,
        string text,
        TextStyle style,
        Point origin,
        TextAlignment alignment)
    {
        var typeface = new Typeface(
            new FontFamily(style.FontFamily),
            FontStyles.Normal,
            style.IsBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            typeface,
            style.FontSizePt * 96.0 / 72.0,
            ToBrush(style.Color),
            1.0)
        {
            TextAlignment = alignment,
        };
        drawingContext.DrawText(formatted, origin);
    }

    private void DrawCenteredMessage(DrawingContext drawingContext, string message)
    {
        var style = new TextStyle("Arial", 8, false, "#FF66717F");
        var typeface = new Typeface(style.FontFamily);
        var formatted = new FormattedText(
            message,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            style.FontSizePt * 96.0 / 72.0,
            ToBrush(style.Color),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
        };
        drawingContext.DrawText(
            formatted,
            new Point(ActualWidth / 2, Math.Max(12, ActualHeight / 2 - formatted.Height / 2)));
    }

    private static Brush ToBrush(string colorText)
    {
        ScientificColorValue color = ScientificStyleColor.TryParseColor(colorText, out ScientificColorValue parsed)
            ? parsed
            : new ScientificColorValue(255, 17, 17, 17);
        var brush = new SolidColorBrush(Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }

    private static string FormatAxisTitle(PlotAxisDefinition axis) =>
        string.IsNullOrWhiteSpace(axis.Unit)
            ? axis.Title
            : $"{axis.Title} ({axis.Unit})";

    private static string FormatTick(double value) =>
        value == 0 || Math.Abs(value) is >= 0.001 and < 10000
            ? value.ToString("0.###", CultureInfo.InvariantCulture)
            : value.ToString("0.##E+0", CultureInfo.InvariantCulture);

    private static int IndexOf(TabularDataAsset asset, Guid columnId)
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

    private sealed record PlotRenderSeries(
        IReadOnlyList<PlotSample> Samples,
        IReadOnlyList<PlotHistogramBin> HistogramBins,
        IReadOnlyList<PlotBoxGroup> BoxGroups,
        PlotDataProjection Projection);

    private sealed record PlotSample(
        double X,
        double Y,
        double? LowerError,
        double? UpperError,
        double? Value);

    private sealed record PlotHistogramBin(double Lower, double Upper, int Count);

    private sealed record PlotBoxGroup(int Index, string Label, IReadOnlyList<double> Values);

    private readonly record struct PlotBounds(
        double XMin,
        double XMax,
        double YMin,
        double YMax);
}
