using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;

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

                BitmapSource cropped = LoadPanelImage(panel, cancellationToken);
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

            foreach (FigureMeasurementOverlayExportItem overlay in
                     document.MeasurementOverlays.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                DrawMeasurementOverlay(drawing, overlay, document, document.Dpi);
            }

            foreach (FigureScientificObjectExportItem scientificObject in
                     document.ScientificObjects.OrderBy(item => item.ZIndex).Where(item => item.IsVisible))
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidateScientificObject(scientificObject, document);
                DrawScientificObject(drawing, scientificObject, document.Dpi);
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

    internal static BitmapSource LoadPanelImage(
        FigurePanelExportItem panel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(panel);
        BitmapSource source = panel.IsComposite
            ? WpfCompositePanelRenderer.Render(panel.EffectiveChannelLayers, cancellationToken)
            : LoadExactCrop(panel.Source.OriginalPath, panel.SourceRect, panel.FrameIndex);
        return WpfImageAdjustmentProcessor.Apply(source, panel.Adjustments);
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
        IReadOnlyList<FigureScaleBarExportSpec> scaleBars = panel.EffectiveScaleBars;
        if (scaleBars.Count == 0)
        {
            return;
        }

        FigureGlobalStyle style = globalStyle ?? FigureGlobalStyle.Default;
        var scaleBarBrush = new SolidColorBrush(ParseColor(style.ScaleBarColor));
        scaleBarBrush.Freeze();
        double thickness = Math.Max(2, style.EffectiveScaleBarThicknessPt / 72.0 * dpi);
        double fontSize = Math.Max(12, style.EffectiveScaleBarFontSizePt / 72.0 * dpi);
        IReadOnlyList<FigureScaleBarGeometry> geometries = FigureScaleBarLayout.Calculate(
            scaleBars,
            panel.SourceRect,
            new FigureImageRect(imageRect.X, imageRect.Y, imageRect.Width, imageRect.Height),
            dpi,
            thickness,
            fontSize);
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
        var labelBrush = new SolidColorBrush(ParseColor(style.EffectiveScaleBarLabelColor));
        labelBrush.Freeze();

        foreach (FigureScaleBarGeometry geometry in geometries)
        {
            Point start = new(geometry.Left, geometry.Y);
            Point end = new(geometry.Right, geometry.Y);
            drawing.DrawLine(outlinePen, start, end);
            drawing.DrawLine(linePen, start, end);
            if (!geometry.Spec.ShowLabel)
            {
                continue;
            }

            var text = new FormattedText(
                geometry.Spec.Label,
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
            double textX = geometry.Right - text.Width;
            Geometry labelGeometry = text.BuildGeometry(new Point(textX, geometry.LabelTop));
            drawing.DrawGeometry(
                labelBrush,
                new Pen(Brushes.Black, Math.Max(2, dpi / 150.0)),
                labelGeometry);
        }
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

    internal static void ValidateScientificObject(
        FigureScientificObjectExportItem scientificObject,
        FigureExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(scientificObject);
        ArgumentNullException.ThrowIfNull(document);
        scientificObject.EnsureValid(document.WidthPixels, document.HeightPixels);
    }

    internal static void DrawScientificObject(
        DrawingContext drawing,
        FigureScientificObjectExportItem scientificObject,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(scientificObject);
        Color stroke = ParseColor(scientificObject.StrokeColor);
        Color fill = ParseColor(scientificObject.FillColor);
        fill.A = (byte)Math.Round(fill.A * scientificObject.FillOpacityPercent / 100.0);
        var strokeBrush = new SolidColorBrush(stroke);
        strokeBrush.Freeze();
        var fillBrush = new SolidColorBrush(fill);
        fillBrush.Freeze();
        var textBrush = new SolidColorBrush(ParseColor(scientificObject.TextColor));
        textBrush.Freeze();
        double strokeWidth = scientificObject.StrokeWidthPt / 72.0 * dpi;
        var pen = new Pen(strokeBrush, strokeWidth)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };

        switch (scientificObject.Kind)
        {
            case FigureScientificObjectKind.PolygonAnnotation:
            case FigureScientificObjectKind.Roi:
                var polygon = new StreamGeometry();
                using (StreamGeometryContext context = polygon.Open())
                {
                    context.BeginFigure(ToPoint(scientificObject.Points[0]), true, true);
                    context.PolyLineTo(scientificObject.Points.Skip(1).Select(ToPoint).ToArray(), true, true);
                }
                polygon.Freeze();
                drawing.DrawGeometry(fillBrush, pen, polygon);
                DrawScientificObjectText(drawing, scientificObject.Label, scientificObject.Points[0], textBrush, scientificObject, dpi);
                break;
            case FigureScientificObjectKind.DirectionMarker:
                DrawDirectionMarker(drawing, scientificObject, pen, strokeBrush, textBrush, dpi);
                break;
            case FigureScientificObjectKind.Colorbar:
                DrawColorbar(drawing, scientificObject, pen, textBrush, dpi);
                break;
            case FigureScientificObjectKind.ChannelLegend:
                DrawChannelLegend(drawing, scientificObject, pen, textBrush, dpi);
                break;
            default:
                throw new InvalidOperationException("不支持的科研对象类型。");
        }
    }

    private static void DrawDirectionMarker(
        DrawingContext drawing,
        FigureScientificObjectExportItem scientificObject,
        Pen pen,
        Brush strokeBrush,
        Brush textBrush,
        int dpi)
    {
        Point start = ToPoint(scientificObject.Points[0]);
        Point end = ToPoint(scientificObject.Points[1]);
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Max(pen.Thickness * 4, 10.0 / 72.0 * dpi);
        double halfWidth = headLength * 0.52;
        Point baseCenter = new(end.X - unitX * headLength, end.Y - unitY * headLength);
        Point left = new(baseCenter.X - unitY * halfWidth, baseCenter.Y + unitX * halfWidth);
        Point right = new(baseCenter.X + unitY * halfWidth, baseCenter.Y - unitX * halfWidth);
        drawing.DrawLine(pen, start, baseCenter);
        var head = new StreamGeometry();
        using (StreamGeometryContext context = head.Open())
        {
            context.BeginFigure(end, true, true);
            context.LineTo(left, true, false);
            context.LineTo(right, true, false);
        }
        head.Freeze();
        drawing.DrawGeometry(strokeBrush, null, head);
        DrawScientificObjectText(drawing, scientificObject.Label, scientificObject.Points[1], textBrush, scientificObject, dpi, 5, -22);
    }

    private static void DrawColorbar(
        DrawingContext drawing,
        FigureScientificObjectExportItem scientificObject,
        Pen pen,
        Brush textBrush,
        int dpi)
    {
        Rect bounds = CreateBounds(scientificObject.Points);
        drawing.DrawRectangle(CreateColormapBrush(scientificObject.Colormap), pen, bounds);
        DrawScientificObjectText(drawing, $"{scientificObject.Maximum:0.###} {scientificObject.Unit}",
            new FigureScientificPoint(bounds.Right + 5, bounds.Top), textBrush, scientificObject, dpi);
        DrawScientificObjectText(drawing, $"{scientificObject.Minimum:0.###} {scientificObject.Unit}",
            new FigureScientificPoint(bounds.Right + 5, bounds.Bottom - scientificObject.FontSizePt / 72.0 * dpi), textBrush, scientificObject, dpi);
        if (!string.IsNullOrWhiteSpace(scientificObject.Label))
        {
            DrawScientificObjectText(drawing, scientificObject.Label,
                new FigureScientificPoint(bounds.X, Math.Max(0, bounds.Y - scientificObject.FontSizePt / 72.0 * dpi - 4)), textBrush, scientificObject, dpi);
        }
    }

    private static void DrawChannelLegend(
        DrawingContext drawing,
        FigureScientificObjectExportItem scientificObject,
        Pen pen,
        Brush textBrush,
        int dpi)
    {
        Rect bounds = CreateBounds(scientificObject.Points);
        drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(185, 12, 18, 25)), pen, bounds);
        double fontSize = scientificObject.FontSizePt / 72.0 * dpi;
        int count = scientificObject.EffectiveChannelLegendEntries.Count;
        double rowHeight = bounds.Height / Math.Max(1, count);
        for (int index = 0; index < count; index++)
        {
            FigureChannelLegendEntry entry = scientificObject.EffectiveChannelLegendEntries[index];
            var swatch = new SolidColorBrush(ParseColor(entry.Color));
            swatch.Freeze();
            double y = bounds.Y + index * rowHeight;
            drawing.DrawRectangle(swatch, null, new Rect(bounds.X + 5, y + Math.Max(2, (rowHeight - fontSize) / 2), Math.Min(16, bounds.Width * 0.18), Math.Max(4, fontSize)));
            DrawScientificObjectText(drawing, entry.Label,
                new FigureScientificPoint(bounds.X + Math.Min(24, bounds.Width * 0.25), y + Math.Max(0, (rowHeight - fontSize) / 2)), textBrush, scientificObject, dpi);
        }
    }

    private static void DrawScientificObjectText(
        DrawingContext drawing,
        string text,
        FigureScientificPoint point,
        Brush brush,
        FigureScientificObjectExportItem scientificObject,
        int dpi,
        double offsetX = 0,
        double offsetY = 0)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }
        var formatted = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(scientificObject.FontFamily), FontStyles.Normal,
                scientificObject.IsBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            scientificObject.FontSizePt / 72.0 * dpi,
            brush,
            pixelsPerDip: 1.0);
        drawing.DrawText(formatted, new Point(point.X + offsetX, point.Y + offsetY));
    }

    private static Rect CreateBounds(IReadOnlyList<FigureScientificPoint> points) => new(
        Math.Min(points[0].X, points[1].X),
        Math.Min(points[0].Y, points[1].Y),
        Math.Abs(points[1].X - points[0].X),
        Math.Abs(points[1].Y - points[0].Y));

    private static Point ToPoint(FigureScientificPoint point) => new(point.X, point.Y);

    internal static IReadOnlyList<Color> GetColormapColors(string colormap) => colormap.ToLowerInvariant() switch
    {
        "magma" => [Color.FromRgb(0, 0, 4), Color.FromRgb(115, 20, 117), Color.FromRgb(252, 136, 97), Color.FromRgb(252, 253, 191)],
        "grayscale" => [Colors.Black, Colors.White],
        _ => [Color.FromRgb(68, 1, 84), Color.FromRgb(59, 82, 139), Color.FromRgb(33, 145, 140), Color.FromRgb(94, 201, 98), Color.FromRgb(253, 231, 37)],
    };
    internal static LinearGradientBrush CreateColormapBrush(string colormap)
    {
        IReadOnlyList<Color> colors = GetColormapColors(colormap);

        var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
        for (int index = 0; index < colors.Count; index++)
        {
            brush.GradientStops.Add(new GradientStop(colors[index], index / (double)Math.Max(1, colors.Count - 1)));
        }
        brush.Freeze();
        return brush;
    }
    internal static void DrawMeasurementOverlay(
        DrawingContext drawing,
        FigureMeasurementOverlayExportItem overlay,
        FigureExportDocument document,
        int dpi)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(document);
        FigurePanelExportItem panel = ResolveMeasurementOverlayPanel(overlay, document);
        FigureMeasurementOverlayGeometry geometry = FigureMeasurementOverlayMapper.Map(
            overlay.ScientificObject,
            panel);
        FigureMeasurementOverlayStyle style = overlay.Style;
        var strokeBrush = new SolidColorBrush(ParseColor(style.StrokeColor));
        strokeBrush.Freeze();
        var pen = new Pen(strokeBrush, Math.Max(0.25, geometry.StrokeWidthPixels))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
            DashStyle = CreateDashStyle(style.LineStyle),
        };
        pen.Freeze();

        switch (overlay.MeasurementKind)
        {
            case ScientificMeasurementKind.Length:
                drawing.DrawLine(pen, ToPoint(geometry.PointA), ToPoint(geometry.PointB));
                break;
            case ScientificMeasurementKind.Angle:
                drawing.DrawLine(pen, ToPoint(geometry.PointA), ToPoint(geometry.PointB));
                drawing.DrawLine(pen, ToPoint(geometry.PointB), ToPoint(geometry.PointC ?? geometry.PointB));
                break;
            case ScientificMeasurementKind.RectangleRoi:
                drawing.DrawRectangle(
                    CreateMeasurementFill(style),
                    pen,
                    CreateBounds(geometry.PointA, geometry.PointB));
                break;
            case ScientificMeasurementKind.CircleRoi:
                Rect bounds = CreateBounds(geometry.PointA, geometry.PointB);
                drawing.DrawEllipse(
                    CreateMeasurementFill(style),
                    pen,
                    new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2),
                    bounds.Width / 2,
                    bounds.Height / 2);
                break;
            case ScientificMeasurementKind.Polyline:
                DrawPolyline(drawing, pen, geometry.PathPoints);
                break;
            default:
                throw new InvalidOperationException("不支持的 Measurement Overlay 类型。");
        }

        if (style.ShowMarkers)
        {
            DrawMeasurementMarkers(drawing, overlay.MeasurementKind, geometry, style);
        }

        if (style.ShowLabel)
        {
            DrawMeasurementLabel(drawing, overlay, geometry, dpi);
        }
    }

    internal static void ValidateMeasurementOverlay(
        FigureMeasurementOverlayExportItem overlay,
        FigureExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        ArgumentNullException.ThrowIfNull(document);
        _ = FigureMeasurementOverlayMapper.Map(
            overlay.ScientificObject,
            ResolveMeasurementOverlayPanel(overlay, document));
    }

    internal static FigurePanelExportItem ResolveMeasurementOverlayPanel(
        FigureMeasurementOverlayExportItem overlay,
        FigureExportDocument document)
    {
        FigurePanelExportItem[] matches = document.Panels
            .Where(panel => panel.IsVisible && panel.PanelId == overlay.PanelId)
            .ToArray();
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Measurement Overlay 必须绑定到一个可见的 Figure Panel。");
        }

        FigureMeasurementOverlayMapper.ValidateRelationship(overlay.ScientificObject, matches[0]);
        return matches[0];
    }

    private static void DrawPolyline(
        DrawingContext drawing,
        Pen pen,
        IReadOnlyList<MeasurementPoint> points)
    {
        if (points.Count < 2)
        {
            throw new InvalidOperationException("Polyline Measurement Overlay 至少需要两个点。");
        }

        var path = new StreamGeometry();
        using (StreamGeometryContext context = path.Open())
        {
            context.BeginFigure(ToPoint(points[0]), isFilled: false, isClosed: false);
            context.PolyLineTo(points.Skip(1).Select(ToPoint).ToArray(), isStroked: true, isSmoothJoin: true);
        }

        path.Freeze();
        drawing.DrawGeometry(null, pen, path);
    }

    private static void DrawMeasurementMarkers(
        DrawingContext drawing,
        ScientificMeasurementKind kind,
        FigureMeasurementOverlayGeometry geometry,
        FigureMeasurementOverlayStyle style)
    {
        var fill = new SolidColorBrush(ParseColor(style.MarkerFillColor));
        fill.Freeze();
        var stroke = new SolidColorBrush(ParseColor(style.MarkerStrokeColor));
        stroke.Freeze();
        var pen = new Pen(stroke, Math.Max(0.25, geometry.StrokeWidthPixels * 0.75));
        pen.Freeze();
        double radius = Math.Max(1, geometry.MarkerSizePixels / 2);
        IReadOnlyList<MeasurementPoint> points = kind switch
        {
            ScientificMeasurementKind.Angle => geometry.PointC is MeasurementPoint pointC
                ? [geometry.PointA, geometry.PointB, pointC]
                : [geometry.PointA, geometry.PointB],
            ScientificMeasurementKind.Polyline => geometry.PathPoints,
            _ => [geometry.PointA, geometry.PointB],
        };
        foreach (MeasurementPoint point in points)
        {
            drawing.DrawEllipse(fill, pen, ToPoint(point), radius, radius);
        }
    }

    private static void DrawMeasurementLabel(
        DrawingContext drawing,
        FigureMeasurementOverlayExportItem overlay,
        FigureMeasurementOverlayGeometry geometry,
        int dpi)
    {
        FigureMeasurementOverlayStyle style = overlay.Style;
        var brush = new SolidColorBrush(ParseColor(style.LabelColor));
        brush.Freeze();
        var text = new FormattedText(
            FigureMeasurementOverlayMapper.CreateLabel(overlay.ScientificObject),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(style.LabelFontFamily),
                FontStyles.Normal,
                style.LabelIsBold ? FontWeights.Bold : FontWeights.Normal,
                FontStretches.Normal),
            style.LabelFontSizePt / 72.0 * dpi,
            brush,
            pixelsPerDip: 1.0);
        drawing.DrawText(text, ToPoint(geometry.LabelAnchor));
    }

    private static Brush CreateMeasurementFill(FigureMeasurementOverlayStyle style)
    {
        Color color = ParseColor(style.FillColor);
        color.A = (byte)Math.Round(color.A * style.FillOpacityPercent / 100.0);
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static DashStyle CreateDashStyle(string lineStyle) => lineStyle switch
    {
        "dash" => DashStyles.Dash,
        "dot" => DashStyles.Dot,
        "dash-dot" => DashStyles.DashDot,
        _ => DashStyles.Solid,
    };

    private static Point ToPoint(MeasurementPoint point) => new(point.X, point.Y);

    private static Rect CreateBounds(MeasurementPoint first, MeasurementPoint second) => new(
        Math.Min(first.X, second.X),
        Math.Min(first.Y, second.Y),
        Math.Abs(second.X - first.X),
        Math.Abs(second.Y - first.Y));
    internal static void ValidatePanel(FigurePanelExportItem panel, FigureExportDocument document)
    {
        if (panel.DestinationRect.Right > document.WidthPixels ||
            panel.DestinationRect.Bottom > document.HeightPixels)
        {
            throw new InvalidOperationException($"面板 {panel.Label} 超出拼版画布边界。");
        }


        if (panel.IsComposite)
        {
            foreach (FigureChannelLayerExportItem layer in panel.EffectiveChannelLayers)
            {
                layer.EnsureValid();
            }

            if (panel.EffectiveChannelLayers.Select(layer => layer.GroupId).Distinct().Count() != 1 ||
                panel.EffectiveChannelLayers.Select(layer => layer.SourceRect.Width).Distinct().Count() != 1 ||
                panel.EffectiveChannelLayers.Select(layer => layer.SourceRect.Height).Distinct().Count() != 1)
            {
                throw new InvalidOperationException(
                    $"复合面板 {panel.Label} 的通道层必须来自同一组且具有相同裁剪尺寸。");
            }
        }

        foreach (FigureScaleBarExportSpec scaleBar in panel.EffectiveScaleBars)
        {
            try
            {
                scaleBar.EnsureValid(panel.SourceRect);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                throw new InvalidOperationException($"面板 {panel.Label} 的比例尺参数无效。", exception);
            }
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
