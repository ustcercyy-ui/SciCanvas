using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

/// <summary>
/// Visual parameters for a measurement pinned to a figure. Values are kept separate
/// from the immutable source pixels and from the measurement's source geometry.
/// </summary>
/// <summary>
/// Export-facing wrapper for a typed scientific measurement overlay.
/// </summary>
public sealed record FigureMeasurementOverlayExportItem(MeasurementOverlayObject ScientificObject)
{
    public Guid Id => ScientificObject.Id;

    public Guid MeasurementId => ScientificObject.MeasurementId;

    public Guid SourceAssetId => ScientificObject.AssetId ?? Guid.Empty;

    public long SourceRevision => ScientificObject.SourceRevision ?? 0;

    public Guid PanelId => ScientificObject.PanelId ?? Guid.Empty;

    public ScientificMeasurementKind MeasurementKind => ScientificObject.SourceGeometry.Kind;

    public ScientificMeasurement SourceGeometry => ScientificObject.SourceGeometry;

    public FigureMeasurementCalibrationRelationship? CalibrationRelationship => ScientificObject.CalibrationRelationship;

    public FigureMeasurementOverlayStyle Style => ScientificObject.Style;

    public bool IsVisible => ScientificObject.IsVisible;

    public int ZIndex => ScientificObject.ZIndex;
}
public sealed record FigureMeasurementOverlayStyle(
    string StrokeColor,
    double StrokeWidthPixels,
    string LineStyle,
    string FillColor,
    double FillOpacityPercent,
    string MarkerStrokeColor,
    string MarkerFillColor,
    double MarkerSizePixels,
    bool ShowMarkers,
    string LabelColor,
    string LabelFontFamily,
    double LabelFontSizePt,
    bool LabelIsBold,
    bool ShowLabel)
{
    public void EnsureValid()
    {
        if (!ScientificStyleColor.ValidateColor(StrokeColor) ||
            !ScientificStyleColor.ValidateColor(FillColor) ||
            !ScientificStyleColor.ValidateColor(MarkerStrokeColor) ||
            !ScientificStyleColor.ValidateColor(MarkerFillColor) ||
            !ScientificStyleColor.ValidateColor(LabelColor) ||
            !double.IsFinite(StrokeWidthPixels) || StrokeWidthPixels is < 0.25 or > 24 ||
            LineStyle is not ("solid" or "dash" or "dot" or "dash-dot") ||
            !double.IsFinite(FillOpacityPercent) || FillOpacityPercent is < 0 or > 100 ||
            !double.IsFinite(MarkerSizePixels) || MarkerSizePixels is < 2 or > 96 ||
            string.IsNullOrWhiteSpace(LabelFontFamily) || LabelFontFamily.Length > 128 ||
            !double.IsFinite(LabelFontSizePt) || LabelFontSizePt is < 4 or > 72)
        {
            throw new InvalidOperationException("Measurement Overlay 的颜色、线型、端点或标签样式无效。");
        }
    }
}

/// <summary>
/// The calibration used when the pinned measurement label was created. Keeping the
/// source revision here makes the physical interpretation auditable after export.
/// </summary>
public sealed record FigureMeasurementCalibrationRelationship(
    Guid SourceAssetId,
    long SourceRevision,
    double UnitsPerPixelX,
    double UnitsPerPixelY,
    string Unit)
{
    public bool IsValid =>
        SourceAssetId != Guid.Empty &&
        SourceRevision >= 1 &&
        double.IsFinite(UnitsPerPixelX) && UnitsPerPixelX > 0 &&
        double.IsFinite(UnitsPerPixelY) && UnitsPerPixelY > 0 &&
        !string.IsNullOrWhiteSpace(Unit);

    public SpatialCalibration ToCalibration() => new(
        SourceAssetId,
        UnitsPerPixelX,
        UnitsPerPixelY,
        Unit,
        CalibrationOrigin.Manual);
}

/// <summary>
/// A resolved figure-space measurement geometry. All values are figure pixel
/// coordinates and are shared by preview and every export backend.
/// </summary>
public sealed record FigureMeasurementOverlayGeometry(
    MeasurementPoint PointA,
    MeasurementPoint PointB,
    MeasurementPoint? PointC,
    IReadOnlyList<MeasurementPoint> PathPoints,
    MeasurementPoint LabelAnchor,
    double StrokeWidthPixels,
    double MarkerSizePixels,
    FigureImageRect ImageRect);

public readonly record struct FigureImageRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;
}

/// <summary>Maps source-pixel measurement geometry through a panel into figure pixels.</summary>
public static class FigureMeasurementOverlayMapper
{
    public static FigureMeasurementOverlayGeometry Map(
        MeasurementOverlayObject overlay,
        FigurePanelExportItem panel)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(panel);
        ValidateRelationship(overlay, panel);

        FigureImageRect imageRect = CalculateImageRect(panel);
        double scale = imageRect.Width / panel.SourceRect.Width;
        ScientificMeasurement measurement = overlay.SourceGeometry;
        MeasurementPoint MapPoint(MeasurementPoint point) => new(
            imageRect.X + (point.X - panel.SourceRect.X) * scale,
            imageRect.Y + (point.Y - panel.SourceRect.Y) * scale);

