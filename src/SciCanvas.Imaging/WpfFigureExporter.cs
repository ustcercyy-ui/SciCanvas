using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Imaging;

public sealed class WpfFigureExporter : IFigureExporter
{
    public Task ExportAsync(
        FigureExportDocument document,
        string targetPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        return Task.Run(
            () => ExportCore(document, targetPath, cancellationToken),
            cancellationToken);
    }

    private static void ExportCore(
        FigureExportDocument document,
        string targetPath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string extension = Path.GetExtension(targetPath).ToLowerInvariant();
        if (extension is ".svg" or ".pdf")
        {
            WpfEditableFigureExporter.Export(document, targetPath, extension, cancellationToken);
            return;
        }

        if (extension is ".tif" or ".tiff" && document.BitDepth == 16)
        {
            WpfHighBitDepthFigureExporter.Export(document, targetPath, cancellationToken);
            return;
        }

        if (document.BitDepth != 8)
        {
            throw new NotSupportedException("16-bit 拼版当前只能导出为 TIFF。");
        }

        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            double deviceIndependentUnitsPerPixel = 96.0 / document.Dpi;
            drawing.PushTransform(new ScaleTransform(
                deviceIndependentUnitsPerPixel,
                deviceIndependentUnitsPerPixel));
            drawing.DrawRectangle(
                new SolidColorBrush(ParseColor(document.BackgroundColor)),
                pen: null,
                new Rect(0, 0, document.WidthPixels, document.HeightPixels));

            foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidatePanel(panel, document);

                BitmapSource cropped = LoadExactCrop(panel.Source.OriginalPath, panel.SourceRect, panel.FrameIndex);
                cropped = WpfImageAdjustmentProcessor.Apply(cropped, panel.Adjustments);
                Rect imageRect = CalculateContainedRect(
                    cropped.PixelWidth,
                    cropped.PixelHeight,
                    panel.DestinationRect);
                FigureGlobalStyle panelStyle = document.GlobalStyle.ResolvePanelOverride(panel.StyleOverride);
                drawing.DrawImage(cropped, imageRect);
                DrawInsetBorder(drawing, panel, imageRect, document.Dpi, panelStyle);
                DrawScaleBar(drawing, panel, imageRect, document.Dpi, panelStyle);
                DrawPanelLabel(drawing, panel.Label, panel.DestinationRect, document.Dpi, panelStyle);
            }

            foreach (FigureAnnotationExportItem annotation in
                     document.Annotations.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateAnnotation(annotation, document);
                DrawAnnotation(drawing, annotation, document.Dpi, document.GlobalStyle);
            }

