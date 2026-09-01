using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

public enum FontUsageKind
{
    FigureDefault,
    PanelLabel,
    ScaleBarText,
    Annotation,
    MeasurementOverlayLabel,
    ScientificObjectLabel,
    ChannelLegend,
    RoiLabel,
    PlotAxis,
    PlotTick,
    PlotLegend,
    PlotAnnotation,
}

public sealed record FontUsage(
    string RequestedFont,
    FontUsageKind UsageKind,
    Guid? FigureId = null,
    Guid? PanelId = null,
    Guid? ObjectId = null,
    string? PanelLabel = null,
    bool IsBold = false)
{
    public FontUsage EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(RequestedFont) || RequestedFont.Trim().Length > 128 ||
            PanelLabel?.Trim().Length > 128 ||
            FigureId == Guid.Empty || PanelId == Guid.Empty || ObjectId == Guid.Empty ||
            !Enum.IsDefined(UsageKind))
        {
            throw new InvalidOperationException("Font usage 必须包含有效字体、usage kind 和可选定位 ID。");
        }

        return this with
        {
            RequestedFont = RequestedFont.Trim(),
            PanelLabel = string.IsNullOrWhiteSpace(PanelLabel) ? null : PanelLabel.Trim(),
        };
    }
}

/// <summary>
/// Canonical traversal for every requested font in an export or scientific project graph.
/// Consumers decide availability, substitution and policy; the collector never mutates styles.
/// </summary>
public static class FontUsageCollector
{
    public static IReadOnlyList<FontUsage> Collect(
        FigureExportDocument document,
        Guid? figureId = null,
        bool includeHidden = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        var usages = new List<FontUsage>();

        Add(usages, document.GlobalStyle.FontFamily, FontUsageKind.FigureDefault, figureId);
        Add(usages, document.GlobalStyle.EffectivePanelLabelFontFamily, FontUsageKind.PanelLabel, figureId, isBold: document.GlobalStyle.PanelLabelIsBold);
        Add(usages, document.GlobalStyle.EffectiveScaleBarFontFamily, FontUsageKind.ScaleBarText, figureId, isBold: document.GlobalStyle.ScaleBarLabelIsBold);

        foreach (FigurePanelExportItem panel in document.Panels.Where(item => includeHidden || item.IsVisible))
        {
            Guid? panelId = NormalizeId(panel.PanelId);
            FigureGlobalStyle style = document.GlobalStyle.ResolvePanelOverride(panel.StyleOverride);
            Add(usages, style.EffectivePanelLabelFontFamily, FontUsageKind.PanelLabel, figureId, panelId, panelLabel: panel.Label, isBold: style.PanelLabelIsBold);
            Add(usages, style.EffectiveScaleBarFontFamily, FontUsageKind.ScaleBarText, figureId, panelId, panelLabel: panel.Label, isBold: style.ScaleBarLabelIsBold);
        }

        foreach (FigurePlotPanelExportItem panel in document.PlotPanels.Where(item => includeHidden || item.IsVisible))
        {
            Guid? panelId = NormalizeId(panel.PanelId);
            FigureGlobalStyle panelStyle = document.GlobalStyle.ResolvePanelOverride(panel.StyleOverride);
            Add(usages, panelStyle.EffectivePanelLabelFontFamily, FontUsageKind.PanelLabel,
                figureId, panelId, panelLabel: panel.Label, isBold: panelStyle.PanelLabelIsBold);
            ResolvedFigurePlotTypography typography = panel.ResolveTypography(document.GlobalStyle);
            Add(usages, typography.Axis.Value.FontFamily, FontUsageKind.PlotAxis, figureId, panelId, NormalizeId(panel.Plot.Id), panel.Label, typography.Axis.Value.IsBold);
            Add(usages, typography.Tick.Value.FontFamily, FontUsageKind.PlotTick, figureId, panelId, NormalizeId(panel.Plot.Id), panel.Label, typography.Tick.Value.IsBold);
            Add(usages, typography.Legend.Value.FontFamily, FontUsageKind.PlotLegend, figureId, panelId, NormalizeId(panel.Plot.Id), panel.Label, typography.Legend.Value.IsBold);
            Add(usages, typography.Annotation.Value.FontFamily, FontUsageKind.PlotAnnotation, figureId, panelId, NormalizeId(panel.Plot.Id), panel.Label, typography.Annotation.Value.IsBold);
        }

        foreach (FigureAnnotationExportItem annotation in document.Annotations.Where(item => includeHidden || item.IsVisible))
        {
            Add(
                usages,
                annotation.FontFamily,
                FontUsageKind.Annotation,
                figureId,
                objectId: NormalizeId(annotation.Id),
                isBold: annotation.IsBold);
        }

        foreach (FigureMeasurementOverlayExportItem overlay in
                 document.MeasurementOverlays.Where(item => includeHidden || item.IsVisible))
        {
            Add(
                usages,
                overlay.Style.LabelFontFamily,
                FontUsageKind.MeasurementOverlayLabel,
                figureId,
                NormalizeId(overlay.PanelId),
                NormalizeId(overlay.Id),
                ResolvePanelLabel(document, overlay.PanelId),
                overlay.Style.LabelIsBold);
        }

        foreach (FigureScientificObjectExportItem scientificObject in
                 document.ScientificObjects.Where(item => includeHidden || item.IsVisible))
        {
            if (scientificObject.Kind == FigureScientificObjectKind.ChannelLegend)
            {
                Add(
                    usages,
                    scientificObject.EffectiveChannelLegend!.FontFamily,
                    FontUsageKind.ChannelLegend,
                    figureId,
                    objectId: NormalizeId(scientificObject.Id),
                    isBold: scientificObject.EffectiveChannelLegend!.IsBold);
            }
            else
            {
                Add(
                    usages,
                    scientificObject.FontFamily,
                    FontUsageKind.ScientificObjectLabel,
                    figureId,
                    objectId: NormalizeId(scientificObject.Id),
                    isBold: scientificObject.IsBold);
            }
        }

        foreach (FigureRoiProjectionExportItem projection in
                 document.RoiProjections.Where(item => includeHidden || item.IsVisible))
        {
            string font = projection.Projection.StyleOverride?.Annotation?.FontFamily ??
                          projection.CanonicalRoi.Style.LabelStyle.FontFamily;
            Add(
                usages,
                font,
                FontUsageKind.RoiLabel,
                figureId,
                NormalizeId(projection.PanelId),
                NormalizeId(projection.Id),
                ResolvePanelLabel(document, projection.PanelId),
                projection.Projection.StyleOverride?.Annotation?.IsBold ??
                projection.CanonicalRoi.Style.LabelStyle.IsBold);
        }

        return Normalize(usages);
    }

