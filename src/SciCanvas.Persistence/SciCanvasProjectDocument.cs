using System.Text.Json.Serialization;

namespace SciCanvas.Persistence;

public sealed class SciCanvasProjectDocument
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "https://scicanvas.org/schemas/scicanvas-project.schema.json";

    public string SchemaVersion { get; init; } = ProjectMigrationPipeline.CurrentVersion;

    public Guid ProjectId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public string? Title { get; init; }

    public ProjectCanvasSnapshot Canvas { get; init; } = new();

    public IReadOnlyList<ProjectSourceSnapshot> Sources { get; init; } = [];

    public IReadOnlyList<ProjectTabularDataAssetSnapshot> DataAssets { get; init; } = [];

    public IReadOnlyList<ProjectPlotSnapshot> Plots { get; init; } = [];

    public IReadOnlyList<ProjectImageLayerSnapshot> Layers { get; init; } = [];

    public IReadOnlyList<ProjectCropPresetSnapshot> CropPresets { get; init; } = [];

    public IReadOnlyList<ProjectGuideSnapshot> Guides { get; init; } = [];

    public IReadOnlyList<ProjectExportProfileSnapshot> ExportProfiles { get; init; } = [];

    public IReadOnlyList<ProjectJournalPresetSnapshot> JournalPresetSnapshots { get; init; } = [];

    public IReadOnlyList<ProjectFontSubstitutionSnapshot> FontSubstitutions { get; init; } = [];

    public IReadOnlyList<ProjectCalibrationSnapshot> Calibrations { get; init; } = [];

    public IReadOnlyList<ProjectMeasurementSnapshot> Measurements { get; init; } = [];

    public IReadOnlyList<ProjectScientificAnalysisSnapshot> Analyses { get; init; } = [];

    public IReadOnlyList<ProjectMultiChannelAssetGroupSnapshot> MultiChannelGroups { get; init; } = [];

    public IReadOnlyList<ProjectLinkGroupSnapshot> LinkGroups { get; init; } = [];

    public IReadOnlyList<ProjectRoiSnapshot> Rois { get; init; } = [];

    public ProjectTemplateSnapshot? TemplateSnapshot { get; init; }

    public IReadOnlyList<ProjectAuditEntrySnapshot> AuditTrail { get; init; } = [];

    public ProjectWorkspaceSnapshot Workspace { get; init; } = new();
}

public sealed class ProjectWorkspaceSnapshot
{
    public Guid ActiveFigureId { get; init; }

    public int MinimumEffectiveDpi { get; init; } = 300;

    public double AlignmentToleranceMm { get; init; } = 0.2;

    public double SpacingToleranceMm { get; init; } = 0.2;

    public bool VerifySourceHashes { get; init; } = true;

    public IReadOnlyList<ProjectFigureSnapshot> Figures { get; init; } = [];
}

public sealed class ProjectFigureSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = "Figure 1";

    public double WidthMm { get; init; }

    public double HeightMm { get; init; }

    public int Dpi { get; init; } = 300;

    public string TemplateId { get; init; } = string.Empty;

    public IReadOnlyList<Guid> LayerIds { get; init; } = [];
}

public sealed class ProjectCanvasSnapshot
{
    public int Width { get; init; }

    public int Height { get; init; }

    public string Background { get; init; } = "white";

    public string? BackgroundColor { get; init; }
}

public sealed class ProjectSourceSnapshot
{
    public Guid Id { get; init; }

    public string DisplayName { get; init; } = string.Empty;

    public string OriginalPath { get; init; } = string.Empty;

    public string? ProjectRelativePath { get; init; }

    public ProjectFingerprintSnapshot Fingerprint { get; init; } = new();

    public ProjectImageMetadataSnapshot Metadata { get; init; } = new();

    public string LinkState { get; init; } = "verified";

    public string AssetKind { get; init; } = "other";

    public IReadOnlyList<string> Tags { get; init; } = [];

    public long SourceRevision { get; init; } = 1;
}

public sealed class ProjectTabularDataAssetSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? SourcePath { get; init; }

    public ProjectFingerprintSnapshot? Fingerprint { get; init; }

    public long SourceRevision { get; init; } = 1;

    public IReadOnlyList<ProjectDataColumnSnapshot> Columns { get; init; } = [];

    public IReadOnlyList<ProjectTabularDataRowSnapshot> Rows { get; init; } = [];

    public ProjectTabularImportMetadataSnapshot ImportMetadata { get; init; } = new();
}

public sealed class ProjectDataColumnSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string DataType { get; init; } = "text";

    public string? Unit { get; init; }

    public string? Role { get; init; }
}

