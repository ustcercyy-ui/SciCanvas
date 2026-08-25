using SciCanvas.Core.Science;

namespace SciCanvas.Core.Workspace;

public enum ScientificObjectKind
{
    ScaleBar,
    Measurement,
    Roi,
    Inset,
    Colorbar,
    PanelLabel,
    DirectionMarker,
    Annotation,
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

public sealed record RoiObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Roi;

    public required IReadOnlyList<MeasurementPoint> SourceGeometry { get; init; }
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
