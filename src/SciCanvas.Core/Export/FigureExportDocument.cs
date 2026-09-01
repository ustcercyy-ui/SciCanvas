using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

public sealed record FigureScaleBarExportSpec(
    double PhysicalUnitsPerSourcePixel,
    double PhysicalLength,
    string Unit,
    bool ShowLabel,
    string? CalibrationUnit = null,
    ScaleBarAnchor Anchor = ScaleBarAnchor.BottomRight,
    Guid Id = default)
{
    public string EffectiveCalibrationUnit => string.IsNullOrWhiteSpace(CalibrationUnit)
        ? Unit
        : CalibrationUnit;

    public ScientificLength DisplayLength => new(PhysicalLength, Unit);

    public ScientificScaleCalibration Calibration => new(
        PhysicalUnitsPerSourcePixel,
        EffectiveCalibrationUnit);

    public double SourcePixelLength => Calibration.SourcePixelsFor(DisplayLength);

    public string Label => DisplayLength.DisplayText;

    public void EnsureValid(PixelRect64 sourceRect, double maximumWidthFraction = 0.8)
    {
        Calibration.EnsureValid();
        DisplayLength.EnsureValid();
        if (!Enum.IsDefined(Anchor) || !double.IsFinite(SourcePixelLength) ||
            SourcePixelLength > sourceRect.Width * maximumWidthFraction)
        {
            throw new InvalidOperationException("比例尺长度或位置无效。");
        }
    }
}

public sealed record FigurePanelExportItem(
    SourceAsset Source,
    PixelRect64 SourceRect,
    PixelRect64 DestinationRect,
    string Label,
    bool IsVisible,
    FigureScaleBarExportSpec? ScaleBar = null,
    ImageAdjustmentParameters? Adjustments = null,
    int FrameIndex = 0,
    bool IsInset = false,
    StyleOverride? StyleOverride = null,
    Guid PanelId = default,
    IReadOnlyList<FigureScaleBarExportSpec>? ScaleBars = null,
    IReadOnlyList<FigureChannelLayerExportItem>? ChannelLayers = null,
    long SourceRevision = 1)
{
    public IReadOnlyList<FigureScaleBarExportSpec> EffectiveScaleBars => ScaleBars is { Count: > 0 }
        ? ScaleBars
        : ScaleBar is null ? [] : [ScaleBar];

    public IReadOnlyList<FigureChannelLayerExportItem> EffectiveChannelLayers => ChannelLayers ?? [];

    public bool IsComposite => EffectiveChannelLayers.Count > 0;
}

public sealed record FigureAnnotationExportItem
{
    public FigureAnnotationExportItem(
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
        int ZIndex)
        : this(
            Kind,
            X,
            Y,
            EndX,
            EndY,
            Text,
            Color,
            Color,
            0,
            Color,
            "Arial",
            FontSizePt,
            StrokeWidthPt,
            IsBold,
            IsVisible,
            ZIndex)
    {
    }

    public FigureAnnotationExportItem(
        string kind,
        double x,
        double y,
        double endX,
        double endY,
        string text,
        string strokeColor,
        string fillColor,
        double fillOpacityPercent,
        string textColor,
        string fontFamily,
        double fontSizePt,
        double strokeWidthPt,
        bool isBold,
        bool isVisible,
        int zIndex)
    {
        Kind = kind;
        X = x;
        Y = y;
        EndX = endX;
        EndY = endY;
        Text = text;
        StrokeColor = strokeColor;
        FillColor = fillColor;
        FillOpacityPercent = fillOpacityPercent;
        TextColor = textColor;
        FontFamily = fontFamily;
        FontSizePt = fontSizePt;
        StrokeWidthPt = strokeWidthPt;
        IsBold = isBold;
        IsVisible = isVisible;
        ZIndex = zIndex;
    }

