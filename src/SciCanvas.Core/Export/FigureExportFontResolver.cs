using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

public sealed record ResolvedFigureExportDocument(
    FigureExportDocument Document,
    IReadOnlyList<ResolvedFont> FontResolutions);

/// <summary>
/// Produces a render-only immutable snapshot with effective fonts. Requested font
/// values in the editor/project document are never changed.
/// </summary>
public static class FigureExportFontResolver
{
    public static ResolvedFigureExportDocument Resolve(
        FigureExportDocument source,
        IEnumerable<FontSubstitutionRule>? substitutions,
        IFontCatalog fontCatalog)
    {
        ArgumentNullException.ThrowIfNull(source);
        var service = new FontResolutionService(fontCatalog);
        FontSubstitutionRule[] rules = (substitutions ?? []).Select(rule => rule.EnsureValid()).ToArray();
        var resolutions = new Dictionary<string, ResolvedFont>(StringComparer.OrdinalIgnoreCase);

        string Effective(string requested)
        {
            ResolvedFont resolved = service.Resolve(requested, rules);
            resolutions.TryAdd(requested.Trim(), resolved);
            return resolved.EffectiveFamily;
        }

        foreach (string requested in FontUsageCollector.Collect(source)
                     .Select(usage => usage.RequestedFont)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _ = Effective(requested);
        }

        FigureGlobalStyle global = source.GlobalStyle with
        {
            FontFamily = Effective(source.GlobalStyle.FontFamily),
            PanelLabelFontFamily = Effective(source.GlobalStyle.EffectivePanelLabelFontFamily),
            ScaleBarFontFamily = Effective(source.GlobalStyle.EffectiveScaleBarFontFamily),
        };
        FigurePanelExportItem[] panels = source.Panels.Select(panel => panel with
        {
            StyleOverride = ResolveStyleOverride(panel.StyleOverride, Effective),
        }).ToArray();
        FigurePlotPanelExportItem[] plotPanels = source.PlotPanels.Select(panel => panel with
        {
            Plot = ResolvePlotColorbarFont(panel.Plot, Effective),
            StyleOverride = ResolveStyleOverride(panel.StyleOverride, Effective),
            TypographyOverride = ResolvePlotTypographyOverride(panel.TypographyOverride, Effective),
        }).ToArray();
        FigureAnnotationExportItem[] annotations = source.Annotations.Select(annotation => annotation with
        {
            FontFamily = Effective(annotation.FontFamily),
        }).ToArray();
        FigureMeasurementOverlayExportItem[] overlays = source.MeasurementOverlays.Select(overlay =>
            overlay with
            {
                ScientificObject = overlay.ScientificObject with
                {
                    Style = overlay.Style with { LabelFontFamily = Effective(overlay.Style.LabelFontFamily) },
                },
            }).ToArray();
        FigureScientificObjectExportItem[] scientificObjects = source.ScientificObjects.Select(item =>
        {
            string requested = item.Kind == FigureScientificObjectKind.ChannelLegend
                ? item.EffectiveChannelLegend!.FontFamily
                : item.FontFamily;
            string effectiveFont = Effective(requested);
            return item with
            {
                FontFamily = effectiveFont,
                ChannelLegend = item.ChannelLegend is null
                    ? null
                    : item.ChannelLegend with { FontFamily = effectiveFont },
            };
        }).ToArray();
        FigureRoiProjectionExportItem[] roiProjections = source.RoiProjections.Select(item => item with
        {
            Projection = item.Projection with
            {
                StyleOverride = ResolveStyleOverride(item.Projection.StyleOverride, Effective),
            },
            CanonicalRoi = item.CanonicalRoi with
            {
                Style = new RoiStyle(
                    item.CanonicalRoi.Style.Shape,
                    item.CanonicalRoi.Style.LabelStyle with
                    {
                        FontFamily = Effective(item.CanonicalRoi.Style.LabelStyle.FontFamily),
                    },
                    item.CanonicalRoi.Style.Label),
            },
        }).ToArray();
        var document = new FigureExportDocument(
            source.WidthPixels,
            source.HeightPixels,
            source.Dpi,
            panels,
            annotations,
            source.BackgroundColor,
            source.BitDepth,
            global,
            overlays,
            scientificObjects,
            roiProjections,
            source.PdfFontStrategy,
            plotPanels);
        return new ResolvedFigureExportDocument(
            document,
            resolutions.Values.OrderBy(item => item.RequestedFamily, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static StyleOverride? ResolveStyleOverride(
        StyleOverride? style,
        Func<string, string> effective)
    {
        if (style is null)
        {
            return null;
        }

        return style with
        {
            PanelLabel = ResolveText(style.PanelLabel, effective),
            Annotation = ResolveText(style.Annotation, effective),
            ScaleBarText = ResolveText(style.ScaleBarText, effective),
            Measurement = style.Measurement is null
                ? null
                : style.Measurement with
                {
                    Label = ResolveText(style.Measurement.Label, effective)!,
                },
        };
    }

    private static PlotObject ResolvePlotColorbarFont(
        PlotObject plot,
        Func<string, string> effective)
    {
        if (plot.PlotType != PlotKind.Heatmap || plot.Colorbar?.LabelStyle is not { } style)
        {
            return plot;
        }

        return plot with
        {
            Colorbar = plot.Colorbar with
            {
                LabelStyle = style with { FontFamily = effective(style.FontFamily) },
            },
        };
    }

    private static TextStyle? ResolveText(TextStyle? style, Func<string, string> effective) =>
        style is null ? null : style with { FontFamily = effective(style.FontFamily) };

    private static FigurePlotTypographyOverride? ResolvePlotTypographyOverride(
        FigurePlotTypographyOverride? style,
        Func<string, string> effective) =>
        style is null
            ? null
            : style with
            {
                Axis = ResolveText(style.Axis, effective),
                Tick = ResolveText(style.Tick, effective),
                Legend = ResolveText(style.Legend, effective),
                Annotation = ResolveText(style.Annotation, effective),
            };
}