        return new FigureMeasurementOverlayGeometry(
            MapPoint(measurement.PointA),
            MapPoint(measurement.PointB),
            measurement.PointC is MeasurementPoint pointC ? MapPoint(pointC) : null,
            measurement.EffectivePathPoints.Select(MapPoint).ToArray(),
            MapPoint(GetLabelAnchor(measurement)),
            overlay.Style.StrokeWidthPixels * scale,
            overlay.Style.MarkerSizePixels * scale,
            imageRect);
    }

    public static FigureImageRect CalculateImageRect(FigurePanelExportItem panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.SourceRect.Width <= 0 || panel.SourceRect.Height <= 0 ||
            panel.DestinationRect.Width <= 0 || panel.DestinationRect.Height <= 0)
        {
            throw new InvalidOperationException("Measurement Overlay 无法映射到无效的面板区域。");
        }

        double scale = Math.Min(
            panel.DestinationRect.Width / (double)panel.SourceRect.Width,
            panel.DestinationRect.Height / (double)panel.SourceRect.Height);
        double width = panel.SourceRect.Width * scale;
        double height = panel.SourceRect.Height * scale;
        return new FigureImageRect(
            panel.DestinationRect.X + (panel.DestinationRect.Width - width) / 2,
            panel.DestinationRect.Y + (panel.DestinationRect.Height - height) / 2,
            width,
            height);
    }

    public static bool FitsPanelSourceRect(MeasurementOverlayObject overlay, FigurePanelExportItem panel) =>
        overlay.SourceGeometry.EffectivePathPoints.All(point =>
            point.X >= panel.SourceRect.X && point.X <= panel.SourceRect.Right &&
            point.Y >= panel.SourceRect.Y && point.Y <= panel.SourceRect.Bottom) &&
        (overlay.SourceGeometry.PointC is not MeasurementPoint pointC ||
         pointC.X >= panel.SourceRect.X && pointC.X <= panel.SourceRect.Right &&
         pointC.Y >= panel.SourceRect.Y && pointC.Y <= panel.SourceRect.Bottom);

    public static string CreateLabel(MeasurementOverlayObject overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (!string.IsNullOrWhiteSpace(overlay.LabelOverride))
        {
            return overlay.LabelOverride;
        }

        ScientificMeasurement measurement = overlay.SourceGeometry;
        SpatialCalibration? calibration = overlay.CalibrationRelationship is { IsValid: true } relationship
            ? relationship.ToCalibration()
            : null;
        if (measurement.Kind == ScientificMeasurementKind.RectangleRoi &&
            measurement.PhysicalRectangle(calibration) is { } rectangle)
        {
            return $"{rectangle.Width:0.###} × {rectangle.Height:0.###} {calibration!.Unit}";
        }

        double value = measurement.PhysicalValue(calibration) ?? measurement.PixelValue;
        string unit = measurement.Kind switch
        {
            ScientificMeasurementKind.Angle => "°",
            ScientificMeasurementKind.RectangleRoi when calibration?.IsValid == true => $"{calibration.Unit}²",
            ScientificMeasurementKind.CircleRoi when calibration?.IsValid == true => calibration.Unit,
            ScientificMeasurementKind.Length when calibration?.IsValid == true => calibration.Unit,
            ScientificMeasurementKind.Polyline when calibration?.IsValid == true => calibration.Unit,
            ScientificMeasurementKind.RectangleRoi => "px²",
            _ => "px",
        };
        return measurement.Kind == ScientificMeasurementKind.CircleRoi
            ? $"Ø {value:0.###} {unit}"
            : $"{value:0.###} {unit}";
    }

    public static void ValidateRelationship(MeasurementOverlayObject overlay, FigurePanelExportItem panel)
    {
        if (overlay.Id == Guid.Empty || overlay.MeasurementId == Guid.Empty ||
            overlay.AssetId is not Guid assetId || assetId == Guid.Empty ||
            overlay.PanelId is not Guid panelId || panelId == Guid.Empty ||
            overlay.SourceRevision is not long revision || revision < 1 ||
            panel.PanelId != panelId || panel.Source.Id != assetId ||
            overlay.SourceGeometry.Id != overlay.MeasurementId ||
            overlay.SourceGeometry.SourceAssetId != assetId ||
            overlay.SourceGeometry.SourceRevision != revision ||
            !overlay.SourceGeometry.IsValid ||
            !FitsPanelSourceRect(overlay, panel))
        {
            throw new InvalidOperationException("Measurement Overlay 与其源测量或目标 Panel 的科学关系无效。");
        }

        if (overlay.CalibrationRelationship is { } calibration &&
            (!calibration.IsValid || calibration.SourceAssetId != assetId ||
             calibration.SourceRevision != revision))
        {
            throw new InvalidOperationException("Measurement Overlay 的标定关系无效或与源修订不一致。");
        }

        overlay.Style.EnsureValid();
        overlay.StyleOverride?.EnsureValid();
    }

    private static MeasurementPoint GetLabelAnchor(ScientificMeasurement measurement) => measurement.Kind switch
    {
        ScientificMeasurementKind.Angle => new(measurement.PointB.X + 14, measurement.PointB.Y - 34),
        ScientificMeasurementKind.RectangleRoi => new(
            Math.Min(measurement.PointA.X, measurement.PointB.X) + 10,
            Math.Min(measurement.PointA.Y, measurement.PointB.Y) - 32),
        ScientificMeasurementKind.CircleRoi => new(
            Math.Min(measurement.PointA.X, measurement.PointB.X) + 10,
            Math.Min(measurement.PointA.Y, measurement.PointB.Y) - 32),
        ScientificMeasurementKind.Polyline => new(measurement.PointB.X + 10, measurement.PointB.Y - 32),
        _ => new(
            (measurement.PointA.X + measurement.PointB.X) / 2 + 10,
            (measurement.PointA.Y + measurement.PointB.Y) / 2 - 32),
    };
}
