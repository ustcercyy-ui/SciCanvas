using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Science;

public enum AssistedRegionMode
{
    BrightParticles,
    DarkParticles,
    DarkPores,
    BrightPhase,
    GrainRegions,
    DarkCracks,
    BrightLamellae,
}

public sealed record AssistedRegionAnalysisOptions(
    AssistedRegionMode Mode,
    PixelRect64 RegionOfInterest,
    bool UseAutomaticThreshold = true,
    double ThresholdNormalized = 0.5,
    int MinimumAreaPixels = 16,
    int MaximumCandidates = 1000)
{
    public bool IsValid =>
        RegionOfInterest.Width > 0 &&
        RegionOfInterest.Height > 0 &&
        double.IsFinite(ThresholdNormalized) &&
        ThresholdNormalized is >= 0 and <= 1 &&
        MinimumAreaPixels is >= 1 and <= 10_000_000 &&
        MaximumCandidates is >= 1 and <= 100_000;

    public bool DetectDarkRegions => Mode is
        AssistedRegionMode.DarkParticles or
        AssistedRegionMode.DarkPores or
        AssistedRegionMode.DarkCracks;

    public bool RequiresElongatedShape => Mode is
        AssistedRegionMode.DarkCracks or
        AssistedRegionMode.BrightLamellae;
}

public sealed record AssistedRegionCandidate(
    int Id,
    PixelRect64 Bounds,
    double CentroidX,
    double CentroidY,
    int AreaPixels,
    int PerimeterPixels,
    double MeanIntensity,
    double AspectRatio)
{
    public double EquivalentDiameterPixels => Math.Sqrt(4 * AreaPixels / Math.PI);

    public double ApproximateWidthPixels => Math.Min(Bounds.Width, Bounds.Height);

    public bool IsValid =>
        Id > 0 &&
        Bounds.Width > 0 &&
        Bounds.Height > 0 &&
        double.IsFinite(CentroidX) &&
        double.IsFinite(CentroidY) &&
        AreaPixels > 0 &&
        PerimeterPixels > 0 &&
        double.IsFinite(MeanIntensity) &&
        MeanIntensity is >= 0 and <= 1 &&
        double.IsFinite(AspectRatio) &&
        AspectRatio >= 1;
}

public sealed record AssistedRegionAnalysisResult(
    AssistedRegionAnalysisOptions Options,
    IReadOnlyList<AssistedRegionCandidate> Candidates,
    double AppliedThresholdNormalized,
    long ForegroundPixelCount,
    long TotalPixelCount,
    string AnalyzerId,
    DateTimeOffset AnalyzedAt)
{
    public double AreaFraction => TotalPixelCount <= 0
        ? 0
        : ForegroundPixelCount / (double)TotalPixelCount;

    public bool IsValid =>
        Options.IsValid &&
        double.IsFinite(AppliedThresholdNormalized) &&
        AppliedThresholdNormalized is >= 0 and <= 1 &&
        ForegroundPixelCount >= 0 &&
        TotalPixelCount > 0 &&
        ForegroundPixelCount <= TotalPixelCount &&
        !string.IsNullOrWhiteSpace(AnalyzerId) &&
        Candidates.All(candidate => candidate.IsValid);
}

public interface IAssistedRegionAnalyzer
{
    Task<AssistedRegionAnalysisResult> AnalyzeAsync(
        SourceAsset source,
        AssistedRegionAnalysisOptions options,
        int frameIndex = 0,
        CancellationToken cancellationToken = default);
}
