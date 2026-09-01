using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class CompositePanelExportTests
{
    [Fact]
    public async Task ExportPng_RebuildsPseudocolorCompositeFromUInt16RawPlanes()
    {
        string root = Path.Combine(Path.GetTempPath(), "scicanvas-composite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string redPath = Path.Combine(root, "Ti.png");
            string greenPath = Path.Combine(root, "Al.png");
            WriteGray16Png(redPath, [0, ushort.MaxValue]);
            WriteGray16Png(greenPath, [ushort.MaxValue, 0]);
            SourceAsset red = CreateSource(redPath);
            SourceAsset green = CreateSource(greenPath);
            Guid groupId = Guid.NewGuid();
            Guid redId = Guid.NewGuid();
            Guid greenId = Guid.NewGuid();
            FigureChannelLayerExportItem[] layers =
            [
                CreateLayer(groupId, red, redId, "Ti", "#FFFF0000"),
                CreateLayer(groupId, green, greenId, "Al", "#FF00FF00"),
            ];
            var panel = new FigurePanelExportItem(
                red,
                new PixelRect64(0, 0, 2, 1),
                new PixelRect64(0, 0, 2, 1),
                string.Empty,
                true,
                ChannelLayers: layers);
            var document = new FigureExportDocument(2, 1, 96, [panel]);
            string output = Path.Combine(root, "composite.png");

            await new WpfFigureExporter().ExportAsync(document, output);

            BitmapSource image = Load(output);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[8];
            converted.CopyPixels(pixels, 8, 0);
            Assert.Equal(0, pixels[2]);
            Assert.Equal(255, pixels[1]);
            Assert.Equal(255, pixels[6]);
            Assert.Equal(0, pixels[5]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ExportPng_ResamplesRegisteredTargetOntoReferenceGridInsteadOfStretchingMappedCrop()
    {
        string root = Path.Combine(Path.GetTempPath(), "scicanvas-registered-composite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string referencePath = Path.Combine(root, "reference.png");
            string targetPath = Path.Combine(root, "target.png");
            WriteGray8Png(referencePath, [0, 0]);
            WriteGray8Png(targetPath, [0, byte.MaxValue, 0]);
            SourceAsset reference = CreateSource(referencePath, 2, 1, 8, "Gray8");
            SourceAsset target = CreateSource(targetPath, 3, 1, 8, "Gray8");
            Guid groupId = Guid.NewGuid();
            Guid referenceChannelId = Guid.NewGuid();
            Guid targetChannelId = Guid.NewGuid();
            SpatialMapping mapping = SpatialMapping.CreateTranslation(
                reference.Id,
                target.Id,
                1,
                1,
                1,
                0,
                DateTimeOffset.UnixEpoch);
            var referenceGrid = new RegisteredReferenceGrid(
                new ScientificPlaneRef(reference.Id, 1, ChannelPlaneSelector.ExternalAsset(0)),
                new PixelRect64(0, 0, 2, 1));
            var resampling = new RegisteredPlaneResamplingSpec(
                mapping,
                referenceGrid,
                target.Metadata.PixelSize,
                RegisteredInterpolation.Bilinear,
                RegisteredBorderPolicy.Transparent);
            FigureChannelLayerExportItem[] layers =
            [
                CreateLayer(
                    groupId,
                    reference,
                    referenceChannelId,
                    "reference",
                    "#FFFF0000",
                    new PixelRect64(0, 0, 2, 1),
                    ScientificSampleType.UInt8,
                    8),
                CreateLayer(
                    groupId,
                    target,
                    targetChannelId,
                    "target",
                    "#FF00FF00",
                    RegisteredPlaneResampler.CalculateSourceReadRegion(resampling),
                    ScientificSampleType.UInt8,
                    8,
                    resampling),
            ];
            Assert.NotEqual(layers[0].SourceRect, layers[1].SourceRect);
            Assert.Equal(layers[0].OutputWidth, layers[1].OutputWidth);
            var panel = new FigurePanelExportItem(
                reference,
                new PixelRect64(0, 0, 2, 1),
                new PixelRect64(0, 0, 2, 1),
                string.Empty,
                true,
                ChannelLayers: layers);
            string output = Path.Combine(root, "registered.png");

            await new WpfFigureExporter().ExportAsync(
                new FigureExportDocument(2, 1, 96, [panel]),
                output);

            BitmapSource image = Load(output);
            var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
            byte[] pixels = new byte[8];
            converted.CopyPixels(pixels, 8, 0);
            Assert.Equal(255, pixels[1]);
            Assert.Equal(0, pixels[5]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static FigureChannelLayerExportItem CreateLayer(
        Guid groupId,
        SourceAsset source,
        Guid channelId,
        string name,
        string color,
        PixelRect64? sourceRect = null,
        ScientificSampleType sampleType = ScientificSampleType.UInt16,
        int bitDepth = 16,
        RegisteredPlaneResamplingSpec? resampling = null)
    {
        var descriptor = new ScientificChannelDescriptor(
            channelId,
            0,
            name,
            ScientificChannelSourceKind.ExternalAsset,
            sampleType,
            bitDepth,
            DefaultColor: color);
        return new FigureChannelLayerExportItem(
            groupId,
            source,
            1,
            sourceRect ?? new PixelRect64(0, 0, 2, 1),
            0,
            descriptor,
            new ChannelDisplaySettings(
                channelId,
                true,
                color,
                1,
                0,
                bitDepth <= 8 ? byte.MaxValue : ushort.MaxValue,
                1,
                false),
            RegistrationResampling: resampling);
    }

    private static SourceAsset CreateSource(
        string path,
        int width = 2,
        int height = 1,
        int bitDepth = 16,
        string pixelFormat = "Gray16") => new(
        Guid.NewGuid(),
        Path.GetFileName(path),
        path,
        new SourceFingerprint(new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), new string('A', 64), null),
        new SciCanvas.Core.Images.ImageMetadata(
            new PixelSize64(width, height),
            1,
            bitDepth,
            pixelFormat),
        SourceLinkState.Verified);

    private static void WriteGray16Png(string path, IReadOnlyList<ushort> values)
    {
        byte[] pixels = new byte[values.Count * 2];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(index * 2, 2), values[index]);
        }

        BitmapSource bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Gray16, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static void WriteGray8Png(string path, IReadOnlyList<byte> values)
    {
        BitmapSource bitmap = BitmapSource.Create(
            values.Count,
            1,
            96,
            96,
            PixelFormats.Gray8,
            null,
            values.ToArray(),
            values.Count);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapSource Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapFrame frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }
}
