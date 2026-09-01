using System.Collections.ObjectModel;
using SciCanvas.Core.Cropping;
using SciCanvas.Core.Geometry;
using LinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;

namespace SciCanvas.Presentation;

/// <summary>
/// Owns link-group state and all guarded cross-panel synchronization. It never
/// replaces a panel SourceAsset and reports every skipped/stale mapping.
/// </summary>
public sealed class FigureLinkCoordinator
{
    public ObservableCollection<LinkGroup> LinkGroups { get; } = [];

    public bool IsSynchronizing { get; private set; }

    public string StatusText { get; set; } = "尚未创建跨素材联动组。";

    public void SynchronizeCrop(
        FigurePanelViewModel changedPanel,
        IReadOnlyCollection<FigurePanelViewModel> panels)
    {
        if (changedPanel.CropLinkGroupId is not Guid groupId || IsSynchronizing)
        {
            return;
        }

        IsSynchronizing = true;
        try
        {
            LinkGroup? group = LinkGroups.FirstOrDefault(candidate => candidate.Id == groupId);
            if (group is null)
            {
                foreach (FigurePanelViewModel linked in panels.Where(panel =>
                             !ReferenceEquals(panel, changedPanel) &&
                             panel.CropLinkGroupId == groupId &&
                             panel.Source.Asset.Id == changedPanel.Source.Asset.Id &&
                             !panel.IsLocked))
                {
                    linked.ApplyLinkedCrop(changedPanel.SourceRect);
                }

                StatusText = "同素材裁剪已同步。";
                return;
            }

            if (!group.SyncOptions.HasFlag(LinkSyncOptions.Crop))
            {
                StatusText = "当前联动组未启用裁剪同步。";
                return;
            }

            Dictionary<Guid, long> revisions = panels
                .Where(panel => group.ContainsAsset(panel.Source.Asset.Id))
                .GroupBy(panel => panel.Source.Asset.Id)
                .ToDictionary(items => items.Key, items => items.First().Source.SourceRevision);
            if (group.AssetIds.Any(assetId => !revisions.ContainsKey(assetId)) ||
                !group.AreMappingsCurrent(revisions))
            {
                StatusText = "联动映射已过期或缺少成员素材；已停止同步，请复核映射修订。";
                return;
            }

            int synchronizedCount = 0;
            int skippedCount = 0;
            foreach (FigurePanelViewModel linked in panels.Where(panel =>
                         !ReferenceEquals(panel, changedPanel) &&
                         panel.CropLinkGroupId == groupId &&
                         !panel.IsLocked))
            {
                if (!group.ContainsAsset(changedPanel.Source.Asset.Id) ||
                    !group.ContainsAsset(linked.Source.Asset.Id))
                {
                    skippedCount++;
                    continue;
                }

                Guid originalAssetId = linked.Source.Asset.Id;
                try
                {
                    PixelRect64 mapped = group.MapCrop(
                        changedPanel.Source.Asset.Id,
                        linked.Source.Asset.Id,
                        changedPanel.SourceRect);
                    if (!CropBoundsValidator.Validate(
                            mapped,
                            linked.Source.Asset.Metadata.PixelSize).IsValid)
                    {
                        skippedCount++;
                        continue;
                    }

                    linked.ApplyLinkedCrop(mapped);
                    if (linked.Source.Asset.Id != originalAssetId)
                    {
                        throw new InvalidOperationException(
                            "跨素材裁剪同步不得替换目标面板的 SourceAsset。");
                    }

                    synchronizedCount++;
                }
                catch (ArgumentOutOfRangeException)
                {
                    skippedCount++;
                }
                catch (OverflowException)
                {
                    skippedCount++;
                }
            }

            StatusText = skippedCount == 0
                ? $"已按 SpatialMapping 同步 {synchronizedCount} 个目标面板；各面板 SourceAsset 保持不变。"
                : $"已同步 {synchronizedCount} 个目标面板，跳过 {skippedCount} 个越界或无映射目标。";
        }
        finally
        {
            IsSynchronizing = false;
        }
    }

    public void SynchronizeColorScale(
        FigurePanelViewModel changedPanel,
        IReadOnlyCollection<FigurePanelViewModel> panels)
    {
        if (changedPanel.CropLinkGroupId is not Guid groupId || IsSynchronizing)
        {
            return;
        }

        LinkGroup? group = LinkGroups.FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is null || !group.SyncOptions.HasFlag(LinkSyncOptions.ColorScale))
        {
            return;
        }

        Dictionary<Guid, long> revisions = panels
            .Where(panel => group.ContainsAsset(panel.Source.Asset.Id))
            .GroupBy(panel => panel.Source.Asset.Id)
            .ToDictionary(items => items.Key, items => items.First().Source.SourceRevision);
        if (group.AssetIds.Any(assetId => !revisions.ContainsKey(assetId)) ||
            !group.AreMappingsCurrent(revisions))
        {
            StatusText = "联动映射已过期；颜色尺度同步已停止。";
            return;
        }

        IsSynchronizing = true;
        try
        {
            foreach (FigurePanelViewModel linked in panels.Where(panel =>
                         !ReferenceEquals(panel, changedPanel) &&
                         panel.CropLinkGroupId == groupId &&
                         !panel.IsLocked))
            {
                Guid originalAssetId = linked.Source.Asset.Id;
                linked.Adjustments = linked.Adjustments with
                {
                    BlackPoint = changedPanel.BlackPoint,
                    WhitePoint = changedPanel.WhitePoint,
                };
                if (linked.Source.Asset.Id != originalAssetId)
                {
                    throw new InvalidOperationException(
                        "颜色尺度同步不得替换目标面板的 SourceAsset。");
                }
            }

            StatusText = "已同步 BlackPoint / WhitePoint；各面板 SourceAsset 保持不变。";
        }
        finally
        {
            IsSynchronizing = false;
        }
    }
}
