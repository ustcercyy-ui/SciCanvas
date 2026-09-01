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

    [Fact]
    public void VisualStyle_RoundTripsIndependentColorsAndLabelTypography()
    {
        ScientificMeasurementViewModel measurement = Create(
            ScientificMeasurementKind.RectangleRoi,
            new MeasurementPoint(10, 20),
            new MeasurementPoint(40, 60));
        var style = new ScientificMeasurementVisualStyle
        {
            StrokeColor = "#FFAA0000",
            FillColor = "#0000FF",
            FillOpacityPercent = 20,
            MarkerStrokeColor = "#FF000000",
            MarkerFillColor = "#FFFFFFFF",
            LabelColor = "#FF00AA00",
            LabelFontFamily = "Consolas",
            LabelFontSizePt = 11,
            LabelIsBold = true,
        };

        measurement.RestoreVisualStyle(style);

        Assert.True(measurement.IsStyleValid);
        Assert.Equal("#FFAA0000", measurement.StrokeColor);
        Assert.Equal("#0000FF", measurement.FillColor);
        Assert.Equal("#FF00AA00", measurement.LabelColor);
        Assert.Equal("Consolas", measurement.LabelFontFamily);
        Assert.Equal(11, measurement.LabelFontSizePt);
        Assert.True(measurement.LabelIsBold);
        Assert.Equal(style with
        {
            StrokeWidthPixels = ScientificMeasurementVisualStyle.Default.StrokeWidthPixels,
            LineStyle = ScientificMeasurementVisualStyle.Default.LineStyle,
            MarkerSizePixels = ScientificMeasurementVisualStyle.Default.MarkerSizePixels,
            ShowMarkers = ScientificMeasurementVisualStyle.Default.ShowMarkers,
            ShowLabel = ScientificMeasurementVisualStyle.Default.ShowLabel,
            IsVisible = ScientificMeasurementVisualStyle.Default.IsVisible,
            IsLocked = ScientificMeasurementVisualStyle.Default.IsLocked,
        }, measurement.VisualStyle);
    }

    [Fact]
    public void StyleValidation_AcceptsRgbAndArgbButRejectsInvalidColorAndFontSize()
    {
        ScientificMeasurementViewModel measurement = Create(
            ScientificMeasurementKind.Length,
            new MeasurementPoint(1, 2),
            new MeasurementPoint(20, 20));

        measurement.StrokeColor = "#123456";
        measurement.LabelColor = "#AA123456";
        Assert.True(measurement.IsStyleValid);

        measurement.FillColor = "blue";
        Assert.False(measurement.IsStyleValid);
        measurement.FillColor = "#FF0000FF";
        measurement.LabelFontSizePt = 73;
        Assert.False(measurement.IsStyleValid);
    }

    [Fact]
    public void LengthDirection_CanBeSetToExactHorizontalVerticalOrArbitraryAngle()
    {
        ScientificMeasurementViewModel measurement = new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "measurement.png",
            ScientificMeasurementKind.Length,
            new MeasurementPoint(50, 50),
            new MeasurementPoint(150, 50),
            pointC: null,
            calibration: null,
            number: 1,
            sourceWidth: 200,
            sourceHeight: 160);
        double originalLength = measurement.Measurement.PixelValue;

        measurement.DirectionAngleDegrees = 90;

        Assert.Equal(90, measurement.DirectionAngleDegrees, 8);
        Assert.Equal(measurement.X1, measurement.X2, 8);
        Assert.Equal(originalLength, measurement.Measurement.PixelValue, 8);

        measurement.DirectionAngleDegrees = 32.5;

        Assert.Equal(32.5, measurement.DirectionAngleDegrees, 8);
        Assert.True(measurement.IsValid);
        Assert.True(measurement.SetHorizontalDirectionCommand.CanExecute(null));
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