    public string Kind { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double EndX { get; init; }

    public double EndY { get; init; }

    public string Text { get; init; }

    public string StrokeColor { get; init; }

    public string FillColor { get; init; }

    public double FillOpacityPercent { get; init; }

    public string TextColor { get; init; }

    public string FontFamily { get; init; }

    public double FontSizePt { get; init; }

    public double StrokeWidthPt { get; init; }

    public bool IsBold { get; init; }

    public bool IsVisible { get; init; }

    public int ZIndex { get; init; }

    public Guid Id { get; init; }

    public string Color => string.Equals(Kind, "text", StringComparison.OrdinalIgnoreCase)
        ? TextColor
        : StrokeColor;
}

public sealed record FigureGlobalStyle(
    string FontFamily,
    double FontSizePt,
    double StrokeWidthPt,
    string TextColor,
    string ShapeColor,
    string ScaleBarColor,
    string? PanelLabelFontFamily = null,
    double? PanelLabelFontSizePt = null,
    string? PanelLabelTextColor = null,
    bool PanelLabelIsBold = true,
    string? ScaleBarLabelColor = null,
    string? ScaleBarFontFamily = null,
    double? ScaleBarFontSizePt = null,
    bool ScaleBarLabelIsBold = true,
    double? ScaleBarThicknessPt = null)
{
    public static FigureGlobalStyle Default { get; } = new(
        "Arial",
        7,
        1.25,
        "#FF111111",
        "#FFE53935",
        "#FFFFFFFF");

    public string EffectivePanelLabelFontFamily =>
        string.IsNullOrWhiteSpace(PanelLabelFontFamily) ? FontFamily : PanelLabelFontFamily;

    public double EffectivePanelLabelFontSizePt => PanelLabelFontSizePt ?? FontSizePt;

    public string EffectivePanelLabelTextColor =>
        string.IsNullOrWhiteSpace(PanelLabelTextColor) ? TextColor : PanelLabelTextColor;

    public string EffectiveScaleBarLabelColor =>
        string.IsNullOrWhiteSpace(ScaleBarLabelColor) ? ScaleBarColor : ScaleBarLabelColor;

    public string EffectiveScaleBarFontFamily =>
        string.IsNullOrWhiteSpace(ScaleBarFontFamily) ? FontFamily : ScaleBarFontFamily;

    public double EffectiveScaleBarFontSizePt => ScaleBarFontSizePt ?? FontSizePt;

    public double EffectiveScaleBarThicknessPt => ScaleBarThicknessPt ?? StrokeWidthPt;

    public FigureGlobalStyle ResolvePanelOverride(StyleOverride? styleOverride)
    {
        if (styleOverride is null || styleOverride.IsEmpty)
        {
            return this;
        }

        styleOverride.EnsureValid();
        TextStyle? panelLabel = styleOverride.PanelLabel;
        TextStyle? scaleBarText = styleOverride.ScaleBarText;
        ScaleBarStyle? scaleBar = styleOverride.ScaleBar;
        return this with
        {
            PanelLabelFontFamily = panelLabel?.FontFamily ?? PanelLabelFontFamily,
            PanelLabelFontSizePt = panelLabel?.FontSizePt ?? PanelLabelFontSizePt,
            PanelLabelTextColor = panelLabel?.Color ?? PanelLabelTextColor,
            PanelLabelIsBold = panelLabel?.IsBold ?? PanelLabelIsBold,
            ScaleBarLabelColor = scaleBarText?.Color ?? ScaleBarLabelColor,
            ScaleBarFontFamily = scaleBarText?.FontFamily ?? ScaleBarFontFamily,
            ScaleBarFontSizePt = scaleBarText?.FontSizePt ?? ScaleBarFontSizePt,
            ScaleBarLabelIsBold = scaleBarText?.IsBold ?? ScaleBarLabelIsBold,
            ScaleBarColor = scaleBar?.Color ?? ScaleBarColor,
            ScaleBarThicknessPt = scaleBar?.BarThicknessPt ?? ScaleBarThicknessPt,
        };
    }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(FontFamily) && FontFamily.Length <= 128 &&
        double.IsFinite(FontSizePt) && FontSizePt is >= 4 and <= 72 &&
        double.IsFinite(StrokeWidthPt) && StrokeWidthPt is >= 0.25 and <= 10 &&
        !string.IsNullOrWhiteSpace(EffectivePanelLabelFontFamily) &&
        EffectivePanelLabelFontFamily.Length <= 128 &&
        double.IsFinite(EffectivePanelLabelFontSizePt) &&
        EffectivePanelLabelFontSizePt is >= 4 and <= 72 &&
        !string.IsNullOrWhiteSpace(EffectiveScaleBarFontFamily) &&
        EffectiveScaleBarFontFamily.Length <= 128 &&
        double.IsFinite(EffectiveScaleBarFontSizePt) &&
        EffectiveScaleBarFontSizePt is >= 4 and <= 72 &&
        double.IsFinite(EffectiveScaleBarThicknessPt) &&
        EffectiveScaleBarThicknessPt is >= 0.25 and <= 10 &&
        IsColor(TextColor) && IsColor(ShapeColor) && IsColor(ScaleBarColor) &&
        IsColor(EffectivePanelLabelTextColor) && IsColor(EffectiveScaleBarLabelColor);

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
        FigureGlobalStyle? globalStyle = null,
        IReadOnlyList<FigureMeasurementOverlayExportItem>? measurementOverlays = null,
        IReadOnlyList<FigureScientificObjectExportItem>? scientificObjects = null,
        IReadOnlyList<FigureRoiProjectionExportItem>? roiProjections = null,
        PdfFontStrategy pdfFontStrategy = PdfFontStrategy.OutlineText,
        IReadOnlyList<FigurePlotPanelExportItem>? plotPanels = null)
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
        MeasurementOverlays = measurementOverlays ?? [];
        ScientificObjects = scientificObjects ?? [];
        RoiProjections = roiProjections ?? [];
        PlotPanels = plotPanels ?? [];
        if (!Enum.IsDefined(pdfFontStrategy))
        {
            throw new ArgumentOutOfRangeException(nameof(pdfFontStrategy));
        }

        PdfFontStrategy = pdfFontStrategy;
        GlobalStyle.EnsureValid();
        foreach (FigurePlotPanelExportItem plotPanel in PlotPanels)
        {
            plotPanel.EnsureValid();
        }
    }

    public int WidthPixels { get; }

    public int HeightPixels { get; }

    public int Dpi { get; }

    public IReadOnlyList<FigurePanelExportItem> Panels { get; }

    public IReadOnlyList<FigureAnnotationExportItem> Annotations { get; }

    public IReadOnlyList<FigureMeasurementOverlayExportItem> MeasurementOverlays { get; }

    public IReadOnlyList<FigureScientificObjectExportItem> ScientificObjects { get; }

    public IReadOnlyList<FigureRoiProjectionExportItem> RoiProjections { get; }

    public IReadOnlyList<FigurePlotPanelExportItem> PlotPanels { get; }

    public string BackgroundColor { get; }

    public int BitDepth { get; }

    public FigureGlobalStyle GlobalStyle { get; }

    public PdfFontStrategy PdfFontStrategy { get; }
}
