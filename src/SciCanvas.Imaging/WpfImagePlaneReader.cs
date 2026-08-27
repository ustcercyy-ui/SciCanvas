using System.Buffers.Binary;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

/// <summary>
/// Reads typed raw UInt8/UInt16 channel planes. It never applies LUT, pseudocolor,
/// display range, gamma, inversion, opacity, or composite settings.
/// </summary>
public sealed class WpfImagePlaneReader : IImagePlaneReader
{
    public ValueTask<ImagePlane> ReadAsync(
        SourceAsset source,
        ImagePlaneRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        request.EnsureValid();
        if (request.AssetId != source.Id)
        {
            throw new InvalidDataException("图像平面请求的 AssetId 与源素材不一致。");
        }

        IReadOnlyList<ImagePlane> planes = ReadPlanes(
            source,
            request.FrameIndex,
            request.Region,
            [request.ChannelSelector],
            request.SourceRevision,
            cancellationToken);
        return ValueTask.FromResult(planes[0]);
    }

    internal static IReadOnlyList<ImagePlane> ReadPlanes(
        SourceAsset source,
        int frameIndex,
        PixelRect64 region,
        IReadOnlyList<ScientificChannelDescriptor> channels,
        long? sourceRevision,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channels);
        if (channels.Count == 0)
        {
            throw new ArgumentException("至少需要读取一个科研通道。", nameof(channels));
        }

        if (frameIndex < 0 || frameIndex >= source.Metadata.FrameCount ||
            sourceRevision < 0 ||
            region.Right > source.Metadata.PixelSize.Width ||
            region.Bottom > source.Metadata.PixelSize.Height)
        {
            throw new InvalidDataException("图像平面请求的帧、修订或源像素区域无效。");
        }

        if (region.Width > int.MaxValue || region.Height > int.MaxValue)
        {
            throw new NotSupportedException("图像平面单边尺寸暂不支持超过 Int32 范围。");
        }

        ScientificSampleType sampleType = source.Metadata.BitsPerChannel switch
        {
            8 => ScientificSampleType.UInt8,
            >= 9 and <= 16 => ScientificSampleType.UInt16,
            _ => throw new NotSupportedException(
                "当前 WPF raw plane reader 仅声明支持 UInt8 与 UInt16 容器；packed 1/2/4-bit 和 Float32 不会被静默转换。"),
        };
        foreach (ScientificChannelDescriptor channel in channels)
        {
            channel.EnsureValid();
            ValidateChannelCompatibility(source, channel, sampleType);
        }

        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource crop = WpfFigureExporter.LoadExactCrop(source.OriginalPath, region, frameIndex);
        PixelFormat readFormat = ResolveReadFormat(source, sampleType);
        BitmapSource converted = crop.Format == readFormat
            ? crop
            : new FormatConvertedBitmap(crop, readFormat, null, 0);
        converted.Freeze();

        int bytesPerPixel = readFormat.BitsPerPixel / 8;
        int stride = checked(converted.PixelWidth * bytesPerPixel);
        byte[] encoded = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(encoded, stride, 0);
        int sampleCount = checked(converted.PixelWidth * converted.PixelHeight);
        var planes = new List<ImagePlane>(channels.Count);
        if (sampleType == ScientificSampleType.UInt8)
        {
            foreach (ScientificChannelDescriptor channel in channels)
            {
                byte[] values = new byte[sampleCount];
                for (int index = 0; index < values.Length; index++)
                {
                    if ((index & 0x3FFF) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    values[index] = ReadUInt8Component(encoded, index * bytesPerPixel, readFormat, channel.Index);
                }

                planes.Add(new ImagePlane(
                    source.Id,
                    sourceRevision,
                    frameIndex,
                    region,
                    channel,
                    new UInt8ImagePlaneSamples(values)));
            }
        }
        else
        {
            foreach (ScientificChannelDescriptor channel in channels)
            {
                ushort[] values = new ushort[sampleCount];
                for (int index = 0; index < values.Length; index++)
                {
                    if ((index & 0x3FFF) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    values[index] = ReadUInt16Component(encoded, index * bytesPerPixel, readFormat, channel.Index);
                }

                planes.Add(new ImagePlane(
                    source.Id,
                    sourceRevision,
                    frameIndex,
                    region,
                    channel,
                    new UInt16ImagePlaneSamples(values)));
            }
        }

        return planes;
    }

    private static void ValidateChannelCompatibility(
        SourceAsset source,
        ScientificChannelDescriptor channel,
        ScientificSampleType sampleType)
    {
        if (channel.SampleType != sampleType || channel.BitDepth != source.Metadata.BitsPerChannel)
        {
            throw new InvalidDataException("科研通道样本类型或位深与源素材元数据不一致。");
        }

        switch (channel.SourceKind)
        {
            case ScientificChannelSourceKind.InterleavedComponent:
                if (source.Metadata.Channels is not (1 or 3 or 4) || channel.Index >= source.Metadata.Channels)
                {
                    throw new NotSupportedException("当前 WPF raw plane reader 仅支持明确的 Gray、RGB 与 RGBA component 映射。");
                }
                break;
            case ScientificChannelSourceKind.FramePlane:
            case ScientificChannelSourceKind.ExternalAsset:
                if (source.Metadata.Channels != 1 || channel.Index != 0)
                {
                    throw new InvalidDataException("FramePlane 与 ExternalAsset 通道必须引用单通道素材的索引 0。");
                }
                break;
            default:
                throw new InvalidDataException("未知科研通道来源类型。");
        }
    }

    private static PixelFormat ResolveReadFormat(
        SourceAsset source,
        ScientificSampleType sampleType) =>
        (sampleType, source.Metadata.Channels) switch
        {
            (ScientificSampleType.UInt8, 1) => PixelFormats.Gray8,
            (ScientificSampleType.UInt8, _) => PixelFormats.Bgra32,
            (ScientificSampleType.UInt16, 1) => PixelFormats.Gray16,
            (ScientificSampleType.UInt16, 3) => PixelFormats.Rgb48,
            (ScientificSampleType.UInt16, 4) => PixelFormats.Rgba64,
            _ => throw new NotSupportedException("不支持的原始通道像素布局。"),
        };

    private static byte ReadUInt8Component(byte[] bytes, int offset, PixelFormat format, int componentIndex)
    {
        if (format == PixelFormats.Gray8)
        {
            return bytes[offset];
        }

        return componentIndex switch
        {
            0 => bytes[offset + 2],
            1 => bytes[offset + 1],
            2 => bytes[offset],
            3 => bytes[offset + 3],
            _ => throw new InvalidDataException("UInt8 component 索引无效。"),
        };
    }

    private static ushort ReadUInt16Component(
        byte[] bytes,
        int offset,
        PixelFormat format,
        int componentIndex)
    {
        if (format == PixelFormats.Gray16)
        {
            return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2));
        }

        int componentOffset = checked(offset + componentIndex * 2);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(componentOffset, 2));
    }
}
