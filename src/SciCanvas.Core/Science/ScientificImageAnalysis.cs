using System.Globalization;
using System.Text;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Science;

public enum ScientificImageAnalysisKind
{
    RoiStatistics,
    LineProfile,
    ParticleAnalysis,
}

public enum ImageAnalysisChannel
{
    Luminance,
    Red,
    Green,
    Blue,
    Alpha,
}

public enum AnalysisResultState
{
    Valid,
    ReviewRequired,
    Invalid,
}

public sealed record AnalysisResultValidity(
    AnalysisResultState State,
    IReadOnlyList<string> Reasons)
{
    public static AnalysisResultValidity Valid { get; } =
        new(AnalysisResultState.Valid, []);

    public static AnalysisResultValidity ReviewRequired(params string[] reasons) =>
        new(AnalysisResultState.ReviewRequired, NormalizeReasons(reasons));

    public static AnalysisResultValidity Invalid(params string[] reasons) =>
        new(AnalysisResultState.Invalid, NormalizeReasons(reasons));

    private static IReadOnlyList<string> NormalizeReasons(IEnumerable<string> reasons) =>
        reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public abstract record ScientificImageAnalysisResult
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public abstract ScientificImageAnalysisKind Kind { get; }

    public Guid SourceAssetId { get; init; }

    public long SourceRevision { get; init; } = 1;

    public int FrameIndex { get; init; }

    public ImageAnalysisChannel Channel { get; init; } = ImageAnalysisChannel.Luminance;

    public string AnalyzerId { get; init; } = string.Empty;

    public DateTimeOffset AnalyzedAt { get; init; } = DateTimeOffset.UtcNow;

    public AnalysisResultValidity Validity { get; init; } = AnalysisResultValidity.Valid;

    public bool IsCurrent(Guid sourceAssetId, long sourceRevision) =>
        SourceAssetId == sourceAssetId &&
        SourceRevision == sourceRevision &&
        Validity.State == AnalysisResultState.Valid;

    public ScientificImageAnalysisResult Revalidate(Guid sourceAssetId, long sourceRevision)
    {
        if (SourceAssetId != sourceAssetId)
        {
            return this with
            {
                Validity = AnalysisResultValidity.Invalid(
                    "分析结果引用的源素材与当前素材不一致。"),
            };
        }

        if (SourceRevision != sourceRevision &&
            Validity.State != AnalysisResultState.Invalid)
        {
            return this with
            {
                Validity = AnalysisResultValidity.ReviewRequired(
                    $"分析基于 source revision {SourceRevision}，当前为 {sourceRevision}；请重新运行或人工复核。"),
            };
        }

        return this;
    }

    public bool HasValidProvenance =>
        Id != Guid.Empty &&
        SourceAssetId != Guid.Empty &&
        SourceRevision >= 1 &&
        FrameIndex >= 0 &&
        !string.IsNullOrWhiteSpace(AnalyzerId) &&
        AnalyzedAt != default;
}

public sealed record IntensityHistogramBin(
    double LowerBound,
    double UpperBound,
    long Count)
{
    public bool IsValid =>
        double.IsFinite(LowerBound) &&
        double.IsFinite(UpperBound) &&
        UpperBound >= LowerBound &&
        Count >= 0;
}

public sealed record IntensityHistogram(
    IReadOnlyList<IntensityHistogramBin> Bins,
    long SampleCount,
    double Minimum,
    double Maximum)
{
    public bool IsValid =>
        SampleCount > 0 &&
        double.IsFinite(Minimum) &&
        double.IsFinite(Maximum) &&
        Maximum >= Minimum &&
        Bins.Count > 0 &&
        Bins.All(bin => bin.IsValid) &&
        Bins.Sum(bin => bin.Count) == SampleCount;
}

public sealed record RoiStatisticsResult : ScientificImageAnalysisResult
{
    public override ScientificImageAnalysisKind Kind =>
        ScientificImageAnalysisKind.RoiStatistics;

    public PixelRect64 Region { get; init; } = new(0, 0, 1, 1);

    public Guid? RoiId { get; init; }

    public Guid? ScientificChannelId { get; init; }

    public Guid? LinkGroupId { get; init; }

    public Guid? MappingId { get; init; }

    /// <summary>Optional canonical polygon in absolute source-pixel coordinates.</summary>
    public IReadOnlyList<MeasurementPoint> PolygonMask { get; init; } = [];

    /// <summary>True only when the canonical ROI was explicitly intersected with the source image.</summary>
    public bool ClippedToImage { get; init; }