public sealed class ProjectTabularDataRowSnapshot
{
    public IReadOnlyList<ProjectTabularDataValueSnapshot> Values { get; init; } = [];
}

public sealed class ProjectTabularDataValueSnapshot
{
    public string? RawText { get; init; }

    public double? NumericValue { get; init; }

    public bool? BooleanValue { get; init; }

    public DateTimeOffset? DateTimeValue { get; init; }
}

public sealed class ProjectTabularImportMetadataSnapshot
{
    public string Format { get; init; } = "csv";

    public DateTimeOffset ImportedAt { get; init; }

    public string EncodingName { get; init; } = "UTF-8";

    public string? Delimiter { get; init; }

    public string? SheetName { get; init; }

    public string? SelectedRange { get; init; }

    public int HeaderRow { get; init; } = 1;

    public int DataRowCount { get; init; }

    public int InferenceRowCount { get; init; }

    public IReadOnlyList<string> OriginalHeaders { get; init; } = [];
}

public sealed class ProjectPlotSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string PlotType { get; init; } = "line";

    public ProjectPlotDataBindingSnapshot Data { get; init; } = new();

    public ProjectPlotAxisSnapshot XAxis { get; init; } = new();

    public ProjectPlotAxisSnapshot YAxis { get; init; } = new();

    public ProjectPlotTypographySnapshot Typography { get; init; } = new();

    public ProjectPlotSeriesStyleSnapshot Style { get; init; } = new();

    public ProjectPlotFilterSnapshot? Filter { get; init; }

    public IReadOnlyList<ProjectPlotTransformSnapshot> Transforms { get; init; } = [];

    public ProjectHeatmapGridSnapshot? HeatmapGrid { get; init; }

    public ProjectPlotColorScaleSnapshot? ColorScale { get; init; }

    public ProjectPlotColorbarSnapshot? Colorbar { get; init; }
}

public sealed class ProjectHeatmapGridSnapshot
{
    public string Kind { get; init; } = "auto";

    public string DuplicateCellPolicy { get; init; } = "error";
}

public sealed class ProjectPlotColorScaleSnapshot
{
    public string Colormap { get; init; } = "viridis";

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public string Scale { get; init; } = "linear";

    public string ClampMode { get; init; } = "clamp";

    public string? NoDataColor { get; init; }

    public bool ShowColorbar { get; init; } = true;
}

public sealed class ProjectPlotColorbarSnapshot
{
    public string Binding { get; init; } = "linked";

    public string Orientation { get; init; } = "vertical";

    public string Position { get; init; } = "right";

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public string? Unit { get; init; }

    public IReadOnlyList<double> Ticks { get; init; } = [];

    public IReadOnlyList<string> TickLabels { get; init; } = [];

    public ProjectTextStyleSnapshot? LabelStyle { get; init; }
}

public sealed class ProjectPlotDataBindingSnapshot
{
    public Guid DataAssetId { get; init; }

    public long SourceRevision { get; init; } = 1;

    public Guid? XColumnId { get; init; }

    public Guid YColumnId { get; init; }

    public Guid? ValueColumnId { get; init; }

    public ProjectPlotErrorBarBindingSnapshot? ErrorBars { get; init; }
}

public sealed class ProjectPlotErrorBarBindingSnapshot
{
    public string Mode { get; init; } = "symmetric";

    public Guid? SymmetricColumnId { get; init; }

    public Guid? LowerColumnId { get; init; }

    public Guid? UpperColumnId { get; init; }
}

public sealed class ProjectPlotAxisSnapshot
{
    public string Title { get; init; } = string.Empty;

    public string? Unit { get; init; }

    public string Scale { get; init; } = "linear";

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? MajorTickInterval { get; init; }

    public int MinorTickCount { get; init; } = 4;
}

public sealed class ProjectPlotTypographySnapshot
{
    public ProjectTextStyleSnapshot Axis { get; init; } = new();

    public ProjectTextStyleSnapshot Tick { get; init; } = new();

    public ProjectTextStyleSnapshot Legend { get; init; } = new();

    public ProjectTextStyleSnapshot Annotation { get; init; } = new();
}

public sealed class ProjectPlotSeriesStyleSnapshot
{
    public string LineColor { get; init; } = "#FF2563EB";

    public double LineWidthPt { get; init; } = 1.25;

    public string LineStyle { get; init; } = "solid";

    public string MarkerShape { get; init; } = "circle";

    public double MarkerSizePt { get; init; } = 5;

    public string MarkerFill { get; init; } = "#FFFFFFFF";

    public string MarkerStroke { get; init; } = "#FF2563EB";
}

public sealed class ProjectPlotFilterSnapshot
{
    public Guid ColumnId { get; init; }

    public string Operator { get; init; } = "equal";

