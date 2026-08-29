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
        FigureScientificObjectExportItem[] scientificObjects = source.ScientificObjects.Select(item => item with
        {
            FontFamily = Effective(item.FontFamily),
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
            source.PdfFontStrategy);
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

    private static TextStyle? ResolveText(TextStyle? style, Func<string, string> effective) =>
        style is null ? null : style with { FontFamily = effective(style.FontFamily) };
}