            drawing.Pop();
        }

        var bitmap = new RenderTargetBitmap(
            document.WidthPixels,
            document.HeightPixels,
            document.Dpi,
            document.Dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();

        BitmapEncoder encoder = CreateEncoder(Path.GetExtension(targetPath));
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        bool targetCreated = false;
        try
        {
            using var output = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: false);
            targetCreated = true;
            encoder.Save(output);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (targetCreated)
            {
                TryDeleteIncompleteTarget(targetPath);
            }

            throw;
        }
    }

    internal static BitmapSource LoadExactCrop(string sourcePath, PixelRect64 crop, int frameIndex = 0)
    {
        if (crop.X > int.MaxValue || crop.Y > int.MaxValue ||
            crop.Width > int.MaxValue || crop.Height > int.MaxValue)
        {
            throw new NotSupportedException("当前 Windows 图像编码器不支持超过32位范围的拼版裁剪坐标。");
        }

        BitmapFrame frame;
        using (var input = new FileStream(
                   sourcePath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 1024 * 1024,
                   useAsync: false))
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (frameIndex < 0 || frameIndex >= decoder.Frames.Count)
            {
                throw new InvalidOperationException($"源图像只有 {decoder.Frames.Count} 帧，无法读取第 {frameIndex + 1} 帧。");
            }

            frame = decoder.Frames[frameIndex];
            frame.Freeze();
        }

        if (crop.Right > frame.PixelWidth || crop.Bottom > frame.PixelHeight)
        {
            throw new InvalidOperationException("拼版中的裁剪区域超出当前源图像边界。");
        }

        var cropped = new CroppedBitmap(
            frame,
            new Int32Rect((int)crop.X, (int)crop.Y, (int)crop.Width, (int)crop.Height));
        cropped.Freeze();
        return cropped;
    }

    internal static Rect CalculateContainedRect(
        int imageWidth,
        int imageHeight,
        PixelRect64 destination)
    {
        double scale = Math.Min(
            destination.Width / (double)imageWidth,
            destination.Height / (double)imageHeight);
        double width = imageWidth * scale;
        double height = imageHeight * scale;
        double left = destination.X + (destination.Width - width) / 2.0;
        double top = destination.Y + (destination.Height - height) / 2.0;
        return new Rect(left, top, width, height);
    }

    internal static void DrawPanelLabel(
        DrawingContext drawing,
        string label,
        PixelRect64 destination,
        int dpi,
        FigureGlobalStyle? globalStyle = null)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return;
        }

        FigureGlobalStyle style = globalStyle ?? FigureGlobalStyle.Default;
        double fontSize = Math.Max(12, style.EffectivePanelLabelFontSizePt / 72.0 * dpi);
        var textBrush = new SolidColorBrush(ParseColor(style.EffectivePanelLabelTextColor));
        textBrush.Freeze();
        var text = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(style.EffectivePanelLabelFontFamily),
                FontStyles.Normal,
                style.PanelLabelIsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            textBrush,
            pixelsPerDip: 1.0);
        double padding = Math.Max(4, dpi / 75.0);
        var background = new Rect(
            destination.X + padding,
            destination.Y + padding,
            text.Width + padding * 2,
            text.Height + padding);
        drawing.DrawRectangle(Brushes.White, null, background);
        drawing.DrawText(text, new Point(background.X + padding, background.Y));
    }

    internal static void DrawInsetBorder(
        DrawingContext drawing,
        FigurePanelExportItem panel,
        Rect imageRect,
        int dpi,
        FigureGlobalStyle? globalStyle = null)
    {
        if (!panel.IsInset)
        {
            return;
        }

        FigureGlobalStyle style = globalStyle ?? FigureGlobalStyle.Default;
        var brush = new SolidColorBrush(ParseColor(style.ShapeColor));
        brush.Freeze();
        var pen = new Pen(brush, Math.Max(1, 0.5 / 72.0 * dpi));
        pen.Freeze();
        drawing.DrawRectangle(null, pen, imageRect);
    }

    internal static void DrawScaleBar(
        DrawingContext drawing,
        FigurePanelExportItem panel,
        Rect imageRect,
        int dpi,
        FigureGlobalStyle? globalStyle = null)
    {
        FigureScaleBarExportSpec? scaleBar = panel.ScaleBar;
        if (scaleBar is null)
        {
            return;
        }

        FigureGlobalStyle style = globalStyle ?? FigureGlobalStyle.Default;
        var scaleBarBrush = new SolidColorBrush(ParseColor(style.ScaleBarColor));
        scaleBarBrush.Freeze();
        double sourcePixels = scaleBar.PhysicalLength / scaleBar.PhysicalUnitsPerSourcePixel;
        double outputPixelsPerSourcePixel = imageRect.Width / panel.SourceRect.Width;
        double barWidth = sourcePixels * outputPixelsPerSourcePixel;
        double margin = Math.Max(10, Math.Min(imageRect.Width, imageRect.Height) * 0.035);
        double thickness = Math.Max(2, style.EffectiveScaleBarThicknessPt / 72.0 * dpi);
        double right = imageRect.Right - margin;
        double y = imageRect.Bottom - margin - thickness / 2.0;
        double left = right - barWidth;

        var outlinePen = new Pen(Brushes.Black, thickness + Math.Max(3, dpi / 100.0))
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
        };
        var linePen = new Pen(scaleBarBrush, thickness)
        {
            StartLineCap = PenLineCap.Square,
            EndLineCap = PenLineCap.Square,
        };
        Point start = new(left, y);
        Point end = new(right, y);
        drawing.DrawLine(outlinePen, start, end);
        drawing.DrawLine(linePen, start, end);

        if (!scaleBar.ShowLabel)
        {
            return;
        }

        var labelBrush = new SolidColorBrush(ParseColor(style.EffectiveScaleBarLabelColor));
        labelBrush.Freeze();
        double fontSize = Math.Max(12, style.EffectiveScaleBarFontSizePt / 72.0 * dpi);
        var text = new FormattedText(
            $"{scaleBar.PhysicalLength:0.###} {scaleBar.Unit}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(style.EffectiveScaleBarFontFamily),
                FontStyles.Normal,
                style.ScaleBarLabelIsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal),
            fontSize,
            labelBrush,
            pixelsPerDip: 1.0);
        double textX = right - text.Width;
        double textY = y - thickness - text.Height - Math.Max(3, dpi / 100.0);
        Geometry geometry = text.BuildGeometry(new Point(textX, textY));
        drawing.DrawGeometry(
            labelBrush,
            new Pen(Brushes.Black, Math.Max(2, dpi / 150.0)),
            geometry);
    }

    internal static void DrawAnnotation(
        DrawingContext drawing,
        FigureAnnotationExportItem annotation,
        int dpi,
        FigureGlobalStyle? globalStyle = null)
    {
        FigureGlobalStyle style = globalStyle ?? FigureGlobalStyle.Default;
        if (string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase))
        {
            var textBrush = new SolidColorBrush(ParseColor(annotation.TextColor));
            textBrush.Freeze();
            double fontSize = annotation.FontSizePt / 72.0 * dpi;
            string fontFamily = string.IsNullOrWhiteSpace(annotation.FontFamily)
                ? style.FontFamily
                : annotation.FontFamily;
            var text = new FormattedText(
                annotation.Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(
                    new FontFamily(fontFamily),
                    FontStyles.Normal,
                    annotation.IsBold ? FontWeights.Bold : FontWeights.Normal,
                    FontStretches.Normal),
                fontSize,
                textBrush,
                pixelsPerDip: 1.0);
            drawing.DrawText(text, new Point(annotation.X, annotation.Y));
            return;
        }

        var strokeBrush = new SolidColorBrush(ParseColor(annotation.StrokeColor));
        strokeBrush.Freeze();
        double strokeWidth = annotation.StrokeWidthPt / 72.0 * dpi;
        var pen = new Pen(strokeBrush, strokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        if (string.Equals(annotation.Kind, "line", StringComparison.OrdinalIgnoreCase))
        {
            drawing.DrawLine(
                pen,
                new Point(annotation.X, annotation.Y),
                new Point(annotation.EndX, annotation.EndY));
            return;
        }

        if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase))
        {
            Color fillColor = ParseColor(annotation.FillColor);
            fillColor.A = (byte)Math.Round(fillColor.A * annotation.FillOpacityPercent / 100.0);
            var fillBrush = new SolidColorBrush(fillColor);
            fillBrush.Freeze();
            var bounds = new Rect(
                annotation.X,
                annotation.Y,
                annotation.EndX - annotation.X,
                annotation.EndY - annotation.Y);
            if (string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase))
            {
                drawing.DrawRectangle(fillBrush, pen, bounds);
            }
            else
            {
                drawing.DrawEllipse(
                    brush: fillBrush,
                    pen: pen,
                    center: new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
                    radiusX: bounds.Width / 2,
                    radiusY: bounds.Height / 2);
            }

            return;
        }

        double dx = annotation.EndX - annotation.X;
        double dy = annotation.EndY - annotation.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Max(strokeWidth * 4, 10.0 / 72.0 * dpi);
        double halfWidth = headLength * 0.52;
        Point tip = new(annotation.EndX, annotation.EndY);
        Point baseCenter = new(
            annotation.EndX - unitX * headLength,
            annotation.EndY - unitY * headLength);
        Point left = new(
            baseCenter.X - unitY * halfWidth,
            baseCenter.Y + unitX * halfWidth);
        Point right = new(
            baseCenter.X + unitY * halfWidth,
            baseCenter.Y - unitX * halfWidth);

        drawing.DrawLine(pen, new Point(annotation.X, annotation.Y), baseCenter);
        var head = new StreamGeometry();
        using (StreamGeometryContext context = head.Open())
        {
            context.BeginFigure(tip, isFilled: true, isClosed: true);
            context.LineTo(left, isStroked: true, isSmoothJoin: false);
            context.LineTo(right, isStroked: true, isSmoothJoin: false);
        }

        head.Freeze();
        drawing.DrawGeometry(strokeBrush, null, head);
    }

    internal static void ValidatePanel(FigurePanelExportItem panel, FigureExportDocument document)
    {
        if (panel.DestinationRect.Right > document.WidthPixels ||
            panel.DestinationRect.Bottom > document.HeightPixels)
        {
            throw new InvalidOperationException($"面板 {panel.Label} 超出拼版画布边界。");
        }


        if (panel.ScaleBar is { } scaleBar &&
            (!double.IsFinite(scaleBar.PhysicalUnitsPerSourcePixel) ||
             scaleBar.PhysicalUnitsPerSourcePixel <= 0 ||
             !double.IsFinite(scaleBar.PhysicalLength) ||
             scaleBar.PhysicalLength <= 0 ||
             string.IsNullOrWhiteSpace(scaleBar.Unit) ||
             scaleBar.PhysicalLength / scaleBar.PhysicalUnitsPerSourcePixel > panel.SourceRect.Width * 0.8))
        {
            throw new InvalidOperationException($"面板 {panel.Label} 的比例尺校准参数无效。");
        }
    }

    internal static void ValidateAnnotation(
        FigureAnnotationExportItem annotation,
        FigureExportDocument document)
    {
        bool isText = string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase);
        bool isArrow = string.Equals(annotation.Kind, "arrow", StringComparison.OrdinalIgnoreCase);
        bool isLine = string.Equals(annotation.Kind, "line", StringComparison.OrdinalIgnoreCase);
        bool isRectangle = string.Equals(annotation.Kind, "rectangle", StringComparison.OrdinalIgnoreCase);
        bool isEllipse = string.Equals(annotation.Kind, "ellipse", StringComparison.OrdinalIgnoreCase);
        bool isShape = isRectangle || isEllipse;
        bool startInside = double.IsFinite(annotation.X) && double.IsFinite(annotation.Y) &&
                           annotation.X >= 0 && annotation.X <= document.WidthPixels &&
                           annotation.Y >= 0 && annotation.Y <= document.HeightPixels;
        bool endInside = double.IsFinite(annotation.EndX) && double.IsFinite(annotation.EndY) &&
                         annotation.EndX >= 0 && annotation.EndX <= document.WidthPixels &&
                         annotation.EndY >= 0 && annotation.EndY <= document.HeightPixels;
        if ((!isText && !isArrow && !isLine && !isShape) || !startInside ||
            ((isArrow || isLine || isShape) && !endInside) ||
            ((isArrow || isLine) && Math.Sqrt(
                Math.Pow(annotation.EndX - annotation.X, 2) +
                Math.Pow(annotation.EndY - annotation.Y, 2)) < 5) ||
            (isShape && (annotation.EndX - annotation.X < 5 ||
                         annotation.EndY - annotation.Y < 5)) ||
            (isText && string.IsNullOrWhiteSpace(annotation.Text)) ||
            string.IsNullOrWhiteSpace(annotation.FontFamily) || annotation.FontFamily.Length > 128 ||
            !double.IsFinite(annotation.FontSizePt) || annotation.FontSizePt is < 4 or > 72 ||
            !double.IsFinite(annotation.StrokeWidthPt) || annotation.StrokeWidthPt is < 0.25 or > 10 ||
            !double.IsFinite(annotation.FillOpacityPercent) || annotation.FillOpacityPercent is < 0 or > 100)
        {
            throw new InvalidOperationException("拼版包含无效的文字、直线、箭头或形状标注参数。");
        }

        _ = ParseColor(annotation.StrokeColor);
        _ = ParseColor(annotation.FillColor);
        _ = ParseColor(annotation.TextColor);
    }

    internal static Color ParseColor(string value)
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        if (hex.Length is not (6 or 8) ||
            !hex.All(character => Uri.IsHexDigit(character)))
        {
            throw new InvalidOperationException("标注颜色必须使用 #RRGGBB 或 #AARRGGBB。");
        }

        byte alpha = hex.Length == 8
            ? byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : byte.MaxValue;
        int offset = hex.Length == 8 ? 2 : 0;
        byte red = byte.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte green = byte.Parse(hex.AsSpan(offset + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        byte blue = byte.Parse(hex.AsSpan(offset + 4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromArgb(alpha, red, green, blue);
    }

    private static BitmapEncoder CreateEncoder(string extension) => extension.ToLowerInvariant() switch
    {
        ".tif" or ".tiff" => new TiffBitmapEncoder
        {
            Compression = TiffCompressOption.Zip,
        },
        ".png" => new PngBitmapEncoder(),
        ".bmp" => new BmpBitmapEncoder(),
        ".jpg" or ".jpeg" => new JpegBitmapEncoder
        {
            QualityLevel = 95,
        },
        _ => throw new NotSupportedException("拼版导出仅支持 TIFF、PNG、BMP 与 JPEG。"),
    };

    private static void TryDeleteIncompleteTarget(string targetPath)
    {
        try
        {
            File.Delete(targetPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
