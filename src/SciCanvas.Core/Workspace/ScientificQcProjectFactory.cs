using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Workspace;

/// <summary>
/// Builds an immutable Core project snapshot from the exact figure export inputs.
/// The snapshot binds QC to source revisions without mutating source assets.
/// </summary>
public static class ScientificQcProjectFactory
{
    public static ScientificProject Create(
        Guid projectId,
        string projectName,
        FigureExportDocument document,
        IReadOnlyCollection<SourceAsset> sources,
        IReadOnlyDictionary<Guid, long>? sourceRevisions = null,
        IReadOnlyDictionary<Guid, SpatialCalibration>? calibrations = null,
        IReadOnlyCollection<ScientificObject>? scientificObjects = null,
        Guid? figureId = null,
        PanelLabelScheme labelScheme = PanelLabelScheme.Custom,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? updatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(sources);
        Guid effectiveProjectId = projectId == Guid.Empty ? Guid.NewGuid() : projectId;
        Guid effectiveFigureId = figureId is { } requestedFigureId && requestedFigureId != Guid.Empty
            ? requestedFigureId
            : effectiveProjectId;
        DateTimeOffset now = updatedAt ?? DateTimeOffset.UtcNow;
        IReadOnlyDictionary<Guid, long> revisions = sourceRevisions ?? new Dictionary<Guid, long>();
        IReadOnlyDictionary<Guid, SpatialCalibration> effectiveCalibrations =
            calibrations ?? new Dictionary<Guid, SpatialCalibration>();

        Dictionary<Guid, ScientificAsset> assets = sources.ToDictionary(
            source => source.Id,
            source => new ScientificAsset(
                source.Id,
                source.DisplayName,
                new AssetSourceReference(
                    source.OriginalPath,
                    Path.GetFileName(source.OriginalPath),
                    source.Fingerprint,
                    Math.Max(1, revisions.GetValueOrDefault(source.Id, 1))),
                source.Metadata,
                InferAssetKind(source.DisplayName),
                effectiveCalibrations.GetValueOrDefault(source.Id),
                new Dictionary<string, object?>(),
                [],
                null,
                source.LinkState,
                createdAt ?? now,
                now));

        ScientificObject[] canonicalObjects = CreateExportScientificObjects(document)
            .Concat(scientificObjects ?? [])
            .GroupBy(item => item.Id)
            .Select(group => group.Last())
            .ToArray();
        Dictionary<Guid, ScientificObject> objectMap = canonicalObjects.ToDictionary(item => item.Id);
        double mmPerPixel = 25.4 / document.Dpi;
        FigurePanel[] panels = document.Panels.Select((panel, index) =>
        {
            FigureRectMm frame = new(
                panel.DestinationRect.X * mmPerPixel,
                panel.DestinationRect.Y * mmPerPixel,
                panel.DestinationRect.Width * mmPerPixel,
                panel.DestinationRect.Height * mmPerPixel);
            NormalizedRect crop = NormalizedRect.FromSourcePixels(
                panel.SourceRect,
                panel.Source.Metadata.PixelSize.Width,
                panel.Source.Metadata.PixelSize.Height);
            return new FigurePanel(
                panel.PanelId,
                effectiveFigureId,
                panel.Source.Id,
                frame,
                crop,
                PanelFitMode.Manual,
                0,
                new PanelAdjustments(
                    panel.Adjustments?.Brightness ?? 0,
                    panel.Adjustments?.Contrast ?? 0,
                    panel.Adjustments?.Gamma ?? 1),
                panel.StyleOverride,
                canonicalObjects
                    .Where(item => item.PanelId == panel.PanelId)
                    .Select(item => item.Id)
                    .ToArray(),
                panel.Label,
                index)
            {
                FrameIndex = panel.FrameIndex,
                ManualCropPixels = panel.SourceRect,
            };
        }).ToArray();
        var figure = new ScientificFigure(
            effectiveFigureId,
            "Figure 1",
            document.WidthPixels * mmPerPixel,
            document.HeightPixels * mmPerPixel,
            panels,
            canonicalObjects.Select(item => item.Id).ToArray(),
            null,
            now)
        {
            LabelScheme = labelScheme,
        };
        var project = new ScientificProject(
            ScientificProject.CurrentSchemaVersion,
            effectiveProjectId,
            string.IsNullOrWhiteSpace(projectName) ? "SciCanvas Project" : projectName.Trim(),
            assets,
            new Dictionary<Guid, ScientificFigure> { [figure.Id] = figure },
            CreateProjectStyle(document.GlobalStyle),
            objectMap,
            createdAt ?? now,
            now);
        project.EnsureValid();
        return project;
    }

