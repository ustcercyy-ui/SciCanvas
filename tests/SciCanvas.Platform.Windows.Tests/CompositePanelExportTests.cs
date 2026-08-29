using System.Buffers.Binary;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
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

    private static FigureChannelLayerExportItem CreateLayer(
        Guid groupId,
        SourceAsset source,
        Guid channelId,
        string name,
        string color)
    {
        var descriptor = new ScientificChannelDescriptor(
            channelId,
            0,
            name,
            ScientificChannelSourceKind.ExternalAsset,
            ScientificSampleType.UInt16,
            16,
            DefaultColor: color);
        return new FigureChannelLayerExportItem(
            groupId,
            source,
            1,
            new PixelRect64(0, 0, 2, 1),
            0,
            descriptor,
            new ChannelDisplaySettings(channelId, true, color, 1, 0, ushort.MaxValue, 1, false));
    }

    private static SourceAsset CreateSource(string path) => new(
        Guid.NewGuid(),
        Path.GetFileName(path),
        path,
        new SourceFingerprint(new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), new string('A', 64), null),
        new SciCanvas.Core.Images.ImageMetadata(new PixelSize64(2, 1), 1, 16, "Gray16"),
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
