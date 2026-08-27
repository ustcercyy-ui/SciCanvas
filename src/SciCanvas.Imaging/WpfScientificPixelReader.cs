using System.Security.Cryptography;
using System.IO;
using System.Text;
using SciCanvas.Core.Channels;
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

        ScientificSampleType sampleType = source.Metadata.BitsPerChannel > 8
            ? ScientificSampleType.UInt16
            : ScientificSampleType.UInt8;
        IReadOnlyList<ScientificChannelDescriptor> descriptors = CreateDescriptors(source, channel, sampleType);
        IReadOnlyList<ImagePlane> planes = WpfImagePlaneReader.ReadPlanes(
            source,
            frameIndex,
            region,
            descriptors,
            sourceRevision: null,
            cancellationToken);
        double maximum = Math.Pow(2, source.Metadata.BitsPerChannel) - 1;
        double[] values = new double[checked(planes[0].Width * planes[0].Height)];
        for (int index = 0; index < values.Length; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            values[index] = channel switch
            {
                ImageAnalysisChannel.Luminance when planes.Count == 3 =>
                    (planes[0].RawSamples.GetValue(index) * 2126 +
                     planes[1].RawSamples.GetValue(index) * 7152 +
                     planes[2].RawSamples.GetValue(index) * 722) / 10_000,
                _ => planes[0].RawSamples.GetValue(index),
            };
        }

        return new ScientificPixelBuffer(
            planes[0].Width,
            planes[0].Height,
            source.Metadata.BitsPerChannel,
            maximum,
            values);
    }

    private static IReadOnlyList<ScientificChannelDescriptor> CreateDescriptors(
        SourceAsset source,
        ImageAnalysisChannel channel,
        ScientificSampleType sampleType)
    {
        if (source.Metadata.Channels == 1)
        {
            if (channel == ImageAnalysisChannel.Alpha)
            {
                throw new InvalidDataException("单通道源素材不包含可供科学分析的原始 Alpha plane。");
            }

            return [CreateDescriptor(0, "Intensity", sampleType, source.Metadata.BitsPerChannel, "#FFFFFFFF")];
        }

        if (channel == ImageAnalysisChannel.Luminance)
        {
            if (source.Metadata.Channels < 3)
            {
                throw new NotSupportedException("无法为没有明确 RGB component 映射的素材计算科学亮度。");
            }

            return
            [
                CreateDescriptor(0, "Red", sampleType, source.Metadata.BitsPerChannel, "#FFFF0000"),
                CreateDescriptor(1, "Green", sampleType, source.Metadata.BitsPerChannel, "#FF00FF00"),
                CreateDescriptor(2, "Blue", sampleType, source.Metadata.BitsPerChannel, "#FF0000FF"),
            ];
        }

        int componentIndex = channel switch
        {
            ImageAnalysisChannel.Red => 0,
            ImageAnalysisChannel.Green => 1,
            ImageAnalysisChannel.Blue => 2,
            ImageAnalysisChannel.Alpha when source.Metadata.Channels == 4 => 3,
            ImageAnalysisChannel.Alpha => throw new InvalidDataException(
                "源素材不包含可供科学分析的原始 Alpha plane。"),
            _ => throw new InvalidDataException("未知科学分析通道。"),
        };
        string color = channel switch
        {
            ImageAnalysisChannel.Red => "#FFFF0000",
            ImageAnalysisChannel.Green => "#FF00FF00",
            ImageAnalysisChannel.Blue => "#FF0000FF",
            _ => "#FFFFFFFF",
        };
        return [CreateDescriptor(componentIndex, channel.ToString(), sampleType, source.Metadata.BitsPerChannel, color)];
    }

    private static ScientificChannelDescriptor CreateDescriptor(
        int index,
        string name,
        ScientificSampleType sampleType,
        int bitDepth,
        string color) => new(
        CreateStableChannelId(index, name),
        index,
        name,
        ScientificChannelSourceKind.InterleavedComponent,
        sampleType,
        bitDepth,
        Role: "ScientificAnalysis",
        DefaultColor: color);

    private static Guid CreateStableChannelId(int index, string name)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"scicanvas-channel:{index}:{name}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}