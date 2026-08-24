using System.Windows;
using SciCanvas.Core.Science;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ScientificMeasurementViewModelTests
{
    [Fact]
    public void MoveBy_TranslatesPolylineAsOneLayerAndClampsToImageBounds()
    {
        MeasurementPoint[] points =
        [
            new(10, 10),
            new(20, 30),
            new(90, 95),
        ];
        ScientificMeasurementViewModel measurement = Create(
            ScientificMeasurementKind.Polyline,
            points[0],
            points[^1],
            pathPoints: points);

        measurement.MoveBy(50, 50, sourceWidth: 100, sourceHeight: 100);

        Assert.Equal(
            [new MeasurementPoint(19, 14), new MeasurementPoint(29, 34), new MeasurementPoint(99, 99)],
            measurement.PathPoints);
        Assert.Equal(measurement.PathPoints[0], measurement.Measurement.PointA);
        Assert.Equal(measurement.PathPoints[^1], measurement.Measurement.PointB);

        measurement.IsLocked = true;
        measurement.MoveBy(-10, -10, sourceWidth: 100, sourceHeight: 100);
        Assert.Equal(new MeasurementPoint(19, 14), measurement.Measurement.PointA);
    }

    [Fact]
    public void VisualStyle_ControlsDashMarkersFillVisibilityAndSelectionHandles()
    {
        ScientificMeasurementViewModel measurement = Create(
            ScientificMeasurementKind.RectangleRoi,
            new MeasurementPoint(10, 20),
            new MeasurementPoint(40, 60));

        measurement.RestoreVisualStyle(new ScientificMeasurementVisualStyle
        {
            StrokeColor = "#FF336699",
            StrokeWidthPixels = 5,
            LineStyle = "dash-dot",
            MarkerSizePixels = 24,
            ShowMarkers = false,
            ShowLabel = false,
            FillOpacityPercent = 25,
            IsVisible = true,
            IsLocked = false,
        });
        measurement.IsSelected = true;

        Assert.Equal("dash-dot", measurement.LineStyle);
        Assert.Equal([5d, 2d, 1d, 2d], measurement.StrokeDashArray!.ToArray());
        Assert.Equal(Visibility.Collapsed, measurement.MarkerVisibility);
        Assert.Equal(Visibility.Collapsed, measurement.LabelVisibility);
        Assert.Equal(Visibility.Visible, measurement.SelectionHandleVisibility);
        Assert.Equal(64, ((System.Windows.Media.SolidColorBrush)measurement.OverlayFill).Color.A);
        Assert.Equal(measurement.VisualStyle, new ScientificMeasurementVisualStyle
        {
            StrokeColor = "#FF336699",
            StrokeWidthPixels = 5,
            LineStyle = "dash-dot",
            MarkerSizePixels = 24,
            ShowMarkers = false,
            ShowLabel = false,
            FillOpacityPercent = 25,
            IsVisible = true,
            IsLocked = false,
        });

        measurement.IsLocked = true;
        Assert.Equal(Visibility.Collapsed, measurement.SelectionHandleVisibility);
        Assert.Contains("已锁定", measurement.LayerStateText, StringComparison.Ordinal);
        measurement.IsVisible = false;
        Assert.Equal(Visibility.Collapsed, measurement.RectangleVisibility);
    }

    [Fact]
    public void UpdateEndpoints_UpdatesPolylineFirstAndLastNodes()
    {
        ScientificMeasurementViewModel measurement = Create(
            ScientificMeasurementKind.Polyline,
            new MeasurementPoint(1, 2),
            new MeasurementPoint(9, 10),
            pathPoints: [new MeasurementPoint(1, 2), new MeasurementPoint(5, 6), new MeasurementPoint(9, 10)]);

        measurement.UpdatePointA(2, 3);
        measurement.UpdatePointB(8, 9);

        Assert.Equal(new MeasurementPoint(2, 3), measurement.PathPoints[0]);
        Assert.Equal(new MeasurementPoint(8, 9), measurement.PathPoints[^1]);
    }

    private static ScientificMeasurementViewModel Create(
        ScientificMeasurementKind kind,
        MeasurementPoint pointA,
        MeasurementPoint pointB,
        MeasurementPoint? pointC = null,
        IReadOnlyList<MeasurementPoint>? pathPoints = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "measurement.png",
            kind,
            pointA,
            pointB,
            pointC,
            calibration: null,
            number: 1,
            pathPoints);
}
