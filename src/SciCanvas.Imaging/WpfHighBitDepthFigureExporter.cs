using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Images;

namespace SciCanvas.Imaging;

/// <summary>
/// Composes figure image planes in a 16-bit RGB buffer. Vector-like labels,
/// scale bars and annotations are rendered separately and alpha-composited
/// without quantizing the underlying scientific image pixels to 8-bit.
/// </summary>
internal static class WpfHighBitDepthFigureExporter
{
    public static void Export(
        FigureExportDocument document,
        string targetPath,
        CancellationToken cancellationToken)
    {
        if (document.BitDepth != 16)
        {
            throw new ArgumentException("高位深导出器只接受 16-bit 文档。", nameof(document));
        }

        long sampleCount = checked((long)document.WidthPixels * document.HeightPixels * 3);
        if (sampleCount > 400_000_000)
        {
            throw new NotSupportedException("16-bit 拼版超过安全内存上限，请降低导出尺寸或拆分图组。");
        }

        ushort[] canvas = new ushort[(int)sampleCount];
        Color background = WpfFigureExporter.ParseColor(document.BackgroundColor);
        if (background.A != byte.MaxValue)
        {
            throw new InvalidOperationException("16-bit RGB TIFF 不支持透明画布；请先选择不透明背景，避免静默扁平化。");
        }
        FillBackground(canvas, background);

        foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WpfFigureExporter.ValidatePanel(panel, document);
            CompositePanel(document, panel, canvas);
        }

        CompositeOverlay(document, canvas, cancellationToken);

        int stride = checked(document.WidthPixels * 6);
        BitmapSource bitmap = BitmapSource.Create(
            document.WidthPixels,
            document.HeightPixels,
            document.Dpi,
            document.Dpi,
            PixelFormats.Rgb48,
            palette: null,
            canvas,
            stride);
        bitmap.Freeze();

