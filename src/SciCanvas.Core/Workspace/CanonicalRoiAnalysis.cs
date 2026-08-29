using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SpatialPoint = SciCanvas.Core.Linking.SpatialPoint;

namespace SciCanvas.Core.Workspace;

public static class RoiPropagationService
{
    public static IReadOnlyList<RoiObject> PropagatePolygon(
        RoiObject referenceRoi,
        SpatialLinkGroup linkGroup,
        IReadOnlyDictionary<Guid, long> sourceRevisions)
    {
        ArgumentNullException.ThrowIfNull(referenceRoi);
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentNullException.ThrowIfNull(sourceRevisions);
        referenceRoi.EnsureValid();
        linkGroup.EnsureValid();
        if (referenceRoi.GeometryKind != RoiGeometryKind.Polygon)
        {
            throw new NotSupportedException("PR8 ROI propagation 仅接受 canonical Polygon ROI。");
        }

        if (!linkGroup.SyncOptions.HasFlag(SciCanvas.Core.Linking.LinkSyncOptions.Roi))
        {
            throw new InvalidOperationException("当前 LinkGroup 未启用 ROI synchronization。");
        }

        if (referenceRoi.AssetId != linkGroup.ReferenceAssetId)
        {
            throw new InvalidOperationException("传播源 ROI 必须绑定 LinkGroup reference asset，以保持单一 MappingId provenance。");
        }

        if (!sourceRevisions.TryGetValue(linkGroup.ReferenceAssetId, out long referenceRevision) ||
            referenceRoi.SourceRevision != referenceRevision ||
            !linkGroup.AreMappingsCurrent(sourceRevisions))
        {
            throw new InvalidOperationException("mapping-revision-stale：ROI propagation 已停止，请先复核或重建 registration。");
        }

        var propagated = new List<RoiObject>(linkGroup.Mappings.Count);
        foreach (SciCanvas.Core.Linking.SpatialMapping mapping in linkGroup.Mappings)
        {
            Guid targetRoiId = Guid.NewGuid();
            MeasurementPoint[] geometry = referenceRoi.SourceGeometry
                .Select(point => mapping.MapForward(new SpatialPoint(point.X, point.Y)))
                .Select(point => new MeasurementPoint(point.X, point.Y))
                .ToArray();
            var target = new RoiObject
            {
                Id = targetRoiId,
                AssetId = mapping.TargetAssetId,
                SourceRevision = mapping.TargetRevision,
                SourceGeometry = Array.AsReadOnly(geometry),
                GeometryKind = RoiGeometryKind.Polygon,
                FrameIndex = referenceRoi.FrameIndex,
                Style = referenceRoi.Style,
                Propagation = new RoiPropagationProvenance(
                    referenceRoi.Id,
                    targetRoiId,
                    linkGroup.Id,
                    mapping.Id),
            };
            propagated.Add(target.EnsureValid());
        }

        return Array.AsReadOnly(propagated.ToArray());
    }
}

/// <summary>
/// Deterministic even-odd polygon mask. Pixels are tested at their centers
/// (x + 0.5, y + 0.5), and points on polygon edges are included.
/// </summary>
public static class PolygonPixelMask
{
    private const double BoundaryTolerance = 1e-10;

    public static bool ContainsPixelCenter(
        IReadOnlyList<MeasurementPoint> polygon,
        int pixelX,
        int pixelY) => Contains(polygon, pixelX + 0.5, pixelY + 0.5);

    public static bool Contains(
        IReadOnlyList<MeasurementPoint> polygon,
        double x,
        double y)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3 || polygon.Any(point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y)) ||
            !double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new InvalidOperationException("Polygon mask 至少需要 3 个有限 source-pixel points。");
        }

        bool inside = false;
        for (int current = 0, previous = polygon.Count - 1;
             current < polygon.Count;
             previous = current++)
        {
            MeasurementPoint first = polygon[previous];
            MeasurementPoint second = polygon[current];
            if (IsOnSegment(first, second, x, y))
            {
                return true;
            }

            bool crosses = (first.Y > y) != (second.Y > y);
            if (crosses)
            {
                double crossingX = first.X +
                    (y - first.Y) * (second.X - first.X) / (second.Y - first.Y);
                if (x < crossingX)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    public static PixelRect64 GetClampedBoundingRegion(
        IReadOnlyList<MeasurementPoint> polygon,
        PixelSize64 imageSize)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (polygon.Count < 3 || polygon.Any(point =>
                !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new InvalidOperationException("Polygon ROI 至少需要 3 个有限 source-pixel points。");
        }

        long left = Math.Clamp(checked((long)Math.Floor(polygon.Min(point => point.X))), 0, imageSize.Width);
        long top = Math.Clamp(checked((long)Math.Floor(polygon.Min(point => point.Y))), 0, imageSize.Height);
        long right = Math.Clamp(checked((long)Math.Ceiling(polygon.Max(point => point.X))), 0, imageSize.Width);
        long bottom = Math.Clamp(checked((long)Math.Ceiling(polygon.Max(point => point.Y))), 0, imageSize.Height);
        if (right <= left || bottom <= top)
        {
            throw new InvalidOperationException("Polygon ROI 与 source image 没有可统计的像素区域。");
        }

        return new PixelRect64(left, top, right - left, bottom - top);
    }

    private static bool IsOnSegment(
        MeasurementPoint first,
        MeasurementPoint second,
        double x,
        double y)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        double cross = (x - first.X) * deltaY - (y - first.Y) * deltaX;
        double scale = Math.Max(1, Math.Abs(deltaX) + Math.Abs(deltaY));
        if (Math.Abs(cross) > BoundaryTolerance * scale)
        {
            return false;
        }

        double dot = (x - first.X) * (x - second.X) + (y - first.Y) * (y - second.Y);
        return dot <= BoundaryTolerance * scale;
    }
}

