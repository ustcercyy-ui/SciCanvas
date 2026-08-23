using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Images;
using SciCanvas.Imaging;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfImageAdjustmentProcessorTests
{
    [Fact]
    public void Apply_BrightnessAndContrastChangePixelTones()
    {
        byte[] sourcePixels =
        [
            64, 64, 64, 255,
            192, 192, 192, 255,
        ];
        BitmapSource source = BitmapSource.Create(
            2,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            sourcePixels,
            8);
        source.Freeze();

        BitmapSource brighter = WpfImageAdjustmentProcessor.Apply(
            source,
            new ImageAdjustmentParameters { Brightness = 0.2 });
        byte[] brighterPixels = new byte[8];
        brighter.CopyPixels(brighterPixels, 8, 0);
        Assert.True(brighterPixels[0] > sourcePixels[0]);
        Assert.True(brighterPixels[4] > sourcePixels[4]);

        BitmapSource higherContrast = WpfImageAdjustmentProcessor.Apply(
            source,
            new ImageAdjustmentParameters { Contrast = 0.5 });
        byte[] contrastPixels = new byte[8];
        higherContrast.CopyPixels(contrastPixels, 8, 0);
        Assert.True(contrastPixels[0] < sourcePixels[0]);
        Assert.True(contrastPixels[4] > sourcePixels[4]);
    }
    [Fact]
    public void Apply_InvertChangesCopiedPixelsWithoutMutatingSource()
    {
        byte[] sourcePixels = [0, 0, 255, 255];
        BitmapSource source = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            sourcePixels,
            4);
        source.Freeze();

        BitmapSource adjusted = WpfImageAdjustmentProcessor.Apply(
            source,
            new ImageAdjustmentParameters { Invert = true });
        byte[] outputPixels = new byte[4];
        adjusted.CopyPixels(outputPixels, 4, 0);

        Assert.Equal(new byte[] { 255, 255, 0, 255 }, outputPixels);
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, sourcePixels);
    }
}
