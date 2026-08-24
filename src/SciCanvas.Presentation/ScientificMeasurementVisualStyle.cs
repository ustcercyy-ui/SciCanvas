namespace SciCanvas.Presentation;

public sealed record ScientificMeasurementVisualStyle
{
    public static ScientificMeasurementVisualStyle Default { get; } = new();

    public string StrokeColor { get; init; } = "#FF22C7E8";

    public double StrokeWidthPixels { get; init; } = 3;

    public string LineStyle { get; init; } = "solid";

    public double MarkerSizePixels { get; init; } = 18;

    public bool ShowMarkers { get; init; } = true;

    public bool ShowLabel { get; init; } = true;

    public double FillOpacityPercent { get; init; } = 8;

    public bool IsVisible { get; init; } = true;

    public bool IsLocked { get; init; }
}