    public string? Operand { get; init; }

    public string Expression { get; init; } = string.Empty;

    public int ExcludedRowCount { get; init; }
}

public sealed class ProjectPlotTransformSnapshot
{
    public Guid ColumnId { get; init; }

    public string Kind { get; init; } = "offset";

    public double? Parameter { get; init; }

    public int? WindowSize { get; init; }

    public string? Alignment { get; init; }
}

public sealed class ProjectMultiChannelAssetGroupSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid ReferenceAssetId { get; init; }

    public bool SameFieldOfViewConfirmed { get; init; }

    public IReadOnlyList<ProjectChannelGroupMemberSnapshot> Members { get; init; } = [];
}

public sealed class ProjectChannelGroupMemberSnapshot
{
    public Guid ChannelId { get; init; }

    public Guid AssetId { get; init; }

    public long? SourceRevision { get; init; }

    public int FrameIndex { get; init; }

    public ProjectChannelPlaneSelectorSnapshot? PlaneSelector { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? Role { get; init; }

    public string Color { get; init; } = "#FFFFFFFF";

    public string NameOrigin { get; init; } = "user";

    public bool IsNameConfirmed { get; init; }

    public bool Visible { get; init; } = true;

    public double Opacity { get; init; } = 1;

    public double DisplayMinimum { get; init; }

    public double DisplayMaximum { get; init; } = 255;

    public double Gamma { get; init; } = 1;

    public bool Invert { get; init; }

    public string Colormap { get; init; } = "viridis";
}

public sealed class ProjectChannelPlaneSelectorSnapshot
{
    public string SourceKind { get; init; } = "externalAsset";

    public int FrameIndex { get; init; }

    public int? ComponentIndex { get; init; }

    public int? ZIndex { get; init; }

    public int? CIndex { get; init; }

    public int? TIndex { get; init; }
}

public sealed class ProjectLinkGroupSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public Guid ReferenceAssetId { get; init; }

    public IReadOnlyList<Guid> AssetIds { get; init; } = [];

    public ProjectLinkSyncOptionsSnapshot SyncOptions { get; init; } = new();

    public IReadOnlyList<ProjectSpatialMappingSnapshot> Mappings { get; init; } = [];
}

public sealed class ProjectLinkSyncOptionsSnapshot
{
    public bool Pan { get; init; }

    public bool Zoom { get; init; }

    public bool Crop { get; init; } = true;

    public bool Roi { get; init; } = true;

    public bool ColorScale { get; init; } = true;
}

public sealed class ProjectSpatialMappingSnapshot
{
    public Guid Id { get; init; }

    public Guid SourceAssetId { get; init; }

    public Guid TargetAssetId { get; init; }

    public long SourceRevision { get; init; }

    public long TargetRevision { get; init; }

    public string Kind { get; init; } = "identity";

    public IReadOnlyList<double> Matrix { get; init; } = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    public string Origin { get; init; } = "userDeclaredIdentity";

    public DateTimeOffset CreatedAt { get; init; }

    public double? ResidualPixels { get; init; }

    public IReadOnlyList<ProjectRegistrationLandmarkSnapshot> Landmarks { get; init; } = [];

    public double? ResidualPhysical { get; init; }

    public string? ResidualPhysicalUnit { get; init; }
}

public sealed class ProjectRegistrationLandmarkSnapshot
{
    public Guid Id { get; init; }

    public double SourceX { get; init; }

    public double SourceY { get; init; }

    public double TargetX { get; init; }

    public double TargetY { get; init; }
}

public sealed class ProjectRoiSnapshot
{
    public Guid Id { get; init; }

    public Guid AssetId { get; init; }

    public long SourceRevision { get; init; }

    public string GeometryKind { get; init; } = "polygon";

    public int FrameIndex { get; init; }

    public IReadOnlyList<ProjectMeasurementPointSnapshot> SourceGeometry { get; init; } = [];

    public ProjectScientificValiditySnapshot Validity { get; init; } = new();

    public ProjectRoiStyleSnapshot Style { get; init; } = new();

    public ProjectRoiPropagationSnapshot? Propagation { get; init; }
}

public sealed class ProjectRoiStyleSnapshot
{
    public string StrokeColor { get; init; } = "#FF22C7E8";

    public double StrokeWidth { get; init; } = 2;

    public string FillColor { get; init; } = "#FF22C7E8";

    public double FillOpacity { get; init; } = 0.12;

    public string? Label { get; init; }

    public string? LabelFont { get; init; } = "Arial";

    public double LabelFontSizePt { get; init; } = 7;

    public bool LabelIsBold { get; init; }

    public string? LabelColor { get; init; } = "#FF22C7E8";
}

