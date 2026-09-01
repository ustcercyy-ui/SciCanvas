using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Export;

/// <summary>
/// A named, repeatable output variant for a figure. Profiles never mutate the
/// editor document; they create a scaled export snapshot instead.
/// </summary>
public sealed record FigureExportProfile
{
    public FigureExportProfile(
        string id,
        string name,
        string format,
        int dpi,
        double scale = 1,
        int? widthPixels = null,
        int? heightPixels = null,
        bool writeProvenance = true,
        int bitDepth = 8,
        PdfFontStrategy pdfFontStrategy = PdfFontStrategy.OutlineText)
    {
        Id = NormalizeRequired(id, nameof(id));
        Name = NormalizeRequired(name, nameof(name));
        Format = NormalizeFormat(format);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        if (bitDepth is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(bitDepth), "输出位深只支持 8 或 16。");
        }
        if (bitDepth == 16 && Format != "tiff")
        {
            throw new ArgumentException("16-bit 输出当前仅支持 TIFF。", nameof(bitDepth));
        }
        if (!double.IsFinite(scale) || scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), "输出比例必须是有限的正数。");
        }

        if (widthPixels is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPixels));
        }

        if (heightPixels is <= 0 or > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPixels));
        }

        Dpi = dpi;
        Scale = scale;
        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
        if (!Enum.IsDefined(pdfFontStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(pdfFontStrategy));
        }

        WriteProvenance = writeProvenance;
        BitDepth = bitDepth;
        PdfFontStrategy = pdfFontStrategy;
    }

    public string Id { get; }

    public string Name { get; }

    public string Format { get; }

    public int Dpi { get; }

    public double Scale { get; }

    public int? WidthPixels { get; }

    public int? HeightPixels { get; }

    public bool WriteProvenance { get; }

    public int BitDepth { get; }

    public PdfFontStrategy PdfFontStrategy { get; }

    public string Extension => $".{Format}";

    public FigureExportDocument Apply(FigureExportDocument source)
    {
        ArgumentNullException.ThrowIfNull(source);
        (double scaleX, double scaleY) = ResolveScale(source);
        int width = ToPixelDimension(source.WidthPixels * scaleX);
        int height = ToPixelDimension(source.HeightPixels * scaleY);

        FigurePanelExportItem[] panels = source.Panels
            .Select(panel => panel with
            {
                DestinationRect = ScaleRect(panel.DestinationRect, scaleX, scaleY),
            })
            .ToArray();
        FigureAnnotationExportItem[] annotations = source.Annotations
            .Select(annotation => annotation with
            {
                X = annotation.X * scaleX,
                Y = annotation.Y * scaleY,
                EndX = annotation.EndX * scaleX,
                EndY = annotation.EndY * scaleY,
            })
            .ToArray();
        FigurePlotPanelExportItem[] plotPanels = source.PlotPanels
            .Select(panel => panel with
            {
                DestinationRect = ScaleRect(panel.DestinationRect, scaleX, scaleY),
            })
            .ToArray();

        return new FigureExportDocument(
            width,
            height,
            Dpi,
            panels,
            annotations,
            source.BackgroundColor,
            BitDepth,
            source.GlobalStyle,
            source.MeasurementOverlays,
            source.ScientificObjects,
            source.RoiProjections,
            PdfFontStrategy,
            plotPanels);
    }

    public static IReadOnlyList<FigureExportProfile> BuiltIns { get; } =
    [
        new(
            "main-tiff",
            "主图 · 无损 TIFF",
            "tiff",
            dpi: 300,
            bitDepth: 16),
        new(
            "supplement-png",
            "补充图 · PNG",
            "png",
            dpi: 300),
        new(
            "thumbnail-png",
            "缩略图 · PNG",
            "png",
            dpi: 150,
            widthPixels: 1200),
    ];

    private (double ScaleX, double ScaleY) ResolveScale(FigureExportDocument source)
    {
        double scaleX = WidthPixels is int width
            ? width / (double)source.WidthPixels
            : Scale;
        double scaleY = HeightPixels is int height
            ? height / (double)source.HeightPixels
            : Scale;

        // A single target dimension preserves the original aspect ratio.
        if (WidthPixels is not null && HeightPixels is null)
        {
            scaleY = scaleX;
        }
        else if (WidthPixels is null && HeightPixels is not null)
        {
            scaleX = scaleY;
        }

        return (scaleX, scaleY);
    }

    private static PixelRect64 ScaleRect(PixelRect64 rect, double scaleX, double scaleY) =>
        new(
            ToPixelCoordinate(rect.X * scaleX),
            ToPixelCoordinate(rect.Y * scaleY),
            ToPixelDimension(rect.Width * scaleX),
            ToPixelDimension(rect.Height * scaleY));

    private static int ToPixelDimension(double value)
    {
        if (!double.IsFinite(value) || value <= 0 || value > int.MaxValue)
        {
            throw new InvalidOperationException("导出预设计算出的画布尺寸超出支持范围。");
        }

        return Math.Max(1, (int)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static long ToPixelCoordinate(double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > long.MaxValue)
        {
            throw new InvalidOperationException("导出预设计算出的面板坐标超出支持范围。");
        }

        return Math.Max(0, (long)Math.Round(value, MidpointRounding.AwayFromZero));
    }

    private static string NormalizeFormat(string value)
    {
        string format = NormalizeRequired(value, nameof(value)).TrimStart('.').ToLowerInvariant();
        return format switch
        {
            "png" or "tiff" or "tif" or "jpg" or "jpeg" or "svg" or "pdf" =>
                format == "tif" ? "tiff" : format == "jpeg" ? "jpg" : format,
            _ => throw new ArgumentException($"不支持的图组导出格式：{value}。", nameof(value)),
        };
    }

    private static string NormalizeRequired(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", parameterName)
            : value.Trim();
}