public static class PolygonRoiStatisticsCalculator
{
    public const string AnalyzerVersion = "scicanvas.polygon-roi-statistics.raw.v1";

    public static RoiStatisticsResult AnalyzeRawPlane(
        ImagePlane plane,
        RoiObject roi,
        int histogramBinCount = 256,
        Guid? linkGroupId = null,
        Guid? mappingId = null)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(roi);
        roi.EnsureValid();
        if (roi.GeometryKind != RoiGeometryKind.Polygon || roi.AssetId != plane.AssetId ||
            roi.SourceRevision != plane.SourceRevision || roi.FrameIndex != plane.FrameIndex)
        {
            throw new InvalidOperationException("Polygon ROI 与 raw plane 的素材、revision 或 frame 不一致。");
        }

        if (histogramBinCount is < 2 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(histogramBinCount), "直方图 bin 数必须为 2–4096。");
        }

        var values = new List<double>();
        for (int localY = 0; localY < plane.Height; localY++)
        {
            int sourceY = checked((int)(plane.Region.Y + localY));
            for (int localX = 0; localX < plane.Width; localX++)
            {
                int sourceX = checked((int)(plane.Region.X + localX));
                if (PolygonPixelMask.ContainsPixelCenter(roi.SourceGeometry, sourceX, sourceY))
                {
                    values.Add(plane.GetRawValue(localX, localY));
                }
            }
        }

        if (values.Count == 0)
        {
            throw new InvalidOperationException("Polygon ROI 内没有 pixel-center sample。");
        }

        double minimum = values.Min();
        double maximum = values.Max();
        double mean = 0;
        double sumSquaredDifferences = 0;
        double integrated = 0;
        for (int index = 0; index < values.Count; index++)
        {
            double value = values[index];
            integrated += value;
            double delta = value - mean;
            mean += delta / (index + 1);
            sumSquaredDifferences += delta * (value - mean);
        }

        double sampleMaximum = Math.Pow(2, plane.BitDepth) - 1;
        double histogramSpan = sampleMaximum + 1;
        long[] counts = new long[histogramBinCount];
        foreach (double value in values)
        {
            int bin = Math.Clamp(
                (int)Math.Floor(value / histogramSpan * histogramBinCount),
                0,
                histogramBinCount - 1);
            counts[bin]++;
        }

        double binWidth = histogramSpan / histogramBinCount;
        IntensityHistogramBin[] bins = counts
            .Select((count, index) => new IntensityHistogramBin(
                index * binWidth,
                Math.Min(sampleMaximum, (index + 1) * binWidth),
                count))
            .ToArray();
        var result = new RoiStatisticsResult
        {
            Id = Guid.NewGuid(),
            SourceAssetId = plane.AssetId,
            SourceRevision = plane.SourceRevision ?? throw new InvalidOperationException("raw plane 缺少 source revision。"),
            FrameIndex = plane.FrameIndex,
            Channel = ImageAnalysisChannel.Luminance,
            AnalyzerId = AnalyzerVersion,
            AnalyzedAt = DateTimeOffset.UtcNow,
            Region = plane.Region,
            RoiId = roi.Id,
            ScientificChannelId = plane.Channel.Id,
            LinkGroupId = linkGroupId,
            MappingId = mappingId,
            PolygonMask = roi.SourceGeometry.ToArray(),
            SourceBitDepth = plane.BitDepth,
            PixelCount = values.Count,
            Minimum = minimum,
            Maximum = maximum,
            Mean = mean,
            StandardDeviation = Math.Sqrt(sumSquaredDifferences / values.Count),
            IntegratedIntensity = integrated,
            Histogram = new IntensityHistogram(bins, values.Count, minimum, maximum),
        };
        if (!result.IsValid)
        {
            throw new InvalidOperationException("Polygon raw statistics result 未通过内部一致性校验。");
        }

        return result;
    }
}
public sealed record RoiAnalysisSource(SourceAsset Asset, long SourceRevision)
{
    public RoiAnalysisSource EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Asset);
        if (SourceRevision < 1)
        {
            throw new InvalidOperationException("ROI analysis source revision 必须大于等于 1。");
        }

        return this;
    }
}

