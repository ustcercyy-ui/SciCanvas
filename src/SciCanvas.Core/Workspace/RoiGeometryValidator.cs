using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Workspace;

public enum RoiGeometryValidationState
{
    Inside,
    PartiallyOutside,
    Outside,
    Degenerate,
    SelfIntersecting,
    Invalid,
}

public readonly record struct RoiGeometryBounds(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public double Width => Right - Left;

    public double Height => Bottom - Top;
}

public sealed record RoiGeometryValidationResult(
    RoiGeometryValidationState State,
    double CoverageFraction,
    RoiGeometryBounds? Bounds,
    IReadOnlyList<string> Reasons)
{
    public bool IsGeometryValid => State is
        RoiGeometryValidationState.Inside or
        RoiGeometryValidationState.PartiallyOutside or
        RoiGeometryValidationState.Outside;

    public bool HasImageIntersection => State is
        RoiGeometryValidationState.Inside or
        RoiGeometryValidationState.PartiallyOutside;

    public bool ClippedToImage => State == RoiGeometryValidationState.PartiallyOutside;
}

public enum RoiBoundaryRole
{
    Reference,
    Propagated,
}

public sealed record RoiBoundaryPolicyResult(
    bool CanPersist,
    bool CanAnalyze,
    ScientificValidity Validity);

/// <summary>
/// Applies the explicit reference/propagated ROI boundary policy after geometry validation.
/// A reference ROI is rejected when partial unless the caller records an explicit confirmation;
/// a propagated partial ROI remains analyzable but requires review.
/// </summary>
public static class RoiOutOfBoundsPolicy
{
    public static RoiBoundaryPolicyResult Evaluate(
        RoiGeometryValidationResult validation,
        RoiBoundaryRole role,
        bool partialReferenceConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(validation);
        string coverage = validation.CoverageFraction.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        return validation.State switch
        {
            RoiGeometryValidationState.Inside =>
                new(true, true, ScientificValidity.Valid),
            RoiGeometryValidationState.PartiallyOutside when role == RoiBoundaryRole.Propagated =>
                new(true, true, ScientificValidity.ReviewRequired(
                    $"Propagated ROI is clipped to the target image (coverage fraction {coverage}).")),
            RoiGeometryValidationState.PartiallyOutside when partialReferenceConfirmed =>
                new(true, true, ScientificValidity.Warning(
                    $"Reference ROI clipping was explicitly confirmed (coverage fraction {coverage}).")),
            RoiGeometryValidationState.PartiallyOutside =>
                new(false, false, ScientificValidity.Invalid(
                    "Reference ROI extends outside the source image; explicitly confirm clipping before saving or analysis.")),
            RoiGeometryValidationState.Outside when role == RoiBoundaryRole.Propagated =>
                new(true, false, ScientificValidity.Invalid(
                    "Propagated ROI is fully outside the target image and cannot be analyzed.")),
            RoiGeometryValidationState.Outside =>
                new(true, false, ScientificValidity.Invalid(
                    "Reference ROI is fully outside the source image.")),
            _ => new(false, false, ScientificValidity.Invalid(validation.Reasons.ToArray())),
        };
    }
}

/// <summary>
/// Validates canonical ROI geometry in source-pixel coordinates and measures the
/// continuous geometry fraction covered by the source image rectangle [0,width]×[0,height].
/// </summary>
public static class RoiGeometryValidator
{
    public const double AreaEpsilon = 1e-9;
    private const double CoordinateEpsilon = 1e-10;
    private const int EllipseIntegrationSteps = 1024;

    public static RoiGeometryValidationResult Validate(RoiObject roi, PixelSize64 sourceSize) =>
        Validate(roi, sourceSize.Width, sourceSize.Height);

    public static RoiGeometryValidationResult Validate(
        RoiObject roi,
        long sourceWidth,
        long sourceHeight)
    {
        ArgumentNullException.ThrowIfNull(roi);
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return Invalid("Source image dimensions must both be greater than zero.");
        }

        if (!Enum.IsDefined(roi.GeometryKind) || roi.SourceGeometry is null)
        {
            return Invalid("ROI geometry kind or source geometry is invalid.");
        }

        MeasurementPoint[] points = roi.SourceGeometry.ToArray();
        if (points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            return Invalid("ROI coordinates must be finite.");
        }

