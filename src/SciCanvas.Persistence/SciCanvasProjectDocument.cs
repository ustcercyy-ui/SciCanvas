using System.Text.Json.Serialization;

namespace SciCanvas.Persistence;

public sealed class SciCanvasProjectDocument
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "https://scicanvas.org/schemas/scicanvas-project.schema.json";

    public string SchemaVersion { get; init; } = "0.1";

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

    public ProjectTemplateSnapshot? TemplateSnapshot { get; init; }

    public IReadOnlyList<ProjectAuditEntrySnapshot> AuditTrail { get; init; } = [];
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

    public double? DpiX { get; init; }

    public double? DpiY { get; init; }

    public double? PhysicalSizeX { get; init; }

    public double? PhysicalSizeY { get; init; }

    public string? PhysicalUnit { get; init; }

    public string? IccProfileName { get; init; }
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

    public ProjectTransformSnapshot Transform { get; init; } = new();

    public IReadOnlyList<object> Adjustments { get; init; } = [];
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

    public int? BitDepth { get; init; }

    public string? ColorMode { get; init; }

    public string? Resampling { get; init; }

    public string? JournalPresetId { get; init; }

    public bool WriteAuditReport { get; init; }
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