public sealed class ProjectRoiPropagationSnapshot
{
    public Guid ReferenceRoiId { get; init; }

    public Guid TargetRoiId { get; init; }

    public Guid LinkGroupId { get; init; }

    public Guid MappingId { get; init; }

    public double TargetCoverageFraction { get; init; } = 1;
}
public sealed class ProjectFingerprintSnapshot
{
    public long ByteLength { get; init; }

    public DateTimeOffset LastWriteTimeUtc { get; init; }

    public string Sha256 { get; init; } = string.Empty;

    public string? WindowsFileId { get; init; }
}

public sealed class ProjectImageMetadataSnapshot
{
    public long Width { get; init; }

    public long Height { get; init; }

    public int Channels { get; init; }

    public int BitsPerChannel { get; init; }

    public string PixelFormat { get; init; } = string.Empty;

    public int FrameCount { get; init; } = 1;

    public double? DpiX { get; init; }

    public double? DpiY { get; init; }

    public double? PhysicalSizeX { get; init; }

    public double? PhysicalSizeY { get; init; }

    public string? PhysicalUnit { get; init; }

    public string? IccProfileName { get; init; }
    public ProjectOmeMetadataSnapshot? Ome { get; init; }
}

public sealed class ProjectOmeMetadataSnapshot
{
    public string DimensionOrder { get; init; } = string.Empty;
    public string PixelType { get; init; } = string.Empty;
    public int SizeZ { get; init; } = 1;
    public int SizeC { get; init; } = 1;
    public int SizeT { get; init; } = 1;
    public double? PhysicalSizeX { get; init; }
    public double? PhysicalSizeY { get; init; }
    public double? PhysicalSizeZ { get; init; }
    public string? PhysicalSizeXUnit { get; init; }
    public string? PhysicalSizeYUnit { get; init; }
    public string? PhysicalSizeZUnit { get; init; }
    public double? TimeIncrement { get; init; }
    public string? TimeIncrementUnit { get; init; }
    public IReadOnlyList<string> ChannelNames { get; init; } = [];
    public string XmlSha256 { get; init; } = string.Empty;
}

public sealed class ProjectImageLayerSnapshot
{
    public string Type { get; init; } = "image";

    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string? PanelLabel { get; init; }

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public int ZIndex { get; init; }

    public double Opacity { get; init; } = 1;

    public Guid SourceAssetId { get; init; }

    /// <summary>
    /// Canonical half-open integer source-pixel crop. For manual crops this
    /// field takes precedence over NormalizedCrop during save/open and export.
    /// </summary>
    public ProjectPixelRectSnapshot SourceRect { get; init; } = new();

    public int FrameIndex { get; init; }

    /// <summary>Whether editing this panel keeps its source crop aspect ratio.</summary>
    public bool LockAspectRatio { get; init; } = true;

    public Guid? CropLinkGroupId { get; init; }

    public Guid? CompositeGroupId { get; init; }

    public ProjectTransformSnapshot Transform { get; init; } = new();

    public IReadOnlyList<ProjectImageAdjustmentSnapshot> Adjustments { get; init; } = [];

    /// <summary>
    /// Derived V2 representation used by Fit/Fill layout and legacy domain
    /// consumers. It is not the canonical truth for a manual crop.
    /// </summary>
    public ProjectNormalizedRectSnapshot? NormalizedCrop { get; init; }

    public ProjectFigureRectMmSnapshot? FrameMm { get; init; }

    public string FitMode { get; init; } = "manual";

    public double RotationDegrees { get; init; }

    public ProjectScientificValiditySnapshot ScientificValidity { get; init; } = new();

    public ProjectPanelStyleOverrideSnapshot? StyleOverride { get; init; }
}

public sealed class ProjectPanelStyleOverrideSnapshot
{
    public ProjectTextStyleSnapshot? PanelLabel { get; init; }

    public ProjectTextStyleSnapshot? Annotation { get; init; }

    public ProjectTextStyleSnapshot? ScaleBarText { get; init; }

    public ProjectScaleBarStyleSnapshot? ScaleBar { get; init; }

    public ProjectShapeStyleSnapshot? Shapes { get; init; }
}

public sealed class ProjectShapeStyleSnapshot
{
    public string StrokeColor { get; init; } = "#FFE53935";

    public string FillColor { get; init; } = "#FFE53935";

    public double FillOpacityPercent { get; init; }

    public double StrokeWidthPt { get; init; } = 1.25;
}

public sealed class ProjectTextStyleSnapshot
{
    public string FontFamily { get; init; } = "Arial";

    public double FontSizePt { get; init; } = 7;

    public bool IsBold { get; init; }

    public string Color { get; init; } = "#FF111111";
}

