using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Images;

namespace SciCanvas.Imaging;

/// <summary>
/// Renders an adjusted panel at source resolution into genuine RGB48 samples.
/// Raw/base loading is deliberately separate from the display-adjusted 8-bit
/// bitmap path used by previews and ordinary raster exports.
/// </summary>
internal static class WpfHighPrecisionPanelRenderer
{
    public static WpfHighPrecisionPanelImage Render(
        FigurePanelExportItem panel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ImageAdjustmentParameters adjustment = (panel.Adjustments ?? new()).Normalize();
        if (!adjustment.IsValid)
        {
            throw new InvalidOperationException(adjustment.ValidationMessage);
        }

        if (adjustment.Channel == "alpha")
        {
            throw new InvalidOperationException("16-bit RGB TIFF cannot represent the selected alpha-channel view.");
        }

        return panel.IsComposite
            ? RenderComposite(panel.EffectiveChannelLayers, adjustment, cancellationToken)
            : RenderRawCrop(panel, adjustment, cancellationToken);
    }

    private static WpfHighPrecisionPanelImage RenderRawCrop(
        FigurePanelExportItem panel,
        ImageAdjustmentParameters adjustment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BitmapSource rawCrop = WpfFigureExporter.LoadExactCrop(
            panel.Source.OriginalPath,
            panel.SourceRect,
            panel.FrameIndex);
        BitmapSource rgb48 = rawCrop.Format == PixelFormats.Rgb48
            ? rawCrop
            : new FormatConvertedBitmap(rawCrop, PixelFormats.Rgb48, null, 0);
        rgb48.Freeze();

        int stride = checked(rgb48.PixelWidth * 6);
        ushort[] samples = new ushort[checked(rgb48.PixelWidth * rgb48.PixelHeight * 3)];
        rgb48.CopyPixels(samples, stride, 0);
        ApplyAdjustments(samples, adjustment, cancellationToken);
        return new WpfHighPrecisionPanelImage(rgb48.PixelWidth, rgb48.PixelHeight, samples);
    }

    private static WpfHighPrecisionPanelImage RenderComposite(
        IReadOnlyList<FigureChannelLayerExportItem> layers,
        ImageAdjustmentParameters adjustment,
        CancellationToken cancellationToken)
    {
        ScientificChannelCompositeResult composite =
            WpfCompositePanelRenderer.ComposeHighPrecision(layers, cancellationToken);
        ushort[] samples = new ushort[checked(composite.Width * composite.Height * 3)];
        for (int index = 0; index < composite.Pixels.Count; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            ScientificDisplayPixel pixel = composite.Pixels[index];
            double red = pixel.Red;
            double green = pixel.Green;
            double blue = pixel.Blue;
            WpfImageAdjustmentMath.Apply(ref red, ref green, ref blue, 1, adjustment);
            int offset = index * 3;
            samples[offset] = ToUInt16(red);
            samples[offset + 1] = ToUInt16(green);
            samples[offset + 2] = ToUInt16(blue);
        }

        return new WpfHighPrecisionPanelImage(composite.Width, composite.Height, samples);
    }

    private static void ApplyAdjustments(
        ushort[] samples,
        ImageAdjustmentParameters adjustment,
        CancellationToken cancellationToken)
    {
        if (adjustment.IsIdentity)
        {
            return;
        }

        for (int index = 0; index < samples.Length / 3; index++)
        {
            if ((index & 0x3FFF) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            int offset = index * 3;
            double red = samples[offset] / (double)ushort.MaxValue;
            double green = samples[offset + 1] / (double)ushort.MaxValue;
            double blue = samples[offset + 2] / (double)ushort.MaxValue;
            WpfImageAdjustmentMath.Apply(ref red, ref green, ref blue, 1, adjustment);
            samples[offset] = ToUInt16(red);
            samples[offset + 1] = ToUInt16(green);
            samples[offset + 2] = ToUInt16(blue);
        }
    }

    private static ushort ToUInt16(double value) =>
        (ushort)Math.Round(Math.Clamp(value, 0, 1) * ushort.MaxValue);
}

internal sealed record WpfHighPrecisionPanelImage(
    int Width,
    int Height,
    ushort[] Rgb48Samples);
