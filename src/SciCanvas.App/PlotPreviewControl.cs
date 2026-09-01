using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;

namespace SciCanvas.App;

public sealed class PlotPreviewControl : FrameworkElement
{
    private const int PreviewDpi = 96;

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

    internal PlotPreviewRenderTimings? LastRenderTimings { get; private set; }

    internal PlotScene? LastRenderScene { get; private set; }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        LastRenderTimings = null;
        LastRenderScene = null;
        drawingContext.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
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
            LastRenderTimings = RenderPlot(drawingContext, plot, asset);
        }
        catch (Exception exception) when (exception is
            InvalidDataException or InvalidOperationException or ArgumentException)
        {
            DrawCenteredMessage(drawingContext, $"Plot 不可预览\n{exception.Message}");
        }
    }

    private PlotPreviewRenderTimings RenderPlot(
        DrawingContext drawingContext,
        PlotObject plot,
        TabularDataAsset asset)
    {
        long totalStarted = Stopwatch.GetTimestamp();
        long projectionStarted = Stopwatch.GetTimestamp();
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);
        TimeSpan projectionElapsed = Stopwatch.GetElapsedTime(projectionStarted);

        PlotSceneBuildResult build = PlotSceneBuilder.BuildWithDiagnostics(
            plot,
            projection,
            plot.Typography,
            new PlotRect(0, 0, ActualWidth, ActualHeight),
            PreviewDpi);
        LastRenderScene = build.Scene;

        long drawingStarted = Stopwatch.GetTimestamp();
        WpfPlotSceneRenderer.Draw(drawingContext, build.Scene);
        TimeSpan drawingElapsed = Stopwatch.GetElapsedTime(drawingStarted);
        return new PlotPreviewRenderTimings(
            projectionElapsed,
            build.Timings.Bounds,
            build.Timings.AxisGeneration,
            build.Timings.HeatmapGeometry,
            drawingElapsed,
            Stopwatch.GetElapsedTime(totalStarted));
    }

    private void DrawCenteredMessage(DrawingContext drawingContext, string message)
    {
        var formatted = new FormattedText(
            message,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Arial"),
            8 * PreviewDpi / 72.0,
            Brushes.SlateGray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            TextAlignment = TextAlignment.Center,
        };
        drawingContext.DrawText(
            formatted,
            new Point(ActualWidth / 2, Math.Max(12, ActualHeight / 2 - formatted.Height / 2)));
    }
}

internal readonly record struct PlotPreviewRenderTimings(
    TimeSpan Projection,
    TimeSpan Bounds,
    TimeSpan AxisGeneration,
    TimeSpan HeatmapGeometry,
    TimeSpan WpfDrawing,
    TimeSpan Total);