        var encoder = new TiffBitmapEncoder
        {
            Compression = TiffCompressOption.Zip,
        };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        SaveNewFile(encoder, targetPath);
    }

    private static void FillBackground(ushort[] canvas, Color background)
    {
        ushort red = Expand(background.R);
        ushort green = Expand(background.G);
        ushort blue = Expand(background.B);
        for (int index = 0; index < canvas.Length; index += 3)
        {
            canvas[index] = red;
            canvas[index + 1] = green;
            canvas[index + 2] = blue;
        }
    }

    private static void CompositePanel(
        FigureExportDocument document,
        FigurePanelExportItem panel,
        ushort[] canvas)
    {
        BitmapSource crop = WpfFigureExporter.LoadExactCrop(
            panel.Source.OriginalPath,
            panel.SourceRect,
            panel.FrameIndex);
        var converted = new FormatConvertedBitmap(crop, PixelFormats.Rgb48, null, 0);
        converted.Freeze();
        int sourceStride = checked(converted.PixelWidth * 6);
        ushort[] source = new ushort[checked(converted.PixelWidth * converted.PixelHeight * 3)];
        converted.CopyPixels(source, sourceStride, 0);

        Rect contained = WpfFigureExporter.CalculateContainedRect(
            converted.PixelWidth,
            converted.PixelHeight,
            panel.DestinationRect);
        int left = Math.Max(0, (int)Math.Ceiling(contained.Left));
        int top = Math.Max(0, (int)Math.Ceiling(contained.Top));
        int right = Math.Min(document.WidthPixels, (int)Math.Floor(contained.Right));
        int bottom = Math.Min(document.HeightPixels, (int)Math.Floor(contained.Bottom));
        ImageAdjustmentParameters adjustment = (panel.Adjustments ?? new()).Normalize();
        if (!adjustment.IsValid)
        {
            throw new InvalidOperationException(adjustment.ValidationMessage);
        }

        if (adjustment.Channel == "alpha")
        {
            throw new InvalidOperationException("16-bit RGB TIFF cannot represent the selected alpha-channel view.");
        }

        for (int y = top; y < bottom; y++)
        {
            double sourceY = ((y + 0.5 - contained.Top) / contained.Height) * converted.PixelHeight - 0.5;
            int y0 = Math.Clamp((int)Math.Floor(sourceY), 0, converted.PixelHeight - 1);
            int y1 = Math.Min(y0 + 1, converted.PixelHeight - 1);
            double fy = Math.Clamp(sourceY - Math.Floor(sourceY), 0, 1);

            for (int x = left; x < right; x++)
            {
                double sourceX = ((x + 0.5 - contained.Left) / contained.Width) * converted.PixelWidth - 0.5;
                int x0 = Math.Clamp((int)Math.Floor(sourceX), 0, converted.PixelWidth - 1);
                int x1 = Math.Min(x0 + 1, converted.PixelWidth - 1);
                double fx = Math.Clamp(sourceX - Math.Floor(sourceX), 0, 1);
                int targetIndex = (y * document.WidthPixels + x) * 3;

                double red = Sample(source, converted.PixelWidth, x0, y0, x1, y1, fx, fy, 0);
                double green = Sample(source, converted.PixelWidth, x0, y0, x1, y1, fx, fy, 1);
                double blue = Sample(source, converted.PixelWidth, x0, y0, x1, y1, fx, fy, 2);
                ApplyAdjustments(ref red, ref green, ref blue, adjustment);
                canvas[targetIndex] = ToUShort(red);
                canvas[targetIndex + 1] = ToUShort(green);
                canvas[targetIndex + 2] = ToUShort(blue);
            }
        }
    }

    private static double Sample(
        ushort[] source,
        int width,
        int x0,
        int y0,
        int x1,
        int y1,
        double fx,
        double fy,
        int channel)
    {
        double top = Lerp(source[(y0 * width + x0) * 3 + channel], source[(y0 * width + x1) * 3 + channel], fx);
        double bottom = Lerp(source[(y1 * width + x0) * 3 + channel], source[(y1 * width + x1) * 3 + channel], fx);
        return Lerp(top, bottom, fy) / ushort.MaxValue;
    }

    private static double Lerp(double left, double right, double amount) =>
        left + (right - left) * amount;

    private static void ApplyAdjustments(
        ref double red,
        ref double green,
        ref double blue,
        ImageAdjustmentParameters adjustment)
    {
        if (adjustment.Channel is "red" or "green" or "blue")
        {
            (red, green, blue) = adjustment.Channel switch
            {
                "red" => (red, 0, 0),
                "green" => (0, green, 0),
                "blue" => (0, 0, blue),
                _ => (red, green, blue),
            };
        }

        red = Transform(red, adjustment);
        green = Transform(green, adjustment);
        blue = Transform(blue, adjustment);
        if (adjustment.Grayscale)
        {
            double gray = red * 0.2126 + green * 0.7152 + blue * 0.0722;
            red = green = blue = gray;
        }

        if (adjustment.Invert)
        {
            red = 1 - red;
            green = 1 - green;
            blue = 1 - blue;
        }

        if (adjustment.Channel == "alpha")
        {
            red = green = blue = 1;
        }
    }

    private static double Transform(double value, ImageAdjustmentParameters adjustment)
    {
        double normalized = (value - adjustment.BlackPoint) /
                            Math.Max(0.0001, adjustment.WhitePoint - adjustment.BlackPoint);
        normalized = Math.Clamp(normalized, 0, 1);
        normalized = (normalized - 0.5) * (1 + adjustment.Contrast) + 0.5 + adjustment.Brightness;
        normalized = Math.Clamp(normalized, 0, 1);
        return Math.Pow(normalized, 1 / adjustment.Gamma);
    }

    private static void CompositeOverlay(
        FigureExportDocument document,
        ushort[] canvas,
        CancellationToken cancellationToken)
    {
        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            double unitsPerPixel = 96.0 / document.Dpi;
            drawing.PushTransform(new ScaleTransform(unitsPerPixel, unitsPerPixel));
            foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Rect imageRect = WpfFigureExporter.CalculateContainedRect(
                    (int)panel.SourceRect.Width,
                    (int)panel.SourceRect.Height,
                    panel.DestinationRect);
                FigureGlobalStyle panelStyle = document.GlobalStyle.ResolvePanelOverride(panel.StyleOverride);
                WpfFigureExporter.DrawInsetBorder(drawing, panel, imageRect, document.Dpi, panelStyle);
                WpfFigureExporter.DrawScaleBar(drawing, panel, imageRect, document.Dpi, panelStyle);
                WpfFigureExporter.DrawPanelLabel(drawing, panel.Label, panel.DestinationRect, document.Dpi, panelStyle);
            }

            foreach (FigureAnnotationExportItem annotation in
                     document.Annotations.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
            {
                WpfFigureExporter.ValidateAnnotation(annotation, document);
                WpfFigureExporter.DrawAnnotation(drawing, annotation, document.Dpi, document.GlobalStyle);
            }

            foreach (FigureMeasurementOverlayExportItem measurementOverlay in
                     document.MeasurementOverlays.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                WpfFigureExporter.DrawMeasurementOverlay(drawing, measurementOverlay, document, document.Dpi);
            }

            foreach (FigureScientificObjectExportItem scientificObject in
                     document.ScientificObjects.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                WpfFigureExporter.ValidateScientificObject(scientificObject, document);
                WpfFigureExporter.DrawScientificObject(drawing, scientificObject, document.Dpi);
            }
            drawing.Pop();
        }

        var overlay = new RenderTargetBitmap(
            document.WidthPixels,
            document.HeightPixels,
            document.Dpi,
            document.Dpi,
            PixelFormats.Pbgra32);
        overlay.Render(visual);
        int stride = checked(document.WidthPixels * 4);
        byte[] pixels = new byte[checked(stride * document.HeightPixels)];
        overlay.CopyPixels(pixels, stride, 0);

        for (int pixel = 0, target = 0; pixel < pixels.Length; pixel += 4, target += 3)
        {
            int alpha = pixels[pixel + 3];
            if (alpha == 0)
            {
                continue;
            }

            int inverse = 255 - alpha;
            canvas[target] = CompositePremultiplied(canvas[target], pixels[pixel + 2], inverse);
            canvas[target + 1] = CompositePremultiplied(canvas[target + 1], pixels[pixel + 1], inverse);
            canvas[target + 2] = CompositePremultiplied(canvas[target + 2], pixels[pixel], inverse);
        }
    }

    private static ushort CompositePremultiplied(ushort destination, byte source, int inverseAlpha) =>
        (ushort)Math.Clamp(source * 257 + destination * inverseAlpha / 255, 0, ushort.MaxValue);

    private static ushort Expand(byte value) => (ushort)(value * 257);

    private static ushort ToUShort(double value) =>
        (ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue);

    private static void SaveNewFile(BitmapEncoder encoder, string targetPath)
    {
        bool created = false;
        try
        {
            using var output = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: false);
            created = true;
            encoder.Save(output);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (created)
            {
                try { File.Delete(targetPath); } catch { }
            }

            throw;
        }
    }
}
