using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;

namespace SciCanvas.Imaging;

/// <summary>Materializes an immutable composite-panel specification from typed raw planes.</summary>
internal static class WpfCompositePanelRenderer
{
    public static BitmapSource Render(
        IReadOnlyList<FigureChannelLayerExportItem> layers,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("复合面板至少需要一个通道层。", nameof(layers));
        }

        var inputs = new List<ScientificChannelCompositeInput>(layers.Count);
        foreach (FigureChannelLayerExportItem layer in layers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            layer.EnsureValid();
            ImagePlane plane = WpfImagePlaneReader.ReadPlanes(
                layer.Source,
                layer.FrameIndex,
                layer.SourceRect,
                [layer.ChannelSelector],
                layer.SourceRevision,
                cancellationToken)[0];
            inputs.Add(new ScientificChannelCompositeInput(plane, layer.DisplaySettings));
        }

        ScientificChannelCompositeResult composite = ScientificChannelComposite.Compose(inputs);
        byte[] pixels = new byte[checked(composite.Width * composite.Height * 4)];
        for (int index = 0; index < composite.Pixels.Count; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ScientificDisplayPixel pixel = composite.Pixels[index];
            int offset = index * 4;
            pixels[offset] = pixel.Blue8;
            pixels[offset + 1] = pixel.Green8;
            pixels[offset + 2] = pixel.Red8;
            pixels[offset + 3] = byte.MaxValue;
        }

        BitmapSource bitmap = BitmapSource.Create(
            composite.Width,
            composite.Height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            checked(composite.Width * 4));
        bitmap.Freeze();
        return bitmap;
    }
}
