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

    public IReadOnlyList<ProjectImageLayerSnapshot> Layers { get; init; } = [];

    public IReadOnlyList<ProjectCropPresetSnapshot> CropPresets { get; init; } = [];

    public IReadOnlyList<ProjectGuideSnapshot> Guides { get; init; } = [];

    public IReadOnlyList<ProjectExportProfileSnapshot> ExportProfiles { get; init; } = [];

    public IReadOnlyList<ProjectCalibrationSnapshot> Calibrations { get; init; } = [];

    public IReadOnlyList<ProjectMeasurementSnapshot> Measurements { get; init; } = [];

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

    public ProjectPixelRectSnapshot SourceRect { get; init; } = new();

    public int FrameIndex { get; init; }

    /// <summary>Whether editing this panel keeps its source crop aspect ratio.</summary>
    public bool LockAspectRatio { get; init; } = true;

    public Guid? CropLinkGroupId { get; init; }

    public ProjectTransformSnapshot Transform { get; init; } = new();

    public IReadOnlyList<ProjectImageAdjustmentSnapshot> Adjustments { get; init; } = [];

    public ProjectNormalizedRectSnapshot? NormalizedCrop { get; init; }

    public ProjectFigureRectMmSnapshot? FrameMm { get; init; }

    public string FitMode { get; init; } = "manual";

    public double RotationDegrees { get; init; }

    public ProjectScientificValiditySnapshot ScientificValidity { get; init; } = new();
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

    public double MarkerSizePixels { get; init; } = 18;

    public bool ShowMarkers { get; init; } = true;

    public bool ShowLabel { get; init; } = true;

    public double FillOpacityPercent { get; init; } = 8;

    public bool IsVisible { get; init; } = true;

    public bool IsLocked { get; init; }

    public IReadOnlyList<ProjectMeasurementPointSnapshot> Points { get; init; } = [];
}

public sealed class ProjectMeasurementPointSnapshot
{
    public double X { get; init; }

    public double Y { get; init; }
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

    public ProjectGlobalStyleSnapshot? GlobalStyle { get; init; }

    public IReadOnlyList<ProjectScientificColorSnapshot> ScientificColors { get; init; } = [];
}

public sealed class ProjectGlobalStyleSnapshot
{
    public string FontFamily { get; init; } = "Arial";

    public double FontSizePt { get; init; } = 7;

    public double StrokeWidthPt { get; init; } = 1.25;

    public string TextColor { get; init; } = "#FF111111";

    public string ShapeColor { get; init; } = "#FFE53935";

    public string ScaleBarColor { get; init; } = "#FFFFFFFF";
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

    public double PhysicalLength { get; init; }

    public string Unit { get; init; } = "µm";

    public bool ShowLabel { get; init; } = true;
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

    public double FontSizePt { get; init; } = 7;

    public double StrokeWidthPt { get; init; } = 1;

    public bool IsBold { get; init; }

    public bool Visible { get; init; } = true;

    public bool Locked { get; init; }

    public int ZIndex { get; init; }
}

public sealed class ProjectAuditEntrySnapshot
{
    public DateTimeOffset Timestamp { get; init; }

    public string Command { get; init; } = string.Empty;

    public IReadOnlyDictionary<string, object?> Parameters { get; init; } =
        new Dictionary<string, object?>();
}