    private static IEnumerable<ScientificObject> CreateExportScientificObjects(FigureExportDocument document)
    {
        foreach (FigureScientificObjectExportItem item in document.ScientificObjects
                     .Where(item => item.Kind == FigureScientificObjectKind.Colorbar))
        {
            FigureColorbarExportSpec colorbar = item.EffectiveColorbar!;
            yield return new ColorbarObject
            {
                Id = item.Id,
                Minimum = colorbar.Minimum,
                Maximum = colorbar.Maximum,
                Unit = colorbar.Unit,
                Colormap = colorbar.Colormap,
                ChannelId = colorbar.ChannelId,
                BindingState = colorbar.BindingState,
                Orientation = colorbar.Orientation,
                Ticks = colorbar.Ticks,
            }.EnsureValid();
        }
        foreach (FigureScientificObjectExportItem item in document.ScientificObjects
                     .Where(item => item.Kind == FigureScientificObjectKind.ChannelLegend))
        {
            FigureChannelLegendExportSpec legend = item.EffectiveChannelLegend!;
            yield return new ChannelLegendObject
            {
                Id = item.Id,
                Items = legend.Items.Select(entry => new ChannelLegendItem(
                    entry.ChannelId,
                    entry.Label,
                    entry.Color)).ToArray(),
                TextStyle = new TextStyle(
                    legend.FontFamily,
                    legend.FontSizePt,
                    legend.IsBold,
                    legend.TextColor),
                ContainerStyle = new ShapeStyle(
                    legend.BorderColor,
                    legend.BackgroundColor,
                    legend.BackgroundOpacityPercent,
                    legend.BorderWidthPt),
                PaddingPixels = legend.PaddingPixels,
            }.EnsureValid();
        }
    }

    private static ProjectStyle CreateProjectStyle(FigureGlobalStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        ProjectStyle defaults = ProjectStyle.Default;
        return defaults with
        {
            PanelLabel = new TextStyle(
                style.EffectivePanelLabelFontFamily,
                style.EffectivePanelLabelFontSizePt,
                style.PanelLabelIsBold,
                style.EffectivePanelLabelTextColor),
            Annotation = new TextStyle(
                style.FontFamily,
                style.FontSizePt,
                false,
                style.TextColor),
            ScaleBarText = new TextStyle(
                style.EffectiveScaleBarFontFamily,
                style.EffectiveScaleBarFontSizePt,
                style.ScaleBarLabelIsBold,
                style.EffectiveScaleBarLabelColor),
            Lines = new LineStyle(
                style.StrokeWidthPt,
                defaults.Lines.BorderWidthPt,
                style.ShapeColor),
            ScaleBar = new ScaleBarStyle(
                defaults.ScaleBar.DefaultPosition,
                style.EffectiveScaleBarThicknessPt,
                style.ScaleBarColor),
            Shapes = new ShapeStyle(
                style.ShapeColor,
                style.ShapeColor,
                0,
                style.StrokeWidthPt),
        };
    }

    private static AssetKind InferAssetKind(string name)
    {
        string value = name?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Contains("stem", StringComparison.Ordinal)) return AssetKind.Stem;
        if (value.Contains("sem", StringComparison.Ordinal)) return AssetKind.Sem;
        if (value.Contains("tem", StringComparison.Ordinal)) return AssetKind.Tem;
        if (value.Contains("ebsd", StringComparison.Ordinal)) return AssetKind.Ebsd;
        if (value.Contains("eds", StringComparison.Ordinal) || value.Contains("edx", StringComparison.Ordinal)) return AssetKind.Eds;
        if (value.Contains("afm", StringComparison.Ordinal)) return AssetKind.Afm;
        if (value.Contains("xrd", StringComparison.Ordinal)) return AssetKind.Xrd;
        if (value.Contains("graph", StringComparison.Ordinal) || value.Contains("plot", StringComparison.Ordinal)) return AssetKind.Graph;
        if (value.Contains("schematic", StringComparison.Ordinal)) return AssetKind.Schematic;
        return AssetKind.Other;
    }
}
