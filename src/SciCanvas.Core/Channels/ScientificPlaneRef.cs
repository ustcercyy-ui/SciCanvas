namespace SciCanvas.Core.Channels;

/// <summary>
/// Persisted selector for one scientific plane. FrameIndex addresses the encoded frame;
/// ComponentIndex addresses an interleaved component within that frame. Optional Z/C/T
/// coordinates reserve stable identity for richer scientific formats without claiming that
/// the current WPF reader can decode them yet.
/// </summary>
public sealed record ChannelPlaneSelector(
    ScientificChannelSourceKind SourceKind,
    int FrameIndex,
    int? ComponentIndex = null,
    int? ZIndex = null,
    int? CIndex = null,
    int? TIndex = null)
{
    public static ChannelPlaneSelector ExternalAsset(int frameIndex = 0) =>
        new(ScientificChannelSourceKind.ExternalAsset, frameIndex);

    public static ChannelPlaneSelector FramePlane(int frameIndex) =>
        new(ScientificChannelSourceKind.FramePlane, frameIndex);

    public static ChannelPlaneSelector InterleavedComponent(int frameIndex, int componentIndex) =>
        new(ScientificChannelSourceKind.InterleavedComponent, frameIndex, componentIndex);

    public ChannelPlaneSelector EnsureValid()
    {
        if (!Enum.IsDefined(SourceKind) || FrameIndex < 0 ||
            ComponentIndex is < 0 || ZIndex is < 0 || CIndex is < 0 || TIndex is < 0 ||
            (SourceKind == ScientificChannelSourceKind.InterleavedComponent) != ComponentIndex.HasValue)
        {
            throw new InvalidOperationException(
                "科研 plane selector 必须包含有效来源类型、帧和匹配的 interleaved component。");
        }

        return this;
    }

    public ScientificChannelDescriptor CreateChannelDescriptor(
        Guid channelId,
        string name,
        ScientificSampleType sampleType,
        int bitDepth,
        string? role,
        string defaultColor) => new ScientificChannelDescriptor(
            channelId,
            SourceKind == ScientificChannelSourceKind.InterleavedComponent
                ? ComponentIndex!.Value
                : 0,
            name,
            SourceKind,
            sampleType,
            bitDepth,
            Role: role,
            DefaultColor: defaultColor).EnsureValid();

    public static ChannelPlaneSelector FromDescriptor(
        int frameIndex,
        ScientificChannelDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        descriptor.EnsureValid();
        return new ChannelPlaneSelector(
            descriptor.SourceKind,
            frameIndex,
            descriptor.SourceKind == ScientificChannelSourceKind.InterleavedComponent
                ? descriptor.Index
                : null).EnsureValid();
    }
}

/// <summary>
/// Stable scientific-plane identity. LinkGroup remains spatial-asset scoped; channel and
/// raw-plane operations use this identity so multiple frames/components of one asset remain distinct.
/// </summary>
public sealed record ScientificPlaneRef(
    Guid AssetId,
    long? SourceRevision,
    ChannelPlaneSelector Selector)
{
    public ScientificChannelSourceKind SourceKind => Selector.SourceKind;

    public int FrameIndex => Selector.FrameIndex;

    public int? ComponentIndex => Selector.ComponentIndex;

    public int? ZIndex => Selector.ZIndex;

    public int? CIndex => Selector.CIndex;

    public int? TIndex => Selector.TIndex;

    public ScientificPlaneRef EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Selector);
        Selector.EnsureValid();
        if (AssetId == Guid.Empty || SourceRevision is < 1)
        {
            throw new InvalidOperationException(
                "ScientificPlaneRef 必须包含有效素材、可选源修订和 plane selector。");
        }

        return this;
    }
}
