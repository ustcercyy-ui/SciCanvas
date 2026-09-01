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
    string BlendMode = "additive",
    RegisteredPlaneResamplingSpec? RegistrationResampling = null)
{
    public ScientificPlaneRef PlaneRef => new(
        Source.Id,
        SourceRevision,
        ChannelPlaneSelector.FromDescriptor(FrameIndex, ChannelSelector));

    public long OutputWidth => RegistrationResampling?.ReferenceGrid.Region.Width ?? SourceRect.Width;

    public long OutputHeight => RegistrationResampling?.ReferenceGrid.Region.Height ?? SourceRect.Height;

    public FigureChannelLayerExportItem EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Source);
        ArgumentNullException.ThrowIfNull(ChannelSelector);
        ArgumentNullException.ThrowIfNull(DisplaySettings);
        ChannelSelector.EnsureValid();
        PlaneRef.EnsureValid();
        DisplaySettings.EnsureValid();
        RegistrationResampling?.EnsureValid();
        if (GroupId == Guid.Empty || SourceRevision < 1 || FrameIndex < 0 ||
            FrameIndex >= Source.Metadata.FrameCount ||
            SourceRect.Right > Source.Metadata.PixelSize.Width ||
            SourceRect.Bottom > Source.Metadata.PixelSize.Height ||
            DisplaySettings.ChannelId != ChannelSelector.Id ||
            (RegistrationResampling is not null &&
             (RegistrationResampling.Mapping.TargetAssetId != Source.Id ||
              RegistrationResampling.Mapping.TargetRevision != SourceRevision ||
              RegistrationResampling.TargetPixelSize != Source.Metadata.PixelSize ||
              !Contains(
                  SourceRect,
                  RegisteredPlaneResampler.CalculateSourceReadRegion(RegistrationResampling)))) ||
            string.IsNullOrWhiteSpace(RenderMode) || RenderMode.Length > 64 ||
            string.IsNullOrWhiteSpace(BlendMode) || BlendMode.Length > 64)
        {
            throw new InvalidOperationException(
                "复合面板通道层必须包含有效组、源素材/revision、帧、裁剪、通道选择器、显示设置及匹配的可选配准重采样。");
        }

        return this;
    }

    private static bool Contains(PixelRect64 outer, PixelRect64 inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
