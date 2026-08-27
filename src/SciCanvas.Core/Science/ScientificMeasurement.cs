namespace SciCanvas.Core.Science;

public enum ScientificMeasurementKind
{
    Length,
    Angle,
    RectangleRoi,
    CircleRoi,
    Polyline,
}

public readonly record struct MeasurementPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

public sealed record ScientificMeasurement(
    Guid Id,
    Guid SourceAssetId,
    ScientificMeasurementKind Kind,
    MeasurementPoint PointA,
    MeasurementPoint PointB,
    MeasurementPoint? PointC = null,
    string? Name = null,
    IReadOnlyList<MeasurementPoint>? PathPoints = null,
    long SourceRevision = 1)
{
    public IReadOnlyList<MeasurementPoint> EffectivePathPoints =>
        Kind == ScientificMeasurementKind.Polyline && PathPoints is { Count: >= 2 }
            ? PathPoints
            : [PointA, PointB];

    public bool IsValid =>
        Id != Guid.Empty &&
        SourceAssetId != Guid.Empty &&
        SourceRevision >= 1 &&
        PointA.IsFinite &&
        PointB.IsFinite &&
        (Kind != ScientificMeasurementKind.Angle || PointC is { IsFinite: true }) &&
        (Kind != ScientificMeasurementKind.Polyline ||
         EffectivePathPoints.Count >= 2 && EffectivePathPoints.All(point => point.IsFinite)) &&
        PixelValue > 0;

    public double PixelValue => Kind switch
    {
        ScientificMeasurementKind.Length => Distance(PointA, PointB),
        ScientificMeasurementKind.Angle => CalculateAngleDegrees(),
        ScientificMeasurementKind.RectangleRoi =>
            Math.Abs(PointB.X - PointA.X) * Math.Abs(PointB.Y - PointA.Y),
        ScientificMeasurementKind.CircleRoi => EquivalentDiameter(PixelArea),
        ScientificMeasurementKind.Polyline => PathLength(EffectivePathPoints),
        _ => 0,
    };

    public double PixelArea => Kind switch
    {
        ScientificMeasurementKind.RectangleRoi =>
            Math.Abs(PointB.X - PointA.X) * Math.Abs(PointB.Y - PointA.Y),
        ScientificMeasurementKind.CircleRoi =>
            Math.PI * Math.Abs(PointB.X - PointA.X) / 2 * Math.Abs(PointB.Y - PointA.Y) / 2,
        _ => 0,
    };

    public double PixelPerimeter => Kind switch
    {
        ScientificMeasurementKind.RectangleRoi =>
            2 * (Math.Abs(PointB.X - PointA.X) + Math.Abs(PointB.Y - PointA.Y)),
        ScientificMeasurementKind.CircleRoi => EllipsePerimeter(
            Math.Abs(PointB.X - PointA.X) / 2,
            Math.Abs(PointB.Y - PointA.Y) / 2),
        _ => 0,
    };

    public double? PhysicalValue(SpatialCalibration? calibration)
    {
        if (calibration?.IsValid != true || calibration.SourceAssetId != SourceAssetId)
        {
            return null;
        }

        return Kind switch
        {
            ScientificMeasurementKind.Length => calibration.ConvertDistance(
                PointB.X - PointA.X,
                PointB.Y - PointA.Y),
            ScientificMeasurementKind.Angle => CalculateAngleDegrees(
                calibration.UnitsPerPixelX,
                calibration.UnitsPerPixelY),
            ScientificMeasurementKind.RectangleRoi =>
                Math.Abs(PointB.X - PointA.X) * calibration.UnitsPerPixelX *
                Math.Abs(PointB.Y - PointA.Y) * calibration.UnitsPerPixelY,
            ScientificMeasurementKind.CircleRoi => EquivalentDiameter(
                PhysicalArea(calibration) ?? 0),
            ScientificMeasurementKind.Polyline => PhysicalPathLength(
                EffectivePathPoints,
                calibration),
            _ => null,
        };
    }

    public double? PhysicalArea(SpatialCalibration? calibration)
    {
        if (calibration?.IsValid != true || calibration.SourceAssetId != SourceAssetId)
        {
            return null;
        }

        return Kind switch
        {
            ScientificMeasurementKind.RectangleRoi =>
                Math.Abs(PointB.X - PointA.X) * calibration.UnitsPerPixelX *
                Math.Abs(PointB.Y - PointA.Y) * calibration.UnitsPerPixelY,
            ScientificMeasurementKind.CircleRoi =>
                Math.PI * Math.Abs(PointB.X - PointA.X) * calibration.UnitsPerPixelX / 2 *
                Math.Abs(PointB.Y - PointA.Y) * calibration.UnitsPerPixelY / 2,
            _ => null,
        };
    }

    public double? PhysicalPerimeter(SpatialCalibration? calibration)
    {
        if (calibration?.IsValid != true || calibration.SourceAssetId != SourceAssetId)
        {
            return null;
        }

        return Kind switch
        {
            ScientificMeasurementKind.RectangleRoi => 2 * (
                Math.Abs(PointB.X - PointA.X) * calibration.UnitsPerPixelX +
                Math.Abs(PointB.Y - PointA.Y) * calibration.UnitsPerPixelY),
            ScientificMeasurementKind.CircleRoi => EllipsePerimeter(
                Math.Abs(PointB.X - PointA.X) * calibration.UnitsPerPixelX / 2,
                Math.Abs(PointB.Y - PointA.Y) * calibration.UnitsPerPixelY / 2),
            _ => null,
        };
    }

    public (double Width, double Height)? PhysicalRectangle(SpatialCalibration? calibration)
    {
        if (Kind != ScientificMeasurementKind.RectangleRoi ||
            calibration?.IsValid != true ||
            calibration.SourceAssetId != SourceAssetId)
        {
            return null;
        }

        return calibration.ConvertRectangle(
            PointB.X - PointA.X,
            PointB.Y - PointA.Y);
    }

    private double CalculateAngleDegrees(double scaleX = 1, double scaleY = 1)
    {
        if (PointC is not MeasurementPoint pointC)
        {
            return 0;
        }

        double commonScale = Math.Max(Math.Abs(scaleX), Math.Abs(scaleY));
        if (!double.IsFinite(commonScale) || commonScale <= 0)
        {
            return 0;
        }

        // Dividing both calibration axes by the same value preserves the angle
        // while avoiding overflow when valid units-per-pixel values are large.
        double normalizedScaleX = scaleX / commonScale;
        double normalizedScaleY = scaleY / commonScale;
        double ax = (PointA.X - PointB.X) * normalizedScaleX;
        double ay = (PointA.Y - PointB.Y) * normalizedScaleY;
        double cx = (pointC.X - PointB.X) * normalizedScaleX;
        double cy = (pointC.Y - PointB.Y) * normalizedScaleY;
        double lengthA = Math.Sqrt(ax * ax + ay * ay);
        double lengthC = Math.Sqrt(cx * cx + cy * cy);
        if (lengthA <= double.Epsilon || lengthC <= double.Epsilon)
        {
            return 0;
        }

        double cosine = Math.Clamp((ax * cx + ay * cy) / (lengthA * lengthC), -1, 1);
        return Math.Acos(cosine) * 180 / Math.PI;
    }

    private static double Distance(MeasurementPoint first, MeasurementPoint second)
    {
        double dx = second.X - first.X;
        double dy = second.Y - first.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double PathLength(IReadOnlyList<MeasurementPoint> points)
    {
        double length = 0;
        for (int index = 1; index < points.Count; index++)
        {
            length += Distance(points[index - 1], points[index]);
        }

        return length;
    }

    private static double PhysicalPathLength(
        IReadOnlyList<MeasurementPoint> points,
        SpatialCalibration calibration)
    {
        double length = 0;
        for (int index = 1; index < points.Count; index++)
        {
            length += calibration.ConvertDistance(
                points[index].X - points[index - 1].X,
                points[index].Y - points[index - 1].Y);
        }

        return length;
    }

    private static double EquivalentDiameter(double area) =>
        area > 0 ? 2 * Math.Sqrt(area / Math.PI) : 0;

    private static double EllipsePerimeter(double radiusX, double radiusY)
    {
        if (radiusX <= 0 || radiusY <= 0)
        {
            return 0;
        }

        double h = Math.Pow(radiusX - radiusY, 2) / Math.Pow(radiusX + radiusY, 2);
        return Math.PI * (radiusX + radiusY) *
               (1 + 3 * h / (10 + Math.Sqrt(4 - 3 * h)));
    }
}

public sealed record MeasurementStatistics(
    int Count,
    double Mean,
    double StandardDeviation,
    double Median,
    double Minimum,
    double Maximum)
{
    public static MeasurementStatistics? Calculate(IEnumerable<double> values)
    {
        double[] ordered = values
            .Where(double.IsFinite)
            .OrderBy(value => value)
            .ToArray();
        if (ordered.Length == 0)
        {
            return null;
        }

        double mean = ordered.Average();
        double variance = ordered.Length > 1
            ? ordered.Sum(value => Math.Pow(value - mean, 2)) / (ordered.Length - 1)
            : 0;
        double median = ordered.Length % 2 == 1
            ? ordered[ordered.Length / 2]
            : (ordered[ordered.Length / 2 - 1] + ordered[ordered.Length / 2]) / 2;
        return new MeasurementStatistics(
            ordered.Length,
            mean,
            Math.Sqrt(variance),
            median,
            ordered[0],
            ordered[^1]);
    }
}
