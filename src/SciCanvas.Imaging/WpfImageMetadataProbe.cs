using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Imaging;

public sealed class WpfImageMetadataProbe : IImageMetadataProbe
{
    public ValueTask<CoreImageMetadata> ProbeAsync(
        Stream source,
        string fileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        if (!source.CanSeek)
        {
            throw new ArgumentException("图像元数据流必须支持定位。", nameof(source));
        }

        source.Position = 0;
        BitmapDecoder decoder = BitmapDecoder.Create(
            source,
            BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.DelayCreation,
            BitmapCacheOption.None);

        BitmapFrame frame = decoder.Frames[0];
        PixelFormat format = frame.Format;
        int channels = DetermineChannelCount(format);
        int bitsPerChannel = Math.Max(1, format.BitsPerPixel / channels);

        return ValueTask.FromResult(new CoreImageMetadata(
            new PixelSize64(frame.PixelWidth, frame.PixelHeight),
            channels,
            bitsPerChannel,
            format.ToString(),
            NormalizeDpi(frame.DpiX),
            NormalizeDpi(frame.DpiY),
            frameCount: decoder.Frames.Count));
    }

    private static int DetermineChannelCount(PixelFormat format)
    {
        if (format == PixelFormats.BlackWhite ||
            format == PixelFormats.Gray2 ||
            format == PixelFormats.Gray4 ||
            format == PixelFormats.Gray8 ||
            format == PixelFormats.Gray16 ||
            format == PixelFormats.Indexed1 ||
            format == PixelFormats.Indexed2 ||
            format == PixelFormats.Indexed4 ||
            format == PixelFormats.Indexed8)
        {
            return 1;
        }

        int maskCount = format.Masks.Count;
        return maskCount > 0 ? maskCount : 1;
    }

    private static double? NormalizeDpi(double dpi)
    {
        return double.IsFinite(dpi) && dpi > 0 ? dpi : null;
    }
}
