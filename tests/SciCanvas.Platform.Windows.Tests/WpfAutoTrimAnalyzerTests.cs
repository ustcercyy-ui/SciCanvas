using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfAutoTrimAnalyzerTests
{
    [Fact]
    public void Suggest_MapsWhiteBorderBackToSourcePixels()
    {
        const int width = 10;
        const int height = 10;
        byte[] pixels = Enumerable.Repeat((byte)255, width * height * 4).ToArray();
        for (int y = 3; y <= 6; y++)
        {
            for (int x = 2; x <= 7; x++)
            {
                int offset = (y * width + x) * 4;
                pixels[offset] = 0;
                pixels[offset + 1] = 0;
                pixels[offset + 2] = 0;
                pixels[offset + 3] = 255;
            }
        }

        BitmapSource preview = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            width * 4);
        preview.Freeze();

        AutoTrimPreviewResult? result = WpfAutoTrimAnalyzer.Suggest(
            preview,
            new PixelSize64(1000, 500),
            paddingPreviewPixels: 0);

        Assert.NotNull(result);
        Assert.Equal(new PixelRect64(200, 150, 600, 200), result!.Crop);
        Assert.Equal(1, result.Confidence);
    }

    [Fact]
    public void Suggest_ReturnsNullWhenContentTouchesEveryEdge()
    {
        byte[] pixels = new byte[4 * 4 * 4];
        for (int index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        BitmapSource preview = BitmapSource.Create(
            4,
            4,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            16);
        preview.Freeze();

        Assert.Null(WpfAutoTrimAnalyzer.Suggest(
            preview,
            new PixelSize64(400, 400),
            paddingPreviewPixels: 0));
    }
}