public sealed class ProjectScaleBarStyleSnapshot
{
    public string DefaultPosition { get; init; } = "bottomRight";

    public double BarThicknessPt { get; init; } = 1.25;

    public string Color { get; init; } = "#FFFFFFFF";
}

public sealed class ProjectNormalizedRectSnapshot
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
}

public sealed class ProjectFigureRectMmSnapshot
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

public sealed class ProjectScientificValiditySnapshot
{
    public string State { get; init; } = "valid";
    public IReadOnlyList<string> Reasons { get; init; } = [];
}

public sealed class ProjectImageAdjustmentSnapshot
{
    public double Brightness { get; init; }
    public double Contrast { get; init; }
    public double Gamma { get; init; } = 1;
    public double BlackPoint { get; init; }
    public double WhitePoint { get; init; } = 1;
    public bool Invert { get; init; }
    public bool Grayscale { get; init; }
    public string Channel { get; init; } = "rgb";
}

public sealed class ProjectPixelRectSnapshot
{
    public long X { get; init; }

    public long Y { get; init; }

    public long Width { get; init; }

    public long Height { get; init; }
}

public sealed class ProjectTransformSnapshot
{
    public double X { get; init; }

    public double Y { get; init; }

    public double ScaleX { get; init; } = 1;

    public double ScaleY { get; init; } = 1;

    public int RotationQuarterTurns { get; init; }
}

public sealed class ProjectCropPresetSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public long Width { get; init; }

    public long Height { get; init; }

    public string Unit { get; init; } = "px";
}

public sealed class ProjectGuideSnapshot
{
    public string Orientation { get; init; } = "horizontal";

    public double Position { get; init; }

    public bool Locked { get; init; }
}

public sealed class ProjectExportProfileSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Format { get; init; } = "tiff";

    public int Dpi { get; init; } = 300;

    public double Scale { get; init; } = 1;

    public int? WidthPixels { get; init; }

    public int? HeightPixels { get; init; }

    public bool WriteProvenance { get; init; } = true;

    public int? BitDepth { get; init; }

    public string? ColorMode { get; init; }

    public string? Resampling { get; init; }

    public string? JournalPresetId { get; init; }

    public bool WriteAuditReport { get; init; }

    public string PdfFontStrategy { get; init; } = "outlineText";
}

public sealed class ProjectFontSubstitutionSnapshot
{
    public string Requested { get; init; } = string.Empty;

    public string Substitute { get; init; } = string.Empty;
}

public sealed class ProjectJournalPresetSnapshot
{
    public string FormatVersion { get; init; } = "1.0";

    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public double FigureWidthMm { get; init; }

    public double? FigureHeightMm { get; init; }

    public int MinimumDpi { get; init; }

    public string PreferredFormat { get; init; } = "tiff";

    public IReadOnlyList<string> AllowedFormats { get; init; } = [];

    public string ColorMode { get; init; } = "RGB";

    public double? MaximumFileSizeMb { get; init; }

    public IReadOnlyList<string> FontRecommendations { get; init; } = [];

    public double? MinimumLineWidthPt { get; init; }

    public string? Notes { get; init; }

    public string? SourceName { get; init; }

    public string? SourceUrl { get; init; }

    public DateTimeOffset? SourceUpdatedAt { get; init; }

    public DateTimeOffset? PresetCreatedAt { get; init; }

    public string? Author { get; init; }

    public string? Organization { get; init; }
}

public sealed class ProjectCalibrationSnapshot
{
    public Guid SourceAssetId { get; init; }

    public double UnitsPerPixelX { get; init; }

    public double UnitsPerPixelY { get; init; }

    public string Unit { get; init; } = "µm";

    public string Origin { get; init; } = "none";

    public double? ReferencePixelLength { get; init; }

    public double? ReferencePhysicalLength { get; init; }

    public double ReferenceStartX { get; init; }

    public double ReferenceStartY { get; init; }

    public double ReferenceEndX { get; init; }

    public double ReferenceEndY { get; init; }
}

public sealed class ProjectMeasurementSnapshot
{
    public Guid Id { get; init; }

    public Guid SourceAssetId { get; init; }

    public long SourceRevision { get; init; } = 1;

    public string Kind { get; init; } = "length";

    public double X1 { get; init; }

    public double Y1 { get; init; }

    public double X2 { get; init; }

    public double Y2 { get; init; }

    public double? X3 { get; init; }

    public double? Y3 { get; init; }

    public string StrokeColor { get; init; } = "#FF22C7E8";

    public double StrokeWidthPixels { get; init; } = 3;

    public string LineStyle { get; init; } = "solid";

    public string FillColor { get; init; } = string.Empty;

    public string MarkerStrokeColor { get; init; } = string.Empty;

