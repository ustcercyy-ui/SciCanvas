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

            WpfImageAdjustmentMath.Apply(ref red, ref green, ref blue, alpha, adjustment);

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

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);

    private sealed class BitmapSourceFactory
    {
        public BitmapSource Create(int width, int height, double dpiX, double dpiY, PixelFormat format, byte[] pixels, int stride) =>
            BitmapSource.Create(width, height, dpiX, dpiY, format, null, pixels, stride);
    }
}
