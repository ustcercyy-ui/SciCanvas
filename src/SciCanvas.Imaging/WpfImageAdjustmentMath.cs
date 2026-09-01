using SciCanvas.Core.Images;

namespace SciCanvas.Imaging;

/// <summary>
/// Shared normalized RGB adjustment math. Callers choose the presentation
/// precision and quantize only after this transform has been applied.
/// </summary>
internal static class WpfImageAdjustmentMath
{
    public static void Apply(
        ref double red,
        ref double green,
        ref double blue,
        double alpha,
        ImageAdjustmentParameters adjustment)
    {
        ArgumentNullException.ThrowIfNull(adjustment);

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
            red = green = blue = alpha;
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
}
