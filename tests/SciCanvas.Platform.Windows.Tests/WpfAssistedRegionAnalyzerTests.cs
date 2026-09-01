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

    [Fact]
    public async Task AnalyzeAsync_UsesConvexCalipersForRotatedFeretWidth()
    {
        using var workspace = new TestWorkspace();
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "diagonal-particle.png",
            5,
            5,
            pixels =>
            {
                pixels[1 * 5 + 1] = 255;
                pixels[2 * 5 + 2] = 255;
                pixels[3 * 5 + 3] = 255;
            });
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 5, 5),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 3);

        AssistedRegionAnalysisResult result = await new WpfAssistedRegionAnalyzer().AnalyzeAsync(
            source,
            options);

        AssistedRegionCandidate particle = Assert.Single(result.Candidates);
        Assert.Equal(Math.Sqrt(18), particle.FeretMaximumPixels, 12);
        Assert.Equal(Math.Sqrt(2), particle.FeretMinimumPixels, 12);
        Assert.True(particle.FeretMinimumPixels < particle.Bounds.Width);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsRoiBeyondPixelSafetyBudgetBeforeReturningAResult()
    {
        using var workspace = new TestWorkspace();
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "pixel-budget.png",
            10,
            10,
            _ => { });
        var policy = AnalysisResourcePolicy.Default with
        {
            MaxPixels = 99,
            MaxComponentsSafety = 10,
            MaxBoundaryPoints = 100,
        };
        var analyzer = new WpfAssistedRegionAnalyzer(policy);
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 10, 10),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 1);

        AnalysisTooComplexException error = await Assert.ThrowsAsync<AnalysisTooComplexException>(
            () => analyzer.AnalyzeAsync(source, options));

        Assert.Equal(AnalysisResourceLimitKind.MaxPixels, error.LimitKind);
        Assert.Equal(100, error.Observed);
        Assert.Equal(99, error.Limit);
    }

    [Fact]
    public async Task AnalyzeAsync_RejectsEstimatedWorkingSetBeyondMemoryBudget()
    {
        using var workspace = new TestWorkspace();
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "memory-budget.png",
            4,
            4,
            _ => { });
        var policy = new AnalysisResourcePolicy(
            MaxPixels: 16,
            MaxComponentsSafety: 1,
            MaxBoundaryPoints: 4,
            MemoryBudgetBytes: 1);
        var analyzer = new WpfAssistedRegionAnalyzer(policy);
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 4, 4),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 1);

        AnalysisTooComplexException error = await Assert.ThrowsAsync<AnalysisTooComplexException>(
            () => analyzer.AnalyzeAsync(source, options));

        Assert.Equal(AnalysisResourceLimitKind.MemoryBudget, error.LimitKind);
        Assert.True(error.Observed > error.Limit);
    }

    [Fact]
    public async Task AnalyzeAsync_AbortsInsteadOfTruncatingQualifyingComponents()
    {
        using var workspace = new TestWorkspace();
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "component-budget.png",
            7,
            7,
            pixels =>
            {
                pixels[1 * 7 + 1] = 255;
                pixels[1 * 7 + 4] = 255;
                pixels[4 * 7 + 1] = 255;
            });
        var policy = new AnalysisResourcePolicy(
            MaxPixels: 49,
            MaxComponentsSafety: 2,
            MaxBoundaryPoints: 100,
            MemoryBudgetBytes: 8 * 1024 * 1024);
        var analyzer = new WpfAssistedRegionAnalyzer(policy);
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 7, 7),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 1);

        AnalysisTooComplexException error = await Assert.ThrowsAsync<AnalysisTooComplexException>(
            () => analyzer.AnalyzeAsync(source, options));

        Assert.Equal(AnalysisResourceLimitKind.MaxComponentsSafety, error.LimitKind);
        Assert.Equal(3, error.Observed);
        Assert.Equal(2, error.Limit);
        Assert.Contains("未返回残缺科研结果", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnalyzeAsync_AbortsWhenSingleComponentExceedsBoundarySupportBudget()
    {
        using var workspace = new TestWorkspace();
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "boundary-budget.png",
            3,
            5,
            pixels =>
            {
                for (int y = 0; y < 5; y++)
                {
                    pixels[y * 3 + 1] = 255;
                }
            });
        var policy = new AnalysisResourcePolicy(
            MaxPixels: 15,
            MaxComponentsSafety: 10,
            MaxBoundaryPoints: 8,
            MemoryBudgetBytes: 8 * 1024 * 1024);
        var analyzer = new WpfAssistedRegionAnalyzer(policy);
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, 3, 5),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 1);

        AnalysisTooComplexException error = await Assert.ThrowsAsync<AnalysisTooComplexException>(
            () => analyzer.AnalyzeAsync(source, options));

        Assert.Equal(AnalysisResourceLimitKind.MaxBoundaryPoints, error.LimitKind);
        Assert.Equal(12, error.Observed);
        Assert.Equal(8, error.Limit);
    }

    [Fact]
    public async Task AnalyzeAsync_RowExtremeHullFitsTightBudgetForNoisyConnectedComponent()
    {
        using var workspace = new TestWorkspace();
        const int size = 51;
        (_, SourceAsset source) = await CreateSourceAsync(
            workspace,
            "checkerboard-component.png",
            size,
            size,
            pixels =>
            {
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        if ((x + y) % 2 == 0)
                        {
                            pixels[y * size + x] = 255;
                        }
                    }
                }
            });
        var policy = new AnalysisResourcePolicy(
            MaxPixels: size * size,
            MaxComponentsSafety: 1,
            MaxBoundaryPoints: size * 4,
            MemoryBudgetBytes: 8 * 1024 * 1024);
        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode.BrightParticles,
            new PixelRect64(0, 0, size, size),
            UseAutomaticThreshold: false,
            ThresholdNormalized: 0.5,
            MinimumAreaPixels: 1);

        AssistedRegionAnalysisResult result =
            await new WpfAssistedRegionAnalyzer(policy).AnalyzeAsync(source, options);

        AssistedRegionCandidate candidate = Assert.Single(result.Candidates);
        Assert.Equal((size * size + 1) / 2, candidate.AreaPixels);
        Assert.Equal(Math.Sqrt(2 * size * size), candidate.FeretMaximumPixels, 10);
        Assert.Equal(policy, result.ResourcePolicy);
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
