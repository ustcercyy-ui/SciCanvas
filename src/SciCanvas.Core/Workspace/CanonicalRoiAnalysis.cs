using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SpatialPoint = SciCanvas.Core.Linking.SpatialPoint;

namespace SciCanvas.Core.Workspace;

public sealed record RoiSourceGeometryContext(long SourceRevision, PixelSize64 PixelSize)
{
    public RoiSourceGeometryContext EnsureValid()
    {
        if (SourceRevision < 1)
        {
            throw new InvalidOperationException("ROI source geometry context revision 必须大于等于 1。");
        }

        return this;
    }
}

public static class RoiPropagationService
{
    public static IReadOnlyList<RoiObject> PropagatePolygon(
        RoiObject referenceRoi,
        SpatialLinkGroup linkGroup,
        IReadOnlyDictionary<Guid, RoiSourceGeometryContext> sourceContexts,
        bool partialReferenceConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(referenceRoi);
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentNullException.ThrowIfNull(sourceContexts);
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

        Dictionary<Guid, RoiSourceGeometryContext> contexts = sourceContexts.ToDictionary(
            item => item.Key,
            item => item.Value.EnsureValid());
        Dictionary<Guid, long> sourceRevisions = contexts.ToDictionary(
            item => item.Key,
            item => item.Value.SourceRevision);
        if (!contexts.TryGetValue(linkGroup.ReferenceAssetId, out RoiSourceGeometryContext? referenceContext) ||
            !sourceRevisions.TryGetValue(linkGroup.ReferenceAssetId, out long referenceRevision) ||
            referenceRoi.SourceRevision != referenceRevision ||
            !linkGroup.AreMappingsCurrent(sourceRevisions))
        {
            throw new InvalidOperationException("mapping-revision-stale：ROI propagation 已停止，请先复核或重建 registration。");
        }

        RoiGeometryValidationResult referenceValidation =
            RoiGeometryValidator.Validate(referenceRoi, referenceContext.PixelSize);
        RoiBoundaryPolicyResult referencePolicy = RoiOutOfBoundsPolicy.Evaluate(
            referenceValidation,
            RoiBoundaryRole.Reference,
            partialReferenceConfirmed);
        if (!referencePolicy.CanPersist || !referencePolicy.CanAnalyze)
        {
            throw new InvalidOperationException(string.Join(" ", referencePolicy.Validity.Reasons));
        }

        if (referenceValidation.State == RoiGeometryValidationState.PartiallyOutside &&
            referenceRoi.Validity.State != ScientificValidityState.Warning)
        {
            throw new InvalidOperationException(
                "已确认裁剪的 reference ROI 必须以 Warning validity 保存，不能伪装为完整 ROI。");
        }

        var propagated = new List<RoiObject>(linkGroup.Mappings.Count);
        foreach (SciCanvas.Core.Linking.SpatialMapping mapping in linkGroup.Mappings)
        {
            if (!contexts.TryGetValue(mapping.TargetAssetId, out RoiSourceGeometryContext? targetContext))
            {
                throw new InvalidOperationException(
                    $"ROI propagation 缺少 target asset {mapping.TargetAssetId:D} 的尺寸/revision context。");
            }

            Guid targetRoiId = Guid.NewGuid();
            MeasurementPoint[] geometry = referenceRoi.SourceGeometry
                .Select(point => mapping.MapForward(new SpatialPoint(point.X, point.Y)))
                .Select(point => new MeasurementPoint(point.X, point.Y))
                .ToArray();
            var targetGeometry = new RoiObject
            {
                Id = targetRoiId,
                AssetId = mapping.TargetAssetId,
                SourceRevision = mapping.TargetRevision,
                SourceGeometry = Array.AsReadOnly(geometry),
                GeometryKind = RoiGeometryKind.Polygon,
                FrameIndex = referenceRoi.FrameIndex,
                Style = referenceRoi.Style,
            };
            RoiGeometryValidationResult targetValidation =
                RoiGeometryValidator.Validate(targetGeometry, targetContext.PixelSize);
            RoiBoundaryPolicyResult targetPolicy = RoiOutOfBoundsPolicy.Evaluate(
                targetValidation,
                RoiBoundaryRole.Propagated);
            if (!targetPolicy.CanPersist)
            {
                throw new InvalidOperationException(string.Join(" ", targetPolicy.Validity.Reasons));
            }

            RoiObject target = targetGeometry with
            {
                Validity = targetPolicy.Validity,
                Propagation = new RoiPropagationProvenance(
                    referenceRoi.Id,
                    targetRoiId,
                    linkGroup.Id,
                    mapping.Id,
                    targetValidation.CoverageFraction),
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
    public const string AnalyzerVersion = "scicanvas.polygon-roi-statistics.raw.v2";

    public static RoiStatisticsResult AnalyzeRawPlane(
        ImagePlane plane,
        RoiObject roi,
        PixelSize64 sourceSize,
        int histogramBinCount = 256,
        Guid? linkGroupId = null,
        Guid? mappingId = null,
        bool partialReferenceConfirmed = false)
    {
        ArgumentNullException.ThrowIfNull(plane);
        ArgumentNullException.ThrowIfNull(roi);
        roi.EnsureValid();
        if (roi.GeometryKind != RoiGeometryKind.Polygon || roi.AssetId != plane.AssetId ||
            roi.SourceRevision != plane.SourceRevision || roi.FrameIndex != plane.FrameIndex)
        {
            throw new InvalidOperationException("Polygon ROI 与 raw plane 的素材、revision 或 frame 不一致。");
        }

        if (plane.Region.Right > sourceSize.Width || plane.Region.Bottom > sourceSize.Height)
        {
            throw new InvalidOperationException("Raw plane region 超出声明的 source image bounds。");
        }

        RoiGeometryValidationResult geometryValidation = RoiGeometryValidator.Validate(roi, sourceSize);
        RoiBoundaryRole boundaryRole = roi.Propagation is null
            ? RoiBoundaryRole.Reference
            : RoiBoundaryRole.Propagated;
        RoiBoundaryPolicyResult boundaryPolicy = RoiOutOfBoundsPolicy.Evaluate(
            geometryValidation,
            boundaryRole,
            partialReferenceConfirmed);
        if (!boundaryPolicy.CanAnalyze)
        {
            throw new InvalidOperationException(string.Join(" ", boundaryPolicy.Validity.Reasons));
        }

        if (roi.Propagation is { } propagation &&
            Math.Abs(propagation.TargetCoverageFraction - geometryValidation.CoverageFraction) > 1e-9)
        {
            throw new InvalidOperationException(
                "Propagated ROI coverage provenance 与当前 source geometry/image bounds 不一致。");
        }

        PixelRect64 requiredRegion = RoiGeometryValidator.GetImageIntersectionBoundingRegion(
            roi,
            sourceSize,
            geometryValidation);
        if (plane.Region.X > requiredRegion.X || plane.Region.Y > requiredRegion.Y ||
            plane.Region.Right < requiredRegion.Right || plane.Region.Bottom < requiredRegion.Bottom)
        {
            throw new InvalidOperationException(
                "Raw plane 未覆盖 ROI 与 source image 的完整交集；拒绝生成不完整统计。");
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
            Validity = geometryValidation.ClippedToImage
                ? AnalysisResultValidity.ReviewRequired(
                    $"ROI was explicitly clipped to the source image; coverage fraction {geometryValidation.CoverageFraction:0.######}.")
                : AnalysisResultValidity.Valid,
            Region = plane.Region,
            RoiId = roi.Id,
            ScientificChannelId = plane.Channel.Id,
            LinkGroupId = linkGroupId,
            MappingId = mappingId,
            PolygonMask = roi.SourceGeometry.ToArray(),
            ClippedToImage = geometryValidation.ClippedToImage,
            CoverageFraction = geometryValidation.CoverageFraction,
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
        bool partialReferenceConfirmed = false,
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

        RoiObject[] validatedTargetRois = propagatedRois
            .Select(roi => roi.EnsureValid())
            .ToArray();
        Dictionary<(Guid AssetId, int FrameIndex), RoiObject> targetRoisByPlane =
            validatedTargetRois
                .GroupBy(roi => (roi.AssetId!.Value, roi.FrameIndex))
                .ToDictionary(group => group.Key, ResolveSpatialRoi);
        var plans = new List<(
            ChannelGroupMember Member,
            RoiAnalysisSource Source,
            RoiObject Roi,
            RoiGeometryValidationResult Validation,
            PixelRect64 Region,
            ScientificChannelDescriptor Descriptor)>(channelGroup.Members.Count);
        foreach (ChannelGroupMember member in channelGroup.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RoiAnalysisSource source = sources[member.AssetId];
            RoiObject roi = member.AssetId == linkGroup.ReferenceAssetId
                ? referenceRoi with { FrameIndex = member.FrameIndex }
                : targetRoisByPlane.TryGetValue((member.AssetId, member.FrameIndex), out RoiObject? propagated)
                    ? propagated with { FrameIndex = member.FrameIndex }
                    : validatedTargetRois
                        .Where(candidate => candidate.AssetId == member.AssetId)
                        .Take(2)
                        .ToArray() is [RoiObject spatial]
                        ? spatial with { FrameIndex = member.FrameIndex }
                    : throw new InvalidOperationException("cross-channel analysis 缺少目标素材的 propagated ROI。");
            roi.EnsureValid();
            RoiGeometryValidationResult validation =
                RoiGeometryValidator.Validate(roi, source.Asset.Metadata.PixelSize);
            RoiBoundaryPolicyResult policy = RoiOutOfBoundsPolicy.Evaluate(
                validation,
                roi.Propagation is null ? RoiBoundaryRole.Reference : RoiBoundaryRole.Propagated,
                partialReferenceConfirmed);
            if (!policy.CanAnalyze)
            {
                throw new InvalidOperationException(
                    $"ROI {roi.Id:D}: {string.Join(" ", policy.Validity.Reasons)}");
            }

            if (roi.Propagation is { } propagation &&
                Math.Abs(propagation.TargetCoverageFraction - validation.CoverageFraction) > 1e-9)
            {
                throw new InvalidOperationException(
                    $"ROI {roi.Id:D} 的 target coverage provenance 与当前 source bounds 不一致。");
            }

            PixelRect64 region = RoiGeometryValidator.GetImageIntersectionBoundingRegion(
                roi,
                source.Asset.Metadata.PixelSize,
                validation);
            ScientificChannelDescriptor descriptor = CreateRawChannelDescriptor(source.Asset, member);
            plans.Add((member, source, roi, validation, region, descriptor));
        }

        // All members are boundary-validated before the first raw read. An outside target
        // therefore fails atomically instead of returning a misleading partial result set.
        var results = new List<CrossChannelRoiStatisticsEntry>(plans.Count);
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ImagePlane plane = await planeReader.ReadAsync(
                plan.Source.Asset,
                new ImagePlaneRequest(
                    plan.Source.Asset.Id,
                    plan.Member.PlaneSelector.FrameIndex,
                    plan.Descriptor,
                    plan.Region,
                    plan.Source.SourceRevision),
                cancellationToken);
            RoiStatisticsResult statistics = PolygonRoiStatisticsCalculator.AnalyzeRawPlane(
                plane,
                plan.Roi,
                plan.Source.Asset.Metadata.PixelSize,
                histogramBinCount,
                linkGroup.Id,
                plan.Roi.Propagation?.MappingId,
                partialReferenceConfirmed);
            results.Add(new CrossChannelRoiStatisticsEntry(plan.Member, plan.Roi, statistics));
        }

        return Array.AsReadOnly(results.ToArray());
    }

    private static RoiObject ResolveSpatialRoi(IGrouping<(Guid AssetId, int FrameIndex), RoiObject> group)
    {
        RoiObject[] candidates = group.ToArray();
        RoiObject first = candidates[0];
        if (candidates.Skip(1).Any(candidate =>
                candidate.GeometryKind != first.GeometryKind ||
                !candidate.SourceGeometry.SequenceEqual(first.SourceGeometry)))
        {
            throw new InvalidOperationException(
                $"同一 spatial asset/frame {group.Key.AssetId:D}/{group.Key.FrameIndex} " +
                "包含多个不一致的 propagated ROI；无法为 channel planes 选择唯一几何。");
        }

        return first;
    }

    private static ScientificChannelDescriptor CreateRawChannelDescriptor(
        SourceAsset source,
        ChannelGroupMember member)
    {
        ScientificSampleType sampleType = source.Metadata.BitsPerChannel switch
        {
            <= 8 => ScientificSampleType.UInt8,
            <= 16 => ScientificSampleType.UInt16,
            _ => throw new NotSupportedException("跨通道 raw statistics 当前支持 UInt8/UInt16。"),
        };
        return member.PlaneSelector.CreateChannelDescriptor(
            member.ChannelId,
            member.Name,
            sampleType,
            source.Metadata.BitsPerChannel,
            member.Role,
            member.Color);
    }
}