    public static IReadOnlyList<FontUsage> Collect(ScientificProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        var usages = new List<FontUsage>();
        Add(usages, project.Style.Annotation.FontFamily, FontUsageKind.FigureDefault, isBold: project.Style.Annotation.IsBold);
        Add(usages, project.Style.PanelLabel.FontFamily, FontUsageKind.PanelLabel, isBold: project.Style.PanelLabel.IsBold);
        Add(usages, project.Style.ScaleBarText.FontFamily, FontUsageKind.ScaleBarText, isBold: project.Style.ScaleBarText.IsBold);
        Add(usages, project.Style.EffectiveMeasurement.Label.FontFamily, FontUsageKind.MeasurementOverlayLabel, isBold: project.Style.EffectiveMeasurement.Label.IsBold);

        foreach (ScientificFigure figure in project.Figures.Values)
        {
            ResolvedProjectStyle figureStyle = ProjectStyleResolver.Resolve(
                project.Style,
                figure.StyleOverride);
            Add(usages, figureStyle.PanelLabel.Value.FontFamily, FontUsageKind.PanelLabel, figure.Id, isBold: figureStyle.PanelLabel.Value.IsBold);
            Add(usages, figureStyle.ScaleBarText.Value.FontFamily, FontUsageKind.ScaleBarText, figure.Id, isBold: figureStyle.ScaleBarText.Value.IsBold);
            foreach (FigurePanel panel in figure.Panels)
            {
                ResolvedProjectStyle panelStyle = ProjectStyleResolver.Resolve(
                    project.Style,
                    figure.StyleOverride,
                    panel.StyleOverride);
                Add(
                    usages,
                    panelStyle.PanelLabel.Value.FontFamily,
                    FontUsageKind.PanelLabel,
                    figure.Id,
                    panel.Id,
                    panelLabel: panel.Label,
                    isBold: panelStyle.PanelLabel.Value.IsBold);
                Add(
                    usages,
                    panelStyle.ScaleBarText.Value.FontFamily,
                    FontUsageKind.ScaleBarText,
                    figure.Id,
                    panel.Id,
                    panelLabel: panel.Label,
                    isBold: panelStyle.ScaleBarText.Value.IsBold);
            }
        }

        foreach (ScientificObject scientificObject in project.ScientificObjects.Values)
        {
            Guid? panelId = NormalizeId(scientificObject.PanelId);
            Guid? objectId = NormalizeId(scientificObject.Id);
            Guid? figureId = ResolveFigureId(project, scientificObject);
            AddStyleOverride(
                usages,
                scientificObject.StyleOverride,
                figureId,
                panelId,
                objectId);
            switch (scientificObject)
            {
                case MeasurementOverlayObject overlay:
                    Add(
                        usages,
                        overlay.Style.LabelFontFamily,
                        FontUsageKind.MeasurementOverlayLabel,
                        figureId,
                        panelId,
                        objectId,
                        isBold: overlay.Style.LabelIsBold);
                    break;
                case RoiObject roi:
                    Add(usages, roi.Style.LabelStyle.FontFamily, FontUsageKind.RoiLabel, figureId, panelId, objectId, isBold: roi.Style.LabelStyle.IsBold);
                    break;
                case ChannelLegendObject legend:
                    Add(usages, legend.TextStyle.FontFamily, FontUsageKind.ChannelLegend, figureId, panelId, objectId, isBold: legend.TextStyle.IsBold);
                    break;
            }
        }

        return Normalize(usages);
    }