    /// <summary>Continuous canonical ROI geometry fraction covered by the source image.</summary>
    public double CoverageFraction { get; init; } = 1;

    public int SourceBitDepth { get; init; } = 8;

    public long PixelCount { get; init; }

    public double Minimum { get; init; }

    public double Maximum { get; init; }

    public double Mean { get; init; }

    public double StandardDeviation { get; init; }

    public double IntegratedIntensity { get; init; }

    public IntensityHistogram Histogram { get; init; } =
        new([], 0, 0, 0);

    public bool IsValid =>
        HasValidProvenance &&
        SourceBitDepth is 8 or 16 &&
        PixelCount > 0 && PixelCount <= Region.Width * Region.Height &&
        (PolygonMask.Count == 0
            ? PixelCount == Region.Width * Region.Height
            : PolygonMask.Count >= 3 && PolygonMask.All(point =>
                double.IsFinite(point.X) && double.IsFinite(point.Y))) &&
        double.IsFinite(CoverageFraction) &&
        CoverageFraction is > 0 and <= 1 &&
        (ClippedToImage
            ? CoverageFraction < 1
            : Math.Abs(CoverageFraction - 1) <= 1e-12) &&
        double.IsFinite(Minimum) &&
        double.IsFinite(Maximum) &&
        double.IsFinite(Mean) &&
        double.IsFinite(StandardDeviation) &&
        double.IsFinite(IntegratedIntensity) &&
        Maximum >= Minimum &&
        Mean >= Minimum &&
        Mean <= Maximum &&
        StandardDeviation >= 0 &&
        Histogram.IsValid &&
        Histogram.SampleCount == PixelCount;
}

public interface IRoiStatisticsAnalyzer
{
    Task<RoiStatisticsResult> AnalyzeAsync(
        SourceAsset source,
        long sourceRevision,
        PixelRect64 region,
        ImageAnalysisChannel channel = ImageAnalysisChannel.Luminance,
        int frameIndex = 0,
        int histogramBinCount = 256,
        CancellationToken cancellationToken = default);
}

public static class ScientificAnalysisTable
{
    public static IReadOnlyList<string> Headers { get; } =
    [
        "AnalysisId", "Kind", "SourceAssetId", "SourceRevision", "State",
        "Frame", "Channel", "RowType", "Index", "PixelX", "PixelY",
        "DistancePixels", "PhysicalDistance", "DistanceUnit",
        "RawIntensity", "NormalizedIntensity", "PixelCount",
        "Minimum", "Maximum", "Mean", "StandardDeviation",
        "IntegratedIntensity", "ClippedToImage", "CoverageFraction",
        "HistogramLower", "HistogramUpper",
        "HistogramCount", "AnalyzerId", "AnalyzedAt",
        "AnalysisMode", "AppliedThreshold", "AreaFraction", "ParticleCount",
        "ParticleAreaPixels", "ParticlePerimeterPixels", "EquivalentDiameterPixels",
        "FeretMaximumPixels", "FeretMinimumPixels", "AspectRatio", "Circularity",
        "ParticleMeanRawIntensity",
    ];