public sealed record CrossChannelRoiStatisticsEntry(
    ChannelGroupMember ChannelMember,
    RoiObject Roi,
    RoiStatisticsResult Statistics);

public static class CrossChannelRoiStatisticsService
{
    public static async Task<IReadOnlyList<CrossChannelRoiStatisticsEntry>> AnalyzeAsync(
        RoiObject referenceRoi,
        IReadOnlyList<RoiObject> propagatedRois,
        SpatialLinkGroup linkGroup,
        MultiChannelAssetGroup channelGroup,
        IReadOnlyDictionary<Guid, RoiAnalysisSource> sources,
        IImagePlaneReader planeReader,
        int histogramBinCount = 256,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(referenceRoi);
        ArgumentNullException.ThrowIfNull(propagatedRois);
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentNullException.ThrowIfNull(channelGroup);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(planeReader);
        referenceRoi.EnsureValid();
        linkGroup.EnsureValid(sources.Keys.ToHashSet());
        channelGroup.EnsureValid(sources.Keys.ToHashSet());
        if (referenceRoi.AssetId != linkGroup.ReferenceAssetId ||
            channelGroup.ReferenceAssetId != linkGroup.ReferenceAssetId)
        {
            throw new InvalidOperationException("ROI、LinkGroup 与 MultiChannel group 必须共享 reference asset。");
        }

        Dictionary<Guid, long> revisions = sources.ToDictionary(
            item => item.Key,
            item => item.Value.EnsureValid().SourceRevision);
        if (!linkGroup.AreMappingsCurrent(revisions) || referenceRoi.SourceRevision != revisions[linkGroup.ReferenceAssetId])
        {
            throw new InvalidOperationException("mapping-revision-stale：cross-channel ROI analysis 已停止。");
        }

        Dictionary<Guid, RoiObject> targetRois = propagatedRois
            .Select(roi => roi.EnsureValid())
            .ToDictionary(roi => roi.AssetId!.Value);
        var results = new List<CrossChannelRoiStatisticsEntry>(channelGroup.Members.Count);
        foreach (ChannelGroupMember member in channelGroup.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoiAnalysisSource source = sources[member.AssetId];
            RoiObject roi = member.AssetId == linkGroup.ReferenceAssetId
                ? referenceRoi with { FrameIndex = member.FrameIndex }
                : targetRois.TryGetValue(member.AssetId, out RoiObject? propagated)
                    ? propagated with { FrameIndex = member.FrameIndex }
                    : throw new InvalidOperationException("cross-channel analysis 缺少目标素材的 propagated ROI。");
            roi.EnsureValid();
            PixelRect64 region = PolygonPixelMask.GetClampedBoundingRegion(
                roi.SourceGeometry,
                source.Asset.Metadata.PixelSize);
            ScientificChannelDescriptor descriptor = CreateRawChannelDescriptor(
                source.Asset,
                member,
                channelGroup);
            ImagePlane plane = await planeReader.ReadAsync(
                source.Asset,
                new ImagePlaneRequest(
                    source.Asset.Id,
                    member.FrameIndex,
                    descriptor,
                    region,
                    source.SourceRevision),
                cancellationToken);
            RoiStatisticsResult statistics = PolygonRoiStatisticsCalculator.AnalyzeRawPlane(
                plane,
                roi,
                histogramBinCount,
                linkGroup.Id,
                roi.Propagation?.MappingId);
            results.Add(new CrossChannelRoiStatisticsEntry(member, roi, statistics));
        }

        return Array.AsReadOnly(results.ToArray());
    }

    private static ScientificChannelDescriptor CreateRawChannelDescriptor(
        SourceAsset source,
        ChannelGroupMember member,
        MultiChannelAssetGroup group)
    {
        if (source.Metadata.Channels != 1)
        {
            throw new NotSupportedException(
                "MultiChannel member 未保存 interleaved component index；跨通道 raw statistics 不会猜测 RGB component。请使用单通道 external assets。");
        }

        ScientificSampleType sampleType = source.Metadata.BitsPerChannel switch
        {
            <= 8 => ScientificSampleType.UInt8,
            <= 16 => ScientificSampleType.UInt16,
            _ => throw new NotSupportedException("跨通道 raw statistics 当前支持 UInt8/UInt16。"),
        };
        bool sameAssetHasMultipleFrames = group.Members.Count(item => item.AssetId == member.AssetId) > 1;
        return new ScientificChannelDescriptor(
            member.ChannelId,
            0,
            member.Name,
            sameAssetHasMultipleFrames
                ? ScientificChannelSourceKind.FramePlane
                : ScientificChannelSourceKind.ExternalAsset,
            sampleType,
            source.Metadata.BitsPerChannel,
            Role: member.Role,
            DefaultColor: member.Color).EnsureValid();
    }
}
