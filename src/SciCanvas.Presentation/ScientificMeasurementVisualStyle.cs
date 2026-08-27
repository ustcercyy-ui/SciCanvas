namespace SciCanvas.Presentation;

public sealed record ScientificMeasurementVisualStyle
{
    public static ScientificMeasurementVisualStyle Default { get; } = new();

    public string StrokeColor { get; init; } = "#FF22C7E8";

    public double StrokeWidthPixels { get; init; } = 3;

    public string LineStyle { get; init; } = "solid";

    public string FillColor { get; init; } = "#FF22C7E8";

    public string MarkerStrokeColor { get; init; } = "#FF22C7E8";

    public string MarkerFillColor { get; init; } = "#FF11171F";

    public double MarkerSizePixels { get; init; } = 18;

    public bool ShowMarkers { get; init; } = true;

    public bool ShowLabel { get; init; } = true;

    public string LabelColor { get; init; } = "#FF22C7E8";

    public string LabelFontFamily { get; init; } = "Arial";

    public double LabelFontSizePt { get; init; } = 16.5;

    public bool LabelIsBold { get; init; } = true;

    public double FillOpacityPercent { get; init; } = 8;

    public bool IsVisible { get; init; } = true;

    public bool IsLocked { get; init; }
}