    public static IReadOnlyList<IReadOnlyList<object?>> CreateRows(
        IEnumerable<ScientificImageAnalysisResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var rows = new List<IReadOnlyList<object?>>();
        foreach (ScientificImageAnalysisResult result in results)
        {
            switch (result)
            {
                case RoiStatisticsResult roi:
                    rows.Add(CreateRow(
                        roi,
                        "summary",
                        pixelCount: roi.PixelCount,
                        minimum: roi.Minimum,
                        maximum: roi.Maximum,
                        mean: roi.Mean,
                        standardDeviation: roi.StandardDeviation,
                        integratedIntensity: roi.IntegratedIntensity));
                    for (int index = 0; index < roi.Histogram.Bins.Count; index++)
                    {
                        IntensityHistogramBin bin = roi.Histogram.Bins[index];
                        rows.Add(CreateRow(
                            roi,
                            "histogram",
                            index: index + 1,
                            histogramLower: bin.LowerBound,
                            histogramUpper: bin.UpperBound,
                            histogramCount: bin.Count));
                    }

                    break;
                case IntensityProfileResult profile:
                    foreach (IntensityProfileSample sample in profile.Samples)
                    {
                        rows.Add(CreateRow(
                            profile,
                            "profile-sample",
                            index: sample.Index,
                            pixelX: sample.PixelX,
                            pixelY: sample.PixelY,
                            distancePixels: sample.DistancePixels,
                            physicalDistance: sample.PhysicalDistance,
                            distanceUnit: profile.DistanceUnit,
                            rawIntensity: sample.RawIntensity,
                            normalizedIntensity: sample.NormalizedIntensity));
                    }

                    break;
                case AssistedRegionAnalysisResult particles:
                    rows.Add(CreateRow(
                        particles,
                        "particle-summary",
                        analysisMode: particles.Options.Mode.ToString(),
                        appliedThreshold: particles.AppliedThresholdNormalized,
                        areaFraction: particles.AreaFraction,
                        particleCount: particles.Candidates.Count));
                    foreach (AssistedRegionCandidate particle in particles.Candidates)
                    {
                        rows.Add(CreateRow(
                            particles,
                            "particle",
                            index: particle.Id,
                            pixelX: particle.CentroidX,
                            pixelY: particle.CentroidY,
                            rawIntensity: particle.RawMeanIntensity,
                            normalizedIntensity: particle.MeanIntensity,
                            analysisMode: particles.Options.Mode.ToString(),
                            particleAreaPixels: particle.AreaPixels,
                            particlePerimeterPixels: particle.PerimeterPixels,
                            equivalentDiameterPixels: particle.EquivalentDiameterPixels,
                            feretMaximumPixels: particle.FeretMaximumPixels,
                            feretMinimumPixels: particle.FeretMinimumPixels,
                            aspectRatio: particle.AspectRatio,
                            circularity: particle.Circularity,
                            particleMeanRawIntensity: particle.RawMeanIntensity));
                    }

                    break;
            }
        }

        return rows;
    }

    public static string CreateCsv(IEnumerable<ScientificImageAnalysisResult> results)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', Headers.Select(EscapeCsv)));
        foreach (IReadOnlyList<object?> row in CreateRows(results))
        {
            csv.AppendLine(string.Join(',', row.Select(FormatCsvValue)));
        }

        return csv.ToString();
    }

    private static IReadOnlyList<object?> CreateRow(
        ScientificImageAnalysisResult result,
        string rowType,
        int? index = null,
        double? pixelX = null,
        double? pixelY = null,
        double? distancePixels = null,
        double? physicalDistance = null,
        string? distanceUnit = null,
        double? rawIntensity = null,
        double? normalizedIntensity = null,
        long? pixelCount = null,
        double? minimum = null,
        double? maximum = null,
        double? mean = null,
        double? standardDeviation = null,
        double? integratedIntensity = null,
        double? histogramLower = null,
        double? histogramUpper = null,
        long? histogramCount = null,
        string? analysisMode = null,
        double? appliedThreshold = null,
        double? areaFraction = null,
        int? particleCount = null,
        int? particleAreaPixels = null,
        int? particlePerimeterPixels = null,
        double? equivalentDiameterPixels = null,
        double? feretMaximumPixels = null,
        double? feretMinimumPixels = null,
        double? aspectRatio = null,
        double? circularity = null,
        double? particleMeanRawIntensity = null) =>
        [
            result.Id,
            result.Kind,
            result.SourceAssetId,
            result.SourceRevision,
            result.Validity.State,
            result.FrameIndex,
            result.Channel,
            rowType,
            index,
            pixelX,
            pixelY,
            distancePixels,
            physicalDistance,
            distanceUnit,
            rawIntensity,
            normalizedIntensity,
            pixelCount,
            minimum,
            maximum,
            mean,
            standardDeviation,
            integratedIntensity,
            result is RoiStatisticsResult roi ? roi.ClippedToImage : null,
            result is RoiStatisticsResult roiCoverage ? roiCoverage.CoverageFraction : null,
            histogramLower,
            histogramUpper,
            histogramCount,
            result.AnalyzerId,
            result.AnalyzedAt,
            analysisMode,
            appliedThreshold,
            areaFraction,
            particleCount,
            particleAreaPixels,
            particlePerimeterPixels,
            equivalentDiameterPixels,
            feretMaximumPixels,
            feretMinimumPixels,
            aspectRatio,
            circularity,
            particleMeanRawIntensity,
        ];

    private static string FormatCsvValue(object? value) => value switch
    {
        null => string.Empty,
        double number => number.ToString("0.###############", CultureInfo.InvariantCulture),
        float number => number.ToString("0.###############", CultureInfo.InvariantCulture),
        IFormattable formattable => EscapeCsv(
            formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty),
        _ => EscapeCsv(value.ToString() ?? string.Empty),
    };

    private static string EscapeCsv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? string.Concat("\"", value.Replace("\"", "\"\""), "\"")
            : value;
}
