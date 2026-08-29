using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Export;

/// <summary>
/// Immutable raw-source specification for one display-only layer of a composite panel.
/// The display settings affect rendering only; scientific analysis continues to use the
/// typed raw plane identified by <see cref="ChannelSelector"/>.
/// </summary>
public sealed record FigureChannelLayerExportItem(
    Guid GroupId,
    SourceAsset Source,
    long SourceRevision,
    PixelRect64 SourceRect,
    int FrameIndex,
    ScientificChannelDescriptor ChannelSelector,
    ChannelDisplaySettings DisplaySettings,
    string RenderMode = "pseudocolor",
    string BlendMode = "additive")
{
    public FigureChannelLayerExportItem EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(ChannelSelector);
        ArgumentNullException.ThrowIfNull(DisplaySettings);
        ChannelSelector.EnsureValid();
        DisplaySettings.EnsureValid();
        if (GroupId == Guid.Empty || SourceRevision < 1 || FrameIndex < 0 ||
            FrameIndex >= Source.Metadata.FrameCount ||
            SourceRect.Right > Source.Metadata.PixelSize.Width ||
            SourceRect.Bottom > Source.Metadata.PixelSize.Height ||
            DisplaySettings.ChannelId != ChannelSelector.Id ||
            string.IsNullOrWhiteSpace(RenderMode) || RenderMode.Length > 64 ||
            string.IsNullOrWhiteSpace(BlendMode) || BlendMode.Length > 64)
        {
            throw new InvalidOperationException(
                "复合面板通道层必须包含有效组、源素材/revision、帧、裁剪、通道选择器和显示设置。");
        }

        return this;
    }
}
