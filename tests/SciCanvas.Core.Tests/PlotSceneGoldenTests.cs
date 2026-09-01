using System.Globalization;
using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class PlotSceneGoldenTests
{
    private const string SeriesColor = "#FFAA1133";
    private static readonly PlotRect Destination = new(0, 0, 420, 300);

    [Theory]
    [InlineData(PlotKind.Line, 1, 5, 1, 5, 4, 0, 0)]
    [InlineData(PlotKind.Scatter, 1, 5, 1, 5, 0, 5, 0)]
    [InlineData(PlotKind.LineAndSymbol, 1, 5, 1, 5, 4, 5, 0)]
    [InlineData(PlotKind.ErrorBar, 1, 5, 0.75, 5.25, 19, 5, 0)]
    [InlineData(PlotKind.Histogram, 1, 5, 0, 1, 0, 0, 5)]
    [InlineData(PlotKind.BoxPlot, -0.5, 1.5, 1, 5, 8, 0, 2)]
    [InlineData(PlotKind.Heatmap, 1, 5, 1, 5, 0, 0, 5)]
    public void Build_AllPlotKinds_MatchesGoldenPrimitiveSummary(
        PlotKind kind,
        double xMinimum,
        double xMaximum,
        double yMinimum,
        double yMaximum,
        int seriesLineCount,
        int markerCount,
        int rectangleCount)
    {
        PlotScene scene = Build(kind);

        AssertClose(xMinimum, scene.AxisBounds.XMinimum);
        AssertClose(xMaximum, scene.AxisBounds.XMaximum);
        AssertClose(yMinimum, scene.AxisBounds.YMinimum);
        AssertClose(yMaximum, scene.AxisBounds.YMaximum);
        AssertClose(61.8666666667, scene.Chart.X);
        AssertClose(25, scene.Chart.Y);
        AssertClose(345.3333333333, scene.Chart.Width);
        AssertClose(223.8, scene.Chart.Height);
        Assert.Equal(seriesLineCount, scene.Primitives.OfType<PlotLine>().Count(line => line.Stroke == SeriesColor));
        Assert.Equal(markerCount, scene.Primitives.OfType<PlotEllipse>().Count());
        Assert.Equal(rectangleCount, kind switch
        {
            PlotKind.Heatmap => scene.Primitives.OfType<PlotHeatmapCell>().Count(),
            _ => scene.Primitives.OfType<PlotRectangle>().Count(rectangle => rectangle.Stroke == SeriesColor),
        });
        Assert.All(scene.Primitives, primitive => Assert.True(
            primitive is PlotLine or PlotPolyline or PlotRectangle or PlotEllipse or
                PlotPolygon or PlotText or PlotHeatmapCell or PlotClipRegion,
            $"Unexpected scene primitive: {primitive.GetType().Name}"));
    }

    [Theory]
    [InlineData(PlotKind.Line)]
    [InlineData(PlotKind.Scatter)]
    [InlineData(PlotKind.LineAndSymbol)]
    [InlineData(PlotKind.ErrorBar)]
    [InlineData(PlotKind.Histogram)]
    [InlineData(PlotKind.Heatmap)]
    public void Build_NumericAxes_HaveGoldenMajorAndMinorTickPositions(PlotKind kind)
    {
        PlotScene scene = Build(kind);
        PlotLine[] xTicks = scene.Primitives.OfType<PlotLine>()
            .Where(line => line.Stroke == "#FF303945" &&
                Close(line.A.X, line.B.X) && Close(line.A.Y, scene.Chart.Bottom))
            .ToArray();
        PlotLine[] yTicks = scene.Primitives.OfType<PlotLine>()
            .Where(line => line.Stroke == "#FF303945" &&
                Close(line.A.Y, line.B.Y) && Close(line.B.X, scene.Chart.Left))
            .ToArray();

        Assert.Equal(21, xTicks.Length);
        Assert.Equal(21, yTicks.Length);
        Assert.Equal(
            [scene.Chart.Left, scene.Chart.Left + scene.Chart.Width / 4, scene.Chart.Left + scene.Chart.Width / 2,
             scene.Chart.Left + scene.Chart.Width * 3 / 4, scene.Chart.Right],
            xTicks.Where(line => Close(line.B.Y - line.A.Y, 4.6666666667)).Select(line => line.A.X),
            DoubleComparer.Instance);
        Assert.Equal(
            [scene.Chart.Bottom, scene.Chart.Bottom - scene.Chart.Height / 4, scene.Chart.Bottom - scene.Chart.Height / 2,
             scene.Chart.Bottom - scene.Chart.Height * 3 / 4, scene.Chart.Top],
            yTicks.Where(line => Close(line.B.X - line.A.X, 4.6666666667)).Select(line => line.A.Y),
            DoubleComparer.Instance);
    }

    [Fact]
    public void Build_XyAndErrorBar_HaveGoldenSeriesPointsAndErrorEndpoints()
    {
        PlotScene lineScene = Build(PlotKind.Line);
        PlotLine[] series = lineScene.Primitives.OfType<PlotLine>()
            .Where(line => line.Stroke == SeriesColor)
            .ToArray();
        AssertPoint(Map(lineScene, 1, 1), series[0].A);
        AssertPoint(Map(lineScene, 2, 2), series[0].B);
        AssertPoint(Map(lineScene, 5, 5), series[^1].B);

        PlotScene errorScene = Build(PlotKind.ErrorBar);
        PlotLine firstError = errorScene.Primitives.OfType<PlotLine>()
            .First(line => line.Stroke == SeriesColor);
        AssertPoint(Map(errorScene, 1, 0.75), firstError.A);
        AssertPoint(Map(errorScene, 1, 1.25), firstError.B);
    }

    [Fact]
    public void Build_HistogramAndBoxPlot_HaveGoldenBinsAndQuartiles()
    {
        PlotScene histogram = Build(PlotKind.Histogram);
        PlotRectangle[] bins = histogram.Primitives.OfType<PlotRectangle>()
            .Where(rectangle => rectangle.Stroke == SeriesColor)
            .ToArray();
        Assert.Equal(5, bins.Length);
        AssertClose(histogram.Chart.Width / 5 - 1, bins[0].Bounds.Width);
        AssertClose(histogram.Chart.Height, bins[0].Bounds.Height);
        AssertClose(histogram.Chart.Left + 0.5, bins[0].Bounds.Left);
        AssertClose(histogram.Chart.Right - 0.5, bins[^1].Bounds.Right);

        PlotScene box = Build(PlotKind.BoxPlot);
        PlotRectangle[] boxes = box.Primitives.OfType<PlotRectangle>()
            .Where(rectangle => rectangle.Stroke == SeriesColor)
            .ToArray();
        Assert.Equal(2, boxes.Length);
        PlotPoint q1 = Map(box, 0, 1.5);
        PlotPoint q3 = Map(box, 0, 2.5);
        AssertClose(q3.Y, boxes[0].Bounds.Top);
        AssertClose(q1.Y - q3.Y, boxes[0].Bounds.Height);
        PlotLine median = box.Primitives.OfType<PlotLine>()
            .Where(line => line.Stroke == SeriesColor)
            .ElementAt(3);
        AssertClose(Map(box, 0, 2).Y, median.A.Y);
        AssertClose(median.A.Y, median.B.Y);
    }

    [Fact]
    public void Build_Heatmap_HasGoldenClippedCellBounds()
    {
        PlotScene scene = Build(PlotKind.Heatmap);
        PlotHeatmapCell[] cells = scene.Primitives.OfType<PlotHeatmapCell>().ToArray();

        Assert.Equal(5, cells.Length);
        Assert.Equal("#FF1E41DC", cells[0].Fill);
        Assert.Equal("#FFFA411E", cells[^1].Fill);
        Assert.Equal(new PlotRect(scene.Chart.Left, scene.Chart.Bottom - 9, 9, 9), cells[0].Bounds);
        Assert.Equal(new PlotRect(scene.Chart.Right - 9, scene.Chart.Top, 9, 9), cells[^1].Bounds);
    }

    private static PlotScene Build(PlotKind kind)
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot(kind);
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);
        return PlotSceneBuilder.Build(plot, projection, plot.Typography, Destination, 96);
    }

    private static (TabularDataAsset Asset, PlotObject Plot) CreatePlot(PlotKind kind)
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Y", TabularDataType.Numeric, Role: DataColumnRole.Y);
        DataColumn error = new(Guid.NewGuid(), "Error", TabularDataType.Numeric, Role: DataColumnRole.YError);
        DataColumn value = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
        DataColumn category = new(Guid.NewGuid(), "Group", TabularDataType.Text, Role: DataColumnRole.Category);
        DataColumn[] columns = [x, y, error, value, category];
        TabularDataRow[] rows = Enumerable.Range(1, 5)
            .Select(index => new TabularDataRow(
            [
                Number(index),
                Number(index),
                Number(0.25),
                Number(index * 10),
                TabularDataValue.FromText(index <= 3 ? "Control" : "Treatment"),
            ]))
            .ToArray();
        var asset = new TabularDataAsset(
            Guid.NewGuid(),
            "Golden plot data",
            null,
            null,
            4,
            columns,
            rows,
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = rows.Length,
                InferenceRowCount = rows.Length,
                OriginalHeaders = columns.Select(column => column.Name).ToArray(),
            }).EnsureValid();
        Guid? xColumn = kind switch
        {
            PlotKind.Histogram => null,
            PlotKind.BoxPlot => category.Id,
            _ => x.Id,
        };
        var plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = $"{kind} golden",
            PlotType = kind,
            Data = new PlotDataBinding(
                asset.Id,
                asset.SourceRevision,
                xColumn,
                y.Id,
                kind == PlotKind.ErrorBar
                    ? new PlotErrorBarBinding(PlotErrorBarMode.Symmetric, SymmetricColumnId: error.Id)
                    : null,
                kind == PlotKind.Heatmap ? value.Id : null),
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default with
            {
                LineColor = SeriesColor,
                MarkerStroke = SeriesColor,
                MarkerFill = "#FFFFFFFF",
                MarkerShape = PlotMarkerShape.Circle,
            },
        };
        return (asset, plot.EnsureValid(asset));
    }

    private static TabularDataValue Number(double value) =>
        TabularDataValue.FromNumber(value.ToString(CultureInfo.InvariantCulture), value);

    private static PlotPoint Map(PlotScene scene, double x, double y) => new(
        scene.Chart.Left + (x - scene.AxisBounds.XMinimum) /
            (scene.AxisBounds.XMaximum - scene.AxisBounds.XMinimum) * scene.Chart.Width,
        scene.Chart.Bottom - (y - scene.AxisBounds.YMinimum) /
            (scene.AxisBounds.YMaximum - scene.AxisBounds.YMinimum) * scene.Chart.Height);

    private static void AssertPoint(PlotPoint expected, PlotPoint actual)
    {
        AssertClose(expected.X, actual.X);
        AssertClose(expected.Y, actual.Y);
    }

    private static void AssertClose(double expected, double actual) =>
        Assert.True(Close(expected, actual), $"Expected {expected:R}, actual {actual:R}.");

    private static bool Close(double left, double right) => Math.Abs(left - right) < 1e-6;

    private sealed class DoubleComparer : IEqualityComparer<double>
    {
        public static DoubleComparer Instance { get; } = new();

        public bool Equals(double x, double y) => Close(x, y);

        public int GetHashCode(double obj) => 0;
    }
}
