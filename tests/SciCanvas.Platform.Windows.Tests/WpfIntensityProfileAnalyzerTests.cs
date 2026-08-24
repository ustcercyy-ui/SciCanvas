using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfIntensityProfileAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_SamplesOriginalGray8PixelsAndPhysicalDistance()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "gradient.png");
        const int width = 16;
        const int height = 4;
        byte[] pixels = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                pixels[y * width + x] = (byte)(x * 17);
            }
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray8,
            palette: null,
            pixels,
            width);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using (var stream = new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            encoder.Save(stream);
        }

        Guid sourceId = Guid.NewGuid();
        var source = new SourceAsset(
            sourceId,
            "gradient.png",
            sourcePath,
            new SourceFingerprint(
                new FileInfo(sourcePath).Length,
                File.GetLastWriteTimeUtc(sourcePath),
                Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))),
                null),
            new CoreImageMetadata(new PixelSize64(width, height), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        var calibration = new SpatialCalibration(
            sourceId,
            0.5,
            0.5,
            "µm",
            CalibrationOrigin.Manual);

        IntensityProfileResult profile = await new WpfIntensityProfileAnalyzer().AnalyzeAsync(
            source,
            new MeasurementPoint(0, 2),
            new MeasurementPoint(15, 2),
            calibration,
            maximumSamples: 16);

        Assert.True(profile.IsValid);
        Assert.Equal(16, profile.Samples.Count);
        Assert.Equal(0, profile.Samples[0].NormalizedIntensity, 8);
        Assert.Equal(1, profile.Samples[^1].NormalizedIntensity, 8);
        Assert.Equal(0.5, profile.Mean, 8);
        Assert.Equal(7.5, profile.Samples[^1].PhysicalDistance!.Value, 8);
        Assert.Equal("µm", profile.DistanceUnit);
        Assert.Equal(8, profile.SourceBitDepth);
    }
}
