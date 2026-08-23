using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SciCanvas.Imaging;

/// <summary>Loads one frame of a multi-page image without changing the source file.</summary>
public static class WpfImageFramePreviewLoader
{
    public static BitmapSource Load(string path, int maximumPixelWidth, int frameIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixelWidth);
        ArgumentOutOfRangeException.ThrowIfNegative(frameIndex);

        using FileStream source = new(Path.GetFullPath(path), new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.SequentialScan,
        });
        BitmapDecoder decoder = BitmapDecoder.Create(
            source,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        if (frameIndex >= decoder.Frames.Count)
        {
            throw new InvalidOperationException(
                $"源图像只有 {decoder.Frames.Count} 帧，无法读取第 {frameIndex + 1} 帧。");
        }

        BitmapSource frame = decoder.Frames[frameIndex];
        frame.Freeze();
        if (frame.PixelWidth <= maximumPixelWidth)
        {
            return frame;
        }

        double scale = maximumPixelWidth / (double)frame.PixelWidth;
        var resized = new TransformedBitmap(
            frame,
            new ScaleTransform(scale, scale));
        resized.Freeze();
        return resized;
    }
}
