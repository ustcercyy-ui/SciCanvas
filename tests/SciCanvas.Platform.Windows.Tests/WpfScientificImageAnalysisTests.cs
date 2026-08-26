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

public sealed class WpfScientificImageAnalysisTests
{
    [Fact]
    public async Task RoiStatistics_UsesRawGray16ValuesWithoutEightBitQuantization()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "roi-gray16.tif");
        ushort[] values = [1001, 40_003, 65_535, 7];
        CreateTiff(path, 2, 2, PixelFormats.Gray16, values, 4);
        SourceAsset source = CreateSource(path, 2, 2, 1, 16, "Gray16");

        RoiStatisticsResult result = await new WpfRoiStatisticsAnalyzer().AnalyzeAsync(
            source,
            sourceRevision: 7,
            new PixelRect64(0, 0, 2, 2),
            histogramBinCount: 4);

        Assert.True(result.IsValid);
        Assert.Equal(7, result.SourceRevision);
        Assert.Equal(16, result.SourceBitDepth);
        Assert.Equal(7, result.Minimum);
        Assert.Equal(65_535, result.Maximum);
        Assert.Equal(values.Sum(value => (double)value), result.IntegratedIntensity);
        Assert.Equal(values.Average(value => (double)value), result.Mean, 10);
        Assert.Equal(4, result.Histogram.SampleCount);
        Assert.Equal(4, result.Histogram.Bins.Sum(bin => bin.Count));
        Assert.NotEqual(0, ((int)result.Mean) % 257);
    }

    [Fact]
    public async Task RoiStatistics_UsesExplicitRgbChannelSelection()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "roi-rgb.png");
        byte[] bgra =
        [
            3, 20, 200, 255,
            7, 40, 100, 255,
        ];
        CreatePng(path, 2, 1, PixelFormats.Bgra32, bgra, 8);
        SourceAsset source = CreateSource(path, 2, 1, 4, 8, "Bgra32");
        var analyzer = new WpfRoiStatisticsAnalyzer();

        RoiStatisticsResult red = await analyzer.AnalyzeAsync(
            source, 1, new PixelRect64(0, 0, 2, 1), ImageAnalysisChannel.Red);
        RoiStatisticsResult green = await analyzer.AnalyzeAsync(
            source, 1, new PixelRect64(0, 0, 2, 1), ImageAnalysisChannel.Green);
        RoiStatisticsResult blue = await analyzer.AnalyzeAsync(
            source, 1, new PixelRect64(0, 0, 2, 1), ImageAnalysisChannel.Blue);

        Assert.Equal(150, red.Mean);
        Assert.Equal(30, green.Mean);
        Assert.Equal(5, blue.Mean);
    }

    [Fact]
    public async Task LineProfile_PreservesRaw16BitSamplesAndAnisotropicDistance()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "line-gray16.tif");
        ushort[] values =
        [
            1001, 2002,
            3003, 40_003,
        ];
        CreateTiff(path, 2, 2, PixelFormats.Gray16, values, 4);
        Guid sourceId = Guid.NewGuid();
        SourceAsset source = CreateSource(path, 2, 2, 1, 16, "Gray16", sourceId);
        var calibration = new SpatialCalibration(
            sourceId,
            2,
            1,
            "µm",
            CalibrationOrigin.Manual);

        IntensityProfileResult result = await new WpfIntensityProfileAnalyzer().AnalyzeAsync(
            source,
            new MeasurementPoint(0, 0),
            new MeasurementPoint(1, 1),
            calibration,
            maximumSamples: 2,
            sourceRevision: 5);

        Assert.True(result.IsValid);
        Assert.Equal(5, result.SourceRevision);
        Assert.Equal(1001, result.Samples[0].RawIntensity);
        Assert.Equal(40_003, result.Samples[1].RawIntensity);
        Assert.Equal(Math.Sqrt(5), result.Samples[1].PhysicalDistance!.Value, 12);
        Assert.Equal(16, result.SourceBitDepth);
    }

    [Fact]
    public async Task ParticleAnalysis_PreservesRawGray16IntensityAndMorphology()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "particles-gray16.tif");
        ushort[] values =
        [
            1000, 1000, 1000, 1000,
            1000, 50003, 50003, 1000,
            1000, 50003, 50003, 1000,
        ];
        CreateTiff(path, 4, 3, PixelFormats.Gray16, values, 8);
        SourceAsset source = CreateSource(path, 4, 3, 1, 16, "Gray16");
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 4, 3),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 2);

        AssistedRegionAnalysisResult result = await new WpfAssistedRegionAnalyzer().AnalyzeAsync(
            source,
            options,
            sourceRevision: 9,
            channel: ImageAnalysisChannel.Luminance);

        AssistedRegionCandidate particle = Assert.Single(result.Candidates);
        Assert.True(result.IsValid);
        Assert.Equal(9, result.SourceRevision);
        Assert.Equal(16, result.SourceBitDepth);
        Assert.Equal(50003, particle.RawMeanIntensity);
        Assert.Equal(4, particle.AreaPixels);
        Assert.Equal(8, particle.PerimeterPixels);
        Assert.Equal(Math.Sqrt(8), particle.FeretMaximumPixels, 12);
        Assert.Equal(Math.PI / 4, particle.Circularity, 12);
        Assert.NotEqual(Math.Round(particle.RawMeanIntensity / 257) * 257, particle.RawMeanIntensity);
    }

    private static SourceAsset CreateSource(
        string path,
        int width,
        int height,
        int channels,
        int bitsPerChannel,
        string pixelFormat,
        Guid? id = null) => new(
        id ?? Guid.NewGuid(),
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
            pixelFormat),
        SourceLinkState.Verified);

    private static void CreateTiff(
        string path,
        int width,
        int height,
        PixelFormat format,
        Array pixels,
        int stride)
    {
        BitmapSource bitmap = BitmapSource.Create(
            width, height, 96, 96, format, null, pixels, stride);
        var encoder = new TiffBitmapEncoder { Compression = TiffCompressOption.Zip };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
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
