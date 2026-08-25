using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Presentation;

public sealed record AutoTrimPreviewResult(
    PixelRect64 Crop,
    double Confidence,
    string Reason);

/// <summary>
/// Finds a white/transparent preview border and maps the suggestion back to
/// immutable source-pixel coordinates. The caller must explicitly apply it.
/// </summary>
public static class WpfAutoTrimAnalyzer
{
    public static AutoTrimPreviewResult? Suggest(
        BitmapSource preview,
        PixelSize64 sourceSize,
        double tolerance = 0.025,
        int paddingPreviewPixels = 4)
    {
        ArgumentNullException.ThrowIfNull(preview);
        if (!double.IsFinite(tolerance) || tolerance is < 0 or > 1 ||
            paddingPreviewPixels < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        }

        BitmapSource bgra = preview.Format == PixelFormats.Bgra32
            ? preview
            : new FormatConvertedBitmap(preview, PixelFormats.Bgra32, null, 0);
        int width = bgra.PixelWidth;
        int height = bgra.PixelHeight;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        int stride = checked(width * 4);
        byte[] pixels = new byte[checked(stride * height)];
        bgra.CopyPixels(pixels, stride, 0);
        byte whiteThreshold = (byte)Math.Clamp(
            Math.Round(255 * (1 - tolerance)),
            0,
            255);
        int left = width;
        int top = height;
        int right = -1;
        int bottom = -1;
        int borderBackground = 0;
        int borderSamples = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int offset = y * stride + x * 4;
                byte blue = pixels[offset];
                byte green = pixels[offset + 1];
                byte red = pixels[offset + 2];
                byte alpha = pixels[offset + 3];
                bool background = alpha <= 5 ||
                                  (red >= whiteThreshold &&
                                   green >= whiteThreshold &&
                                   blue >= whiteThreshold);
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    borderSamples++;
                    if (background)
                    {
                        borderBackground++;
                    }
                }

                if (background)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x);
                bottom = Math.Max(bottom, y);
            }
        }

        if (right < left || bottom < top)
        {
            return null;
        }

        left = Math.Max(0, left - paddingPreviewPixels);
        top = Math.Max(0, top - paddingPreviewPixels);
        right = Math.Min(width - 1, right + paddingPreviewPixels);
        bottom = Math.Min(height - 1, bottom + paddingPreviewPixels);
        if (left == 0 && top == 0 && right == width - 1 && bottom == height - 1)
        {
            return null;
        }

        long sourceLeft = Math.Clamp(
            (long)Math.Floor(left / (double)width * sourceSize.Width),
            0,
            sourceSize.Width - 1);
        long sourceTop = Math.Clamp(
            (long)Math.Floor(top / (double)height * sourceSize.Height),
            0,
            sourceSize.Height - 1);
        long sourceRight = Math.Clamp(
            (long)Math.Ceiling((right + 1) / (double)width * sourceSize.Width),
            sourceLeft + 1,
            sourceSize.Width);
        long sourceBottom = Math.Clamp(
            (long)Math.Ceiling((bottom + 1) / (double)height * sourceSize.Height),
            sourceTop + 1,
            sourceSize.Height);
        double confidence = borderSamples == 0
            ? 0
            : borderBackground / (double)borderSamples;
        return new AutoTrimPreviewResult(
            new PixelRect64(
                sourceLeft,
                sourceTop,
                sourceRight - sourceLeft,
                sourceBottom - sourceTop),
            confidence,
            $"检测到白色/透明边界；边缘背景置信度 {confidence:P0}。");
    }
}
