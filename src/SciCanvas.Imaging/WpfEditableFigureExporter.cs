using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;

namespace SciCanvas.Imaging;

internal static class WpfEditableFigureExporter
{
    public static void Export(
        FigureExportDocument document,
        string targetPath,
        string extension,
        CancellationToken cancellationToken)
    {
        if (extension == ".svg")
        {
            ExportSvg(document, targetPath, cancellationToken);
            return;
        }

        ExportPdf(document, targetPath, cancellationToken);
    }

    private static void ExportSvg(
        FigureExportDocument document,
        string targetPath,
        CancellationToken cancellationToken)
    {
        var svg = new StringBuilder(64 * 1024);
        svg.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{document.WidthPixels}\" height=\"{document.HeightPixels}\" viewBox=\"0 0 {document.WidthPixels} {document.HeightPixels}\" data-dpi=\"{document.Dpi}\">\n");
        AppendSvgRect(svg, 0, 0, document.WidthPixels, document.HeightPixels, WpfFigureExporter.ParseColor(document.BackgroundColor), null);

        int panelIndex = 0;
        foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidatePanel(panel, document);
            BitmapSource cropped = WpfFigureExporter.LoadPanelImage(panel, cancellationToken);
            Rect imageRect = WpfFigureExporter.CalculateContainedRect(
                cropped.PixelWidth,
                cropped.PixelHeight,
                panel.DestinationRect);
            FigureGlobalStyle panelStyle = document.GlobalStyle.ResolvePanelOverride(panel.StyleOverride);
            string dataUri = $"data:image/png;base64,{Convert.ToBase64String(EncodePng(cropped))}";
            svg.Append($"  <g id=\"panel-{panelIndex++}\" data-source=\"{Escape(panel.Source.DisplayName)}\" data-label=\"{Escape(panel.Label)}\">\n");
            svg.Append($"    <image x=\"{F(imageRect.X)}\" y=\"{F(imageRect.Y)}\" width=\"{F(imageRect.Width)}\" height=\"{F(imageRect.Height)}\" preserveAspectRatio=\"none\" href=\"{dataUri}\"/>\n");
            if (panel.IsInset)
            {
                Color insetColor = WpfFigureExporter.ParseColor(document.GlobalStyle.ShapeColor);
                svg.Append($"    <rect x=\"{F(imageRect.X)}\" y=\"{F(imageRect.Y)}\" width=\"{F(imageRect.Width)}\" height=\"{F(imageRect.Height)}\" fill=\"none\" stroke=\"{ColorHex(insetColor)}\" stroke-width=\"{F(Math.Max(1, 0.5 / 72.0 * document.Dpi))}\"/>\n");
            }
            AppendSvgScaleBar(svg, panel, imageRect, document.Dpi, panelStyle);
            AppendSvgPanelLabel(svg, panel.Label, panel.DestinationRect, document.Dpi, panelStyle);
            svg.Append("  </g>\n");
        }

        foreach (FigureAnnotationExportItem annotation in
                 document.Annotations.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidateAnnotation(annotation, document);
            AppendSvgAnnotation(svg, annotation, document.Dpi, document.GlobalStyle);
        }

        foreach (FigureMeasurementOverlayExportItem overlay in
                 document.MeasurementOverlays.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AppendSvgMeasurementOverlay(
                svg,
                overlay,
                WpfFigureExporter.ResolveMeasurementOverlayPanel(overlay, document),
                document.Dpi);
        }

        foreach (FigureScientificObjectExportItem scientificObject in
                 document.ScientificObjects.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidateScientificObject(scientificObject, document);
            AppendSvgScientificObject(svg, scientificObject, document.Dpi);
        }
        svg.Append("</svg>\n");
        WriteNewFile(targetPath, Encoding.UTF8.GetBytes(svg.ToString()));
    }

    private static void AppendSvgPanelLabel(
        StringBuilder svg,
        string label,
        PixelRect64 destination,
        int dpi,
        FigureGlobalStyle style)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        double fontSize = Math.Max(12, style.EffectivePanelLabelFontSizePt / 72.0 * dpi);
        double padding = Math.Max(4, dpi / 75.0);
        double width = label.Length * fontSize * 0.68 + padding * 2;
        double height = fontSize * 1.35 + padding;
        AppendSvgRect(svg, destination.X + padding, destination.Y + padding, width, height, Colors.White, null);
        Color textColor = WpfFigureExporter.ParseColor(style.EffectivePanelLabelTextColor);
        svg.Append($"    <text x=\"{F(destination.X + padding * 2)}\" y=\"{F(destination.Y + padding)}\" font-family=\"{Escape(style.EffectivePanelLabelFontFamily)}\" font-size=\"{F(fontSize)}\" font-weight=\"{(style.PanelLabelIsBold ? "700" : "400")}\" dominant-baseline=\"hanging\" fill=\"{ColorHex(textColor)}\">{Escape(label)}</text>\n");
    }

    private static void AppendSvgScaleBar(
        StringBuilder svg,
        FigurePanelExportItem panel,
        Rect imageRect,
        int dpi,
        FigureGlobalStyle style)
    {
        IReadOnlyList<FigureScaleBarExportSpec> scaleBars = panel.EffectiveScaleBars;
        if (scaleBars.Count == 0)
        {
            return;
        }

        double thickness = Math.Max(2, style.EffectiveScaleBarThicknessPt / 72.0 * dpi);
        double fontSize = Math.Max(12, style.EffectiveScaleBarFontSizePt / 72.0 * dpi);
        IReadOnlyList<FigureScaleBarGeometry> geometries = FigureScaleBarLayout.Calculate(
            scaleBars,
            panel.SourceRect,
            new FigureImageRect(imageRect.X, imageRect.Y, imageRect.Width, imageRect.Height),
            dpi,
            thickness,
            fontSize);
        Color scaleBarColor = WpfFigureExporter.ParseColor(style.ScaleBarColor);
        Color labelColor = WpfFigureExporter.ParseColor(style.EffectiveScaleBarLabelColor);
        foreach (FigureScaleBarGeometry geometry in geometries)
        {
            svg.Append($"    <line x1=\"{F(geometry.Left)}\" y1=\"{F(geometry.Y)}\" x2=\"{F(geometry.Right)}\" y2=\"{F(geometry.Y)}\" stroke=\"#000000\" stroke-width=\"{F(thickness + Math.Max(3, dpi / 100.0))}\" stroke-linecap=\"square\"/>\n");
            svg.Append($"    <line x1=\"{F(geometry.Left)}\" y1=\"{F(geometry.Y)}\" x2=\"{F(geometry.Right)}\" y2=\"{F(geometry.Y)}\" stroke=\"{ColorHex(scaleBarColor)}\" stroke-width=\"{F(thickness)}\" stroke-linecap=\"square\"/>\n");
            if (geometry.Spec.ShowLabel)
            {
                double textX = geometry.Right - (geometry.Spec.Label.Length * fontSize * 0.58);
                svg.Append($"    <text x=\"{F(textX)}\" y=\"{F(geometry.LabelTop)}\" font-family=\"{Escape(style.EffectiveScaleBarFontFamily)}\" font-size=\"{F(fontSize)}\" font-weight=\"{(style.ScaleBarLabelIsBold ? "700" : "400")}\" dominant-baseline=\"hanging\" fill=\"{ColorHex(labelColor)}\" stroke=\"#000000\" stroke-width=\"{F(Math.Max(2, dpi / 150.0))}\" paint-order=\"stroke\">{Escape(geometry.Spec.Label)}</text>\n");
            }
        }
    }
    private static void AppendSvgMeasurementOverlay(
        StringBuilder svg,
        FigureMeasurementOverlayExportItem overlay,
        FigurePanelExportItem panel,
        int dpi)
    {
        FigureMeasurementOverlayGeometry geometry = FigureMeasurementOverlayMapper.Map(overlay.ScientificObject, panel);
        FigureMeasurementOverlayStyle style = overlay.Style;
        Color stroke = WpfFigureExporter.ParseColor(style.StrokeColor);
        Color fill = WpfFigureExporter.ParseColor(style.FillColor);
        string strokeWidth = F(Math.Max(0.25, geometry.StrokeWidthPixels));
        string dash = CreateSvgDashArray(style.LineStyle);
        string dashAttribute = string.IsNullOrEmpty(dash) ? string.Empty : $" stroke-dasharray=\"{dash}\"";
        string fillOpacity = F(Math.Clamp(fill.A / 255.0 * style.FillOpacityPercent / 100.0, 0, 1));
        svg.Append($"  <g data-scientific-object=\"measurement-overlay\" data-overlay-id=\"{overlay.Id}\" data-measurement-id=\"{overlay.MeasurementId}\" data-source-id=\"{overlay.SourceAssetId}\" data-source-revision=\"{overlay.SourceRevision}\">\n");
        AppendSvgMeasurementShape(svg, overlay.MeasurementKind, geometry, stroke, fill, strokeWidth, dashAttribute, fillOpacity);

        if (style.ShowMarkers)
        {
            Color markerStroke = WpfFigureExporter.ParseColor(style.MarkerStrokeColor);
            Color markerFill = WpfFigureExporter.ParseColor(style.MarkerFillColor);
            double radius = Math.Max(1, geometry.MarkerSizePixels / 2);
            foreach (MeasurementPoint point in GetMarkerPoints(overlay.MeasurementKind, geometry))
            {
                svg.Append($"    <circle cx=\"{F(point.X)}\" cy=\"{F(point.Y)}\" r=\"{F(radius)}\" fill=\"{ColorHex(markerFill)}\" stroke=\"{ColorHex(markerStroke)}\" stroke-width=\"{F(Math.Max(0.25, geometry.StrokeWidthPixels * 0.75))}\"/>\n");
            }
        }

        if (style.ShowLabel)
        {
            Color label = WpfFigureExporter.ParseColor(style.LabelColor);
            svg.Append($"    <text x=\"{F(geometry.LabelAnchor.X)}\" y=\"{F(geometry.LabelAnchor.Y)}\" font-family=\"{Escape(style.LabelFontFamily)}\" font-size=\"{F(style.LabelFontSizePt / 72.0 * dpi)}\" font-weight=\"{(style.LabelIsBold ? "700" : "400")}\" dominant-baseline=\"hanging\" fill=\"{ColorHex(label)}\">{Escape(FigureMeasurementOverlayMapper.CreateLabel(overlay.ScientificObject))}</text>\n");
        }

        svg.Append("  </g>\n");
    }

    private static void AppendSvgMeasurementShape(
        StringBuilder svg,
        ScientificMeasurementKind kind,
        FigureMeasurementOverlayGeometry geometry,
        Color stroke,
        Color fill,
        string strokeWidth,
        string dashAttribute,
        string fillOpacity)
    {
        string common = $"stroke=\"{ColorHex(stroke)}\" stroke-width=\"{strokeWidth}\"{dashAttribute}";
        switch (kind)
        {
            case ScientificMeasurementKind.Length:
                svg.Append($"    <line x1=\"{F(geometry.PointA.X)}\" y1=\"{F(geometry.PointA.Y)}\" x2=\"{F(geometry.PointB.X)}\" y2=\"{F(geometry.PointB.Y)}\" {common}/>\n");
                break;
            case ScientificMeasurementKind.Angle:
                svg.Append($"    <polyline points=\"{Points([geometry.PointA, geometry.PointB, geometry.PointC ?? geometry.PointB])}\" fill=\"none\" {common}/>\n");
                break;
            case ScientificMeasurementKind.RectangleRoi:
                svg.Append($"    <rect x=\"{F(Math.Min(geometry.PointA.X, geometry.PointB.X))}\" y=\"{F(Math.Min(geometry.PointA.Y, geometry.PointB.Y))}\" width=\"{F(Math.Abs(geometry.PointB.X - geometry.PointA.X))}\" height=\"{F(Math.Abs(geometry.PointB.Y - geometry.PointA.Y))}\" fill=\"{ColorHex(fill)}\" fill-opacity=\"{fillOpacity}\" {common}/>\n");
                break;
            case ScientificMeasurementKind.CircleRoi:
                double cx = (geometry.PointA.X + geometry.PointB.X) / 2;
                double cy = (geometry.PointA.Y + geometry.PointB.Y) / 2;
                svg.Append($"    <ellipse cx=\"{F(cx)}\" cy=\"{F(cy)}\" rx=\"{F(Math.Abs(geometry.PointB.X - geometry.PointA.X) / 2)}\" ry=\"{F(Math.Abs(geometry.PointB.Y - geometry.PointA.Y) / 2)}\" fill=\"{ColorHex(fill)}\" fill-opacity=\"{fillOpacity}\" {common}/>\n");
                break;
            case ScientificMeasurementKind.Polyline:
                svg.Append($"    <polyline points=\"{Points(geometry.PathPoints)}\" fill=\"none\" {common}/>\n");
                break;
            default:
                throw new InvalidOperationException("不支持的 Measurement Overlay 类型。");
        }
    }

    private static IReadOnlyList<MeasurementPoint> GetMarkerPoints(
        ScientificMeasurementKind kind,
        FigureMeasurementOverlayGeometry geometry) => kind switch
    {
        ScientificMeasurementKind.Length => [geometry.PointA, geometry.PointB],
        ScientificMeasurementKind.Angle => [geometry.PointA, geometry.PointB, geometry.PointC ?? geometry.PointB],
        ScientificMeasurementKind.Polyline => geometry.PathPoints,
        _ => [geometry.PointA, geometry.PointB],
    };

    private static string Points(IEnumerable<MeasurementPoint> points) => string.Join(
        " ",
        points.Select(point => $"{F(point.X)},{F(point.Y)}"));

    private static string CreateSvgDashArray(string lineStyle) => lineStyle switch
    {
        "dash" => "5 3",
        "dot" => "1 2",
        "dash-dot" => "5 2 1 2",
        _ => string.Empty,
    };
    private static void AppendSvgAnnotation(
        StringBuilder svg,
        FigureAnnotationExportItem annotation,
        int dpi,
        FigureGlobalStyle style)
    {
        if (string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            Color textColor = WpfFigureExporter.ParseColor(annotation.TextColor);
            string textOpacity = textColor.A < 255
                ? $" fill-opacity=\"{F(textColor.A / 255.0)}\""
                : string.Empty;
            string fontFamily = string.IsNullOrWhiteSpace(annotation.FontFamily)
                ? style.FontFamily
                : annotation.FontFamily;
            double fontSize = annotation.FontSizePt / 72.0 * dpi;
            svg.Append($"  <text x=\"{F(annotation.X)}\" y=\"{F(annotation.Y)}\" font-family=\"{Escape(fontFamily)}\" font-size=\"{F(fontSize)}\" font-weight=\"{(annotation.IsBold ? "700" : "400")}\" dominant-baseline=\"hanging\" fill=\"{ColorHex(textColor)}\"{textOpacity}>{Escape(annotation.Text)}</text>\n");
            return;
        }

        Color strokeColor = WpfFigureExporter.ParseColor(annotation.StrokeColor);
        string stroke = ColorHex(strokeColor);
        string strokeOpacity = strokeColor.A < 255
            ? $" stroke-opacity=\"{F(strokeColor.A / 255.0)}\""
            : string.Empty;
        double strokeWidth = annotation.StrokeWidthPt / 72.0 * dpi;
        if (string.Equals(annotation.Kind, "line", StringComparison.OrdinalIgnoreCase))
        {
            svg.Append($"  <line x1=\"{F(annotation.X)}\" y1=\"{F(annotation.Y)}\" x2=\"{F(annotation.EndX)}\" y2=\"{F(annotation.EndY)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{F(strokeWidth)}\" stroke-linecap=\"round\"{strokeOpacity}/>\n");
            return;
        }

        if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase))
        {
            AppendSvgShapeStyle(
                svg,
                annotation,
                stroke,
                strokeWidth,
                strokeOpacity,
                "rect",
                $"x=\"{F(annotation.X)}\" y=\"{F(annotation.Y)}\" width=\"{F(annotation.EndX - annotation.X)}\" height=\"{F(annotation.EndY - annotation.Y)}\"");
            return;
        }

        if (string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase))
        {
            AppendSvgShapeStyle(
                svg,
                annotation,
                stroke,
                strokeWidth,
                strokeOpacity,
                "ellipse",
                $"cx=\"{F((annotation.X + annotation.EndX) / 2)}\" cy=\"{F((annotation.Y + annotation.EndY) / 2)}\" rx=\"{F((annotation.EndX - annotation.X) / 2)}\" ry=\"{F((annotation.EndY - annotation.Y) / 2)}\"");
            return;
        }

        double dx = annotation.EndX - annotation.X;
        double dy = annotation.EndY - annotation.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Max(strokeWidth * 4, 10.0 / 72.0 * dpi);
        double halfWidth = headLength * 0.52;
        double baseX = annotation.EndX - unitX * headLength;
        double baseY = annotation.EndY - unitY * headLength;
        double leftX = baseX - unitY * halfWidth;
        double leftY = baseY + unitX * halfWidth;
        double rightX = baseX + unitY * halfWidth;
        double rightY = baseY - unitX * halfWidth;
        svg.Append($"  <g fill=\"{stroke}\" stroke=\"{stroke}\" stroke-width=\"{F(strokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{strokeOpacity}>\n");
        svg.Append($"    <line x1=\"{F(annotation.X)}\" y1=\"{F(annotation.Y)}\" x2=\"{F(baseX)}\" y2=\"{F(baseY)}\"/>\n");
        svg.Append($"    <polygon points=\"{F(annotation.EndX)},{F(annotation.EndY)} {F(leftX)},{F(leftY)} {F(rightX)},{F(rightY)}\"/>\n  </g>\n");
    }

    private static void AppendSvgShapeStyle(
        StringBuilder svg,
        FigureAnnotationExportItem annotation,
        string stroke,
        double strokeWidth,
        string strokeOpacity,
        string element,
        string geometry)
    {
        Color fillColor = WpfFigureExporter.ParseColor(annotation.FillColor);
        double fillOpacity = fillColor.A / 255.0 * annotation.FillOpacityPercent / 100.0;
        svg.Append($"  <{element} {geometry} fill=\"{ColorHex(fillColor)}\" fill-opacity=\"{F(fillOpacity)}\" stroke=\"{stroke}\" stroke-width=\"{F(strokeWidth)}\"{strokeOpacity}/>\n");
    }

    private static void AppendSvgScientificObject(StringBuilder svg, FigureScientificObjectExportItem item, int dpi)
    {
        Color stroke = WpfFigureExporter.ParseColor(item.StrokeColor);
        Color fill = WpfFigureExporter.ParseColor(item.FillColor);
        Color text = WpfFigureExporter.ParseColor(item.TextColor);
        double line = item.StrokeWidthPt / 72.0 * dpi;
        double font = item.FontSizePt / 72.0 * dpi;
        string common = $"data-scientific-object=\"{item.Kind}\" data-object-id=\"{item.Id}\" stroke=\"{ColorHex(stroke)}\" stroke-width=\"{F(line)}\"";
        if (item.Kind is FigureScientificObjectKind.PolygonAnnotation or FigureScientificObjectKind.Roi)
        {
            double opacity = fill.A / 255.0 * item.FillOpacityPercent / 100.0;
            string points = string.Join(" ", item.Points.Select(point => $"{F(point.X)},{F(point.Y)}"));
            svg.Append($"  <g {common}><polygon points=\"{points}\" fill=\"{ColorHex(fill)}\" fill-opacity=\"{F(opacity)}\"/>\n");
            AppendSvgScientificText(svg, item.Label, item.Points[0].X, item.Points[0].Y - font - 4, text, item, font);
            svg.Append("  </g>\n");
            return;
        }
        if (item.Kind == FigureScientificObjectKind.DirectionMarker)
        {
            FigureScientificPoint start = item.Points[0];
            FigureScientificPoint end = item.Points[1];
            (FigureScientificPoint left, FigureScientificPoint right, FigureScientificPoint baseCenter) = DirectionHead(start, end, Math.Max(line * 4, 10.0 / 72.0 * dpi));
            svg.Append($"  <g {common} fill=\"{ColorHex(stroke)}\"><line x1=\"{F(start.X)}\" y1=\"{F(start.Y)}\" x2=\"{F(baseCenter.X)}\" y2=\"{F(baseCenter.Y)}\"/><polygon points=\"{F(end.X)},{F(end.Y)} {F(left.X)},{F(left.Y)} {F(right.X)},{F(right.Y)}\"/>\n");
            AppendSvgScientificText(svg, item.Label, end.X + 5, end.Y - font - 5, text, item, font);
            svg.Append("  </g>\n");
            return;
        }
        (double x, double y, double width, double height) = ScientificBounds(item.Points);
        if (item.Kind == FigureScientificObjectKind.Colorbar)
        {
            string gradientId = $"scientific-colorbar-{item.Id:N}";
            IReadOnlyList<Color> colors = WpfFigureExporter.GetColormapColors(item.Colormap);
            svg.Append($"  <defs><linearGradient id=\"{gradientId}\" x1=\"0\" y1=\"1\" x2=\"0\" y2=\"0\">");
            for (int index = 0; index < colors.Count; index++) svg.Append($"<stop offset=\"{F(index / (double)Math.Max(1, colors.Count - 1))}\" stop-color=\"{ColorHex(colors[index])}\"/>");
            svg.Append($"</linearGradient></defs>\n  <g {common}><rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" fill=\"url(#{gradientId})\"/>\n");
            AppendSvgScientificText(svg, $"{item.Maximum:0.###} {item.Unit}", x + width + 5, y, text, item, font);
            AppendSvgScientificText(svg, $"{item.Minimum:0.###} {item.Unit}", x + width + 5, y + height - font, text, item, font);
            AppendSvgScientificText(svg, item.Label, x, Math.Max(0, y - font - 4), text, item, font);
            svg.Append("  </g>\n");
            return;
        }
        svg.Append($"  <g {common}><rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" fill=\"#0C1219\" fill-opacity=\"0.75\"/>\n");
        double row = height / Math.Max(1, item.EffectiveChannelLegendEntries.Count);
        for (int index = 0; index < item.EffectiveChannelLegendEntries.Count; index++)
        {
            FigureChannelLegendEntry entry = item.EffectiveChannelLegendEntries[index];
            double rowY = y + index * row;
            svg.Append($"    <rect x=\"{F(x + 5)}\" y=\"{F(rowY + Math.Max(2, (row - font) / 2))}\" width=\"{F(Math.Min(16, width * 0.18))}\" height=\"{F(Math.Max(4, font))}\" fill=\"{ColorHex(WpfFigureExporter.ParseColor(entry.Color))}\" stroke=\"none\"/>\n");
            AppendSvgScientificText(svg, entry.Label, x + Math.Min(24, width * 0.25), rowY + Math.Max(0, (row - font) / 2), text, item, font);
        }
        svg.Append("  </g>\n");
    }

    private static void AppendSvgScientificText(StringBuilder svg, string value, double x, double y, Color color, FigureScientificObjectExportItem item, double font)
    {
        if (!string.IsNullOrWhiteSpace(value)) svg.Append($"    <text x=\"{F(x)}\" y=\"{F(y)}\" font-family=\"{Escape(item.FontFamily)}\" font-size=\"{F(font)}\" font-weight=\"{(item.IsBold ? "700" : "400")}\" dominant-baseline=\"hanging\" fill=\"{ColorHex(color)}\">{Escape(value)}</text>\n");
    }

    private static (FigureScientificPoint Left, FigureScientificPoint Right, FigureScientificPoint BaseCenter) DirectionHead(FigureScientificPoint start, FigureScientificPoint end, double head)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double ux = dx / length;
        double uy = dy / length;
        double half = head * 0.52;
        var baseCenter = new FigureScientificPoint(end.X - ux * head, end.Y - uy * head);
        return (new FigureScientificPoint(baseCenter.X - uy * half, baseCenter.Y + ux * half), new FigureScientificPoint(baseCenter.X + uy * half, baseCenter.Y - ux * half), baseCenter);
    }

    private static (double X, double Y, double Width, double Height) ScientificBounds(IReadOnlyList<FigureScientificPoint> points) =>
        (Math.Min(points[0].X, points[1].X), Math.Min(points[0].Y, points[1].Y), Math.Abs(points[1].X - points[0].X), Math.Abs(points[1].Y - points[0].Y));
    private static void AppendSvgRect(
        StringBuilder svg,
        double x,
        double y,
        double width,
        double height,
        Color color,
        string? stroke)
    {
        string opacity = color.A < 255 ? $" opacity=\"{F(color.A / 255.0)}\"" : string.Empty;
        svg.Append($"  <rect x=\"{F(x)}\" y=\"{F(y)}\" width=\"{F(width)}\" height=\"{F(height)}\" fill=\"{ColorHex(color)}\"{(stroke is null ? string.Empty : $" stroke=\"{stroke}\"")}{opacity}/>\n");
    }

    private static void ExportPdf(
        FigureExportDocument document,
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (document.PdfFontStrategy == PdfFontStrategy.EmbedSubsetWhenPermitted)
        {
            throw new NotSupportedException(
                "Strict PDF font embedding is unavailable: the current writer does not implement reliable subsetting and ToUnicode maps.");
        }

        var pdf = new PdfDocumentBuilder();
        int catalogObject = pdf.AddObject(null);
        int pagesObject = pdf.AddObject(null);
        int pageObject = pdf.AddObject(null);
        double scale = 72.0 / document.Dpi;
        double pageWidth = document.WidthPixels * scale;
        double pageHeight = document.HeightPixels * scale;
        var content = new StringBuilder(64 * 1024);
        AppendPdfColor(content, WpfFigureExporter.ParseColor(document.BackgroundColor), fill: true);
        content.Append($"0 0 {F(document.WidthPixels * scale)} {F(document.HeightPixels * scale)} re f\n");

        List<(string Name, int ObjectNumber)> images = [];
        var opacityStates = new Dictionary<(int Fill, int Stroke), (string Name, int ObjectNumber)>();
        IEnumerable<(int Fill, int Stroke)> opacityKeys = document.Annotations
            .Where(annotation => annotation.IsVisible)
            .Select(GetPdfAnnotationOpacityKey)
            .Concat(document.MeasurementOverlays
                .Where(overlay => overlay.IsVisible)
                .Select(GetPdfMeasurementOverlayFillOpacityKey));
        foreach ((int Fill, int Stroke) key in opacityKeys
                     .Where(key => key.Fill != 1_000_000 || key.Stroke != 1_000_000)
                     .Distinct())
        {
            string name = $"GS{opacityStates.Count + 1}";
            int objectNumber = pdf.AddObject(Encoding.ASCII.GetBytes(
                $"<< /Type /ExtGState /ca {F(key.Fill / 1_000_000.0)} /CA {F(key.Stroke / 1_000_000.0)} >>"));
            opacityStates[key] = (name, objectNumber);
        }
        int panelIndex = 0;
        foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidatePanel(panel, document);
            BitmapSource cropped = WpfFigureExporter.LoadPanelImage(panel, cancellationToken);
            Rect imageRect = WpfFigureExporter.CalculateContainedRect(cropped.PixelWidth, cropped.PixelHeight, panel.DestinationRect);
            FigureGlobalStyle panelStyle = document.GlobalStyle.ResolvePanelOverride(panel.StyleOverride);
            int imageObject = pdf.AddObject(BuildPdfImage(cropped));
            string imageName = $"Im{panelIndex++}";
            images.Add((imageName, imageObject));
            double x = imageRect.X * scale;
            double y = pageHeight - imageRect.Bottom * scale;
            content.Append($"q\n{F(imageRect.Width * scale)} 0 0 {F(imageRect.Height * scale)} {F(x)} {F(y)} cm\n/{imageName} Do\nQ\n");
            if (panel.IsInset)
            {
                AppendPdfColor(content, WpfFigureExporter.ParseColor(document.GlobalStyle.ShapeColor), fill: false);
                content.Append($"0.5 w {F(imageRect.X * scale)} {F(pageHeight - imageRect.Bottom * scale)} {F(imageRect.Width * scale)} {F(imageRect.Height * scale)} re S\n");
            }
            AppendPdfScaleBar(content, panel, imageRect, document.Dpi, scale, pageHeight, panelStyle);
            AppendPdfPanelLabel(content, panel.Label, panel.DestinationRect, document.Dpi, scale, pageHeight, panelStyle);
        }

        foreach (FigureAnnotationExportItem annotation in
                 document.Annotations.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidateAnnotation(annotation, document);
            (int Fill, int Stroke) opacityKey = GetPdfAnnotationOpacityKey(annotation);
            bool hasOpacityState = opacityStates.TryGetValue(opacityKey, out var opacityState);
            if (hasOpacityState)
            {
                content.Append($"q /{opacityState.Name} gs\n");
            }
            AppendPdfAnnotation(content, annotation, document.Dpi, scale, pageHeight, document.GlobalStyle);
            if (hasOpacityState)
            {
                content.Append("Q\n");
            }
        }

        foreach (FigureMeasurementOverlayExportItem overlay in
                 document.MeasurementOverlays.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            (int Fill, int Stroke) fillOpacityKey = GetPdfMeasurementOverlayFillOpacityKey(overlay);
            string? fillOpacityStateName = opacityStates.TryGetValue(fillOpacityKey, out var fillOpacityState)
                ? fillOpacityState.Name
                : null;
            AppendPdfMeasurementOverlay(
                content,
                overlay,
                WpfFigureExporter.ResolveMeasurementOverlayPanel(overlay, document),
                document.Dpi,
                scale,
                pageHeight,
                fillOpacityStateName);
        }
        foreach (FigureScientificObjectExportItem scientificObject in
                 document.ScientificObjects.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidateScientificObject(scientificObject, document);
            AppendPdfScientificObject(content, scientificObject, document.Dpi, scale, pageHeight);
        }
        int contentObject = pdf.AddObject(BuildPdfStream(Encoding.ASCII.GetBytes(content.ToString())));
        string xObjects = images.Count == 0
            ? string.Empty
            : $"/XObject << {string.Join(" ", images.Select(image => $"/{image.Name} {image.ObjectNumber} 0 R"))} >> ";
        string extGStates = opacityStates.Count == 0
            ? string.Empty
            : $"/ExtGState << {string.Join(" ", opacityStates.Values.Select(state => $"/{state.Name} {state.ObjectNumber} 0 R"))} >> ";
        string resources = $"<< {xObjects}{extGStates}>>";
        pdf.SetObject(pageObject, Encoding.ASCII.GetBytes(
            $"<< /Type /Page /Parent {pagesObject} 0 R /MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] /Resources {resources} /Contents {contentObject} 0 R >>"));
        pdf.SetObject(pagesObject, Encoding.ASCII.GetBytes(
            $"<< /Type /Pages /Count 1 /Kids [{pageObject} 0 R] >>"));
        pdf.SetObject(catalogObject, Encoding.ASCII.GetBytes(
            $"<< /Type /Catalog /Pages {pagesObject} 0 R >>"));
        WriteNewFile(targetPath, pdf.ToBytes(catalogObject));
    }

    private static void AppendPdfMeasurementOverlay(
        StringBuilder content,
        FigureMeasurementOverlayExportItem overlay,
        FigurePanelExportItem panel,
        int dpi,
        double scale,
        double pageHeight,
        string? fillOpacityStateName)
    {
        FigureMeasurementOverlayGeometry geometry = FigureMeasurementOverlayMapper.Map(
            overlay.ScientificObject,
            panel);
        FigureMeasurementOverlayStyle style = overlay.Style;
        Color stroke = WpfFigureExporter.ParseColor(style.StrokeColor);
        Color fill = WpfFigureExporter.ParseColor(style.FillColor);
        content.Append("q\n");
        AppendPdfColor(content, stroke, fill: false);
        content.Append($"{F(Math.Max(0.25, geometry.StrokeWidthPixels) * scale)} w ");
        AppendPdfDash(content, style.LineStyle, scale);
        switch (overlay.MeasurementKind)
        {
            case ScientificMeasurementKind.Length:
                AppendPdfLine(content, geometry.PointA, geometry.PointB, scale, pageHeight);
                break;
            case ScientificMeasurementKind.Angle:
                AppendPdfLine(content, geometry.PointA, geometry.PointB, scale, pageHeight);
                AppendPdfLine(content, geometry.PointB, geometry.PointC ?? geometry.PointB, scale, pageHeight);
                break;
            case ScientificMeasurementKind.RectangleRoi:
                AppendPdfMeasurementFill(
                    content,
                    fill,
                    style.FillOpacityPercent,
                    fillOpacityStateName,
                    () => AppendPdfRectForPoints(content, geometry.PointA, geometry.PointB, scale, pageHeight));
                AppendPdfColor(content, stroke, fill: false);
                AppendPdfRectForPoints(content, geometry.PointA, geometry.PointB, scale, pageHeight);
                content.Append("S\n");
                break;
            case ScientificMeasurementKind.CircleRoi:
                AppendPdfMeasurementFill(
                    content,
                    fill,
                    style.FillOpacityPercent,
                    fillOpacityStateName,
                    () => AppendPdfEllipseForPoints(content, geometry.PointA, geometry.PointB, scale, pageHeight));
                AppendPdfColor(content, stroke, fill: false);
                AppendPdfEllipseForPoints(content, geometry.PointA, geometry.PointB, scale, pageHeight);
                content.Append("S\n");
                break;
            case ScientificMeasurementKind.Polyline:
                AppendPdfPolyline(content, geometry.PathPoints, scale, pageHeight);
                break;
            default:
                throw new InvalidOperationException("不支持的 Measurement Overlay 类型。");
        }

        if (style.ShowMarkers)
        {
            Color markerStroke = WpfFigureExporter.ParseColor(style.MarkerStrokeColor);
            Color markerFill = WpfFigureExporter.ParseColor(style.MarkerFillColor);
            double radius = Math.Max(1, geometry.MarkerSizePixels / 2);
            AppendPdfColor(content, markerStroke, fill: false);
            content.Append($"{F(Math.Max(0.25, geometry.StrokeWidthPixels * 0.75) * scale)} w ");
            AppendPdfColor(content, markerFill, fill: true);
            foreach (MeasurementPoint point in GetMarkerPoints(overlay.MeasurementKind, geometry))
            {
                AppendPdfEllipse(
                    content,
                    (point.X - radius) * scale,
                    pageHeight - (point.Y + radius) * scale,
                    radius * 2 * scale,
                    radius * 2 * scale);
                content.Append("B\n");
            }
        }

        if (style.ShowLabel)
        {
            Color label = WpfFigureExporter.ParseColor(style.LabelColor);
            FormattedText text = CreateText(
                FigureMeasurementOverlayMapper.CreateLabel(overlay.ScientificObject),
                style.LabelFontSizePt / 72.0 * dpi,
                style.LabelIsBold ? FontWeights.Bold : FontWeights.Normal,
                style.LabelFontFamily);
            AppendPdfGeometry(
                content,
                text.BuildGeometry(new Point(geometry.LabelAnchor.X, geometry.LabelAnchor.Y)),
                label,
                scale,
                pageHeight,
                fill: true);
        }

        content.Append("Q\n");
    }

    private static void AppendPdfMeasurementFill(
        StringBuilder content,
        Color fill,
        double fillOpacityPercent,
        string? fillOpacityStateName,
        Action appendPath)
    {
        if (fillOpacityPercent <= 0)
        {
            return;
        }

        content.Append("q ");
        if (!string.IsNullOrWhiteSpace(fillOpacityStateName))
        {
            content.Append($"/{fillOpacityStateName} gs ");
        }
        AppendPdfColor(content, fill, fill: true);
        appendPath();
        content.Append("f\nQ\n");
    }
    private static void AppendPdfDash(StringBuilder content, string lineStyle, double scale)
    {
        string dash = lineStyle switch
        {
            "dash" => $"[{F(5 * scale)} {F(3 * scale)}] 0 d ",
            "dot" => $"[{F(scale)} {F(2 * scale)}] 0 d ",
            "dash-dot" => $"[{F(5 * scale)} {F(2 * scale)} {F(scale)} {F(2 * scale)}] 0 d ",
            _ => "[] 0 d ",
        };
        content.Append(dash);
    }

    private static void AppendPdfLine(
        StringBuilder content,
        MeasurementPoint first,
        MeasurementPoint second,
        double scale,
        double pageHeight) => content.Append(
            $"{F(first.X * scale)} {F(pageHeight - first.Y * scale)} m {F(second.X * scale)} {F(pageHeight - second.Y * scale)} l S\n");

    private static void AppendPdfPolyline(
        StringBuilder content,
        IReadOnlyList<MeasurementPoint> points,
        double scale,
        double pageHeight)
    {
        if (points.Count < 2)
        {
            throw new InvalidOperationException("Polyline Measurement Overlay 至少需要两个点。");
        }

        content.Append($"{F(points[0].X * scale)} {F(pageHeight - points[0].Y * scale)} m ");
        foreach (MeasurementPoint point in points.Skip(1))
        {
            content.Append($"{F(point.X * scale)} {F(pageHeight - point.Y * scale)} l ");
        }
        content.Append("S\n");
    }

    private static void AppendPdfRectForPoints(
        StringBuilder content,
        MeasurementPoint first,
        MeasurementPoint second,
        double scale,
        double pageHeight)
    {
        double x = Math.Min(first.X, second.X);
        double y = Math.Min(first.Y, second.Y);
        double width = Math.Abs(second.X - first.X);
        double height = Math.Abs(second.Y - first.Y);
        AppendPdfRect(content, x * scale, pageHeight - (y + height) * scale, width * scale, height * scale);
    }

    private static void AppendPdfEllipseForPoints(
        StringBuilder content,
        MeasurementPoint first,
        MeasurementPoint second,
        double scale,
        double pageHeight)
    {
        double x = Math.Min(first.X, second.X);
        double y = Math.Min(first.Y, second.Y);
        double width = Math.Abs(second.X - first.X);
        double height = Math.Abs(second.Y - first.Y);
        AppendPdfEllipse(content, x * scale, pageHeight - (y + height) * scale, width * scale, height * scale);
    }
    private static void AppendPdfPanelLabel(
        StringBuilder content,
        string label,
        PixelRect64 destination,
        int dpi,
        double scale,
        double pageHeight,
        FigureGlobalStyle style)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        double fontSize = Math.Max(12, style.EffectivePanelLabelFontSizePt / 72.0 * dpi);
        double padding = Math.Max(4, dpi / 75.0);
        double width = label.Length * fontSize * 0.68 + padding * 2;
        double height = fontSize * 1.35 + padding;
        AppendPdfColor(content, Colors.White, fill: true);
        AppendPdfRect(content, (destination.X + padding) * scale, pageHeight - (destination.Y + padding + height) * scale, width * scale, height * scale);
        Color textColor = WpfFigureExporter.ParseColor(style.EffectivePanelLabelTextColor);
        FormattedText text = CreateText(
            label,
            fontSize,
            style.PanelLabelIsBold ? FontWeights.Bold : FontWeights.Normal,
            style.EffectivePanelLabelFontFamily);
        AppendPdfGeometry(content, text.BuildGeometry(new Point((destination.X + padding * 2), destination.Y + padding)), textColor, scale, pageHeight, fill: true);
    }

    private static void AppendPdfScaleBar(
        StringBuilder content,
        FigurePanelExportItem panel,
        Rect imageRect,
        int dpi,
        double scale,
        double pageHeight,
        FigureGlobalStyle style)
    {
        IReadOnlyList<FigureScaleBarExportSpec> scaleBars = panel.EffectiveScaleBars;
        if (scaleBars.Count == 0)
        {
            return;
        }

        double thickness = Math.Max(2, style.EffectiveScaleBarThicknessPt / 72.0 * dpi);
        double fontSize = Math.Max(12, style.EffectiveScaleBarFontSizePt / 72.0 * dpi);
        IReadOnlyList<FigureScaleBarGeometry> geometries = FigureScaleBarLayout.Calculate(
            scaleBars,
            panel.SourceRect,
            new FigureImageRect(imageRect.X, imageRect.Y, imageRect.Width, imageRect.Height),
            dpi,
            thickness,
            fontSize);
        Color scaleBarColor = WpfFigureExporter.ParseColor(style.ScaleBarColor);
        Color labelColor = WpfFigureExporter.ParseColor(style.EffectiveScaleBarLabelColor);
        foreach (FigureScaleBarGeometry geometry in geometries)
        {
            AppendPdfColor(content, Colors.Black, fill: false);
            content.Append($"{F((thickness + Math.Max(3, dpi / 100.0)) * scale)} w {F(geometry.Left * scale)} {F(pageHeight - geometry.Y * scale)} m {F(geometry.Right * scale)} {F(pageHeight - geometry.Y * scale)} l S\n");
            AppendPdfColor(content, scaleBarColor, fill: false);
            content.Append($"{F(thickness * scale)} w {F(geometry.Left * scale)} {F(pageHeight - geometry.Y * scale)} m {F(geometry.Right * scale)} {F(pageHeight - geometry.Y * scale)} l S\n");
            if (geometry.Spec.ShowLabel)
            {
                FormattedText text = CreateText(
                    geometry.Spec.Label,
                    fontSize,
                    style.ScaleBarLabelIsBold ? FontWeights.Bold : FontWeights.Normal,
                    style.EffectiveScaleBarFontFamily);
                double textX = geometry.Right - text.Width;
                AppendPdfGeometry(content, text.BuildGeometry(new Point(textX, geometry.LabelTop)), labelColor, scale, pageHeight, fill: true, stroke: Colors.Black, strokeWidth: Math.Max(2, dpi / 150.0) * scale);
            }
        }
    }
    private static void AppendPdfScientificObject(
        StringBuilder content,
        FigureScientificObjectExportItem item,
        int dpi,
        double scale,
        double pageHeight)
    {
        Color stroke = WpfFigureExporter.ParseColor(item.StrokeColor);
        Color fill = WpfFigureExporter.ParseColor(item.FillColor);
        Color text = WpfFigureExporter.ParseColor(item.TextColor);
        double strokeWidth = item.StrokeWidthPt / 72.0 * dpi * scale;
        double fontSize = item.FontSizePt / 72.0 * dpi;
        if (item.Kind is FigureScientificObjectKind.PolygonAnnotation or FigureScientificObjectKind.Roi)
        {
            AppendPdfColor(content, fill, fill: true);
            AppendPdfColor(content, stroke, fill: false);
            content.Append($"{F(strokeWidth)} w ");
            FigureScientificPoint first = item.Points[0];
            content.Append($"{F(first.X * scale)} {F(pageHeight - first.Y * scale)} m ");
            foreach (FigureScientificPoint point in item.Points.Skip(1)) content.Append($"{F(point.X * scale)} {F(pageHeight - point.Y * scale)} l ");
            content.Append("h B\n");
            AppendPdfScientificText(content, item.Label, item.Points[0].X, item.Points[0].Y - fontSize - 4, text, item, fontSize, scale, pageHeight);
            return;
        }
        if (item.Kind == FigureScientificObjectKind.DirectionMarker)
        {
            FigureScientificPoint start = item.Points[0];
            FigureScientificPoint end = item.Points[1];
            (FigureScientificPoint left, FigureScientificPoint right, FigureScientificPoint baseCenter) = DirectionHead(start, end, Math.Max(strokeWidth / scale * 4, 10.0 / 72.0 * dpi));
            AppendPdfColor(content, stroke, fill: false);
            content.Append($"{F(strokeWidth)} w {F(start.X * scale)} {F(pageHeight - start.Y * scale)} m {F(baseCenter.X * scale)} {F(pageHeight - baseCenter.Y * scale)} l S\n");
            AppendPdfColor(content, stroke, fill: true);
            content.Append($"{F(end.X * scale)} {F(pageHeight - end.Y * scale)} m {F(left.X * scale)} {F(pageHeight - left.Y * scale)} l {F(right.X * scale)} {F(pageHeight - right.Y * scale)} l h f\n");
            AppendPdfScientificText(content, item.Label, end.X + 5, end.Y - fontSize - 5, text, item, fontSize, scale, pageHeight);
            return;
        }
        (double x, double y, double width, double height) = ScientificBounds(item.Points);
        if (item.Kind == FigureScientificObjectKind.Colorbar)
        {
            IReadOnlyList<Color> colors = WpfFigureExporter.GetColormapColors(item.Colormap);
            double slice = height / colors.Count;
            for (int index = 0; index < colors.Count; index++)
            {
                AppendPdfColor(content, colors[index], fill: true);
                AppendPdfRect(content, x * scale, pageHeight - (y + (index + 1) * slice) * scale, width * scale, slice * scale);
                content.Append("f\n");
            }
            AppendPdfColor(content, stroke, fill: false);
            content.Append($"{F(strokeWidth)} w ");
            AppendPdfRect(content, x * scale, pageHeight - (y + height) * scale, width * scale, height * scale);
            content.Append("S\n");
            AppendPdfScientificText(content, $"{item.Maximum:0.###} {item.Unit}", x + width + 5, y, text, item, fontSize, scale, pageHeight);
            AppendPdfScientificText(content, $"{item.Minimum:0.###} {item.Unit}", x + width + 5, y + height - fontSize, text, item, fontSize, scale, pageHeight);
            AppendPdfScientificText(content, item.Label, x, Math.Max(0, y - fontSize - 4), text, item, fontSize, scale, pageHeight);
            return;
        }
        AppendPdfColor(content, Color.FromRgb(12, 18, 25), fill: true);
        AppendPdfColor(content, stroke, fill: false);
        content.Append($"{F(strokeWidth)} w ");
        AppendPdfRect(content, x * scale, pageHeight - (y + height) * scale, width * scale, height * scale);
        content.Append("B\n");
        double row = height / Math.Max(1, item.EffectiveChannelLegendEntries.Count);
        for (int index = 0; index < item.EffectiveChannelLegendEntries.Count; index++)
        {
            FigureChannelLegendEntry entry = item.EffectiveChannelLegendEntries[index];
            double rowY = y + index * row;
            AppendPdfColor(content, WpfFigureExporter.ParseColor(entry.Color), fill: true);
            AppendPdfRect(content, (x + 5) * scale, pageHeight - (rowY + Math.Max(2, (row - fontSize) / 2) + Math.Max(4, fontSize)) * scale, Math.Min(16, width * 0.18) * scale, Math.Max(4, fontSize) * scale);
            content.Append("f\n");
            AppendPdfScientificText(content, entry.Label, x + Math.Min(24, width * 0.25), rowY + Math.Max(0, (row - fontSize) / 2), text, item, fontSize, scale, pageHeight);
        }
    }

    private static void AppendPdfScientificText(StringBuilder content, string value, double x, double y, Color color, FigureScientificObjectExportItem item, double font, double scale, double pageHeight)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            FormattedText label = CreateText(value, font, item.IsBold ? FontWeights.Bold : FontWeights.Normal, item.FontFamily);
            AppendPdfGeometry(content, label.BuildGeometry(new Point(x, y)), color, scale, pageHeight, fill: true);
        }
    }
    private static void AppendPdfAnnotation(
        StringBuilder content,
        FigureAnnotationExportItem annotation,
        int dpi,
        double scale,
        double pageHeight,
        FigureGlobalStyle style)
    {
        if (string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            Color textColor = WpfFigureExporter.ParseColor(annotation.TextColor);
            string fontFamily = string.IsNullOrWhiteSpace(annotation.FontFamily)
                ? style.FontFamily
                : annotation.FontFamily;
            FormattedText text = CreateText(
                annotation.Text,
                annotation.FontSizePt / 72.0 * dpi,
                annotation.IsBold ? FontWeights.Bold : FontWeights.Normal,
                fontFamily);
            AppendPdfGeometry(content, text.BuildGeometry(new Point(annotation.X, annotation.Y)), textColor, scale, pageHeight, fill: true);
            return;
        }

        Color strokeColor = WpfFigureExporter.ParseColor(annotation.StrokeColor);
        double strokeWidth = annotation.StrokeWidthPt / 72.0 * dpi * scale;
        AppendPdfColor(content, strokeColor, fill: false);
        content.Append($"{F(strokeWidth)} w ");
        if (string.Equals(annotation.Kind, "line", StringComparison.OrdinalIgnoreCase))
        {
            content.Append($"{F(annotation.X * scale)} {F(pageHeight - annotation.Y * scale)} m {F(annotation.EndX * scale)} {F(pageHeight - annotation.EndY * scale)} l S\n");
            return;
        }

        if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase))
        {
            bool hasFill = annotation.FillOpacityPercent > 0;
            if (hasFill)
            {
                AppendPdfColor(content, WpfFigureExporter.ParseColor(annotation.FillColor), fill: true);
            }
            AppendPdfRect(content, annotation.X * scale, pageHeight - annotation.EndY * scale, (annotation.EndX - annotation.X) * scale, (annotation.EndY - annotation.Y) * scale);
            content.Append(hasFill ? "B\n" : "S\n");
            return;
        }

        if (string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase))
        {
            bool hasFill = annotation.FillOpacityPercent > 0;
            if (hasFill)
            {
                AppendPdfColor(content, WpfFigureExporter.ParseColor(annotation.FillColor), fill: true);
            }
            AppendPdfEllipse(content, annotation.X * scale, pageHeight - annotation.EndY * scale, (annotation.EndX - annotation.X) * scale, (annotation.EndY - annotation.Y) * scale);
            content.Append(hasFill ? "B\n" : "S\n");
            return;
        }

        double dx = annotation.EndX - annotation.X;
        double dy = annotation.EndY - annotation.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Max(strokeWidth / scale * 4, 10.0 / 72.0 * dpi);
        double halfWidth = headLength * 0.52;
        double baseX = annotation.EndX - unitX * headLength;
        double baseY = annotation.EndY - unitY * headLength;
        double leftX = baseX - unitY * halfWidth;
        double leftY = baseY + unitX * halfWidth;
        double rightX = baseX + unitY * halfWidth;
        double rightY = baseY - unitX * halfWidth;
        content.Append($"{F(annotation.X * scale)} {F(pageHeight - annotation.Y * scale)} m {F(baseX * scale)} {F(pageHeight - baseY * scale)} l S\n");
        AppendPdfColor(content, strokeColor, fill: true);
        content.Append($"{F(annotation.EndX * scale)} {F(pageHeight - annotation.EndY * scale)} m {F(leftX * scale)} {F(pageHeight - leftY * scale)} l {F(rightX * scale)} {F(pageHeight - rightY * scale)} l h f\n");
    }

    private static (int Fill, int Stroke) GetPdfMeasurementOverlayFillOpacityKey(
        FigureMeasurementOverlayExportItem overlay)
    {
        static int Alpha(double value) =>
            (int)Math.Round(Math.Clamp(value, 0, 1) * 1_000_000, MidpointRounding.AwayFromZero);

        bool hasRoiFill = overlay.MeasurementKind is ScientificMeasurementKind.RectangleRoi or
            ScientificMeasurementKind.CircleRoi;
        if (!hasRoiFill || overlay.Style.FillOpacityPercent <= 0)
        {
            return (1_000_000, 1_000_000);
        }

        Color fill = WpfFigureExporter.ParseColor(overlay.Style.FillColor);
        return (Alpha(fill.A / 255.0 * overlay.Style.FillOpacityPercent / 100.0), 1_000_000);
    }
    private static (int Fill, int Stroke) GetPdfAnnotationOpacityKey(
        FigureAnnotationExportItem annotation)
    {
        static int Alpha(double value) =>
            (int)Math.Round(Math.Clamp(value, 0, 1) * 1_000_000, MidpointRounding.AwayFromZero);

        if (string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            Color text = WpfFigureExporter.ParseColor(annotation.TextColor);
            return (Alpha(text.A / 255.0), 1_000_000);
        }

        Color stroke = WpfFigureExporter.ParseColor(annotation.StrokeColor);
        double strokeAlpha = stroke.A / 255.0;
        if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase))
        {
            Color fill = WpfFigureExporter.ParseColor(annotation.FillColor);
            double fillAlpha = annotation.FillOpacityPercent <= 0
                ? 1
                : fill.A / 255.0 * annotation.FillOpacityPercent / 100.0;
            return (Alpha(fillAlpha), Alpha(strokeAlpha));
        }

        bool arrow = string.Equals(annotation.Kind, "arrow", StringComparison.OrdinalIgnoreCase);
        return (arrow ? Alpha(strokeAlpha) : 1_000_000, Alpha(strokeAlpha));
    }

    private static void AppendPdfGeometry(
        StringBuilder content,
        Geometry geometry,
        Color fillColor,
        double scale,
        double pageHeight,
        bool fill,
        Color? stroke = null,
        double strokeWidth = 0)
    {
        AppendPdfColor(content, fillColor, fill: true);
        if (stroke is { } strokeColor)
        {
            AppendPdfColor(content, strokeColor, fill: false);
            content.Append($"{F(strokeWidth)} w ");
        }

        PathGeometry flattened = geometry.GetFlattenedPathGeometry();
        foreach (PathFigure figure in flattened.Figures)
        {
            content.Append($"{F(figure.StartPoint.X * scale)} {F(pageHeight - figure.StartPoint.Y * scale)} m ");
            foreach (PathSegment segment in figure.Segments)
            {
                switch (segment)
                {
                    case LineSegment line:
                        content.Append($"{F(line.Point.X * scale)} {F(pageHeight - line.Point.Y * scale)} l ");
                        break;
                    case PolyLineSegment polyLine:
                        foreach (Point point in polyLine.Points)
                        {
                            content.Append($"{F(point.X * scale)} {F(pageHeight - point.Y * scale)} l ");
                        }
                        break;
                    case BezierSegment bezier:
                        content.Append($"{F(bezier.Point1.X * scale)} {F(pageHeight - bezier.Point1.Y * scale)} {F(bezier.Point2.X * scale)} {F(pageHeight - bezier.Point2.Y * scale)} {F(bezier.Point3.X * scale)} {F(pageHeight - bezier.Point3.Y * scale)} c ");
                        break;
                }
            }

            if (figure.IsClosed)
            {
                content.Append("h ");
            }

            content.Append(stroke is null ? "f\n" : "B\n");
        }
    }

    private static void AppendPdfRect(StringBuilder content, double x, double y, double width, double height) =>
        content.Append($"{F(x)} {F(y)} {F(width)} {F(height)} re ");

    private static void AppendPdfEllipse(StringBuilder content, double x, double y, double width, double height)
    {
        const double Kappa = 0.5522847498;
        double rx = width / 2;
        double ry = height / 2;
        double cx = x + rx;
        double cy = y + ry;
        content.Append($"{F(cx + rx)} {F(cy)} m ");
        content.Append($"{F(cx + rx)} {F(cy + Kappa * ry)} {F(cx + Kappa * rx)} {F(cy + ry)} {F(cx)} {F(cy + ry)} c ");
        content.Append($"{F(cx - Kappa * rx)} {F(cy + ry)} {F(cx - rx)} {F(cy + Kappa * ry)} {F(cx - rx)} {F(cy)} c ");
        content.Append($"{F(cx - rx)} {F(cy - Kappa * ry)} {F(cx - Kappa * rx)} {F(cy - ry)} {F(cx)} {F(cy - ry)} c ");
        content.Append($"{F(cx + Kappa * rx)} {F(cy - ry)} {F(cx + rx)} {F(cy - Kappa * ry)} {F(cx + rx)} {F(cy)} c ");
    }

    private static FormattedText CreateText(string text, double fontSize, FontWeight weight, string fontFamily) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(fontFamily), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            Brushes.Black,
            pixelsPerDip: 1.0);

    private static byte[] BuildPdfImage(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);
        converted.Freeze();
        int stride = converted.PixelWidth * 3;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(pixels);
        }

        byte[] data = compressed.ToArray();
        string header = $"<< /Type /XObject /Subtype /Image /Width {converted.PixelWidth} /Height {converted.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode /Length {data.Length} >>\nstream\n";
        return Encoding.ASCII.GetBytes(header).Concat(data).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray();
    }

    private static byte[] BuildPdfStream(byte[] data)
    {
        byte[] header = Encoding.ASCII.GetBytes($"<< /Length {data.Length} >>\nstream\n");
        return header.Concat(data).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray();
    }

    private static byte[] EncodePng(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }

    private static void AppendPdfColor(StringBuilder content, Color color, bool fill)
    {
        string command = fill ? "rg" : "RG";
        content.Append($"{F(color.R / 255.0)} {F(color.G / 255.0)} {F(color.B / 255.0)} {command} ");
    }

    private static void WriteNewFile(string path, byte[] bytes)
    {
        bool created = false;
        try
        {
            using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            output.Write(bytes);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            throw;
        }
    }

    private static string ColorHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    private static string Escape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class PdfDocumentBuilder
    {
        private readonly List<byte[]?> _objects = [];

        public int AddObject(byte[]? value)
        {
            _objects.Add(value);
            return _objects.Count;
        }

        public void SetObject(int objectNumber, byte[] value) => _objects[objectNumber - 1] = value;

        public byte[] ToBytes(int rootObject)
        {
            using var output = new MemoryStream();
            WriteAscii(output, "%PDF-1.7\n%\xE2\xE3\xCF\xD3\n");
            long[] offsets = new long[_objects.Count + 1];
            for (int index = 0; index < _objects.Count; index++)
            {
                offsets[index + 1] = output.Position;
                WriteAscii(output, $"{index + 1} 0 obj\n");
                output.Write(_objects[index] ?? throw new InvalidOperationException("PDF 对象未完成。"));
                WriteAscii(output, "\nendobj\n");
            }

            long xref = output.Position;
            WriteAscii(output, $"xref\n0 {_objects.Count + 1}\n0000000000 65535 f \n");
            for (int index = 1; index < offsets.Length; index++)
            {
                WriteAscii(output, $"{offsets[index]:D10} 00000 n \n");
            }

            WriteAscii(output, $"trailer\n<< /Size {_objects.Count + 1} /Root {rootObject} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
            return output.ToArray();
        }

        private static void WriteAscii(Stream stream, string value) =>
            stream.Write(Encoding.ASCII.GetBytes(value));
    }
}
