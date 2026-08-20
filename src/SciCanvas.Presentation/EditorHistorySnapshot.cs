using SciCanvas.Core.Geometry;

namespace SciCanvas.Presentation;

internal sealed record EditorHistorySnapshot(
    string TemplateId,
    IReadOnlyList<Guid> SourceIds,
    Guid? SelectedSourceId,
    PixelRect64? ActiveCrop,
    bool LockCropSizeAcrossSources,
    bool CropOverlayVisible,
    WorkspaceMode WorkspaceMode,
    string BackgroundColor,
    bool AutoPanelLabelsEnabled,
    bool ShowPanelLabels,
    string PanelLabelSequence,
    Guid? SelectedPanelId,
    IReadOnlyList<Guid> SelectedPanelIds,
    Guid? SelectedAnnotationId,
    Guid? SelectedGuideId,
    bool SnappingEnabled,
    double SnapTolerancePixels,
    long ExactSpacingPixels,
    IReadOnlyList<PanelHistorySnapshot> Panels,
    IReadOnlyList<AnnotationHistorySnapshot> Annotations,
    IReadOnlyList<GuideHistorySnapshot> Guides);

internal sealed record PanelHistorySnapshot(
    Guid Id,
    Guid SourceId,
    PixelRect64 SourceRect,
    string SlotId,
    PixelRect64 DestinationRect,
    string Label,
    bool IsVisible,
    bool IsLocked,
    int ZIndex,
    bool ShowScaleBar,
    double PhysicalUnitsPerSourcePixel,
    double ScaleBarPhysicalLength,
    string ScaleBarUnit,
    bool ScaleBarShowLabel);

internal sealed record AnnotationHistorySnapshot(
    Guid Id,
    FigureAnnotationKind Kind,
    double X,
    double Y,
    double EndX,
    double EndY,
    string Text,
    string Color,
    double FontSizePt,
    double StrokeWidthPt,
    bool IsBold,
    bool IsVisible,
    bool IsLocked,
    int ZIndex);

internal sealed record GuideHistorySnapshot(
    Guid Id,
    FigureGuideOrientation Orientation,
    double Position,
    bool IsLocked);
