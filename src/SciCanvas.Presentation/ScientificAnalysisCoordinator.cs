using System.IO;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Presentation;

/// <summary>
/// Owns analyzer selection and the shared validity boundary for all scientific
/// analysis commands. View-models remain responsible only for presenting and
/// persisting a successful result.
/// </summary>
public sealed class ScientificAnalysisCoordinator
{
    private readonly IIntensityProfileAnalyzer _intensityProfileAnalyzer;
    private readonly IRoiStatisticsAnalyzer _roiStatisticsAnalyzer;
    private readonly IAssistedRegionAnalyzer _assistedRegionAnalyzer;

    public ScientificAnalysisCoordinator(
        IIntensityProfileAnalyzer intensityProfileAnalyzer,
        IRoiStatisticsAnalyzer roiStatisticsAnalyzer,
        IAssistedRegionAnalyzer assistedRegionAnalyzer)
    {
        _intensityProfileAnalyzer = intensityProfileAnalyzer ??
            throw new ArgumentNullException(nameof(intensityProfileAnalyzer));
        _roiStatisticsAnalyzer = roiStatisticsAnalyzer ??
            throw new ArgumentNullException(nameof(roiStatisticsAnalyzer));
        _assistedRegionAnalyzer = assistedRegionAnalyzer ??
            throw new ArgumentNullException(nameof(assistedRegionAnalyzer));
    }

    public AnalysisResourcePolicy? ResourcePolicy =>
        (_assistedRegionAnalyzer as IAnalysisResourcePolicyProvider)?.ResourcePolicy;

    public async Task<RoiStatisticsResult> AnalyzeRoiAsync(
        SourceAsset source,
        long sourceRevision,
        PixelRect64 region,
        ImageAnalysisChannel channel,
        CancellationToken cancellationToken = default)
    {
        RoiStatisticsResult result = await _roiStatisticsAnalyzer.AnalyzeAsync(
            source,
            sourceRevision,
            region,
            channel,
            cancellationToken: cancellationToken);
        return result.IsValid
            ? result
            : throw new InvalidDataException("ROI 统计结果无效。");
    }

    public async Task<IntensityProfileResult> AnalyzeProfileAsync(
        SourceAsset source,
        long sourceRevision,
        MeasurementPoint start,
        MeasurementPoint end,
        SpatialCalibration? calibration,
        ImageAnalysisChannel channel,
        CancellationToken cancellationToken = default)
    {
        IntensityProfileResult result = await _intensityProfileAnalyzer.AnalyzeAsync(
            source,
            start,
            end,
            calibration,
            channel: channel,
            sourceRevision: sourceRevision,
            cancellationToken: cancellationToken);
        return result.IsValid
            ? result
            : throw new InvalidDataException("强度剖面结果无效或采样点不足。");
    }

    public async Task<AssistedRegionAnalysisResult> AnalyzeRegionsAsync(
        SourceAsset source,
        long sourceRevision,
        AssistedRegionAnalysisOptions options,
        ImageAnalysisChannel channel,
        CancellationToken cancellationToken = default)
    {
        AssistedRegionAnalysisResult result = await _assistedRegionAnalyzer.AnalyzeAsync(
            source,
            options,
            sourceRevision: sourceRevision,
            channel: channel,
            cancellationToken: cancellationToken);
        return result.IsValid
            ? result
            : throw new InvalidDataException("候选区域分析结果无效。请调整阈值或最小面积。");
    }
}
