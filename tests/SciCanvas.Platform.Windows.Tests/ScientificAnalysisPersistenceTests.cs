using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ScientificAnalysisPersistenceTests
{
    [Fact]
    public void AcceptRevision_MarksExistingAnalysisForReviewWithoutChangingProvenance()
    {
        SourceAssetItemViewModel source = CreateSource();
        source.AddAnalysisResult(CreateRoi(source.Asset.Id, source.SourceRevision));

        SourceAsset replacement = source.Asset with
        {
            Fingerprint = new SourceFingerprint(
                2,
                DateTimeOffset.UtcNow.AddMinutes(1),
                new string('C', 64),
                null),
        };
        source.AcceptRevision(replacement, source.Preview);

        RoiStatisticsResult result = Assert.IsType<RoiStatisticsResult>(Assert.Single(source.AnalysisResults));
        Assert.Equal(2, source.SourceRevision);
        Assert.Equal(1, result.SourceRevision);
        Assert.Equal(AnalysisResultState.ReviewRequired, result.Validity.State);
        Assert.False(result.IsCurrent(source.Asset.Id, source.SourceRevision));
        Assert.Contains("revision 1", result.Validity.Reasons[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JsonProjectStore_RoundTripsScientificAnalysis()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("source.tif", [1, 2, 3, 4]);
        string projectPath = Path.Combine(workspace.Root, "analysis.scicanvas");
        Guid sourceId = Guid.NewGuid();
        RoiStatisticsResult roi = CreateRoi(sourceId, 1);
        var snapshot = new ProjectScientificAnalysisSnapshot
        {
            Id = roi.Id,
            SourceAssetId = sourceId,
            SourceRevision = 1,
            Kind = "roiStatistics",
            FrameIndex = 0,
            Channel = "luminance",
            AnalyzerId = roi.AnalyzerId,
            AnalyzedAt = roi.AnalyzedAt,
            Validity = new ProjectScientificValiditySnapshot { State = "valid" },
            SourceBitDepth = 16,
            Region = new ProjectPixelRectSnapshot { X = 0, Y = 0, Width = 2, Height = 2 },
            PixelCount = 4,
            Minimum = 7,
            Maximum = 65535,
            Mean = 20000,
            StandardDeviation = 100,
            IntegratedIntensity = 80000,
            Histogram =
            [
                new ProjectIntensityHistogramBinSnapshot
                {
                    LowerBound = 0,
                    UpperBound = 65535,
                    Count = 4,
                },
            ],
        };
        var particleSnapshot = new ProjectScientificAnalysisSnapshot
        {
            Id = Guid.NewGuid(),
            SourceAssetId = sourceId,
            SourceRevision = 1,
            Kind = "particleAnalysis",
            FrameIndex = 0,
            Channel = "luminance",
            AnalyzerId = "test.particle.v2",
            AnalyzedAt = DateTimeOffset.UtcNow,
            Validity = new ProjectScientificValiditySnapshot { State = "valid" },
            SourceBitDepth = 16,
            Region = new ProjectPixelRectSnapshot { X = 0, Y = 0, Width = 2, Height = 2 },
            AnalysisMode = "brightParticles",
            UseAutomaticThreshold = false,
            ThresholdNormalized = 0.5,
            AppliedThresholdNormalized = 0.5,
            MinimumAreaPixels = 1,
            MaximumCandidates = 10,
            ForegroundPixelCount = 1,
            TotalPixelCount = 4,
            Particles =
            [
                new ProjectParticleSnapshot
                {
                    Id = 1,
                    Bounds = new ProjectPixelRectSnapshot { X = 0, Y = 0, Width = 1, Height = 1 },
                    CentroidX = 0,
                    CentroidY = 0,
                    AreaPixels = 1,
                    PerimeterPixels = 4,
                    MeanIntensity = 50003 / 65535d,
                    RawMeanIntensity = 50003,
                    AspectRatio = 1,
                    FeretMaximumPixels = Math.Sqrt(2),
                    FeretMinimumPixels = 1,
                },
            ],
        };
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Canvas = new ProjectCanvasSnapshot { Width = 2, Height = 2, Background = "white" },
            Sources =
            [
                new ProjectSourceSnapshot
                {
                    Id = sourceId,
                    DisplayName = "source.tif",
                    OriginalPath = sourcePath,
                    SourceRevision = 1,
                    Fingerprint = new ProjectFingerprintSnapshot
                    {
                        ByteLength = 4,
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath),
                        Sha256 = new string('A', 64),
                    },
                    Metadata = new ProjectImageMetadataSnapshot
                    {
                        Width = 2,
                        Height = 2,
                        Channels = 1,
                        BitsPerChannel = 16,
                        PixelFormat = "Gray16",
                        FrameCount = 1,
                    },
                },
            ],
            Analyses = [snapshot, particleSnapshot],
        };

        var store = new JsonProjectStore();
        await store.SaveAsync(projectPath, document);
        SciCanvasProjectDocument restored = await store.LoadAsync(projectPath);

        Assert.Equal(2, restored.Analyses.Count);
        ProjectScientificAnalysisSnapshot restoredSnapshot = Assert.Single(
            restored.Analyses,
            item => item.Kind == "roiStatistics");
        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, restored.SchemaVersion);
        Assert.Equal(65535, restoredSnapshot.Maximum);
        Assert.Equal(4, Assert.Single(restoredSnapshot.Histogram).Count);
        ProjectScientificAnalysisSnapshot restoredParticles = Assert.Single(
            restored.Analyses,
            item => item.Kind == "particleAnalysis");
        Assert.Equal(50003, Assert.Single(restoredParticles.Particles).RawMeanIntensity);
        Assert.Equal("brightParticles", restoredParticles.AnalysisMode);
    }

    [Fact]
    public void AnalysisTableXlsxWriter_PreservesRawIntensityAndProvenanceColumns()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "analyses.xlsx");
        Guid sourceId = Guid.NewGuid();
        var profile = new IntensityProfileResult(
            [
                new IntensityProfileSample(0, 0, 0, 0, null, 0) { RawIntensity = 7 },
                new IntensityProfileSample(1, 1, 0, 1, null, 1) { RawIntensity = 65535 },
            ],
            "px",
            16)
        {
            SourceAssetId = sourceId,
            SourceRevision = 3,
            AnalyzerId = "test.profile.v1",
            Channel = ImageAnalysisChannel.Blue,
        };

        AnalysisTableXlsxWriter.WriteNew(targetPath, [profile]);

        using ZipArchive archive = ZipFile.OpenRead(targetPath);
        Assert.Contains("Analyses", ReadEntry(archive, "xl/workbook.xml"), StringComparison.Ordinal);
        string sheet = ReadEntry(archive, "xl/worksheets/sheet1.xml");
        Assert.Contains("RawIntensity", sheet, StringComparison.Ordinal);
        Assert.Contains("SourceRevision", sheet, StringComparison.Ordinal);
        Assert.Contains(">65535<", sheet, StringComparison.Ordinal);
        Assert.Contains(sourceId.ToString(), sheet, StringComparison.OrdinalIgnoreCase);
    }

    private static RoiStatisticsResult CreateRoi(Guid sourceId, long sourceRevision) => new()
    {
        SourceAssetId = sourceId,
        SourceRevision = sourceRevision,
        AnalyzerId = "test.roi.v1",
        SourceBitDepth = 16,
        Region = new PixelRect64(0, 0, 2, 2),
        PixelCount = 4,
        Minimum = 7,
        Maximum = 65535,
        Mean = 20000,
        StandardDeviation = 100,
        IntegratedIntensity = 80000,
        Histogram = new IntensityHistogram(
            [new IntensityHistogramBin(0, 65535, 4)],
            4,
            7,
            65535),
    };

    private static SourceAssetItemViewModel CreateSource()
    {
        byte[] pixels = new byte[2 * 2 * 4];
        BitmapSource preview = BitmapSource.Create(
            2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        preview.Freeze();
        var asset = new SourceAsset(
            Guid.NewGuid(),
            "source.tif",
            "source.tif",
            new SourceFingerprint(1, DateTimeOffset.UtcNow, new string('A', 64), null),
            new CoreImageMetadata(new PixelSize64(2, 2), 1, 16, "Gray16"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(path));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
