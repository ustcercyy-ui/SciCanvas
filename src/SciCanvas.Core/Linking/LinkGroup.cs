using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Linking;

[Flags]
public enum LinkSyncOptions
{
    None = 0,
    Pan = 1 << 0,
    Zoom = 1 << 1,
    Crop = 1 << 2,
    Roi = 1 << 3,
    ColorScale = 1 << 4,
}

/// <summary>
/// Spatial asset-level relationship only. A mapping is shared by every scientific plane
/// of an asset; frame/component identity is carried separately by ScientificPlaneRef.
/// </summary>
public sealed record LinkGroup(
    Guid Id,
    string Name,
    Guid ReferenceAssetId,
    IReadOnlyList<Guid> AssetIds,
    LinkSyncOptions SyncOptions,
    IReadOnlyList<SpatialMapping> Mappings)
{
    public LinkGroup EnsureValid(IReadOnlySet<Guid>? availableAssetIds = null)
    {
        if (Id == Guid.Empty || ReferenceAssetId == Guid.Empty ||
            string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 128 ||
            AssetIds.Count < 2 || AssetIds.Any(id => id == Guid.Empty) ||
            AssetIds.Distinct().Count() != AssetIds.Count ||
            !AssetIds.Contains(ReferenceAssetId) ||
            SyncOptions == LinkSyncOptions.None ||
            (SyncOptions & ~(LinkSyncOptions.Pan | LinkSyncOptions.Zoom | LinkSyncOptions.Crop |
                             LinkSyncOptions.Roi | LinkSyncOptions.ColorScale)) != 0)
        {
            throw new InvalidOperationException("LinkGroup 必须包含有效名称、参考素材、至少两个唯一素材和同步选项。");
        }

        if (availableAssetIds is not null && AssetIds.Any(id => !availableAssetIds.Contains(id)))
        {
            throw new InvalidOperationException("LinkGroup 引用了当前工程中不存在的源素材。");
        }

        Guid[] targets = AssetIds.Where(id => id != ReferenceAssetId).ToArray();
        foreach (SpatialMapping mapping in Mappings)
        {
            mapping.EnsureValid();
        }

        if (Mappings.Count != targets.Length ||
            Mappings.Select(mapping => mapping.Id).Distinct().Count() != Mappings.Count ||
            Mappings.Any(mapping => mapping.SourceAssetId != ReferenceAssetId ||
                                    !targets.Contains(mapping.TargetAssetId)) ||
            Mappings.Select(mapping => mapping.TargetAssetId).Distinct().Count() != targets.Length)
        {
            throw new InvalidOperationException("LinkGroup 必须为每个非参考素材保存且只保存一个从参考素材出发的 SpatialMapping。");
        }

        return this;
    }

    public bool ContainsAsset(Guid assetId) => AssetIds.Contains(assetId);

    public bool AreMappingsCurrent(IReadOnlyDictionary<Guid, long> sourceRevisions)
    {
        ArgumentNullException.ThrowIfNull(sourceRevisions);
        return Mappings.All(mapping =>
            sourceRevisions.TryGetValue(mapping.SourceAssetId, out long sourceRevision) &&
            sourceRevisions.TryGetValue(mapping.TargetAssetId, out long targetRevision) &&
            mapping.MatchesRevisions(sourceRevision, targetRevision));
    }

    public SpatialPoint MapPoint(Guid sourceAssetId, Guid targetAssetId, SpatialPoint point)
    {
        EnsureValid();
        if (!ContainsAsset(sourceAssetId) || !ContainsAsset(targetAssetId))
        {
            throw new InvalidOperationException("待同步素材不属于当前 LinkGroup。");
        }

        if (sourceAssetId == targetAssetId)
        {
            return point;
        }

        SpatialPoint referencePoint = sourceAssetId == ReferenceAssetId
            ? point
            : GetReferenceMapping(sourceAssetId).MapReverse(point);
        return targetAssetId == ReferenceAssetId
            ? referencePoint
            : GetReferenceMapping(targetAssetId).MapForward(referencePoint);
    }

    public PixelRect64 MapCrop(Guid sourceAssetId, Guid targetAssetId, PixelRect64 sourceRect) =>
        SpatialMappingGeometry.MapBoundingRect(
            sourceRect,
            point => MapPoint(sourceAssetId, targetAssetId, point));

    public LinkGroup ReplaceMapping(SpatialMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        mapping.EnsureValid();
        if (mapping.SourceAssetId != ReferenceAssetId || !AssetIds.Contains(mapping.TargetAssetId))
        {
            throw new InvalidOperationException("替换映射必须从 LinkGroup 参考素材指向现有目标素材。");
        }

        SpatialMapping[] replacements = Mappings
            .Select(existing => existing.TargetAssetId == mapping.TargetAssetId ? mapping : existing)
            .ToArray();
        if (!replacements.Any(item => item.TargetAssetId == mapping.TargetAssetId))
        {
            throw new InvalidOperationException("LinkGroup 中不存在指定目标素材的映射。");
        }

        return this with { Mappings = Array.AsReadOnly(replacements) };
    }

    private SpatialMapping GetReferenceMapping(Guid targetAssetId) =>
        Mappings.Single(mapping => mapping.TargetAssetId == targetAssetId);
}
