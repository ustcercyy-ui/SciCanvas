using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

/// <summary>
/// Export wrapper for a Figure reference to a canonical ROI. Geometry remains owned by
/// <see cref="RoiObject"/> and is resolved only while mapping/rendering.
/// </summary>
public sealed record FigureRoiProjectionExportItem(
    RoiFigureProjectionObject Projection,
    RoiObject CanonicalRoi)
{
    public Guid Id => Projection.Id;

    public Guid RoiId => Projection.RoiId;

    public Guid PanelId => Projection.PanelId ?? Guid.Empty;

    public Guid AssetId => Projection.AssetId ?? Guid.Empty;

    public long SourceRevision => Projection.SourceRevision ?? 0;

    public bool IsVisible => Projection.IsVisible;

    public int ZIndex => Projection.ZIndex;
}

public sealed record FigureRoiProjectionGeometry(
    RoiGeometryKind Kind,
    IReadOnlyList<MeasurementPoint> Points,
    MeasurementPoint LabelAnchor,
    FigureImageRect ImageRect,
    RoiStyle Style,
    double StrokeWidthPixels,
    double LabelFontSizePixels);

/// <summary>Maps canonical source-pixel ROI geometry through its referenced panel.</summary>
public static class FigureRoiProjectionMapper
{
    public static FigureRoiProjectionGeometry Map(
        FigureRoiProjectionExportItem item,
        FigurePanelExportItem panel,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ValidateRelationship(item, panel);

        FigureImageRect imageRect = FigureMeasurementOverlayMapper.CalculateImageRect(panel);
        double scale = imageRect.Width / panel.SourceRect.Width;
        MeasurementPoint MapPoint(MeasurementPoint point) => new(
            imageRect.X + (point.X - panel.SourceRect.X) * scale,
            imageRect.Y + (point.Y - panel.SourceRect.Y) * scale);

        MeasurementPoint[] points = item.CanonicalRoi.SourceGeometry
            .Select(MapPoint)
            .ToArray();
        RoiStyle style = ResolveStyle(item);
        MeasurementPoint labelAnchor = GetLabelAnchor(item.CanonicalRoi.GeometryKind, points);
        return new FigureRoiProjectionGeometry(
            item.CanonicalRoi.GeometryKind,
            Array.AsReadOnly(points),
            labelAnchor,
            imageRect,
            style,
            style.Shape.StrokeWidthPt / 72.0 * dpi,
            style.LabelStyle.FontSizePt / 72.0 * dpi);
    }

    public static void ValidateRelationship(
        FigureRoiProjectionExportItem item,
        FigurePanelExportItem panel)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(panel);
        RoiFigureProjectionObject projection = item.Projection;
        RoiObject roi = item.CanonicalRoi;
        roi.EnsureValid();
        if (projection.Id == Guid.Empty || projection.RoiId == Guid.Empty ||
            projection.RoiId != roi.Id ||
            projection.PanelId is not Guid panelId || panelId == Guid.Empty ||
            projection.AssetId is not Guid assetId || assetId == Guid.Empty ||
            projection.SourceRevision is not long revision || revision < 1 ||
            panel.PanelId != panelId || panel.Source.Id != assetId ||
            panel.SourceRevision != revision ||
            roi.AssetId != assetId || roi.SourceRevision != revision ||
            roi.FrameIndex != panel.FrameIndex ||
            !FitsPanelSourceRect(roi, panel.SourceRect))
        {
            throw new InvalidOperationException(
                "ROI Figure Projection 必须引用同一 canonical ROI、Panel、Asset、source revision/frame，且 ROI 必须位于 panel crop 内。");
        }

        projection.StyleOverride?.EnsureValid();
        _ = ResolveStyle(item);
    }

    public static bool FitsPanelSourceRect(RoiObject roi, SciCanvas.Core.Geometry.PixelRect64 sourceRect)
    {
        ArgumentNullException.ThrowIfNull(roi);
        return roi.SourceGeometry.All(point =>
            point.X >= sourceRect.X && point.X <= sourceRect.Right &&
            point.Y >= sourceRect.Y && point.Y <= sourceRect.Bottom);
    }

    private static RoiStyle ResolveStyle(FigureRoiProjectionExportItem item)
    {
        ShapeStyle shape = item.Projection.StyleOverride?.Shapes ?? item.CanonicalRoi.Style.Shape;
        TextStyle label = item.Projection.StyleOverride?.Annotation ?? item.CanonicalRoi.Style.LabelStyle;
        return new RoiStyle(shape, label, item.CanonicalRoi.Style.Label).EnsureValid();
    }

    private static MeasurementPoint GetLabelAnchor(
        RoiGeometryKind kind,
        IReadOnlyList<MeasurementPoint> points)
    {
        if (points.Count == 0)
        {
            return new MeasurementPoint(0, 0);
        }

        return kind is RoiGeometryKind.Rectangle or RoiGeometryKind.Ellipse
            ? new MeasurementPoint(
                Math.Min(points[0].X, points[1].X),
                Math.Min(points[0].Y, points[1].Y))
            : points[0];
    }
}
