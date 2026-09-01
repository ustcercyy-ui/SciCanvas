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

public enum AnalysisResourceLimitKind
{
    MaxPixels,
    MaxComponentsSafety,
    MaxBoundaryPoints,
    MemoryBudget,
}

/// <summary>
/// Hard safety limits for particle analysis. Reaching a limit aborts the analysis;
/// it never turns a complete scientific result into a silently truncated one.
/// </summary>
public sealed record AnalysisResourcePolicy(
    long MaxPixels = 50_000_000,
    int MaxComponentsSafety = 250_000,
    int MaxBoundaryPoints = 1_000_000,
    long MemoryBudgetBytes = 1_073_741_824)
{
    public static AnalysisResourcePolicy Default { get; } = new();

    public bool IsValid =>
        MaxPixels > 0 &&
        MaxComponentsSafety > 0 &&
        MaxBoundaryPoints >= 4 &&
        MemoryBudgetBytes > 0;
}

public sealed class AnalysisTooComplexException : InvalidOperationException
{
    public const string ErrorCode = "AnalysisTooComplex";

    public AnalysisTooComplexException(
        AnalysisResourceLimitKind limitKind,
        long observed,
        long limit,
        string detail)
        : base(
            $"{ErrorCode}：{detail}（需要 {observed:N0}，安全上限 {limit:N0}）。" +
            "分析已中止且未返回残缺科研结果。请提高 MinimumArea、调整 threshold 或缩小 ROI。")
    {
        if (observed < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(observed));
        }
        if (limit < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit));
        }

        LimitKind = limitKind;
        Observed = observed;
        Limit = limit;
    }

    public AnalysisResourceLimitKind LimitKind { get; }

    public long Observed { get; }

    public long Limit { get; }
}

public sealed record AssistedRegionAnalysisOptions(
    AssistedRegionMode Mode,
    PixelRect64 RegionOfInterest,
    bool UseAutomaticThreshold = true,
    double ThresholdNormalized = 0.5,
    int MinimumAreaPixels = 16)
{
    public bool IsValid =>
        RegionOfInterest.Width > 0 &&
        RegionOfInterest.Height > 0 &&
        double.IsFinite(ThresholdNormalized) &&
        ThresholdNormalized is >= 0 and <= 1 &&
        MinimumAreaPixels is >= 1 and <= 10_000_000;

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
    private double? _feretMaximumPixels;
    private double? _feretMinimumPixels;

    public double RawMeanIntensity { get; init; }

    public double EquivalentDiameterPixels => Math.Sqrt(4 * AreaPixels / Math.PI);

    public double ApproximateWidthPixels => Math.Min(Bounds.Width, Bounds.Height);

    public double FeretMaximumPixels
    {
        get => _feretMaximumPixels ?? Math.Sqrt(
            Bounds.Width * (double)Bounds.Width + Bounds.Height * (double)Bounds.Height);
        init => _feretMaximumPixels = value;
    }

    public double FeretMinimumPixels
    {
        get => _feretMinimumPixels ?? Math.Min(Bounds.Width, Bounds.Height);
        init => _feretMinimumPixels = value;
    }

    public double Circularity => Math.Clamp(
        4 * Math.PI * AreaPixels / (PerimeterPixels * (double)PerimeterPixels),
        0,
        1);

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
        double.IsFinite(RawMeanIntensity) &&
        RawMeanIntensity >= 0 &&
        double.IsFinite(AspectRatio) &&
        AspectRatio >= 1 &&
        double.IsFinite(FeretMaximumPixels) && FeretMaximumPixels > 0 &&
        double.IsFinite(FeretMinimumPixels) && FeretMinimumPixels > 0 &&
        FeretMaximumPixels >= FeretMinimumPixels;
}

public sealed record AssistedRegionAnalysisResult(
    AssistedRegionAnalysisOptions Options,
    IReadOnlyList<AssistedRegionCandidate> Candidates,
    double AppliedThresholdNormalized,
    long ForegroundPixelCount,
    long TotalPixelCount) : ScientificImageAnalysisResult
{
    public override ScientificImageAnalysisKind Kind =>
        ScientificImageAnalysisKind.ParticleAnalysis;

    public int SourceBitDepth { get; init; } = 8;

    public AnalysisResourcePolicy ResourcePolicy { get; init; } = AnalysisResourcePolicy.Default;

    public double AreaFraction => TotalPixelCount <= 0
        ? 0
        : ForegroundPixelCount / (double)TotalPixelCount;

    public bool IsValid =>
        HasValidProvenance &&
        SourceBitDepth is 8 or 16 &&
        ResourcePolicy.IsValid &&
        Options.IsValid &&
        double.IsFinite(AppliedThresholdNormalized) &&
        AppliedThresholdNormalized is >= 0 and <= 1 &&
        ForegroundPixelCount >= 0 &&
        TotalPixelCount > 0 &&
        ForegroundPixelCount <= TotalPixelCount &&
        !string.IsNullOrWhiteSpace(AnalyzerId) &&
        Candidates.All(candidate =>
            candidate.IsValid &&
            candidate.RawMeanIntensity <= (SourceBitDepth == 16 ? ushort.MaxValue : byte.MaxValue));
}

public sealed record ParticleAnalysisRecipe(
    string Name,
    AssistedRegionMode Mode,
    bool UseAutomaticThreshold,
    double ThresholdNormalized,
    int MinimumAreaPixels,
    ImageAnalysisChannel Channel)
{
    public int Version { get; init; } = 1;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(Name) &&
        Version == 1 &&
        double.IsFinite(ThresholdNormalized) &&
        ThresholdNormalized is >= 0 and <= 1 &&
        MinimumAreaPixels is >= 1 and <= 10_000_000;

    public AssistedRegionAnalysisOptions CreateOptions(PixelRect64 region)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("颗粒分析配方无效。");
        }

        return new AssistedRegionAnalysisOptions(
            Mode,
            region,
            UseAutomaticThreshold,
            ThresholdNormalized,
            MinimumAreaPixels);
    }
}

public interface IAssistedRegionAnalyzer
{
    Task<AssistedRegionAnalysisResult> AnalyzeAsync(
        SourceAsset source,
        AssistedRegionAnalysisOptions options,
        int frameIndex = 0,
        CancellationToken cancellationToken = default,
        long sourceRevision = 1,
        ImageAnalysisChannel channel = ImageAnalysisChannel.Luminance);
}

public interface IAnalysisResourcePolicyProvider
{
    AnalysisResourcePolicy ResourcePolicy { get; }
}
