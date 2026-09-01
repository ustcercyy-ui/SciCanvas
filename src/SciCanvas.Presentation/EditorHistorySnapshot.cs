using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using LinkingLinkGroup = SciCanvas.Core.Linking.LinkGroup;
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
    Guid? SelectedScientificObjectId,
    Guid? SelectedGuideId,
    bool SnappingEnabled,
    double SnapTolerancePixels,
    long ExactSpacingPixels,
    int FigureQcMinimumDpi,
    IReadOnlyList<PanelHistorySnapshot> Panels,
    IReadOnlyList<AnnotationHistorySnapshot> Annotations,
    IReadOnlyList<ScientificObjectHistorySnapshot> ScientificObjects,
    IReadOnlyList<GuideHistorySnapshot> Guides,
    IReadOnlyList<CalibrationHistorySnapshot> Calibrations,
    IReadOnlyList<MeasurementHistorySnapshot> Measurements,
    IReadOnlyList<AnalysisHistorySnapshot> Analyses,
    IReadOnlyList<MultiChannelAssetGroup> MultiChannelGroups,
    IReadOnlyList<LinkingLinkGroup>? LinkGroups = null,
    IReadOnlyList<RoiObject>? Rois = null,
    IReadOnlyList<JournalExportPreset>? JournalPresets = null,
    IReadOnlyList<FontSubstitutionRule>? FontSubstitutions = null,
    IReadOnlyList<PlotPanelHistorySnapshot>? PlotPanels = null,
    Guid? SelectedPlotPanelId = null);

internal sealed record PlotPanelHistorySnapshot(
    Guid Id,
    Guid PlotId,
    PixelRect64 DestinationRect,
    string Label,
    bool IsVisible,
    bool IsLocked,
    int ZIndex,
    StyleOverride? StyleOverride,
    FigurePlotTypographyOverride? TypographyOverride);

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
    string CalibrationUnit,
    ScaleBarAnchor PrimaryScaleBarAnchor,
    IReadOnlyList<AdditionalScaleBarHistorySnapshot> AdditionalScaleBars,
    bool ScaleBarShowLabel,
    int FrameIndex,
    ImageAdjustmentParameters Adjustments,
    bool IsAspectRatioLocked,
    Guid? CropLinkGroupId,
    Guid? CompositeGroupId,
    PanelFitMode FitMode,
    double RotationDegrees,
    ScientificValidity ReplacementValidity,
    StyleOverride? StyleOverride);

internal sealed record AdditionalScaleBarHistorySnapshot(
    Guid Id,
    double PhysicalLength,
    string Unit,
    ScaleBarAnchor Anchor,
    bool ShowLabel,
    bool IsVisible);
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

internal sealed record ScientificObjectHistorySnapshot(
    Guid Id,
    FigureScientificObjectKind Kind,
    string PointsText,
    string Label,
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
    int ZIndex,
    double Minimum,
    double Maximum,
    string Unit,
    string Colormap,
    string ChannelEntriesText,
    Guid? ChannelId,
    ColorbarBindingState ColorbarBindingState,
    FigureObjectOrientation ColorbarOrientation,
    string ColorbarTicksText,
    double ChannelLegendPadding);
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
