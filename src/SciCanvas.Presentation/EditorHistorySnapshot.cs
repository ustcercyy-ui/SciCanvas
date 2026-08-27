using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

internal sealed record EditorHistorySnapshot(
    string TemplateId,
    int CanvasWidth,
    int CanvasHeight,
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
    FigureGlobalStyle GlobalStyle,
    IReadOnlyList<ScientificColorDefinition> ScientificColors,
    Guid? SelectedPanelId,
    IReadOnlyList<Guid> SelectedPanelIds,
    Guid? SelectedAnnotationId,
    Guid? SelectedGuideId,
    bool SnappingEnabled,
    double SnapTolerancePixels,
    long ExactSpacingPixels,
    int FigureQcMinimumDpi,
    IReadOnlyList<PanelHistorySnapshot> Panels,
    IReadOnlyList<AnnotationHistorySnapshot> Annotations,
    IReadOnlyList<GuideHistorySnapshot> Guides,
    IReadOnlyList<CalibrationHistorySnapshot> Calibrations,
    IReadOnlyList<MeasurementHistorySnapshot> Measurements,
    IReadOnlyList<AnalysisHistorySnapshot> Analyses);

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
    bool ScaleBarShowLabel,
    int FrameIndex,
    ImageAdjustmentParameters Adjustments,
    bool IsAspectRatioLocked,
    Guid? CropLinkGroupId,
    PanelFitMode FitMode,
    double RotationDegrees,
    ScientificValidity ReplacementValidity,
    StyleOverride? StyleOverride);

internal sealed record AnnotationHistorySnapshot(
    Guid Id,
    FigureAnnotationKind Kind,
    double X,
    double Y,
    double EndX,
    double EndY,
    string Text,
    string StrokeColor,
    string FillColor,
    double FillOpacityPercent,
    string TextColor,
    string FontFamily,
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

internal sealed record CalibrationHistorySnapshot(
    Guid SourceId,
    SpatialCalibration Calibration,
    double ReferenceStartX,
    double ReferenceStartY,
    double ReferenceEndX,
    double ReferenceEndY);

internal sealed record MeasurementHistorySnapshot(
    Guid Id,
    Guid SourceId,
    long SourceRevision,
    ScientificMeasurementKind Kind,
    MeasurementPoint PointA,
    MeasurementPoint PointB,
    MeasurementPoint? PointC,
    IReadOnlyList<MeasurementPoint> PathPoints,
    string StrokeColor,
    double StrokeWidthPixels,
    string LineStyle,
    string FillColor,
    string MarkerStrokeColor,
    string MarkerFillColor,
    double MarkerSizePixels,
    bool ShowMarkers,
    bool ShowLabel,
    string LabelColor,
    string LabelFontFamily,
    double LabelFontSizePt,
    bool LabelIsBold,
    double FillOpacityPercent,
    bool IsVisible,
    bool IsLocked);

internal sealed record AnalysisHistorySnapshot(
    Guid SourceId,
    ScientificImageAnalysisResult Result);