    private static void AddStyleOverride(
        ICollection<FontUsage> usages,
        StyleOverride? style,
        Guid? figureId,
        Guid? panelId,
        Guid? objectId)
    {
        if (style?.PanelLabel is { } panelLabel)
        {
            Add(usages, panelLabel.FontFamily, FontUsageKind.PanelLabel, figureId, panelId, objectId, isBold: panelLabel.IsBold);
        }
        if (style?.Annotation is { } annotation)
        {
            Add(usages, annotation.FontFamily, FontUsageKind.Annotation, figureId, panelId, objectId, isBold: annotation.IsBold);
        }
        if (style?.ScaleBarText is { } scaleBar)
        {
            Add(usages, scaleBar.FontFamily, FontUsageKind.ScaleBarText, figureId, panelId, objectId, isBold: scaleBar.IsBold);
        }
        if (style?.Measurement is { } measurement)
        {
            Add(
                usages,
                measurement.Label.FontFamily,
                FontUsageKind.MeasurementOverlayLabel,
                figureId,
                panelId,
                objectId,
                isBold: measurement.Label.IsBold);
        }
    }

    private static Guid? ResolveFigureId(ScientificProject project, ScientificObject scientificObject)
    {
        if (scientificObject.PanelId is Guid panelId)
        {
            return project.Figures.Values.FirstOrDefault(
                figure => figure.Panels.Any(panel => panel.Id == panelId))?.Id;
        }

        return project.Figures.Values.FirstOrDefault(
            figure => figure.ScientificObjectIds.Contains(scientificObject.Id))?.Id;
    }

    private static IReadOnlyList<FontUsage> Normalize(IEnumerable<FontUsage> usages) =>
        usages
            .Select(usage => usage.EnsureValid())
            .DistinctBy(usage => (
                usage.RequestedFont.ToUpperInvariant(),
                usage.UsageKind,
                usage.FigureId,
                usage.PanelId,
                usage.ObjectId,
                usage.PanelLabel?.ToUpperInvariant(),
                usage.IsBold))
            .OrderBy(usage => usage.RequestedFont, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.UsageKind)
            .ThenBy(usage => usage.FigureId)
            .ThenBy(usage => usage.PanelId)
            .ThenBy(usage => usage.ObjectId)
            .ThenBy(usage => usage.PanelLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.IsBold)
            .ToArray();

    private static void Add(
        ICollection<FontUsage> usages,
        string? font,
        FontUsageKind kind,
        Guid? figureId = null,
        Guid? panelId = null,
        Guid? objectId = null,
        string? panelLabel = null,
        bool isBold = false)
    {
        if (!string.IsNullOrWhiteSpace(font))
        {
            usages.Add(new FontUsage(
                font.Trim(),
                kind,
                NormalizeId(figureId),
                NormalizeId(panelId),
                NormalizeId(objectId),
                panelLabel,
                isBold));
        }
    }

    private static string? ResolvePanelLabel(FigureExportDocument document, Guid panelId) =>
        document.Panels.FirstOrDefault(panel => panel.PanelId == panelId)?.Label ??
        document.PlotPanels.FirstOrDefault(panel => panel.PanelId == panelId)?.Label;

    private static Guid? NormalizeId(Guid? id) => id is { } value && value != Guid.Empty ? value : null;
}