    public string MarkerFillColor { get; init; } = string.Empty;

    public double MarkerSizePixels { get; init; } = 18;

    public bool ShowMarkers { get; init; } = true;

    public bool ShowLabel { get; init; } = true;

    public string LabelColor { get; init; } = string.Empty;

    public string LabelFontFamily { get; init; } = string.Empty;

    public double LabelFontSizePt { get; init; }

    public bool LabelIsBold { get; init; }

    public double FillOpacityPercent { get; init; } = 8;

    public bool IsVisible { get; init; } = true;

    public bool IsLocked { get; init; }

    public IReadOnlyList<ProjectMeasurementPointSnapshot> Points { get; init; } = [];
}

public sealed class ProjectMeasurementOverlaySnapshot
{
    public Guid Id { get; init; }

    public Guid MeasurementId { get; init; }

    public Guid PanelId { get; init; }

    public ProjectMeasurementSnapshot SourceGeometry { get; init; } = new();

    public ProjectMeasurementOverlayCalibrationSnapshot? CalibrationRelationship { get; init; }

    public ProjectMeasurementOverlayStyleSnapshot Style { get; init; } = new();

    public string? LabelOverride { get; init; }

    public bool IsVisible { get; init; } = true;

    public int ZIndex { get; init; }
}

public sealed class ProjectMeasurementOverlayCalibrationSnapshot
{
    public Guid SourceAssetId { get; init; }

    public long SourceRevision { get; init; } = 1;

    public double UnitsPerPixelX { get; init; }

    public double UnitsPerPixelY { get; init; }

    public string Unit { get; init; } = "µm";
}

public sealed class ProjectMeasurementOverlayStyleSnapshot
{
    public string StrokeColor { get; init; } = "#FF22C7E8";

    public double StrokeWidthPixels { get; init; } = 3;

    public string LineStyle { get; init; } = "solid";

    public string FillColor { get; init; } = "#FF22C7E8";

    public double FillOpacityPercent { get; init; } = 8;

    public string MarkerStrokeColor { get; init; } = "#FF22C7E8";

    public string MarkerFillColor { get; init; } = "#FF11171F";

    public double MarkerSizePixels { get; init; } = 18;

    public bool ShowMarkers { get; init; } = true;

    public string LabelColor { get; init; } = "#FF22C7E8";

    public string LabelFontFamily { get; init; } = "Arial";

    public double LabelFontSizePt { get; init; } = 9;

    public bool LabelIsBold { get; init; }

    public bool ShowLabel { get; init; } = true;
}
public sealed class ProjectMeasurementPointSnapshot
{
    public double X { get; init; }

    public double Y { get; init; }
}

public sealed class ProjectScientificAnalysisSnapshot
{
    public Guid Id { get; init; }

    public Guid SourceAssetId { get; init; }

    public long SourceRevision { get; init; } = 1;

    public string Kind { get; init; } = "roiStatistics";

    public int FrameIndex { get; init; }

    public string Channel { get; init; } = "luminance";

    public string AnalyzerId { get; init; } = string.Empty;

    public DateTimeOffset AnalyzedAt { get; init; }

    public ProjectScientificValiditySnapshot Validity { get; init; } = new();

    public int SourceBitDepth { get; init; } = 8;

    public ProjectPixelRectSnapshot? Region { get; init; }

    public Guid? RoiId { get; init; }

    public Guid? ScientificChannelId { get; init; }

    public Guid? LinkGroupId { get; init; }

    public Guid? MappingId { get; init; }

    public IReadOnlyList<ProjectMeasurementPointSnapshot> PolygonMask { get; init; } = [];

    public bool ClippedToImage { get; init; }

    public double CoverageFraction { get; init; } = 1;

    public long? PixelCount { get; init; }

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? Mean { get; init; }

    public double? StandardDeviation { get; init; }

    public double? IntegratedIntensity { get; init; }

    public IReadOnlyList<ProjectIntensityHistogramBinSnapshot> Histogram { get; init; } = [];

    public string? DistanceUnit { get; init; }

    public IReadOnlyList<ProjectIntensityProfileSampleSnapshot> Samples { get; init; } = [];

    public string? AnalysisMode { get; init; }

    public bool? UseAutomaticThreshold { get; init; }

    public double? ThresholdNormalized { get; init; }

    public double? AppliedThresholdNormalized { get; init; }

    public int? MinimumAreaPixels { get; init; }

    public int? MaximumCandidates { get; init; }

    public long? AnalysisMaxPixels { get; init; }

    public int? AnalysisMaxComponentsSafety { get; init; }

    public int? AnalysisMaxBoundaryPoints { get; init; }

    public long? AnalysisMemoryBudgetBytes { get; init; }

