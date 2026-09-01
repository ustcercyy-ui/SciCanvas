using System.Collections;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Channels;

public abstract class ImagePlaneSampleBuffer
{
    public abstract ScientificSampleType SampleType { get; }

    public abstract int Count { get; }

    public abstract double GetValue(int index);
}

public sealed class UInt8ImagePlaneSamples : ImagePlaneSampleBuffer, IReadOnlyList<byte>
{
    private readonly byte[] _values;

    public UInt8ImagePlaneSamples(IEnumerable<byte> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values.ToArray();
    }

    public override ScientificSampleType SampleType => ScientificSampleType.UInt8;

    public override int Count => _values.Length;

    public byte this[int index] => _values[index];

    public ReadOnlyMemory<byte> Memory => _values;

    public override double GetValue(int index) => _values[index];

    public IEnumerator<byte> GetEnumerator() => ((IEnumerable<byte>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

public sealed class UInt16ImagePlaneSamples : ImagePlaneSampleBuffer, IReadOnlyList<ushort>
{
    private readonly ushort[] _values;

    public UInt16ImagePlaneSamples(IEnumerable<ushort> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        _values = values.ToArray();
    }

    public override ScientificSampleType SampleType => ScientificSampleType.UInt16;

    public override int Count => _values.Length;

    public ushort this[int index] => _values[index];

    public ReadOnlyMemory<ushort> Memory => _values;

    public override double GetValue(int index) => _values[index];

    public IEnumerator<ushort> GetEnumerator() => ((IEnumerable<ushort>)_values).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _values.GetEnumerator();
}

/// <summary>A typed, single-channel raw plane bound to a source revision and source-pixel region.</summary>
public sealed record ImagePlane
{
    public ImagePlane(
        Guid assetId,
        long? sourceRevision,
        int frameIndex,
        PixelRect64 region,
        ScientificChannelDescriptor channel,
        ImagePlaneSampleBuffer rawSamples)
    {
        ArgumentNullException.ThrowIfNull(channel);
        ArgumentNullException.ThrowIfNull(rawSamples);
        channel.EnsureValid();
        if (assetId == Guid.Empty || sourceRevision < 0 || frameIndex < 0 ||
            channel.SampleType != rawSamples.SampleType ||
            region.Width > int.MaxValue || region.Height > int.MaxValue ||
            rawSamples.Count != checked((int)(region.Width * region.Height)))
        {
            throw new InvalidOperationException("原始图像平面与素材、修订、区域、通道或样本缓冲区不一致。");
        }

        AssetId = assetId;
        SourceRevision = sourceRevision;
        FrameIndex = frameIndex;
        Region = region;
        Channel = channel;
        RawSamples = rawSamples;
    }

    public Guid AssetId { get; }

    public long? SourceRevision { get; }

    public int FrameIndex { get; }

    public ScientificPlaneRef PlaneRef => new(
        AssetId,
        SourceRevision,
        ChannelPlaneSelector.FromDescriptor(FrameIndex, Channel));

    public PixelRect64 Region { get; }

    public int Width => checked((int)Region.Width);

    public int Height => checked((int)Region.Height);

    public int BitDepth => Channel.BitDepth;

    public ScientificSampleType SampleType => Channel.SampleType;

    public ScientificChannelDescriptor Channel { get; }

    public ImagePlaneSampleBuffer RawSamples { get; }

    public double GetRawValue(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        return RawSamples.GetValue(checked(y * Width + x));
    }
}

public sealed record ImagePlaneRequest(
    Guid AssetId,
    int FrameIndex,
    ScientificChannelDescriptor ChannelSelector,
    PixelRect64 Region,
    long? SourceRevision = null)
{
    public ChannelPlaneSelector PlaneSelector =>
        ChannelPlaneSelector.FromDescriptor(FrameIndex, ChannelSelector);

    public ScientificPlaneRef PlaneRef => new(AssetId, SourceRevision, PlaneSelector);

    public ImagePlaneRequest EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(ChannelSelector);
        ChannelSelector.EnsureValid();
        PlaneRef.EnsureValid();
        if (AssetId == Guid.Empty || FrameIndex < 0 || SourceRevision < 0)
        {
            throw new InvalidOperationException("图像平面请求必须包含有效素材、帧、通道和可选源修订。");
        }

        return this;
    }
}

public interface IImagePlaneReader
{
    ValueTask<ImagePlane> ReadAsync(
        SourceAsset source,
        ImagePlaneRequest request,
        CancellationToken cancellationToken = default);
}
