using System.IO;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

public sealed class WpfRoiStatisticsAnalyzer : IRoiStatisticsAnalyzer
{
    public const string AnalyzerVersion = "scicanvas.roi-statistics.v1";

    public Task<RoiStatisticsResult> AnalyzeAsync(
        SourceAsset source,
        long sourceRevision,
        PixelRect64 region,
        ImageAnalysisChannel channel = ImageAnalysisChannel.Luminance,
        int frameIndex = 0,
        int histogramBinCount = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (sourceRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        }

        if (frameIndex < 0 || frameIndex >= Math.Max(1, source.Metadata.FrameCount))
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        if (histogramBinCount is < 2 or > 4096)
        {
            throw new ArgumentOutOfRangeException(
                nameof(histogramBinCount),
                "直方图 bin 数必须为 2–4096。");
        }

        return Task.Run(
            () => AnalyzeCore(
                source,
                sourceRevision,
                region,
                channel,
                frameIndex,
                histogramBinCount,
                cancellationToken),
            cancellationToken);
    }

    private static RoiStatisticsResult AnalyzeCore(
        SourceAsset source,
        long sourceRevision,
        PixelRect64 region,
        ImageAnalysisChannel channel,
        int frameIndex,
        int histogramBinCount,
        CancellationToken cancellationToken)
    {
        ScientificPixelBuffer pixels = WpfScientificPixelReader.ReadRegion(
            source,
            region,
            frameIndex,
            channel,
            cancellationToken);
        long count = pixels.Values.Count;
        double minimum = double.PositiveInfinity;
        double maximum = double.NegativeInfinity;
        double mean = 0;
        double sumSquaredDifferences = 0;
        double integrated = 0;
        long currentCount = 0;
        foreach (double value in pixels.Values)
        {
            currentCount++;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
            integrated += value;
            double delta = value - mean;
            mean += delta / currentCount;
            double deltaAfterMean = value - mean;
            sumSquaredDifferences += delta * deltaAfterMean;
        }

        double standardDeviation = Math.Sqrt(sumSquaredDifferences / Math.Max(1, count));
        long[] counts = new long[histogramBinCount];
        double histogramSpan = pixels.MaximumValue + 1;
        foreach (double value in pixels.Values)
        {
            int bin = Math.Clamp(
                (int)Math.Floor(value / histogramSpan * histogramBinCount),
                0,
                histogramBinCount - 1);
            counts[bin]++;
        }

        double binWidth = histogramSpan / histogramBinCount;
        IntensityHistogramBin[] bins = counts
            .Select((binCount, index) => new IntensityHistogramBin(
                index * binWidth,
                Math.Min(pixels.MaximumValue, (index + 1) * binWidth),
                binCount))
            .ToArray();
        var histogram = new IntensityHistogram(bins, count, minimum, maximum);
        return new RoiStatisticsResult
        {
            Id = Guid.NewGuid(),
            SourceAssetId = source.Id,
            SourceRevision = sourceRevision,
            FrameIndex = frameIndex,
            Channel = channel,
            AnalyzerId = AnalyzerVersion,
            AnalyzedAt = DateTimeOffset.UtcNow,
            Region = region,
            SourceBitDepth = pixels.SourceBitDepth,
            PixelCount = count,
            Minimum = minimum,
            Maximum = maximum,
            Mean = mean,
            StandardDeviation = standardDeviation,
            IntegratedIntensity = integrated,
            Histogram = histogram,
        };
    }
}
