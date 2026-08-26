using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class NextStageImagingTests
{
    [Fact]
    public async Task ExportAsync_WritesRgb48TiffWithoutEightBitQuantization()
    {
        using var workspace = new TemporaryWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "gray16.tif");
        string targetPath = Path.Combine(workspace.Root, "figure16.tif");
        ushort[] sourceValues = [1001, 40_003];
        CreateGray16Tiff(sourcePath, 2, 1, sourceValues);
        SourceAsset source = CreateAsset(sourcePath, 2, 1, bitsPerChannel: 16, pixelFormat: "Gray16");
        var document = new FigureExportDocument(
            2,
            1,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 2, 1),
                new PixelRect64(0, 0, 2, 1),
                string.Empty,
                true)],
            bitDepth: 16);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapFrame frame = LoadFrame(targetPath);
        Assert.Equal(48, frame.Format.BitsPerPixel);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Rgb48, null, 0);
        ushort[] pixels = new ushort[6];
        converted.CopyPixels(pixels, stride: 12, offset: 0);
        Assert.InRange(pixels[0], 990, 1010);
        Assert.InRange(pixels[3], 39_990, 40_015);
        Assert.NotEqual(0, pixels[0] % 257);
        Assert.NotEqual(0, pixels[3] % 257);
    }

    [Fact]
    public async Task MetadataProbe_ReadsNormalizedOmeXmlFromTiffDescription()
    {
        using var workspace = new TemporaryWorkspace();
        string path = Path.Combine(workspace.Root, "ome.tif");
        const string xml = "<?xml version=\"1.0\"?><ome:OME xmlns:ome=\"http://www.openmicroscopy.org/Schemas/OME/2016-06\"><ome:Image ID=\"Image:0\"><ome:Pixels ID=\"Pixels:0\" DimensionOrder=\"XYZCT\" Type=\"uint16\" SizeX=\"2\" SizeY=\"2\" SizeZ=\"3\" SizeC=\"2\" SizeT=\"4\" PhysicalSizeX=\"0.25\" PhysicalSizeXUnit=\"µm\" PhysicalSizeY=\"0.25\" PhysicalSizeYUnit=\"µm\"><ome:Channel ID=\"Channel:0:0\" Name=\"DAPI\"/><ome:Channel ID=\"Channel:0:1\" Name=\"FITC\"/></ome:Pixels></ome:Image></ome:OME>";
        CreateTiffWithDescription(path, xml);

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        CoreImageMetadata metadata = await new WpfImageMetadataProbe().ProbeAsync(stream, path, CancellationToken.None);

        Assert.NotNull(metadata.Ome);
        Assert.Equal("XYZCT", metadata.Ome!.DimensionOrder);
        Assert.Equal("uint16", metadata.Ome.PixelType);
        Assert.Equal(3, metadata.Ome.SizeZ);
        Assert.Equal(2, metadata.Ome.SizeC);
        Assert.Equal(4, metadata.Ome.SizeT);
        Assert.Equal(["DAPI", "FITC"], metadata.Ome.ChannelNames);
        Assert.Matches("^[0-9A-F]{64}$", metadata.Ome.XmlSha256);
        Assert.Equal(0.25, metadata.PhysicalSizeX);
        Assert.Equal(0.25, metadata.PhysicalSizeY);
        Assert.Equal("µm", metadata.PhysicalUnit);

        SourceAsset source = CreateAsset(path, 2, 2, bitsPerChannel: 8, pixelFormat: "Gray8") with
        {
            Metadata = metadata,
        };
        BitmapSource preview = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Gray8, null, new byte[4], 2);
        preview.Freeze();
        var sourceItem = new SciCanvas.Presentation.SourceAssetItemViewModel(source, preview);
        Assert.True(sourceItem.Calibration.IsCalibrated);
        Assert.Equal(CalibrationOrigin.Metadata, sourceItem.Calibration.Origin);
        Assert.Equal(0.25, sourceItem.Calibration.UnitsPerPixelX);
        Assert.Equal(0.25, sourceItem.Calibration.UnitsPerPixelY);
    }

    private static SourceAsset CreateAsset(
        string path,
        int width,
        int height,
        int bitsPerChannel,
        string pixelFormat) => new(
        Guid.NewGuid(),
        Path.GetFileName(path),
        path,
        new SourceFingerprint(
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            null),
        new CoreImageMetadata(new PixelSize64(width, height), 1, bitsPerChannel, pixelFormat),
        SourceLinkState.Verified);

    private static void CreateGray16Tiff(string path, int width, int height, ushort[] pixels)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray16,
            null,
            pixels,
            width * 2);
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static void CreateTiffWithDescription(string path, string description)
    {
        byte[] pixels = [0, 64, 128, 255];
        BitmapSource bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Gray8, null, pixels, 2);
        var metadata = new BitmapMetadata("tiff");
        metadata.SetQuery("/ifd/{ushort=270}", description);
        var encoder = new TiffBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap, null, metadata, null));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapFrame LoadFrame(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapFrame frame = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "SciCanvas.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
