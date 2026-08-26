using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Workspace;

public static class CoordinateTransforms
{
    public static NormalizedPoint SourceToPanel(
        MeasurementPoint sourcePoint,
        long sourceWidth,
        long sourceHeight,
        NormalizedRect crop,
        double rotationDegrees = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        double sourceU = sourcePoint.X / sourceWidth;
        double sourceV = sourcePoint.Y / sourceHeight;
        double u = (sourceU - crop.X) / crop.Width;
        double v = (sourceV - crop.Y) / crop.Height;
        if (u is < -1e-9 or > 1.000000001 || v is < -1e-9 or > 1.000000001)
        {
            throw new ArgumentOutOfRangeException(nameof(sourcePoint), "源坐标不在 Panel crop 内。" );
        }

        (u, v) = RotateNormalized(u, v, rotationDegrees);
        return new NormalizedPoint(Math.Clamp(u, 0, 1), Math.Clamp(v, 0, 1));
    }

    public static MeasurementPoint PanelToSource(
        NormalizedPoint panelPoint,
        long sourceWidth,
        long sourceHeight,
        NormalizedRect crop,
        double rotationDegrees = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        (double u, double v) = RotateNormalized(
            panelPoint.U,
            panelPoint.V,
            -rotationDegrees);
        return new MeasurementPoint(
            (crop.X + u * crop.Width) * sourceWidth,
            (crop.Y + v * crop.Height) * sourceHeight);
    }

    public static FigurePointMm PanelToFigure(
        NormalizedPoint panelPoint,
        FigureRectMm frame) => new(
            frame.X + panelPoint.U * frame.Width,
            frame.Y + panelPoint.V * frame.Height);

    public static NormalizedPoint FigureToPanel(
        FigurePointMm figurePoint,
        FigureRectMm frame)
    {
        double u = (figurePoint.X - frame.X) / frame.Width;
        double v = (figurePoint.Y - frame.Y) / frame.Height;
        return new NormalizedPoint(u, v);
    }

    private static (double U, double V) RotateNormalized(
        double u,
        double v,
        double rotationDegrees)
    {
        double radians = rotationDegrees * Math.PI / 180.0;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double x = u - 0.5;
        double y = v - 0.5;
        return (
            x * cosine - y * sine + 0.5,
            x * sine + y * cosine + 0.5);
    }
}

public static class PanelCropCalculator
{
    /// <summary>
    /// Resolves a panel crop while keeping a manual crop as an exact half-open
    /// source-pixel rectangle [x, x + width) × [y, y + height).
    /// </summary>
    public static PixelRect64 ResolveSourcePixels(
        PanelFitMode fitMode,
        long sourceWidthPixels,
        long sourceHeightPixels,
        FigureRectMm frame,
        PixelRect64 manualCrop)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeightPixels);
        if (manualCrop.Right > sourceWidthPixels || manualCrop.Bottom > sourceHeightPixels)
        {
            throw new ArgumentOutOfRangeException(
                nameof(manualCrop),
                "手动裁剪区域必须完全位于源图半开像素边界内。");
        }

        if (fitMode == PanelFitMode.Manual)
        {
            return manualCrop;
        }

        return Resolve(
                fitMode,
                sourceWidthPixels,
                sourceHeightPixels,
                frame,
                NormalizedRect.FromSourcePixels(
                    manualCrop,
                    sourceWidthPixels,
                    sourceHeightPixels))
            .ToSourcePixels(sourceWidthPixels, sourceHeightPixels);
    }

    public static NormalizedRect Resolve(
        PanelFitMode fitMode,
        long sourceWidthPixels,
        long sourceHeightPixels,
        FigureRectMm frame,
        NormalizedRect manualCrop)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeightPixels);

        return fitMode switch
        {
            PanelFitMode.Fit => NormalizedRect.Full,
            PanelFitMode.Manual => manualCrop,
            PanelFitMode.Fill => CalculateFillCrop(
                sourceWidthPixels,
                sourceHeightPixels,
                frame.Width / frame.Height),
            _ => throw new ArgumentOutOfRangeException(nameof(fitMode)),
        };
    }

    private static NormalizedRect CalculateFillCrop(
        long sourceWidth,
        long sourceHeight,
        double targetAspect)
    {
        double sourceAspect = sourceWidth / (double)sourceHeight;
        if (Math.Abs(sourceAspect - targetAspect) < 1e-9)
        {
            return NormalizedRect.Full;
        }

        if (sourceAspect > targetAspect)
        {
            double width = targetAspect / sourceAspect;
            return new NormalizedRect((1 - width) / 2, 0, width, 1);
        }

        double height = sourceAspect / targetAspect;
        return new NormalizedRect(0, (1 - height) / 2, 1, height);
    }
}

public static class EffectiveDpiCalculator
{
    public static double Calculate(
        long sourceWidthPixels,
        long sourceHeightPixels,
        NormalizedRect crop,
        double panelWidthMm,
        double panelHeightMm)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeightPixels);
        if (!double.IsFinite(panelWidthMm) || panelWidthMm <= 0 ||
            !double.IsFinite(panelHeightMm) || panelHeightMm <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(panelWidthMm));
        }

        double visibleWidthPixels = sourceWidthPixels * crop.Width;
        double visibleHeightPixels = sourceHeightPixels * crop.Height;
        double dpiX = visibleWidthPixels / (panelWidthMm / 25.4);
        double dpiY = visibleHeightPixels / (panelHeightMm / 25.4);
        return Math.Min(dpiX, dpiY);
    }
}
