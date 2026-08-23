using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Images;

namespace SciCanvas.Imaging;

/// <summary>Applies explicitly recorded display adjustments to a bitmap copy.</summary>
public static class WpfImageAdjustmentProcessor
{
    public static BitmapSource Apply(BitmapSource source, ImageAdjustmentParameters? parameters)
    {
        ArgumentNullException.ThrowIfNull(source);
        ImageAdjustmentParameters adjustment = (parameters ?? new()).Normalize();
        if (!adjustment.IsValid)
        {
            throw new InvalidOperationException(adjustment.ValidationMessage);
        }

        if (adjustment.IsIdentity)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        for (int index = 0; index < pixels.Length; index += 4)
        {
            double blue = pixels[index] / 255.0;
            double green = pixels[index + 1] / 255.0;
            double red = pixels[index + 2] / 255.0;
            double alpha = pixels[index + 3] / 255.0;

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

            pixels[index] = ToByte(blue);
            pixels[index + 1] = ToByte(green);
            pixels[index + 2] = ToByte(red);
            pixels[index + 3] = ToByte(alpha);
        }

        var output = new BitmapSourceFactory().Create(
            converted.PixelWidth,
            converted.PixelHeight,
            96,
            96,
            PixelFormats.Bgra32,
            pixels,
            stride);
        output.Freeze();
        return output;
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

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private sealed class BitmapSourceFactory
    {
        public BitmapSource Create(int width, int height, double dpiX, double dpiY, PixelFormat format, byte[] pixels, int stride) =>
            BitmapSource.Create(width, height, dpiX, dpiY, format, null, pixels, stride);
    }
}
