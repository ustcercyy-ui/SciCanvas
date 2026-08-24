using SciCanvas.Core.Science;

namespace SciCanvas.Core.Tests;

public sealed class ScientificMeasurementTests
{
    [Fact]
    public void ManualCalibration_DrivesAnisotropicPhysicalLength()
    {
        Guid sourceId = Guid.NewGuid();
        var calibration = new SpatialCalibration(
            sourceId,
            UnitsPerPixelX: 2,
            UnitsPerPixelY: 1,
            Unit: "nm",
            Origin: CalibrationOrigin.Manual);
        var measurement = new ScientificMeasurement(
            Guid.NewGuid(),
            sourceId,
            ScientificMeasurementKind.Length,
            new MeasurementPoint(0, 0),
            new MeasurementPoint(3, 4));

        Assert.True(calibration.IsValid);
        Assert.True(calibration.IsAnisotropic);
        Assert.Equal(Math.Sqrt(52), measurement.PhysicalValue(calibration)!.Value, 8);
    }

    [Fact]
    public void ThreePointAngle_UsesMiddlePointAsVertex()
    {
        var measurement = new ScientificMeasurement(
            Guid.NewGuid(),
            Guid.NewGuid(),
            ScientificMeasurementKind.Angle,
            new MeasurementPoint(10, 0),
            new MeasurementPoint(0, 0),
            new MeasurementPoint(0, 10));

        Assert.True(measurement.IsValid);
        Assert.Equal(90, measurement.PixelValue, 8);
    }

    [Fact]
    public void RectangleRoi_ReportsPhysicalAreaAndDimensions()
    {
        Guid sourceId = Guid.NewGuid();
        SpatialCalibration calibration = SpatialCalibration.FromReference(
            sourceId,
            referencePixelLength: 100,
            referencePhysicalLength: 10,
            unit: "µm");
        var measurement = new ScientificMeasurement(
            Guid.NewGuid(),
            sourceId,
            ScientificMeasurementKind.RectangleRoi,
            new MeasurementPoint(10, 20),
            new MeasurementPoint(60, 50));

        Assert.Equal(15, measurement.PhysicalValue(calibration)!.Value, 8);
        Assert.Equal((5d, 3d), measurement.PhysicalRectangle(calibration)!.Value);
    }

    [Fact]
    public void CircleRoi_ReportsEquivalentDiameterAreaAndPerimeter()
    {
        Guid sourceId = Guid.NewGuid();
        SpatialCalibration calibration = SpatialCalibration.FromReference(
            sourceId,
            referencePixelLength: 100,
            referencePhysicalLength: 10,
            unit: "µm");
        var measurement = new ScientificMeasurement(
            Guid.NewGuid(),
            sourceId,
            ScientificMeasurementKind.CircleRoi,
            new MeasurementPoint(10, 20),
            new MeasurementPoint(50, 60));

        Assert.True(measurement.IsValid);
        Assert.Equal(40, measurement.PixelValue, 8);
        Assert.Equal(Math.PI * 400, measurement.PixelArea, 8);
        Assert.Equal(Math.PI * 40, measurement.PixelPerimeter, 8);
        Assert.Equal(4, measurement.PhysicalValue(calibration)!.Value, 8);
        Assert.Equal(Math.PI * 4, measurement.PhysicalArea(calibration)!.Value, 8);
        Assert.Equal(Math.PI * 4, measurement.PhysicalPerimeter(calibration)!.Value, 8);
    }

    [Fact]
    public void Polyline_SumsEverySegmentWithAnisotropicCalibration()
    {
        Guid sourceId = Guid.NewGuid();
        var calibration = new SpatialCalibration(
            sourceId,
            2,
            1,
            "nm",
            CalibrationOrigin.Manual);
        MeasurementPoint[] points =
        [
            new(0, 0),
            new(3, 4),
            new(6, 4),
        ];
        var measurement = new ScientificMeasurement(
            Guid.NewGuid(),
            sourceId,
            ScientificMeasurementKind.Polyline,
            points[0],
            points[^1],
            PathPoints: points);

        Assert.True(measurement.IsValid);
        Assert.Equal(8, measurement.PixelValue, 8);
        Assert.Equal(Math.Sqrt(52) + 6, measurement.PhysicalValue(calibration)!.Value, 8);
    }

    [Fact]
    public void Statistics_UsesSampleStandardDeviation()
    {
        MeasurementStatistics statistics = Assert.IsType<MeasurementStatistics>(
            MeasurementStatistics.Calculate([1, 2, 3]));

        Assert.Equal(3, statistics.Count);
        Assert.Equal(2, statistics.Mean, 8);
        Assert.Equal(1, statistics.StandardDeviation, 8);
        Assert.Equal(2, statistics.Median, 8);
        Assert.Equal(1, statistics.Minimum, 8);
        Assert.Equal(3, statistics.Maximum, 8);
    }

    [Fact]
    public void Histogram_UsesDeterministicBinsAndIncludesMaximumSample()
    {
        MeasurementHistogram histogram = Assert.IsType<MeasurementHistogram>(
            MeasurementHistogram.Create([0, 1, 2, 3, 4], requestedBinCount: 2));

        Assert.Equal(5, histogram.SampleCount);
        Assert.Equal(2, histogram.Bins.Count);
        Assert.Equal([2, 3], histogram.Bins.Select(bin => bin.Count).ToArray());
        Assert.Equal(0, histogram.Minimum);
        Assert.Equal(4, histogram.Maximum);
        Assert.Equal(3, histogram.MaximumBinCount);
    }

    [Fact]
    public void Histogram_CollapsesConstantValuesIntoOneBin()
    {
        MeasurementHistogram histogram = Assert.IsType<MeasurementHistogram>(
            MeasurementHistogram.Create([2.5, 2.5, 2.5]));

        MeasurementHistogramBin bin = Assert.Single(histogram.Bins);
        Assert.Equal(2.5, bin.LowerBound);
        Assert.Equal(2.5, bin.UpperBound);
        Assert.Equal(3, bin.Count);
    }
}
