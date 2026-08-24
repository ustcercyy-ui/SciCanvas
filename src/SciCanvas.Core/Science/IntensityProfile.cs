using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Science;

public sealed record IntensityProfileSample(
    int Index,
    double PixelX,
    double PixelY,
    double DistancePixels,
    double? PhysicalDistance,
    double NormalizedIntensity);

public sealed record IntensityProfileResult(
    IReadOnlyList<IntensityProfileSample> Samples,
    string DistanceUnit,
    int SourceBitDepth)
{
    public bool IsValid => Samples.Count >= 2 &&
                           Samples.All(sample =>
                               double.IsFinite(sample.DistancePixels) &&
                               double.IsFinite(sample.NormalizedIntensity) &&
                               sample.NormalizedIntensity is >= 0 and <= 1);

    public double Minimum => Samples.Count == 0 ? 0 : Samples.Min(sample => sample.NormalizedIntensity);

    public double Maximum => Samples.Count == 0 ? 0 : Samples.Max(sample => sample.NormalizedIntensity);

    public double Mean => Samples.Count == 0 ? 0 : Samples.Average(sample => sample.NormalizedIntensity);
}

public interface IIntensityProfileAnalyzer
{
    Task<IntensityProfileResult> AnalyzeAsync(
        SourceAsset source,
        MeasurementPoint start,
        MeasurementPoint end,
        SpatialCalibration? calibration,
        int frameIndex = 0,
        int maximumSamples = 2048,
        CancellationToken cancellationToken = default);
}
