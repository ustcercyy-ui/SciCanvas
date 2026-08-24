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

public sealed class WpfAssistedRegionAnalyzerTests
{
    [Fact]
    public async Task AnalyzeAsync_FindsBrightComponentsAndReportsAreaFraction()
    {
        using var workspace = new TestWorkspace();
        (string path, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "particles.png",
            20,
            20,
            pixels =>
            {
                FillRectangle(pixels, 20, 2, 2, 4, 4, 255);
                FillRectangle(pixels, 20, 10, 10, 3, 3, 255);
            });
        _ = path;
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 20, 20),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 4);

        AssistedRegionAnalysisResult result = await new WpfAssistedRegionAnalyzer().AnalyzeAsync(
            source,
            options);

        Assert.True(result.IsValid);
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal([16, 9], result.Candidates.Select(candidate => candidate.AreaPixels).ToArray());
        Assert.Equal(25, result.ForegroundPixelCount);
        Assert.Equal(0.0625, result.AreaFraction, 8);
        Assert.Equal(16, result.Candidates[0].PerimeterPixels);
        Assert.Equal(4, result.Candidates[0].EquivalentDiameterPixels * Math.Sqrt(Math.PI) / 2, 8);
        Assert.Equal(WpfAssistedRegionAnalyzer.AnalyzerVersion, result.AnalyzerId);
    }

    [Fact]
    public async Task AnalyzeAsync_CrackModeKeepsOnlyElongatedDarkComponents()
    {
        using var workspace = new TestWorkspace();
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "cracks.png",
            16,
            12,
            pixels =>
            {
                Array.Fill(pixels, (byte)255);
                FillRectangle(pixels, 16, 2, 2, 8, 1, 0);
                FillRectangle(pixels, 16, 12, 7, 2, 2, 0);
            });
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.DarkCracks,
            new PixelRect64(0, 0, 16, 12),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 2);

        AssistedRegionAnalysisResult result = await new WpfAssistedRegionAnalyzer().AnalyzeAsync(
            source,
            options);

        AssistedRegionCandidate crack = Assert.Single(result.Candidates);
        Assert.Equal(8, crack.AreaPixels);
        Assert.Equal(8, crack.AspectRatio);
        Assert.Equal(new PixelRect64(2, 2, 8, 1), crack.Bounds);
    }

    private static async Task<(string Path, SourceAsset Source)> CreateSourceAsync(
        TestWorkspace workspace,
        string fileName,
        int width,
        int height,
        Action<byte[]> configure)
    {
        string path = Path.Combine(workspace.Root, fileName);
        byte[] pixels = new byte[width * height];
        configure(pixels);
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
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            encoder.Save(output);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(path);
        var source = new SourceAsset(
            Guid.NewGuid(),
            fileName,
            path,
            new SourceFingerprint(
                fileBytes.LongLength,
                File.GetLastWriteTimeUtc(path),
                Convert.ToHexString(SHA256.HashData(fileBytes)),
                null),
            new CoreImageMetadata(new PixelSize64(width, height), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        return (path, source);
    }

    private static void FillRectangle(
        byte[] pixels,
        int stride,
        int x,
        int y,
        int width,
        int height,
        byte value)
    {
        for (int row = y; row < y + height; row++)
        {
            for (int column = x; column < x + width; column++)
            {
                pixels[row * stride + column] = value;
            }
        }
    }
}
