using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

internal sealed record ScientificPixelBuffer(
    int Width,
    int Height,
    int SourceBitDepth,
    double MaximumValue,
    IReadOnlyList<double> Values);

internal static class WpfScientificPixelReader
{
    public static ScientificPixelBuffer ReadRegion(
        SourceAsset source,
        PixelRect64 region,
        int frameIndex,
        ImageAnalysisChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (region.Right > source.Metadata.PixelSize.Width ||
            region.Bottom > source.Metadata.PixelSize.Height)
        {
            throw new InvalidDataException("分析区域超出源图半开像素边界。");
        }

        if (region.Width > int.MaxValue || region.Height > int.MaxValue)
        {
            throw new NotSupportedException("分析区域单边尺寸暂不支持超过 Int32 范围。");
        }

        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource crop = WpfFigureExporter.LoadExactCrop(
            source.OriginalPath,
            region,
            frameIndex);
        bool use16Bit = source.Metadata.BitsPerChannel > 8;
        PixelFormat readFormat = ResolveReadFormat(crop.Format, use16Bit);
        BitmapSource converted = crop.Format == readFormat
            ? crop
            : new FormatConvertedBitmap(crop, readFormat, null, 0);
        converted.Freeze();

        int bytesPerPixel = readFormat.BitsPerPixel / 8;
        int stride = checked(converted.PixelWidth * bytesPerPixel);
        byte[] bytes = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(bytes, stride, 0);
        double maximum = use16Bit ? ushort.MaxValue : byte.MaxValue;
        double[] values = new double[checked(converted.PixelWidth * converted.PixelHeight)];
        for (int index = 0; index < values.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int offset = index * bytesPerPixel;
            (double red, double green, double blue, double alpha) =
                ReadChannels(bytes, offset, readFormat, maximum);

            values[index] = channel switch
            {
                ImageAnalysisChannel.Red => red,
                ImageAnalysisChannel.Green => green,
                ImageAnalysisChannel.Blue => blue,
                ImageAnalysisChannel.Alpha => alpha,
                _ => (red * 2126 + green * 7152 + blue * 722) / 10_000,
            };
        }

        return new ScientificPixelBuffer(
            converted.PixelWidth,
            converted.PixelHeight,
            use16Bit ? 16 : 8,
            maximum,
            values);
    }

    private static PixelFormat ResolveReadFormat(PixelFormat sourceFormat, bool use16Bit)
    {
        if (use16Bit &&
            (sourceFormat == PixelFormats.Gray16 ||
             sourceFormat == PixelFormats.Rgb48 ||
             sourceFormat == PixelFormats.Rgba64))
        {
            return sourceFormat;
        }

        if (!use16Bit && sourceFormat == PixelFormats.Gray8)
        {
            return sourceFormat;
        }

        return use16Bit ? PixelFormats.Rgba64 : PixelFormats.Bgra32;
    }

    private static (double Red, double Green, double Blue, double Alpha) ReadChannels(
        byte[] bytes,
        int offset,
        PixelFormat format,
        double maximum)
    {
        if (format == PixelFormats.Gray16)
        {
            double gray = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
            return (gray, gray, gray, maximum);
        }

        if (format == PixelFormats.Rgb48)
        {
            return (
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2)),
                maximum);
        }

        if (format == PixelFormats.Rgba64)
        {
            return (
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 2, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 4, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 6, 2)));
        }

        if (format == PixelFormats.Gray8)
        {
            double gray = bytes[offset];
            return (gray, gray, gray, maximum);
        }

        return (
            bytes[offset + 2],
            bytes[offset + 1],
            bytes[offset],
            bytes[offset + 3]);
    }
}
