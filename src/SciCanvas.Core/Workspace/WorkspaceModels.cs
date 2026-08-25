using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Workspace;

public sealed record AssetSourceReference(
    string Path,
    string FileName,
    SourceFingerprint Fingerprint,
    long SourceRevision)
{
    public AssetSourceReference NextRevision(
        string path,
        string fileName,
        SourceFingerprint fingerprint) => new(
            path,
            fileName,
            fingerprint,
            checked(SourceRevision + 1));
}

public sealed record AssetPreviewDescriptor(
    string CacheKey,
    int WidthPixels,
    int HeightPixels);

public sealed record ScientificAsset(
    Guid Id,
    string Name,
    AssetSourceReference Source,
    ImageMetadata Image,
    AssetKind Kind,
    SpatialCalibration? Calibration,
    IReadOnlyDictionary<string, object?> Metadata,
    IReadOnlyList<string> Tags,
    AssetPreviewDescriptor? Preview,
    SourceLinkState LinkState,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool HasValidCalibration =>
        Calibration?.IsValid == true && Calibration.SourceAssetId == Id;

    public ScientificValidity CalibrationValidity => Calibration switch
    {
        null => ScientificValidity.Warning("Calibration missing."),
        { IsValid: false } calibration => ScientificValidity.Invalid(calibration.ValidationMessage),
        { SourceAssetId: var sourceId } when sourceId != Id =>
            ScientificValidity.Invalid("Calibration belongs to another source asset."),
        { IsAnisotropic: true } =>
            ScientificValidity.Warning("X/Y calibration differs; verify anisotropic pixels."),
        _ => ScientificValidity.Valid,
    };
}

public sealed record PanelAdjustments(
    double Brightness = 0,
    double Contrast = 0,
    double Gamma = 1)
{
    public ImageAdjustmentParameters ToImageAdjustmentParameters() => new()
    {
        Brightness = Brightness,
        Contrast = Contrast,
        Gamma = Gamma,
    };
}

public sealed record FigurePanel(
    Guid Id,
    Guid FigureId,
    Guid AssetId,
    FigureRectMm Frame,
    NormalizedRect Crop,
    PanelFitMode FitMode,
    double RotationDegrees,
    PanelAdjustments Adjustments,
    StyleOverride? StyleOverride,
    IReadOnlyList<Guid> ScientificObjectIds,
    string Label,
    int ZIndex)
{
    public FigurePanel ReplaceAsset(Guid assetId) => this with { AssetId = assetId };

    public FigurePanel ResizeFrame(double widthMm, double heightMm) => this with
    {
        Frame = Frame.WithSize(widthMm, heightMm),
    };
}

public sealed record ScientificFigure(
    Guid Id,
    string Name,
    double WidthMm,
    double HeightMm,
    IReadOnlyList<FigurePanel> Panels,
    IReadOnlyList<Guid> ScientificObjectIds,
    StyleOverride? StyleOverride,
    DateTimeOffset UpdatedAt)
{
    public void EnsureValid()
    {
        if (Id == Guid.Empty || string.IsNullOrWhiteSpace(Name) ||
            !double.IsFinite(WidthMm) || WidthMm <= 0 ||
            !double.IsFinite(HeightMm) || HeightMm <= 0 ||
            Panels.Any(panel => panel.FigureId != Id) ||
            Panels.Select(panel => panel.Id).Distinct().Count() != Panels.Count)
        {
            throw new InvalidOperationException("Figure 结构无效。" );
        }
    }
}

public sealed record FigureTemplate(
    string Id,
    string Name,
    double WidthMm,
    double HeightMm,
    IReadOnlyList<FigureRectMm> PanelFrames,
    StyleOverride? DefaultStyle)
{
    public ScientificFigure Instantiate(string figureName)
    {
        Guid figureId = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new ScientificFigure(
            figureId,
            figureName,
            WidthMm,
            HeightMm,
            PanelFrames.Select((frame, index) => new FigurePanel(
                Guid.NewGuid(),
                figureId,
                Guid.Empty,
                frame,
                NormalizedRect.Full,
                PanelFitMode.Fit,
                0,
                new PanelAdjustments(),
                null,
                [],
                CreatePanelLabel(index),
                index)).ToArray(),
            [],
            DefaultStyle,
            now);
    }

    private static string CreatePanelLabel(int index) => $"({(char)('a' + index)})";
}

public sealed record ScientificProject(
    int SchemaVersion,
    Guid Id,
    string Name,
    IReadOnlyDictionary<Guid, ScientificAsset> Assets,
    IReadOnlyDictionary<Guid, ScientificFigure> Figures,
    ProjectStyle Style,
    IReadOnlyDictionary<Guid, ScientificObject> ScientificObjects,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public const int CurrentSchemaVersion = 2;

    public void EnsureValid()
    {
        if (SchemaVersion <= 0 || Id == Guid.Empty || string.IsNullOrWhiteSpace(Name))
        {
            throw new InvalidOperationException("Project 结构无效。" );
        }

        Style.EnsureValid();
        foreach (ScientificFigure figure in Figures.Values)
        {
            figure.EnsureValid();
            foreach (FigurePanel panel in figure.Panels.Where(panel => panel.AssetId != Guid.Empty))
            {
                if (!Assets.ContainsKey(panel.AssetId))
                {
                    throw new InvalidOperationException($"Panel {panel.Id} 引用了不存在的 Asset。" );
                }
            }
        }
    }
}
