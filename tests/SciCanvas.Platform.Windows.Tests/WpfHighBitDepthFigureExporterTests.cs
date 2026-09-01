using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfHighBitDepthFigureExporterTests
{
    [Fact]
    public async Task Export16BitTiff_AppliesNonIdentityAdjustmentExactlyOnce()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "gray16.png");
        string outputPath = Path.Combine(workspace.Root, "adjusted.tiff");
        WriteGray16Png(sourcePath, 1, 1, [20_000]);
        var adjustment = new ImageAdjustmentParameters
        {
            Brightness = 0.05,
            Contrast = 0.25,
            Gamma = 1.8,
            BlackPoint = 0.1,
            WhitePoint = 0.9,
        };
        FigureExportDocument document = CreateDocument(
            CreateSource(sourcePath, 1, 1, 1, 16, "Gray16"),
            1,
            1,
            adjustment);

        await new WpfFigureExporter().ExportAsync(document, outputPath);

        BitmapSource frame = LoadFirstFrame(outputPath);
        ushort[] samples = ReadRgb48(frame);
        Assert.Equal(48, frame.Format.BitsPerPixel);
        // 20000/65535 -> black/white -> contrast -> brightness -> gamma = 30040.
        Assert.Equal(new ushort[] { 30_040, 30_040, 30_040 }, samples);
        // Applying the same adjustment a second time would produce 43840.
        Assert.DoesNotContain((ushort)43_840, samples);
    }

    [Theory]
    [InlineData("rgb", true, false, 64_535, 63_535, 62_535)]
    [InlineData("rgb", false, true, 1_860, 1_860, 1_860)]
    [InlineData("red", false, false, 1_000, 0, 0)]
    [InlineData("green", false, false, 0, 2_000, 0)]
    [InlineData("blue", false, false, 0, 0, 3_000)]
    public async Task Export16BitTiff_PreservesAdjustmentSemantics(
        string channel,
        bool invert,
        bool grayscale,
        int expectedRed,
        int expectedGreen,
        int expectedBlue)
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, $"rgb48-{channel}-{invert}-{grayscale}.tiff");
        string outputPath = Path.Combine(workspace.Root, "adjusted.tiff");
        WriteRgb48Tiff(sourcePath, 1, 1, [1_000, 2_000, 3_000]);
        var adjustment = new ImageAdjustmentParameters
        {
            Channel = channel,
            Invert = invert,
            Grayscale = grayscale,
        };
        FigureExportDocument document = CreateDocument(
            CreateSource(sourcePath, 1, 1, 3, 16, "Rgb48"),
            1,
            1,
            adjustment);

        await new WpfFigureExporter().ExportAsync(document, outputPath);

        Assert.Equal(
            new ushort[] { (ushort)expectedRed, (ushort)expectedGreen, (ushort)expectedBlue },
            ReadRgb48(LoadFirstFrame(outputPath)));
    }

    [Fact]
    public async Task Export16BitComposite_RetainsPrecisionBeyondEightBits()
    {
        using var workspace = new TestWorkspace();
        ushort[] raw = [1_000, 1_001, 32_768, 50_000];
        string sourcePath = Path.Combine(workspace.Root, "channel.png");
        string outputPath = Path.Combine(workspace.Root, "composite.tiff");
        WriteGray16Png(sourcePath, 4, 1, raw);
        SourceAsset source = CreateSource(sourcePath, 4, 1, 1, 16, "Gray16");
        Guid groupId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        var descriptor = new ScientificChannelDescriptor(
            channelId,
            0,
            "Raw",
            ScientificChannelSourceKind.ExternalAsset,
            ScientificSampleType.UInt16,
            16,
            DefaultColor: "#FFFFFFFF");
        var layer = new FigureChannelLayerExportItem(
            groupId,
            source,
            1,
            new PixelRect64(0, 0, 4, 1),
            0,
            descriptor,
            new ChannelDisplaySettings(
                channelId,
                true,
                "#FFFFFFFF",
                1,
                0,
                ushort.MaxValue,
                1,
                false));
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(0, 0, 4, 1),
            new PixelRect64(0, 0, 4, 1),
            string.Empty,
            true,
            ChannelLayers: [layer]);
        var document = new FigureExportDocument(4, 1, 96, [panel], bitDepth: 16);

        await new WpfFigureExporter().ExportAsync(document, outputPath);

        ushort[] actual = ReadRgb48(LoadFirstFrame(outputPath));
        ushort[] expected = raw.SelectMany(value => new[] { value, value, value }).ToArray();
        Assert.Equal(expected, actual);
        Assert.False(actual.All(sample => sample % 257 == 0),
            "RGB48 composite samples still look quantized through an 8-bit boundary.");
        Assert.Equal(1, actual[3] - actual[0]);
    }

    private static FigureExportDocument CreateDocument(
        SourceAsset source,
        int width,
        int height,
        ImageAdjustmentParameters adjustment)
    {
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(0, 0, width, height),
            new PixelRect64(0, 0, width, height),
            string.Empty,
            true,
            Adjustments: adjustment);
        return new FigureExportDocument(width, height, 96, [panel], bitDepth: 16);
    }

    private static SourceAsset CreateSource(
        string path,
        int width,
        int height,
        int channels,
        int bitsPerChannel,
        string pixelFormat) => new(
        Guid.NewGuid(),
        Path.GetFileName(path),
        path,
        new SourceFingerprint(
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            new string('A', 64),
            null),
        new CoreImageMetadata(
            new PixelSize64(width, height),
            channels,
            bitsPerChannel,
            pixelFormat),
        SourceLinkState.Verified);

    private static void WriteGray16Png(
        string path,
        int width,
        int height,
        IReadOnlyList<ushort> values)
    {
        byte[] pixels = new byte[checked(values.Count * 2)];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(index * 2, 2), values[index]);
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray16,
            null,
            pixels,
            checked(width * 2));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static void WriteRgb48Tiff(
        string path,
        int width,
        int height,
        ushort[] samples)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Rgb48,
            null,
            samples,
            checked(width * 6));
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapSource LoadFirstFrame(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapFrame frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private static ushort[] ReadRgb48(BitmapSource source)
    {
        BitmapSource rgb48 = source.Format == PixelFormats.Rgb48
            ? source
            : new FormatConvertedBitmap(source, PixelFormats.Rgb48, null, 0);
        rgb48.Freeze();
        ushort[] samples = new ushort[checked(rgb48.PixelWidth * rgb48.PixelHeight * 3)];
        rgb48.CopyPixels(samples, checked(rgb48.PixelWidth * 6), 0);
        return samples;
    }
}
