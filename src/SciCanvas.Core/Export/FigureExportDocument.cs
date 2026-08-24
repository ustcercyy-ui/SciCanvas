using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Export;

public sealed record FigureScaleBarExportSpec(
    double PhysicalUnitsPerSourcePixel,
    double PhysicalLength,
    string Unit,
    bool ShowLabel);

public sealed record FigurePanelExportItem(
    SourceAsset Source,
    PixelRect64 SourceRect,
    PixelRect64 DestinationRect,
    string Label,
    bool IsVisible,
    FigureScaleBarExportSpec? ScaleBar = null,
    ImageAdjustmentParameters? Adjustments = null,
    int FrameIndex = 0,
    bool IsInset = false);

public sealed record FigureAnnotationExportItem(
    string Kind,
    double X,
    double Y,
    double EndX,
    double EndY,
    string Text,
    string Color,
    double FontSizePt,
    double StrokeWidthPt,
    bool IsBold,
    bool IsVisible,
    int ZIndex);

public sealed record FigureGlobalStyle(
    string FontFamily,
    double FontSizePt,
    double StrokeWidthPt,
    string TextColor,
    string ShapeColor,
    string ScaleBarColor)
{
    public static FigureGlobalStyle Default { get; } = new(
        "Arial",
        7,
        1.25,
        "#FF111111",
        "#FFE53935",
        "#FFFFFFFF");

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(FontFamily) && FontFamily.Length <= 128 &&
        double.IsFinite(FontSizePt) && FontSizePt is >= 4 and <= 72 &&
        double.IsFinite(StrokeWidthPt) && StrokeWidthPt is >= 0.25 and <= 10 &&
        IsColor(TextColor) && IsColor(ShapeColor) && IsColor(ScaleBarColor);

    public void EnsureValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("全局图样式无效：字体 4–72 pt、线宽 0.25–10 pt，颜色须为 #RRGGBB 或 #AARRGGBB。");
        }
    }

    private static bool IsColor(string? value)
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }
}

public sealed record FigureExportDocument
{
    public FigureExportDocument(
        int widthPixels,
        int heightPixels,
        int dpi,
        IReadOnlyList<FigurePanelExportItem> panels,
        IReadOnlyList<FigureAnnotationExportItem>? annotations = null,
        string backgroundColor = "#FFFFFFFF",
        int bitDepth = 8,
        FigureGlobalStyle? globalStyle = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ArgumentNullException.ThrowIfNull(panels);
        if (bitDepth is not (8 or 16))
        {
            throw new ArgumentOutOfRangeException(nameof(bitDepth), "导出位深只支持 8 或 16。");
        }

        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
        Dpi = dpi;
        Panels = panels;
        Annotations = annotations ?? [];
        BackgroundColor = backgroundColor;
        BitDepth = bitDepth;
        GlobalStyle = globalStyle ?? FigureGlobalStyle.Default;
        GlobalStyle.EnsureValid();
    }

    public int WidthPixels { get; }

    public int HeightPixels { get; }

    public int Dpi { get; }

    public IReadOnlyList<FigurePanelExportItem> Panels { get; }

    public IReadOnlyList<FigureAnnotationExportItem> Annotations { get; }

    public string BackgroundColor { get; }

    public int BitDepth { get; }

    public FigureGlobalStyle GlobalStyle { get; }
}
