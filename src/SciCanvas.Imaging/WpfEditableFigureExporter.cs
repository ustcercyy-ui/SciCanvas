using System.IO;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;

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
            BitmapSource cropped = WpfFigureExporter.LoadExactCrop(panel.Source.OriginalPath, panel.SourceRect, panel.FrameIndex);
            cropped = WpfImageAdjustmentProcessor.Apply(cropped, panel.Adjustments);
            Rect imageRect = WpfFigureExporter.CalculateContainedRect(
                cropped.PixelWidth,
                cropped.PixelHeight,
                panel.DestinationRect);
            string dataUri = $"data:image/png;base64,{Convert.ToBase64String(EncodePng(cropped))}";
            svg.Append($"  <g id=\"panel-{panelIndex++}\" data-source=\"{Escape(panel.Source.DisplayName)}\" data-label=\"{Escape(panel.Label)}\">\n");
            svg.Append($"    <image x=\"{F(imageRect.X)}\" y=\"{F(imageRect.Y)}\" width=\"{F(imageRect.Width)}\" height=\"{F(imageRect.Height)}\" preserveAspectRatio=\"none\" href=\"{dataUri}\"/>\n");
            if (panel.IsInset)
            {
                Color insetColor = WpfFigureExporter.ParseColor(document.GlobalStyle.ShapeColor);
                svg.Append($"    <rect x=\"{F(imageRect.X)}\" y=\"{F(imageRect.Y)}\" width=\"{F(imageRect.Width)}\" height=\"{F(imageRect.Height)}\" fill=\"none\" stroke=\"{ColorHex(insetColor)}\" stroke-width=\"{F(Math.Max(1, 0.5 / 72.0 * document.Dpi))}\"/>\n");
            }
            AppendSvgScaleBar(svg, panel, imageRect, document.Dpi, document.GlobalStyle);
            AppendSvgPanelLabel(svg, panel.Label, panel.DestinationRect, document.Dpi, document.GlobalStyle);
            svg.Append("  </g>\n");
        }

        foreach (FigureAnnotationExportItem annotation in
                 document.Annotations.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidateAnnotation(annotation, document);
            AppendSvgAnnotation(svg, annotation, document.Dpi, document.GlobalStyle);
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

        double fontSize = Math.Max(12, style.FontSizePt / 72.0 * dpi);
        double padding = Math.Max(4, dpi / 75.0);
        double width = label.Length * fontSize * 0.68 + padding * 2;
        double height = fontSize * 1.35 + padding;
        AppendSvgRect(svg, destination.X + padding, destination.Y + padding, width, height, Colors.White, null);
        Color textColor = WpfFigureExporter.ParseColor(style.TextColor);
        svg.Append($"    <text x=\"{F(destination.X + padding * 2)}\" y=\"{F(destination.Y + padding)}\" font-family=\"{Escape(style.FontFamily)}\" font-size=\"{F(fontSize)}\" font-weight=\"700\" dominant-baseline=\"hanging\" fill=\"{ColorHex(textColor)}\">{Escape(label)}</text>\n");
    }

    private static void AppendSvgScaleBar(
        StringBuilder svg,
        FigurePanelExportItem panel,
        Rect imageRect,
        int dpi,
        FigureGlobalStyle style)
    {
        if (panel.ScaleBar is not { } scaleBar)
        {
            return;
        }

        double sourcePixels = scaleBar.PhysicalLength / scaleBar.PhysicalUnitsPerSourcePixel;
        double outputPixelsPerSourcePixel = imageRect.Width / panel.SourceRect.Width;
        double barWidth = sourcePixels * outputPixelsPerSourcePixel;
        double margin = Math.Max(10, Math.Min(imageRect.Width, imageRect.Height) * 0.035);
        double thickness = Math.Max(2, style.StrokeWidthPt / 72.0 * dpi);
        double right = imageRect.Right - margin;
        double y = imageRect.Bottom - margin - thickness / 2.0;
        double left = right - barWidth;
        svg.Append($"    <line x1=\"{F(left)}\" y1=\"{F(y)}\" x2=\"{F(right)}\" y2=\"{F(y)}\" stroke=\"#000000\" stroke-width=\"{F(thickness + Math.Max(3, dpi / 100.0))}\" stroke-linecap=\"square\"/>\n");
        Color scaleBarColor = WpfFigureExporter.ParseColor(style.ScaleBarColor);
        string scaleBarHex = ColorHex(scaleBarColor);
        svg.Append($"    <line x1=\"{F(left)}\" y1=\"{F(y)}\" x2=\"{F(right)}\" y2=\"{F(y)}\" stroke=\"{scaleBarHex}\" stroke-width=\"{F(thickness)}\" stroke-linecap=\"square\"/>\n");
        if (scaleBar.ShowLabel)
        {
            double fontSize = Math.Max(12, style.FontSizePt / 72.0 * dpi);
            double textX = right - ($"{scaleBar.PhysicalLength:0.###} {scaleBar.Unit}".Length * fontSize * 0.58);
            double textY = y - thickness - fontSize * 1.3;
            svg.Append($"    <text x=\"{F(textX)}\" y=\"{F(textY)}\" font-family=\"{Escape(style.FontFamily)}\" font-size=\"{F(fontSize)}\" font-weight=\"700\" fill=\"{scaleBarHex}\" stroke=\"#000000\" stroke-width=\"{F(Math.Max(2, dpi / 150.0))}\" paint-order=\"stroke\">{Escape($"{scaleBar.PhysicalLength:0.###} {scaleBar.Unit}")}</text>\n");
        }
    }

    private static void AppendSvgAnnotation(
        StringBuilder svg,
        FigureAnnotationExportItem annotation,
        int dpi,
        FigureGlobalStyle style)
    {
        Color color = WpfFigureExporter.ParseColor(annotation.Color);
        string fill = ColorHex(color);
        string opacity = color.A < 255 ? $" opacity=\"{F(color.A / 255.0)}\"" : string.Empty;
        if (string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            double fontSize = annotation.FontSizePt / 72.0 * dpi;
            svg.Append($"  <text x=\"{F(annotation.X)}\" y=\"{F(annotation.Y)}\" font-family=\"{Escape(style.FontFamily)}\" font-size=\"{F(fontSize)}\" font-weight=\"{(annotation.IsBold ? "700" : "400")}\" dominant-baseline=\"hanging\" fill=\"{fill}\"{opacity}>{Escape(annotation.Text)}</text>\n");
            return;
        }

        double strokeWidth = annotation.StrokeWidthPt / 72.0 * dpi;
        if (string.Equals(annotation.Kind, "line", StringComparison.OrdinalIgnoreCase))
        {
            svg.Append($"  <line x1=\"{F(annotation.X)}\" y1=\"{F(annotation.Y)}\" x2=\"{F(annotation.EndX)}\" y2=\"{F(annotation.EndY)}\" fill=\"none\" stroke=\"{fill}\" stroke-width=\"{F(strokeWidth)}\" stroke-linecap=\"round\"{opacity}/>\n");
            return;
        }

        if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase))
        {
            svg.Append($"  <rect x=\"{F(annotation.X)}\" y=\"{F(annotation.Y)}\" width=\"{F(annotation.EndX - annotation.X)}\" height=\"{F(annotation.EndY - annotation.Y)}\" fill=\"none\" stroke=\"{fill}\" stroke-width=\"{F(strokeWidth)}\"{opacity}/>\n");
            return;
        }

        if (string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase))
        {
            svg.Append($"  <ellipse cx=\"{F((annotation.X + annotation.EndX) / 2)}\" cy=\"{F((annotation.Y + annotation.EndY) / 2)}\" rx=\"{F((annotation.EndX - annotation.X) / 2)}\" ry=\"{F((annotation.EndY - annotation.Y) / 2)}\" fill=\"none\" stroke=\"{fill}\" stroke-width=\"{F(strokeWidth)}\"{opacity}/>\n");
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
        svg.Append($"  <g fill=\"{fill}\" stroke=\"{fill}\" stroke-width=\"{F(strokeWidth)}\" stroke-linecap=\"round\" stroke-linejoin=\"round\"{opacity}>\n");
        svg.Append($"    <line x1=\"{F(annotation.X)}\" y1=\"{F(annotation.Y)}\" x2=\"{F(baseX)}\" y2=\"{F(baseY)}\"/>\n");
        svg.Append($"    <polygon points=\"{F(annotation.EndX)},{F(annotation.EndY)} {F(leftX)},{F(leftY)} {F(rightX)},{F(rightY)}\"/>\n  </g>\n");
    }

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
        int panelIndex = 0;
        foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidatePanel(panel, document);
            BitmapSource cropped = WpfFigureExporter.LoadExactCrop(panel.Source.OriginalPath, panel.SourceRect, panel.FrameIndex);
            cropped = WpfImageAdjustmentProcessor.Apply(cropped, panel.Adjustments);
            Rect imageRect = WpfFigureExporter.CalculateContainedRect(cropped.PixelWidth, cropped.PixelHeight, panel.DestinationRect);
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
            AppendPdfScaleBar(content, panel, imageRect, document.Dpi, scale, pageHeight, document.GlobalStyle);
            AppendPdfPanelLabel(content, panel.Label, panel.DestinationRect, document.Dpi, scale, pageHeight, document.GlobalStyle);
        }

        foreach (FigureAnnotationExportItem annotation in
                 document.Annotations.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidateAnnotation(annotation, document);
            AppendPdfAnnotation(content, annotation, document.Dpi, scale, pageHeight, document.GlobalStyle);
        }

        int contentObject = pdf.AddObject(BuildPdfStream(Encoding.ASCII.GetBytes(content.ToString())));
        string resources = images.Count == 0
            ? "<< >>"
            : $"<< /XObject << {string.Join(" ", images.Select(image => $"/{image.Name} {image.ObjectNumber} 0 R"))} >> >>";
        pdf.SetObject(pageObject, Encoding.ASCII.GetBytes(
            $"<< /Type /Page /Parent {pagesObject} 0 R /MediaBox [0 0 {F(pageWidth)} {F(pageHeight)}] /Resources {resources} /Contents {contentObject} 0 R >>"));
        pdf.SetObject(pagesObject, Encoding.ASCII.GetBytes(
            $"<< /Type /Pages /Count 1 /Kids [{pageObject} 0 R] >>"));
        pdf.SetObject(catalogObject, Encoding.ASCII.GetBytes(
            $"<< /Type /Catalog /Pages {pagesObject} 0 R >>"));
        WriteNewFile(targetPath, pdf.ToBytes(catalogObject));
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

        double fontSize = Math.Max(12, style.FontSizePt / 72.0 * dpi);
        double padding = Math.Max(4, dpi / 75.0);
        double width = label.Length * fontSize * 0.68 + padding * 2;
        double height = fontSize * 1.35 + padding;
        AppendPdfColor(content, Colors.White, fill: true);
        AppendPdfRect(content, (destination.X + padding) * scale, pageHeight - (destination.Y + padding + height) * scale, width * scale, height * scale);
        Color textColor = WpfFigureExporter.ParseColor(style.TextColor);
        FormattedText text = CreateText(label, fontSize, FontWeights.Bold, style.FontFamily);
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
        if (panel.ScaleBar is not { } scaleBar)
        {
            return;
        }

        double sourcePixels = scaleBar.PhysicalLength / scaleBar.PhysicalUnitsPerSourcePixel;
        double barWidth = sourcePixels * imageRect.Width / panel.SourceRect.Width;
        double margin = Math.Max(10, Math.Min(imageRect.Width, imageRect.Height) * 0.035);
        double thickness = Math.Max(2, style.StrokeWidthPt / 72.0 * dpi);
        double right = imageRect.Right - margin;
        double y = imageRect.Bottom - margin - thickness / 2;
        double left = right - barWidth;
        AppendPdfColor(content, Colors.Black, fill: false);
        content.Append($"{F((thickness + Math.Max(3, dpi / 100.0)) * scale)} w {F(left * scale)} {F(pageHeight - y * scale)} m {F(right * scale)} {F(pageHeight - y * scale)} l S\n");
        Color scaleBarColor = WpfFigureExporter.ParseColor(style.ScaleBarColor);
        AppendPdfColor(content, scaleBarColor, fill: false);
        content.Append($"{F(thickness * scale)} w {F(left * scale)} {F(pageHeight - y * scale)} m {F(right * scale)} {F(pageHeight - y * scale)} l S\n");
        if (scaleBar.ShowLabel)
        {
            string textValue = $"{scaleBar.PhysicalLength:0.###} {scaleBar.Unit}";
            double fontSize = Math.Max(12, style.FontSizePt / 72.0 * dpi);
            FormattedText text = CreateText(textValue, fontSize, FontWeights.Bold, style.FontFamily);
            double textX = right - text.Width;
            double textY = y - thickness - text.Height - Math.Max(3, dpi / 100.0);
            AppendPdfGeometry(content, text.BuildGeometry(new Point(textX, textY)), scaleBarColor, scale, pageHeight, fill: true, stroke: Colors.Black, strokeWidth: Math.Max(2, dpi / 150.0) * scale);
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
        Color color = WpfFigureExporter.ParseColor(annotation.Color);
        if (string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            FormattedText text = CreateText(
                annotation.Text,
                annotation.FontSizePt / 72.0 * dpi,
                annotation.IsBold ? FontWeights.Bold : FontWeights.Normal,
                style.FontFamily);
            AppendPdfGeometry(content, text.BuildGeometry(new Point(annotation.X, annotation.Y)), color, scale, pageHeight, fill: true);
            return;
        }

        double strokeWidth = annotation.StrokeWidthPt / 72.0 * dpi * scale;
        AppendPdfColor(content, color, fill: false);
        content.Append($"{F(strokeWidth)} w ");
        if (string.Equals(annotation.Kind, "line", StringComparison.OrdinalIgnoreCase))
        {
            content.Append($"{F(annotation.X * scale)} {F(pageHeight - annotation.Y * scale)} m {F(annotation.EndX * scale)} {F(pageHeight - annotation.EndY * scale)} l S\n");
            return;
        }

        if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase))
        {
            AppendPdfRect(content, annotation.X * scale, pageHeight - annotation.EndY * scale, (annotation.EndX - annotation.X) * scale, (annotation.EndY - annotation.Y) * scale);
            content.Append("S\n");
            return;
        }

        if (string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase))
        {
            AppendPdfEllipse(content, annotation.X * scale, pageHeight - annotation.EndY * scale, (annotation.EndX - annotation.X) * scale, (annotation.EndY - annotation.Y) * scale);
            content.Append("S\n");
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
        AppendPdfColor(content, color, fill: true);
        content.Append($"{F(annotation.EndX * scale)} {F(pageHeight - annotation.EndY * scale)} m {F(leftX * scale)} {F(pageHeight - leftY * scale)} l {F(rightX * scale)} {F(pageHeight - rightY * scale)} l h f\n");
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
