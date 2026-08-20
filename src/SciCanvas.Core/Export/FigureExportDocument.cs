using SciCanvas.Core.Geometry;
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
    FigureScaleBarExportSpec? ScaleBar = null);

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

public sealed record FigureExportDocument
{
    public FigureExportDocument(
        int widthPixels,
        int heightPixels,
        int dpi,
        IReadOnlyList<FigurePanelExportItem> panels,
        IReadOnlyList<FigureAnnotationExportItem>? annotations = null,
        string backgroundColor = "#FFFFFFFF")
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        ArgumentNullException.ThrowIfNull(panels);

        WidthPixels = widthPixels;
        HeightPixels = heightPixels;
        Dpi = dpi;
        Panels = panels;
        Annotations = annotations ?? [];
        BackgroundColor = backgroundColor;
    }

    public int WidthPixels { get; }

    public int HeightPixels { get; }

    public int Dpi { get; }

    public IReadOnlyList<FigurePanelExportItem> Panels { get; }

    public IReadOnlyList<FigureAnnotationExportItem> Annotations { get; }

    public string BackgroundColor { get; }
}
