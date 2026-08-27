using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

/// <summary>
/// A physical length with an explicit display unit. The conversion source remains
/// the panel's calibration; changing this unit never changes the underlying scale.
/// </summary>
public sealed record ScientificLength(double Value, string Unit)
{
    public string DisplayText => $"{Value:0.###} {Unit}";

    public void EnsureValid()
    {
        if (!double.IsFinite(Value) || Value <= 0 || string.IsNullOrWhiteSpace(Unit))
        {
            throw new InvalidOperationException("物理长度必须是带单位的有限正数。");
        }

        _ = ScientificLengthUnits.Normalize(Unit);
    }

    public double InUnit(string targetUnit)
    {
        EnsureValid();
        return ScientificLengthUnits.Convert(Value, Unit, targetUnit);
    }
}

/// <summary>Source-pixel calibration used to turn a displayed physical length into geometry.</summary>
public sealed record ScientificScaleCalibration(double UnitsPerSourcePixel, string Unit)
{
    public void EnsureValid()
    {
        if (!double.IsFinite(UnitsPerSourcePixel) || UnitsPerSourcePixel <= 0 ||
            string.IsNullOrWhiteSpace(Unit))
        {
            throw new InvalidOperationException("比例尺缺少有效的源像素校准。");
        }

        _ = ScientificLengthUnits.Normalize(Unit);
    }

    public double SourcePixelsFor(ScientificLength length)
    {
        EnsureValid();
        return length.InUnit(Unit) / UnitsPerSourcePixel;
    }
}

/// <summary>Exact figure-space placement shared by WPF preview and every exporter.</summary>
public sealed record FigureScaleBarGeometry(
    FigureScaleBarExportSpec Spec,
    double Left,
    double Right,
    double Y,
    double LabelTop,
    bool LabelAppearsBelow);

public static class FigureScaleBarLayout
{
    public static IReadOnlyList<FigureScaleBarGeometry> Calculate(
        IEnumerable<FigureScaleBarExportSpec> specifications,
        PixelRect64 sourceRect,
        FigureImageRect imageRect,
        int dpi,
        double thicknessPixels,
        double labelFontPixels)
    {
        ArgumentNullException.ThrowIfNull(specifications);
        if (sourceRect.Width <= 0 || sourceRect.Height <= 0 || imageRect.Width <= 0 || imageRect.Height <= 0)
        {
            throw new InvalidOperationException("比例尺无法在空白源区域或图像区域中定位。");
        }

        FigureScaleBarExportSpec[] bars = specifications.ToArray();
        var result = new List<FigureScaleBarGeometry>(bars.Length);
        foreach (IGrouping<ScaleBarAnchor, FigureScaleBarExportSpec> group in bars.GroupBy(item =>
                     item.Anchor == ScaleBarAnchor.Custom ? ScaleBarAnchor.BottomRight : item.Anchor))
        {
            int stackIndex = 0;
            foreach (FigureScaleBarExportSpec spec in group)
            {
                result.Add(CalculateOne(spec, sourceRect, imageRect, dpi, thicknessPixels, labelFontPixels, stackIndex++));
            }
        }

        return result;
    }

    private static FigureScaleBarGeometry CalculateOne(
        FigureScaleBarExportSpec spec,
        PixelRect64 sourceRect,
        FigureImageRect imageRect,
        int dpi,
        double thicknessPixels,
        double labelFontPixels,
        int stackIndex)
    {
        double sourcePixels = spec.SourcePixelLength;
        double barWidth = sourcePixels * imageRect.Width / sourceRect.Width;
        double margin = Math.Max(10, Math.Min(imageRect.Width, imageRect.Height) * 0.035);
        double thickness = Math.Max(2, thicknessPixels);
        double gap = Math.Max(3, dpi / 100.0);
        double step = Math.Max(thickness * 2 + gap, labelFontPixels * 1.35 + thickness + gap * 2);
        bool top = spec.Anchor is ScaleBarAnchor.TopLeft or ScaleBarAnchor.TopRight;
        bool left = spec.Anchor is ScaleBarAnchor.BottomLeft or ScaleBarAnchor.TopLeft;
        // Custom remains intentionally deterministic until direct manipulation is added.
        double y = top
            ? imageRect.Y + margin + thickness / 2 + stackIndex * step
            : imageRect.Bottom - margin - thickness / 2 - stackIndex * step;
        double lineLeft = left ? imageRect.X + margin : imageRect.Right - margin - barWidth;
        double lineRight = lineLeft + barWidth;
        double labelTop = top
            ? y + thickness / 2 + gap
            : y - thickness / 2 - gap - labelFontPixels * 1.2;
        return new FigureScaleBarGeometry(spec, lineLeft, lineRight, y, labelTop, top);
    }
}