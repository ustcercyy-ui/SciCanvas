using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Tests;

public sealed class ScientificImageAnalysisTests
{
    [Fact]
    public void Revalidate_SourceRevisionChangeMarksResultReviewRequired()
    {
        Guid sourceId = Guid.NewGuid();
        var result = new RoiStatisticsResult
        {
            SourceAssetId = sourceId,
            SourceRevision = 3,
            AnalyzerId = "test.roi.v1",
            Region = new PixelRect64(0, 0, 1, 1),
            PixelCount = 1,
            Minimum = 10,
            Maximum = 10,
            Mean = 10,
            IntegratedIntensity = 10,
            Histogram = new IntensityHistogram(
                [new IntensityHistogramBin(0, 255, 1)],
                1,
                10,
                10),
        };

        ScientificImageAnalysisResult revalidated = result.Revalidate(sourceId, 4);

        Assert.Equal(AnalysisResultState.ReviewRequired, revalidated.Validity.State);
        Assert.Contains("revision 3", Assert.Single(revalidated.Validity.Reasons));
        Assert.False(revalidated.IsCurrent(sourceId, 4));
    }

    [Fact]
    public void AnalysisTable_ExportsRoiSummaryHistogramAndRawProfileSamples()
    {
        Guid sourceId = Guid.NewGuid();
        var roi = new RoiStatisticsResult
        {
            SourceAssetId = sourceId,
            SourceRevision = 2,
            AnalyzerId = "test.roi.v1",
            Region = new PixelRect64(0, 0, 2, 1),
            SourceBitDepth = 16,
            PixelCount = 2,
            Minimum = 1001,
            Maximum = 40003,
            Mean = 20502,
            StandardDeviation = 19501,
            IntegratedIntensity = 41004,
            Histogram = new IntensityHistogram(
                [
                    new IntensityHistogramBin(0, 32768, 1),
                    new IntensityHistogramBin(32768, 65535, 1),
                ],
                2,
                1001,
                40003),
        };
        var profile = new IntensityProfileResult(
            [
                new IntensityProfileSample(1, 0, 0, 0, 0, 1001 / 65535d)
                {
                    RawIntensity = 1001,
                },
                new IntensityProfileSample(2, 1, 0, 1, 2, 40003 / 65535d)
                {
                    RawIntensity = 40003,
                },
            ],
            "µm",
            16)
        {
            SourceAssetId = sourceId,
            SourceRevision = 2,
            AnalyzerId = "test.profile.v1",
        };
        var particles = new AssistedRegionAnalysisResult(
            new AssistedRegionAnalysisOptions(
                AssistedRegionMode.BrightParticles,
                new PixelRect64(0, 0, 4, 4),
                UseAutomaticThreshold: false,
                ThresholdNormalized: 0.5,
                MinimumAreaPixels: 2),
            [
                new AssistedRegionCandidate(
                    1,
                    new PixelRect64(1, 1, 2, 2),
                    1.5,
                    1.5,
                    4,
                    8,
                    50003 / 65535d,
                    1)
                {
                    RawMeanIntensity = 50003,
                },
            ],
            0.5,
            4,
            16)
        {
            SourceAssetId = sourceId,
            SourceRevision = 2,
            AnalyzerId = "test.particle.v2",
            SourceBitDepth = 16,
        };

        string csv = ScientificAnalysisTable.CreateCsv([roi, profile, particles]);

        Assert.Contains("summary", csv, StringComparison.Ordinal);
        Assert.Contains("histogram", csv, StringComparison.Ordinal);
        Assert.Contains("profile-sample", csv, StringComparison.Ordinal);
        Assert.Contains("particle-summary", csv, StringComparison.Ordinal);
        Assert.Contains("particle", csv, StringComparison.Ordinal);
        Assert.Contains("Circularity", csv, StringComparison.Ordinal);
        Assert.Contains("50003", csv, StringComparison.Ordinal);
        Assert.Contains("40003", csv, StringComparison.Ordinal);
        Assert.Contains("41004", csv, StringComparison.Ordinal);
    }
}