    public long? ForegroundPixelCount { get; init; }

    public long? TotalPixelCount { get; init; }

    public IReadOnlyList<ProjectParticleSnapshot> Particles { get; init; } = [];
}

public sealed class ProjectIntensityHistogramBinSnapshot
{
    public double LowerBound { get; init; }

    public double UpperBound { get; init; }

    public long Count { get; init; }
}

public sealed class ProjectIntensityProfileSampleSnapshot
{
    public int Index { get; init; }

    public double PixelX { get; init; }

    public double PixelY { get; init; }

    public double DistancePixels { get; init; }

    public double? PhysicalDistance { get; init; }

    public double RawIntensity { get; init; }

    public double NormalizedIntensity { get; init; }
}

public sealed class ProjectParticleSnapshot
{
    public int Id { get; init; }

    public ProjectPixelRectSnapshot Bounds { get; init; } = new();

    public double CentroidX { get; init; }

    public double CentroidY { get; init; }

    public int AreaPixels { get; init; }

    public int PerimeterPixels { get; init; }

    public double MeanIntensity { get; init; }

    public double RawMeanIntensity { get; init; }

    public double AspectRatio { get; init; }

    public double FeretMaximumPixels { get; init; }

    public double FeretMinimumPixels { get; init; }
}

public sealed class ProjectTemplateSnapshot
{
    public string TemplateId { get; init; } = string.Empty;

    public string WorkspaceMode { get; init; } = "crop";

    public Guid? SelectedSourceId { get; init; }

    public ProjectPixelRectSnapshot? ActiveCrop { get; init; }

    public bool LockCropSizeAcrossSources { get; init; } = true;

    public bool CropOverlayVisible { get; init; } = true;

    public bool SnappingEnabled { get; init; } = true;

    public double SnapTolerancePixels { get; init; } = 12;

    public long ExactSpacingPixels { get; init; } = 24;

    public bool AutoPanelLabelsEnabled { get; init; } = true;

    public bool ShowPanelLabels { get; init; } = true;

    public string PanelLabelSequence { get; init; } = "lowercase";

    public IReadOnlyDictionary<Guid, string> LayerSlots { get; init; } =
        new Dictionary<Guid, string>();

    public IReadOnlyDictionary<Guid, ProjectScaleBarSnapshot> ScaleBars { get; init; } =
        new Dictionary<Guid, ProjectScaleBarSnapshot>();

    public IReadOnlyList<ProjectAnnotationSnapshot> Annotations { get; init; } = [];

    public IReadOnlyList<ProjectFigureScientificObjectSnapshot> ScientificObjects { get; init; } = [];

    public IReadOnlyList<ProjectRoiFigureProjectionSnapshot> RoiProjections { get; init; } = [];

    public IReadOnlyList<ProjectMeasurementOverlaySnapshot> MeasurementOverlays { get; init; } = [];

    public IReadOnlyList<ProjectFigurePlotPanelSnapshot> PlotPanels { get; init; } = [];

    public ProjectGlobalStyleSnapshot? GlobalStyle { get; init; }

    public IReadOnlyList<ProjectScientificColorSnapshot> ScientificColors { get; init; } = [];
}

public sealed class ProjectFigurePlotPanelSnapshot
{
    public Guid Id { get; init; }

    public Guid PlotId { get; init; }

    public ProjectPixelRectSnapshot DestinationRect { get; init; } = new();

    public string Label { get; init; } = string.Empty;

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public int ZIndex { get; init; }

    public ProjectPanelStyleOverrideSnapshot? StyleOverride { get; init; }

    public ProjectPlotTypographyOverrideSnapshot? TypographyOverride { get; init; }
}

public sealed class ProjectPlotTypographyOverrideSnapshot
{
    public ProjectTextStyleSnapshot? Axis { get; init; }

    public ProjectTextStyleSnapshot? Tick { get; init; }

    public ProjectTextStyleSnapshot? Legend { get; init; }

    public ProjectTextStyleSnapshot? Annotation { get; init; }
}

public sealed class ProjectGlobalStyleSnapshot
{
    public string FontFamily { get; init; } = "Arial";

    public double FontSizePt { get; init; } = 7;

    public double StrokeWidthPt { get; init; } = 1.25;

    public string TextColor { get; init; } = "#FF111111";

    public string ShapeColor { get; init; } = "#FFE53935";

    public string ScaleBarColor { get; init; } = "#FFFFFFFF";

    public string PanelLabelFontFamily { get; init; } = "Arial";

    public double PanelLabelFontSizePt { get; init; } = 7;

    public string PanelLabelTextColor { get; init; } = "#FF111111";

    public bool PanelLabelIsBold { get; init; } = true;

