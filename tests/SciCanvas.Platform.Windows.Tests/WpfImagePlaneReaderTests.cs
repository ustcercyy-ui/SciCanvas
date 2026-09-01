using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfImagePlaneReaderTests
{
    [Fact]
    public async Task ReadAsync_PreservesGray16SamplesRegionAndSourceRevision()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "raw-gray16.tif");
        ushort[] values =
        [
            1001, 40_003, 65_535,
            7, 8193, 32_769,
        ];
        CreateTiff(path, 3, 2, PixelFormats.Gray16, [values], stride: 6);
        SourceAsset source = CreateSource(path, 3, 2, 1, 16, "Gray16");
        ScientificChannelDescriptor channel = CreateChannel(
            0, "Intensity", ScientificChannelSourceKind.ExternalAsset,
            ScientificSampleType.UInt16, 16, "#FFFFFFFF");
        var request = new ImagePlaneRequest(
            source.Id,
            FrameIndex: 0,
            channel,
            new PixelRect64(1, 0, 2, 2),
            SourceRevision: 12);

        ImagePlane plane = await new WpfImagePlaneReader().ReadAsync(source, request);

        Assert.Equal(2, plane.Width);
        Assert.Equal(2, plane.Height);
        Assert.Equal(12, plane.SourceRevision);
        Assert.Equal(new PixelRect64(1, 0, 2, 2), plane.Region);
        UInt16ImagePlaneSamples raw = Assert.IsType<UInt16ImagePlaneSamples>(plane.RawSamples);
        Assert.Equal(new ushort[] { 40_003, 65_535, 8193, 32_769 }, raw);
        Assert.NotEqual(0, raw[0] % 257);
    }

    [Fact]
    public async Task ReadAsync_ExtractsSemanticRgbComponentWithoutDisplayProcessing()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "components.png");
        byte[] bgra =
        [
            3, 20, 200, 255,
            7, 40, 100, 128,
        ];
        CreatePng(path, 2, 1, PixelFormats.Bgra32, bgra, stride: 8);
        SourceAsset source = CreateSource(path, 2, 1, 4, 8, "Bgra32");
        ScientificChannelDescriptor green = CreateChannel(
            1, "Green", ScientificChannelSourceKind.InterleavedComponent,
            ScientificSampleType.UInt8, 8, "#FF00FF00");
        var request = new ImagePlaneRequest(
            source.Id,
            FrameIndex: 0,
            green,
            new PixelRect64(0, 0, 2, 1));

        ImagePlane plane = await new WpfImagePlaneReader().ReadAsync(source, request);

        UInt8ImagePlaneSamples raw = Assert.IsType<UInt8ImagePlaneSamples>(plane.RawSamples);
        Assert.Equal(new byte[] { 20, 40 }, raw);
        Assert.Equal(20, plane.GetRawValue(0, 0));
        Assert.Equal(40, plane.GetRawValue(1, 0));
    }

    [Fact]
    public async Task ReadAsync_ExtractsAllThreeComponentsFromExactTwoPixelRgbFixture()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "rgb-2x1.png");
        CreatePng(
            path,
            width: 2,
            height: 1,
            PixelFormats.Rgb24,
            new byte[] { 10, 20, 30, 40, 50, 60 },
            stride: 6);
        SourceAsset source = CreateSource(path, 2, 1, 3, 8, "Rgb24");
        var reader = new WpfImagePlaneReader();

        ImagePlane red = await reader.ReadAsync(
            source,
            new ImagePlaneRequest(
                source.Id,
                0,
                CreateChannel(0, "R", ScientificChannelSourceKind.InterleavedComponent,
                    ScientificSampleType.UInt8, 8, "#FFFF0000"),
                new PixelRect64(0, 0, 2, 1),
                SourceRevision: 4));
        ImagePlane green = await reader.ReadAsync(
            source,
            new ImagePlaneRequest(
                source.Id,
                0,
                CreateChannel(1, "G", ScientificChannelSourceKind.InterleavedComponent,
                    ScientificSampleType.UInt8, 8, "#FF00FF00"),
                new PixelRect64(0, 0, 2, 1),
                SourceRevision: 4));
        ImagePlane blue = await reader.ReadAsync(
            source,
            new ImagePlaneRequest(
                source.Id,
                0,
                CreateChannel(2, "B", ScientificChannelSourceKind.InterleavedComponent,
                    ScientificSampleType.UInt8, 8, "#FF0000FF"),
                new PixelRect64(0, 0, 2, 1),
                SourceRevision: 4));

        Assert.Equal(new byte[] { 10, 40 }, Assert.IsType<UInt8ImagePlaneSamples>(red.RawSamples));
        Assert.Equal(new byte[] { 20, 50 }, Assert.IsType<UInt8ImagePlaneSamples>(green.RawSamples));
        Assert.Equal(new byte[] { 30, 60 }, Assert.IsType<UInt8ImagePlaneSamples>(blue.RawSamples));
        Assert.Equal(
            ChannelPlaneSelector.InterleavedComponent(0, 2),
            blue.PlaneRef.Selector);
    }

    [Fact]
    public async Task ReadAsync_PreservesRgb48ComponentOrderAndPrecision()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "rgb48.tif");
        ushort[] rgb =
        [
            1001, 2002, 3003,
            40_004, 50_005, 60_006,
        ];
        CreateTiff(path, 2, 1, PixelFormats.Rgb48, [rgb], stride: 12);
        SourceAsset source = CreateSource(path, 2, 1, 3, 16, "Rgb48");
        ScientificChannelDescriptor blue = CreateChannel(
            2, "Blue", ScientificChannelSourceKind.InterleavedComponent,
            ScientificSampleType.UInt16, 16, "#FF0000FF");

        ImagePlane plane = await new WpfImagePlaneReader().ReadAsync(
            source,
            new ImagePlaneRequest(source.Id, 0, blue, new PixelRect64(0, 0, 2, 1)));

        UInt16ImagePlaneSamples raw = Assert.IsType<UInt16ImagePlaneSamples>(plane.RawSamples);
        Assert.Equal(new ushort[] { 3003, 60_006 }, raw);
        Assert.NotEqual(0, raw[0] % 257);
    }
    [Fact]
    public async Task ReadAsync_UsesExplicitFrameRequestWithoutAssumingOmeChannelMapping()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "frames.tif");
        CreateTiff(
            path,
            2,
            1,
            PixelFormats.Gray16,
            [new ushort[] { 1001, 2002 }, new ushort[] { 30_003, 40_004 }],
            stride: 4);
        SourceAsset source = CreateSource(path, 2, 1, 1, 16, "Gray16", frameCount: 2);
        ScientificChannelDescriptor planeChannel = CreateChannel(
            0, "Explicit plane", ScientificChannelSourceKind.FramePlane,
            ScientificSampleType.UInt16, 16, "#FFFFFFFF");

        ImagePlane secondFrame = await new WpfImagePlaneReader().ReadAsync(
            source,
            new ImagePlaneRequest(source.Id, 1, planeChannel, new PixelRect64(0, 0, 2, 1)));

        UInt16ImagePlaneSamples raw = Assert.IsType<UInt16ImagePlaneSamples>(secondFrame.RawSamples);
        Assert.Equal(new ushort[] { 30_003, 40_004 }, raw);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await new WpfImagePlaneReader().ReadAsync(
            source,
            new ImagePlaneRequest(source.Id, 2, planeChannel, new PixelRect64(0, 0, 2, 1))));
    }

    [Fact]
    public async Task ReadAsync_RejectsDescriptorThatMisstatesRawSampleType()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "mismatch.tif");
        CreateTiff(path, 1, 1, PixelFormats.Gray16, [new ushort[] { 1001 }], stride: 2);
        SourceAsset source = CreateSource(path, 1, 1, 1, 16, "Gray16");
        ScientificChannelDescriptor invalid = CreateChannel(
            0, "Wrong", ScientificChannelSourceKind.ExternalAsset,
            ScientificSampleType.UInt8, 8, "#FFFFFFFF");

        await Assert.ThrowsAsync<InvalidDataException>(async () => await new WpfImagePlaneReader().ReadAsync(
            source,
            new ImagePlaneRequest(source.Id, 0, invalid, new PixelRect64(0, 0, 1, 1))));
    }

    private static ScientificChannelDescriptor CreateChannel(
        int index,
        string name,
        ScientificChannelSourceKind sourceKind,
        ScientificSampleType sampleType,
        int bitDepth,
        string color) => new(
        Guid.NewGuid(),
        index,
        name,
        sourceKind,
        sampleType,
        bitDepth,
        DefaultColor: color);

    private static SourceAsset CreateSource(
        string path,
        int width,
        int height,
        int channels,
        int bitsPerChannel,
        string pixelFormat,
        int frameCount = 1) => new(
        Guid.NewGuid(),
        Path.GetFileName(path),
        path,
        new SourceFingerprint(
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            null),
        new CoreImageMetadata(
            new PixelSize64(width, height),
            channels,
            bitsPerChannel,
            pixelFormat,
            frameCount: frameCount),
        SourceLinkState.Verified);

    private static void CreateTiff(
        string path,
        int width,
        int height,
        PixelFormat format,
        IReadOnlyList<Array> frames,
        int stride)
    {
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
        foreach (Array pixels in frames)
        {
            BitmapSource bitmap = BitmapSource.Create(
                width, height, 96, 96, format, null, pixels, stride);
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
        }

        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static void CreatePng(
        string path,
        int width,
        int height,
        PixelFormat format,
        Array pixels,
        int stride)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, format, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }
}
