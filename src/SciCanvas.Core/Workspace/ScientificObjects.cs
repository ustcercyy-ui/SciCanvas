using SciCanvas.Core.Export;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Workspace;

public enum ScientificObjectKind
{
    ScaleBar,
    Measurement,
    AnalysisResult,
    Roi,
    Inset,
    Colorbar,
    PanelLabel,
    DirectionMarker,
    Annotation,
    MeasurementOverlay,
}

public abstract record ScientificObject
{
    public required Guid Id { get; init; }

    public abstract ScientificObjectKind Kind { get; }

    public Guid? AssetId { get; init; }

    public Guid? PanelId { get; init; }

    public long? SourceRevision { get; init; }

    public StyleOverride? StyleOverride { get; init; }

    public ScientificValidity Validity { get; init; } = ScientificValidity.Valid;
}

public sealed record ScaleBarPlacement(
    ScaleBarAnchor Anchor,
    double OffsetXMm,
    double OffsetYMm);

public sealed record ScaleBarObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.ScaleBar;

    public required double PhysicalLength { get; init; }

    public required string Unit { get; init; }

    public required ScaleBarPlacement Placement { get; init; }

    public string? StyleRef { get; init; }
}

public sealed record MeasurementObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Measurement;

    public required ScientificMeasurement Measurement { get; init; }

    public string? CalibrationRevision { get; init; }

    public (double Value, string Unit)? CachedValue { get; init; }
}


/// <summary>
/// A figure-level scientific object which pins a source measurement to one panel
/// without turning the measurement into a presentation-only annotation.
/// </summary>
public sealed record MeasurementOverlayObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.MeasurementOverlay;

    public required Guid MeasurementId { get; init; }

    public required ScientificMeasurement SourceGeometry { get; init; }

    public FigureMeasurementCalibrationRelationship? CalibrationRelationship { get; init; }

    public required FigureMeasurementOverlayStyle Style { get; init; }

    public string? LabelOverride { get; init; }

    public bool IsVisible { get; init; } = true;

    public int ZIndex { get; init; }
}
public sealed record AnalysisResultObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.AnalysisResult;

    public required ScientificImageAnalysisResult Result { get; init; }
}

public enum RoiGeometryKind
{
    Rectangle,
    Ellipse,
    Polygon,
    Polyline,
}

public sealed record RoiStyle(
    string StrokeColor,
    double StrokeWidth,
    string FillColor,
    double FillOpacity,
    string? Label = null,
    string? LabelFont = null,
    string? LabelColor = null)
{
    public static RoiStyle Default { get; } = new(
        "#FF22C7E8",
        2,
        "#FF22C7E8",
        0.12,
        null,
        "Arial",
        "#FF22C7E8");

    public RoiStyle EnsureValid()
    {
        if (!ScientificStyleColor.ValidateColor(StrokeColor) ||
            !ScientificStyleColor.ValidateColor(FillColor) ||
            !double.IsFinite(StrokeWidth) || StrokeWidth <= 0 ||
            !double.IsFinite(FillOpacity) || FillOpacity is < 0 or > 1 ||
            Label?.Length > 256 || LabelFont?.Length > 128 ||
            (LabelColor is not null && !ScientificStyleColor.ValidateColor(LabelColor)))
        {
            throw new InvalidOperationException("ROI style 必须包含有效颜色、线宽、填充透明度与可选标签。");
        }

        return this;
    }
}

public sealed record RoiPropagationProvenance(
    Guid ReferenceRoiId,
    Guid TargetRoiId,
    Guid LinkGroupId,
    Guid MappingId)
{
    public RoiPropagationProvenance EnsureValid()
    {
        if (ReferenceRoiId == Guid.Empty || TargetRoiId == Guid.Empty ||
            LinkGroupId == Guid.Empty || MappingId == Guid.Empty ||
            ReferenceRoiId == TargetRoiId)
        {
            throw new InvalidOperationException("ROI propagation provenance 缺少有效 ROI、LinkGroup 或 Mapping ID。");
        }

        return this;
    }
}

/// <summary>Canonical ROI geometry is always stored in source pixel coordinates.</summary>
public sealed record RoiObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Roi;

    public required IReadOnlyList<MeasurementPoint> SourceGeometry { get; init; }

    public RoiGeometryKind GeometryKind { get; init; } = RoiGeometryKind.Polygon;

    public int FrameIndex { get; init; }

    public RoiStyle Style { get; init; } = RoiStyle.Default;

    public RoiPropagationProvenance? Propagation { get; init; }

    public RoiObject EnsureValid()
    {
        int minimumPoints = GeometryKind switch
        {
            RoiGeometryKind.Rectangle or RoiGeometryKind.Ellipse => 2,
            RoiGeometryKind.Polygon => 3,
            RoiGeometryKind.Polyline => 2,
            _ => int.MaxValue,
        };
        if (Id == Guid.Empty || AssetId is not Guid assetId || assetId == Guid.Empty ||
            SourceRevision is not long revision || revision < 1 || FrameIndex < 0 ||
            !Enum.IsDefined(GeometryKind) || SourceGeometry.Count < minimumPoints ||
            SourceGeometry.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new InvalidOperationException("Canonical ROI 必须绑定素材/revision/frame，并保存有效 source-pixel geometry。");
        }

        Style.EnsureValid();
        if (Propagation is not null)
        {
            Propagation.EnsureValid();
            if (Propagation.TargetRoiId != Id)
            {
                throw new InvalidOperationException("ROI propagation 的 TargetRoiId 必须等于当前 ROI ID。");
            }
        }

        return this;
    }
}

public sealed record InsetObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Inset;

    public required Guid RoiObjectId { get; init; }

    public required Guid InsetPanelId { get; init; }
}

public sealed record ColorbarObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Colorbar;

    public required double Minimum { get; init; }

    public required double Maximum { get; init; }

    public required string Unit { get; init; }

    public required string Colormap { get; init; }

    public Guid? ChannelId { get; init; }

    public IReadOnlyList<double> Ticks { get; init; } = [];
}

public sealed record PanelLabelObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.PanelLabel;

    public required string Text { get; init; }
}

public sealed record DirectionMarkerObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.DirectionMarker;

    public required string Label { get; init; }

    public required NormalizedPoint Start { get; init; }

    public required NormalizedPoint End { get; init; }
}

public sealed record AnnotationObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Annotation;

    public required string AnnotationKind { get; init; }

    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<NormalizedPoint> PanelGeometry { get; init; } = [];
}

public sealed record LinkGroup(
    Guid Id,
    IReadOnlyList<Guid> PanelIds,
    LinkSyncOptions Sync);

public sealed record LinkSyncOptions(
    bool Pan = false,
    bool Zoom = false,
    bool Crop = false,
    bool Roi = false,
    bool ColorScale = false);

public enum SpatialMappingKind
{
    Identity,
    Affine,
}

public sealed record SpatialMapping(
    Guid SourceAssetId,
    Guid TargetAssetId,
    SpatialMappingKind Kind,
    IReadOnlyList<double> Matrix);