    public string ScaleBarLabelColor { get; init; } = "#FFFFFFFF";

    public string ScaleBarFontFamily { get; init; } = "Arial";

    public double ScaleBarFontSizePt { get; init; } = 7;

    public bool ScaleBarLabelIsBold { get; init; } = true;

    public double ScaleBarThicknessPt { get; init; } = 1.25;
}

public sealed class ProjectScientificColorSnapshot
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public string Color { get; init; } = "#FF000000";
}

public sealed class ProjectScaleBarSnapshot
{
    public bool Enabled { get; init; }

    public double PhysicalUnitsPerSourcePixel { get; init; }

    /// <summary>Unit of PhysicalUnitsPerSourcePixel. Older files fall back to Unit.</summary>
    public string? CalibrationUnit { get; init; }

    public double PhysicalLength { get; init; }

    /// <summary>Display unit for the primary bar.</summary>
    public string Unit { get; init; } = "µm";

    public string Anchor { get; init; } = "bottomRight";

    public bool ShowLabel { get; init; } = true;

    public IReadOnlyList<ProjectAdditionalScaleBarSnapshot> AdditionalBars { get; init; } = [];
}

public sealed class ProjectAdditionalScaleBarSnapshot
{
    public Guid Id { get; init; }

    public double PhysicalLength { get; init; }

    public string Unit { get; init; } = "µm";

    public string Anchor { get; init; } = "bottomRight";

    public bool ShowLabel { get; init; } = true;

    public bool IsVisible { get; init; } = true;
}

public sealed class ProjectAnnotationSnapshot
{
    public Guid Id { get; init; }

    public string Kind { get; init; } = "text";

    public double X { get; init; }

    public double Y { get; init; }

    public double EndX { get; init; }

    public double EndY { get; init; }

    public string Text { get; init; } = string.Empty;

    public string Color { get; init; } = "#FF111111";

    public string StrokeColor { get; init; } = string.Empty;

    public string FillColor { get; init; } = string.Empty;

    public double FillOpacityPercent { get; init; }

    public string TextColor { get; init; } = string.Empty;

    public string FontFamily { get; init; } = string.Empty;

    public double FontSizePt { get; init; } = 7;

    public double StrokeWidthPt { get; init; } = 1;

    public bool IsBold { get; init; }

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public int ZIndex { get; init; }
}

public sealed class ProjectFigureScientificObjectSnapshot
{
    public Guid Id { get; init; }

    public string Kind { get; init; } = "polygonAnnotation";

    /// <summary>Final-canvas points encoded as invariant x,y; x,y pairs.</summary>
    public string Points { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string StrokeColor { get; init; } = "#FFFFB300";

    public string FillColor { get; init; } = "#FFFFB300";

    public double FillOpacityPercent { get; init; } = 12;

    public string TextColor { get; init; } = "#FFFFFFFF";

    public string FontFamily { get; init; } = "Arial";

    public double FontSizePt { get; init; } = 7;

    public double StrokeWidthPt { get; init; } = 1.25;

    public bool IsBold { get; init; } = true;

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public int ZIndex { get; init; }

    public double Minimum { get; init; }

    public double Maximum { get; init; } = 1;

    public string Unit { get; init; } = "a.u.";

    public string Colormap { get; init; } = "viridis";

    /// <summary>Invariant label|#AARRGGBB; label|#AARRGGBB pairs.</summary>
    public string ChannelEntries { get; init; } = string.Empty;

    public Guid? ChannelId { get; init; }

    public string ColorbarBindingState { get; init; } = "detached";

    public string Orientation { get; init; } = "vertical";

    public IReadOnlyList<ProjectColorbarTickSnapshot> Ticks { get; init; } = [];

    public double ChannelLegendPadding { get; init; } = 5;
}

public sealed class ProjectColorbarTickSnapshot
{
    public double Value { get; init; }

    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// Persisted Figure reference to a canonical ROI. Geometry is deliberately absent and is
/// reconstructed from ProjectRoiSnapshot.SourceGeometry plus the referenced panel.
/// </summary>
public sealed class ProjectRoiFigureProjectionSnapshot
{
    public Guid Id { get; init; }

    public Guid RoiId { get; init; }

    public Guid PanelId { get; init; }

    public Guid AssetId { get; init; }

    public long SourceRevision { get; init; }

    public ProjectPanelStyleOverrideSnapshot? StyleOverride { get; init; }

    public bool Visible { get; init; } = true;

    public int ZIndex { get; init; }
}
public sealed class ProjectAuditEntrySnapshot
{
    public DateTimeOffset Timestamp { get; init; }

    public string Command { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, object?> Parameters { get; init; } =
        new Dictionary<string, object?>();
}
