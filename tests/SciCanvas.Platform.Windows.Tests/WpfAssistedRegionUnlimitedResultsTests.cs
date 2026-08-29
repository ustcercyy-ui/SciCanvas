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

public sealed class WpfAssistedRegionUnlimitedResultsTests
{
    [Fact]
    public async Task AnalyzeAsync_ReturnsAllQualifyingComponentsBeyondLegacyThousandLimit()
    {
        using var workspace = new TestWorkspace();
        string path = Path.Combine(workspace.Root, "many-particles.png");
        const int width = 100;
        const int height = 100;
        byte[] pixels = new byte[width * height];
        for (int y = 1; y < height; y += 3)
        {
            for (int x = 1; x < width; x += 3)
            {
                pixels[y * width + x] = byte.MaxValue;
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
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            encoder.Save(output);
        }

        byte[] fileBytes = await File.ReadAllBytesAsync(path);
        var source = new SourceAsset(
            Guid.NewGuid(),
            "many-particles.png",
            path,
            new SourceFingerprint(
                fileBytes.LongLength,
                File.GetLastWriteTimeUtc(path),
                Convert.ToHexString(SHA256.HashData(fileBytes)),
                null),
            new CoreImageMetadata(new PixelSize64(width, height), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, width, height),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 1);

        AssistedRegionAnalysisResult result = await new WpfAssistedRegionAnalyzer().AnalyzeAsync(
            source,
            options);

        Assert.Equal(1089, result.Candidates.Count);
        Assert.Equal(Enumerable.Range(1, 1089), result.Candidates.Select(candidate => candidate.Id));
    }
}
