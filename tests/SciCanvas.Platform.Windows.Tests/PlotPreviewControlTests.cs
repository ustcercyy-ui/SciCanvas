using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.App;
using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Imaging;
using Xunit.Abstractions;

namespace SciCanvas.Platform.Windows.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class PlotPreviewControlTests
{
    private readonly ITestOutputHelper _output;

    public PlotPreviewControlTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(PlotKind.Line)]
    [InlineData(PlotKind.Scatter)]
    [InlineData(PlotKind.LineAndSymbol)]
    [InlineData(PlotKind.ErrorBar)]
    [InlineData(PlotKind.Histogram)]
    [InlineData(PlotKind.BoxPlot)]
    [InlineData(PlotKind.Heatmap)]
    public void VectorPreview_RendersSeriesPixelsForEveryTwoDimensionalKind(PlotKind kind)
    {
        PlotPreviewRenderTimings? previewTimings = null;
        TimeSpan bitmapRenderElapsed = TimeSpan.Zero;
        TimeSpan copyPixelsElapsed = TimeSpan.Zero;
        WpfTestInvocationTiming hostTiming = WpfTestHost.Invoke(() =>
        {
            TabularDataAsset asset = CreateAsset();
            PlotObject plot = CreatePlot(asset, kind);
            var control = new PlotPreviewControl
            {
                Width = 420,
                Height = 300,
                DataAsset = asset,
                Plot = plot,
            };
            control.Measure(new Size(420, 300));
            control.Arrange(new Rect(0, 0, 420, 300));
            var bitmap = new RenderTargetBitmap(
                420,
                300,
                96,
                96,
                PixelFormats.Pbgra32);

            long renderStarted = Stopwatch.GetTimestamp();
            bitmap.Render(control);
            bitmapRenderElapsed = Stopwatch.GetElapsedTime(renderStarted);
            previewTimings = control.LastRenderTimings;

            var pixels = new byte[420 * 300 * 4];
            long copyPixelsStarted = Stopwatch.GetTimestamp();
            bitmap.CopyPixels(pixels, 420 * 4, 0);
            copyPixelsElapsed = Stopwatch.GetElapsedTime(copyPixelsStarted);
            int chromaticPixels = 0;
            for (int index = 0; index < pixels.Length; index += 4)
            {
                byte blue = pixels[index];
                byte green = pixels[index + 1];
                byte red = pixels[index + 2];
                if (Math.Max(red, Math.Max(green, blue)) -
                    Math.Min(red, Math.Min(green, blue)) > 35)
                {
                    chromaticPixels++;
                }
            }

            Assert.True(
                chromaticPixels > 10,
                $"{kind} preview did not render a chromatic series.");
        }, TimeSpan.FromSeconds(5));

        Assert.True(previewTimings.HasValue, "Plot preview did not publish render timings.");
        PlotPreviewRenderTimings renderTimings = previewTimings.Value;
        _output.WriteLine(
            $"{kind}: projection={renderTimings.Projection.TotalMilliseconds:0.000} ms; " +
            $"bounds={renderTimings.Bounds.TotalMilliseconds:0.000} ms; " +
            $"axis={renderTimings.AxisGeneration.TotalMilliseconds:0.000} ms; " +
            $"heatmapGeometry={renderTimings.HeatmapGeometry.TotalMilliseconds:0.000} ms; " +
            $"wpfDrawing={renderTimings.WpfDrawing.TotalMilliseconds:0.000} ms; " +
            $"RenderTargetBitmap.Render={bitmapRenderElapsed.TotalMilliseconds:0.000} ms; " +
            $"CopyPixels={copyPixelsElapsed.TotalMilliseconds:0.000} ms; " +
            $"hostSerialization={hostTiming.SerializationWait.TotalMilliseconds:0.000} ms; " +
            $"hostDispatcherQueue={hostTiming.DispatcherQueueWait.TotalMilliseconds:0.000} ms; " +
            $"hostExecution={hostTiming.Execution.TotalMilliseconds:0.000} ms.");
    }

    [Fact]
    public void CoreSceneBuild_HeatmapIsFastAndDoesNotRequireWpfDispatcher()
    {
        TabularDataAsset asset = CreateAsset();
        PlotObject plot = CreatePlot(asset, PlotKind.Heatmap);
        FigurePlotPanelExportItem panel = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(0, 0, 420, 300),
            "a",
            typographyOverride: FigurePlotTypographyOverride.FromPlot(plot.Typography));

        long started = Stopwatch.GetTimestamp();
        FigurePlotScene scene = FigurePlotSceneBuilder.Build(
            panel,
            FigureGlobalStyle.Default,
            96);
        TimeSpan elapsed = Stopwatch.GetElapsedTime(started);

        _output.WriteLine(
            $"12-point Heatmap core scene: {elapsed.TotalMilliseconds:0.000} ms; " +
            $"primitives={scene.Primitives.Count}.");
        Assert.True(scene.Primitives.Count > 12, "Heatmap scene did not include axes and cells.");
        Assert.True(
            elapsed < TimeSpan.FromSeconds(2),
            $"12-point Heatmap core scene took {elapsed.TotalMilliseconds:0.0} ms.");
    }

    private static PlotObject CreatePlot(TabularDataAsset asset, PlotKind kind)
    {
        Guid x = asset.Columns[0].Id;
        Guid y = asset.Columns[1].Id;
        PlotDataBinding binding = kind switch
        {
            PlotKind.Histogram => new PlotDataBinding(asset.Id, 4, null, y),
            PlotKind.BoxPlot => new PlotDataBinding(
                asset.Id,
                4,
                asset.Columns[5].Id,
                y),
            PlotKind.Heatmap => new PlotDataBinding(
                asset.Id,
                4,
                x,
                y,
                ValueColumnId: asset.Columns[4].Id),
            PlotKind.ErrorBar => new PlotDataBinding(
                asset.Id,
                4,
                x,
                y,
                new PlotErrorBarBinding(
                    PlotErrorBarMode.Asymmetric,
                    LowerColumnId: asset.Columns[2].Id,
                    UpperColumnId: asset.Columns[3].Id)),
            _ => new PlotDataBinding(asset.Id, 4, x, y),
        };
        return new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = $"{kind} preview",
            PlotType = kind,
            Data = binding,
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default with
            {
                LineColor = "#FFFF2020",
                MarkerFill = "#FFFF2020",
                MarkerStroke = "#FFFF2020",
                MarkerShape = PlotMarkerShape.Circle,
            },
        }.EnsureValid(asset);
    }

    private static TabularDataAsset CreateAsset()
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Y", TabularDataType.Numeric, Role: DataColumnRole.Y);
        DataColumn low = new(Guid.NewGuid(), "Low", TabularDataType.Numeric, Role: DataColumnRole.YError);
        DataColumn high = new(Guid.NewGuid(), "High", TabularDataType.Numeric, Role: DataColumnRole.YError);
        DataColumn value = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
        DataColumn category = new(Guid.NewGuid(), "Group", TabularDataType.Text, Role: DataColumnRole.Category);
        DataColumn[] columns = [x, y, low, high, value, category];
        TabularDataRow[] rows = Enumerable.Range(0, 12)
            .Select(index => new TabularDataRow(
            [
                TabularDataValue.FromNumber((index + 1).ToString(), index + 1),
                TabularDataValue.FromNumber((index * index + 5).ToString(), index * index + 5),
                TabularDataValue.FromNumber("1", 1),
                TabularDataValue.FromNumber("2", 2),
                TabularDataValue.FromNumber((index * 3).ToString(), index * 3),
                TabularDataValue.FromText(index % 2 == 0 ? "Control" : "Treatment"),
            ]))
            .ToArray();
        return new TabularDataAsset(
            Guid.NewGuid(),
            "Preview data",
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
    }
}