        return roi.GeometryKind switch
        {
            RoiGeometryKind.Rectangle => ValidateRectangle(points, sourceWidth, sourceHeight),
            RoiGeometryKind.Ellipse => ValidateEllipse(points, sourceWidth, sourceHeight),
            RoiGeometryKind.Polygon => ValidatePolygon(points, sourceWidth, sourceHeight),
            RoiGeometryKind.Polyline => ValidatePolyline(points, sourceWidth, sourceHeight),
            _ => Invalid("ROI geometry kind is not supported."),
        };
    }

    public static PixelRect64 GetImageIntersectionBoundingRegion(
        RoiObject roi,
        PixelSize64 sourceSize,
        RoiGeometryValidationResult? validation = null)
    {
        validation ??= Validate(roi, sourceSize);
        if (!validation.HasImageIntersection || validation.Bounds is not RoiGeometryBounds bounds)
        {
            throw new InvalidOperationException("ROI geometry has no analyzable intersection with the source image.");
        }

        double leftValue = Math.Clamp(Math.Floor(bounds.Left), 0, sourceSize.Width);
        double topValue = Math.Clamp(Math.Floor(bounds.Top), 0, sourceSize.Height);
        double rightValue = Math.Clamp(Math.Ceiling(bounds.Right), 0, sourceSize.Width);
        double bottomValue = Math.Clamp(Math.Ceiling(bounds.Bottom), 0, sourceSize.Height);
        long left = checked((long)leftValue);
        long top = checked((long)topValue);
        long right = checked((long)rightValue);
        long bottom = checked((long)bottomValue);
        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException("ROI intersection contains no source-pixel bounding region.");
        }

        return new PixelRect64(left, top, right - left, bottom - top);
    }

    private static RoiGeometryValidationResult ValidateRectangle(
        IReadOnlyList<MeasurementPoint> points,
        double sourceWidth,
        double sourceHeight)
    {
        if (points.Count != 2)
        {
            return Invalid("Rectangle ROI must contain exactly two opposite-corner points.");
        }

        RoiGeometryBounds bounds = BoundsFromCorners(points[0], points[1]);
        double area = bounds.Width * bounds.Height;
        if (bounds.Width <= CoordinateEpsilon || bounds.Height <= CoordinateEpsilon ||
            !double.IsFinite(area) || area <= AreaEpsilon)
        {
            return Degenerate(bounds, "Rectangle ROI width and height must both be greater than zero.");
        }

        double intersectionArea = IntersectionArea(bounds, sourceWidth, sourceHeight);
        return ClassifyByMeasure(bounds, area, intersectionArea, sourceWidth, sourceHeight);
    }

    private static RoiGeometryValidationResult ValidateEllipse(
        IReadOnlyList<MeasurementPoint> points,
        double sourceWidth,
        double sourceHeight)
    {
        if (points.Count != 2)
        {
            return Invalid("Ellipse ROI must contain exactly two bounding-box corner points.");
        }

        RoiGeometryBounds bounds = BoundsFromCorners(points[0], points[1]);
        if (bounds.Width <= CoordinateEpsilon || bounds.Height <= CoordinateEpsilon)
        {
            return Degenerate(bounds, "Ellipse ROI width and height must both be greater than zero.");
        }

        double totalArea = Math.PI * bounds.Width * bounds.Height / 4;
        if (!double.IsFinite(totalArea) || totalArea <= AreaEpsilon)
        {
            return Degenerate(bounds, "Ellipse ROI area is below the supported epsilon.");
        }

        if (IsInside(bounds, sourceWidth, sourceHeight))
        {
            return Inside(bounds);
        }

        double intersectionArea = EllipseIntersectionArea(bounds, sourceWidth, sourceHeight);
        return ClassifyByMeasure(bounds, totalArea, intersectionArea, sourceWidth, sourceHeight);
    }

    private static RoiGeometryValidationResult ValidatePolygon(
        IReadOnlyList<MeasurementPoint> sourcePoints,
        double sourceWidth,
        double sourceHeight)
    {
        MeasurementPoint[] points = RemoveClosingDuplicate(sourcePoints);
        if (points.Length < 3)
        {
            return Degenerate(GetBoundsOrNull(points), "Polygon ROI must contain at least three vertices.");
        }

        RoiGeometryBounds bounds = GetBounds(points);
        if (HasZeroLengthEdge(points, closePath: true))
        {
            return Degenerate(bounds, "Polygon ROI contains a zero-length edge.");
        }

        if (IsSelfIntersecting(points))
        {
            return new RoiGeometryValidationResult(
                RoiGeometryValidationState.SelfIntersecting,
                0,
                bounds,
                ["Polygon ROI self-intersects."]);
        }

        double totalArea = PolygonArea(points);
        if (!double.IsFinite(totalArea) || totalArea <= AreaEpsilon)
        {
            return Degenerate(bounds, "Polygon ROI area is below the supported epsilon.");
        }

        if (IsInside(bounds, sourceWidth, sourceHeight))
        {
            return Inside(bounds);
        }

        IReadOnlyList<MeasurementPoint> clipped = ClipPolygonToImage(points, sourceWidth, sourceHeight);
        double intersectionArea = clipped.Count >= 3 ? PolygonArea(clipped) : 0;
        return ClassifyByMeasure(bounds, totalArea, intersectionArea, sourceWidth, sourceHeight);
    }

    private static RoiGeometryValidationResult ValidatePolyline(
        IReadOnlyList<MeasurementPoint> points,
        double sourceWidth,
        double sourceHeight)
    {
        if (points.Count < 2 || points.Distinct().Count() < 2)
        {
            return Degenerate(GetBoundsOrNull(points), "Polyline ROI must contain at least two distinct points.");
        }

        RoiGeometryBounds bounds = GetBounds(points);
        double totalLength = 0;
        double clippedLength = 0;
        for (int index = 0; index < points.Count - 1; index++)
        {
            MeasurementPoint first = points[index];
            MeasurementPoint second = points[index + 1];
            double length = Distance(first, second);
            totalLength += length;
            clippedLength += ClippedSegmentLength(first, second, sourceWidth, sourceHeight);
        }

        if (!double.IsFinite(totalLength) || totalLength <= CoordinateEpsilon)
        {
            return Degenerate(bounds, "Polyline ROI total length must be greater than zero.");
        }

        if (IsInside(bounds, sourceWidth, sourceHeight))
        {
            return Inside(bounds);
        }

        return ClassifyByMeasure(bounds, totalLength, clippedLength, sourceWidth, sourceHeight);
    }

    private static RoiGeometryValidationResult ClassifyByMeasure(
        RoiGeometryBounds bounds,
        double totalMeasure,
        double intersectionMeasure,
        double sourceWidth,
        double sourceHeight)
    {
        if (!double.IsFinite(intersectionMeasure) ||
            intersectionMeasure <= AreaEpsilon * Math.Max(1, totalMeasure))
        {
            return new RoiGeometryValidationResult(
                RoiGeometryValidationState.Outside,
                0,
                bounds,
                ["ROI geometry is fully outside the source image."]);
        }

        if (IsInside(bounds, sourceWidth, sourceHeight))
        {
            return Inside(bounds);
        }

        double coverage = Math.Clamp(intersectionMeasure / totalMeasure, 0, 1);
        coverage = Math.Min(coverage, 1 - 1e-12);
        return new RoiGeometryValidationResult(
            RoiGeometryValidationState.PartiallyOutside,
            coverage,
            bounds,
            ["ROI geometry extends outside the source image."]);
    }

    private static RoiGeometryValidationResult Inside(RoiGeometryBounds bounds) => new(
        RoiGeometryValidationState.Inside,
        1,
        bounds,
        []);

    private static RoiGeometryValidationResult Invalid(string reason) => new(
        RoiGeometryValidationState.Invalid,
        0,
        null,
        [reason]);

    private static RoiGeometryValidationResult Degenerate(RoiGeometryBounds? bounds, string reason) => new(
        RoiGeometryValidationState.Degenerate,
        0,
        bounds,
        [reason]);

    private static RoiGeometryBounds BoundsFromCorners(MeasurementPoint first, MeasurementPoint second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Max(first.X, second.X),
        Math.Max(first.Y, second.Y));

    private static RoiGeometryBounds GetBounds(IReadOnlyList<MeasurementPoint> points) => new(
        points.Min(point => point.X),
        points.Min(point => point.Y),
        points.Max(point => point.X),
        points.Max(point => point.Y));

    private static RoiGeometryBounds? GetBoundsOrNull(IReadOnlyList<MeasurementPoint> points) =>
        points.Count == 0 ? null : GetBounds(points);

    private static bool IsInside(RoiGeometryBounds bounds, double sourceWidth, double sourceHeight) =>
        bounds.Left >= 0 && bounds.Top >= 0 &&
        bounds.Right <= sourceWidth && bounds.Bottom <= sourceHeight;

    private static double IntersectionArea(
        RoiGeometryBounds bounds,
        double sourceWidth,
        double sourceHeight) =>
        Math.Max(0, Math.Min(bounds.Right, sourceWidth) - Math.Max(bounds.Left, 0)) *
        Math.Max(0, Math.Min(bounds.Bottom, sourceHeight) - Math.Max(bounds.Top, 0));

    private static double EllipseIntersectionArea(
        RoiGeometryBounds bounds,
        double sourceWidth,
        double sourceHeight)
    {
        double left = Math.Max(bounds.Left, 0);
        double right = Math.Min(bounds.Right, sourceWidth);
        if (right <= left)
        {
            return 0;
        }

        double centerX = (bounds.Left + bounds.Right) / 2;
        double centerY = (bounds.Top + bounds.Bottom) / 2;
        double radiusX = bounds.Width / 2;
        double radiusY = bounds.Height / 2;
        double step = (right - left) / EllipseIntegrationSteps;
        double sum = 0;
        for (int index = 0; index <= EllipseIntegrationSteps; index++)
        {
            double x = left + index * step;
            double normalizedX = (x - centerX) / radiusX;
            double halfHeight = radiusY * Math.Sqrt(Math.Max(0, 1 - normalizedX * normalizedX));
            double verticalOverlap = Math.Max(
                0,
                Math.Min(centerY + halfHeight, sourceHeight) - Math.Max(centerY - halfHeight, 0));
            int weight = index is 0 or EllipseIntegrationSteps ? 1 : index % 2 == 0 ? 2 : 4;
            sum += weight * verticalOverlap;
        }

        return sum * step / 3;
    }

    private static MeasurementPoint[] RemoveClosingDuplicate(IReadOnlyList<MeasurementPoint> points)
    {
        if (points.Count > 1 && Distance(points[0], points[^1]) <= CoordinateEpsilon)
        {
            return points.Take(points.Count - 1).ToArray();
        }

        return points.ToArray();
    }

    private static bool HasZeroLengthEdge(IReadOnlyList<MeasurementPoint> points, bool closePath)
    {
        int edgeCount = closePath ? points.Count : points.Count - 1;
        for (int index = 0; index < edgeCount; index++)
        {
            if (Distance(points[index], points[(index + 1) % points.Count]) <= CoordinateEpsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSelfIntersecting(IReadOnlyList<MeasurementPoint> polygon)
    {
        for (int firstEdge = 0; firstEdge < polygon.Count; firstEdge++)
        {
            int firstNext = (firstEdge + 1) % polygon.Count;
            for (int secondEdge = firstEdge + 1; secondEdge < polygon.Count; secondEdge++)
            {
                int secondNext = (secondEdge + 1) % polygon.Count;
                bool adjacent = firstEdge == secondEdge || firstNext == secondEdge || secondNext == firstEdge;
                if (!adjacent && SegmentsIntersect(
                        polygon[firstEdge],
                        polygon[firstNext],
                        polygon[secondEdge],
                        polygon[secondNext]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool SegmentsIntersect(
        MeasurementPoint firstStart,
        MeasurementPoint firstEnd,
        MeasurementPoint secondStart,
        MeasurementPoint secondEnd)
    {
        double o1 = Orientation(firstStart, firstEnd, secondStart);
        double o2 = Orientation(firstStart, firstEnd, secondEnd);
        double o3 = Orientation(secondStart, secondEnd, firstStart);
        double o4 = Orientation(secondStart, secondEnd, firstEnd);
        if (((o1 > CoordinateEpsilon && o2 < -CoordinateEpsilon) ||
             (o1 < -CoordinateEpsilon && o2 > CoordinateEpsilon)) &&
            ((o3 > CoordinateEpsilon && o4 < -CoordinateEpsilon) ||
             (o3 < -CoordinateEpsilon && o4 > CoordinateEpsilon)))
        {
            return true;
        }

        return Math.Abs(o1) <= CoordinateEpsilon && IsOnSegment(firstStart, firstEnd, secondStart) ||
               Math.Abs(o2) <= CoordinateEpsilon && IsOnSegment(firstStart, firstEnd, secondEnd) ||
               Math.Abs(o3) <= CoordinateEpsilon && IsOnSegment(secondStart, secondEnd, firstStart) ||
               Math.Abs(o4) <= CoordinateEpsilon && IsOnSegment(secondStart, secondEnd, firstEnd);
    }

    private static double Orientation(MeasurementPoint first, MeasurementPoint second, MeasurementPoint third) =>
        (second.X - first.X) * (third.Y - first.Y) -
        (second.Y - first.Y) * (third.X - first.X);

    private static bool IsOnSegment(MeasurementPoint first, MeasurementPoint second, MeasurementPoint point) =>
        point.X >= Math.Min(first.X, second.X) - CoordinateEpsilon &&
        point.X <= Math.Max(first.X, second.X) + CoordinateEpsilon &&
        point.Y >= Math.Min(first.Y, second.Y) - CoordinateEpsilon &&
        point.Y <= Math.Max(first.Y, second.Y) + CoordinateEpsilon;

    private static double PolygonArea(IReadOnlyList<MeasurementPoint> polygon)
    {
        double twiceArea = 0;
        for (int index = 0; index < polygon.Count; index++)
        {
            MeasurementPoint current = polygon[index];
            MeasurementPoint next = polygon[(index + 1) % polygon.Count];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        return Math.Abs(twiceArea) / 2;
    }

    private static IReadOnlyList<MeasurementPoint> ClipPolygonToImage(
        IReadOnlyList<MeasurementPoint> polygon,
        double sourceWidth,
        double sourceHeight)
    {
        IReadOnlyList<MeasurementPoint> clipped = polygon.ToArray();
        clipped = ClipAgainstBoundary(clipped, point => point.X >= 0,
            (start, end) => IntersectVertical(start, end, 0));
        clipped = ClipAgainstBoundary(clipped, point => point.X <= sourceWidth,
            (start, end) => IntersectVertical(start, end, sourceWidth));
        clipped = ClipAgainstBoundary(clipped, point => point.Y >= 0,
            (start, end) => IntersectHorizontal(start, end, 0));
        clipped = ClipAgainstBoundary(clipped, point => point.Y <= sourceHeight,
            (start, end) => IntersectHorizontal(start, end, sourceHeight));
        return clipped;
    }

    private static IReadOnlyList<MeasurementPoint> ClipAgainstBoundary(
        IReadOnlyList<MeasurementPoint> input,
        Func<MeasurementPoint, bool> isInside,
        Func<MeasurementPoint, MeasurementPoint, MeasurementPoint> intersect)
    {
        if (input.Count == 0)
        {
            return [];
        }

        var output = new List<MeasurementPoint>();
        MeasurementPoint previous = input[^1];
        bool previousInside = isInside(previous);
        foreach (MeasurementPoint current in input)
        {
            bool currentInside = isInside(current);
            if (currentInside)
            {
                if (!previousInside)
                {
                    output.Add(intersect(previous, current));
                }

                output.Add(current);
            }
            else if (previousInside)
            {
                output.Add(intersect(previous, current));
            }

            previous = current;
            previousInside = currentInside;
        }

        return output;
    }

    private static MeasurementPoint IntersectVertical(
        MeasurementPoint start,
        MeasurementPoint end,
        double x)
    {
        double amount = (x - start.X) / (end.X - start.X);
        return new MeasurementPoint(x, start.Y + amount * (end.Y - start.Y));
    }

    private static MeasurementPoint IntersectHorizontal(
        MeasurementPoint start,
        MeasurementPoint end,
        double y)
    {
        double amount = (y - start.Y) / (end.Y - start.Y);
        return new MeasurementPoint(start.X + amount * (end.X - start.X), y);
    }

    private static double ClippedSegmentLength(
        MeasurementPoint start,
        MeasurementPoint end,
        double sourceWidth,
        double sourceHeight)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double lower = 0;
        double upper = 1;
        if (!ClipTest(-deltaX, start.X, ref lower, ref upper) ||
            !ClipTest(deltaX, sourceWidth - start.X, ref lower, ref upper) ||
            !ClipTest(-deltaY, start.Y, ref lower, ref upper) ||
            !ClipTest(deltaY, sourceHeight - start.Y, ref lower, ref upper))
        {
            return 0;
        }

        return Math.Max(0, upper - lower) * Distance(start, end);
    }

    private static bool ClipTest(double direction, double distance, ref double lower, ref double upper)
    {
        if (Math.Abs(direction) <= CoordinateEpsilon)
        {
            return distance >= 0;
        }

        double ratio = distance / direction;
        if (direction < 0)
        {
            if (ratio > upper)
            {
                return false;
            }

            lower = Math.Max(lower, ratio);
        }
        else
        {
            if (ratio < lower)
            {
                return false;
            }

            upper = Math.Min(upper, ratio);
        }

        return true;
    }

    private static double Distance(MeasurementPoint first, MeasurementPoint second) =>
        Math.Sqrt(
            (second.X - first.X) * (second.X - first.X) +
            (second.Y - first.Y) * (second.Y - first.Y));
}
