using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Channels;

public enum ChannelNameOrigin
{
    User,
    FilenameSuggestion,
    OmeMetadata,
}

public sealed record ChannelGroupMember(
    Guid ChannelId,
    Guid AssetId,
    int FrameIndex,
    string Name,
    string? Role,
    string Color,
    ChannelNameOrigin NameOrigin,
    bool IsNameConfirmed,
    ChannelDisplaySettings DisplaySettings)
{
    public ChannelGroupMember EnsureValid()
    {
        DisplaySettings.EnsureValid();
        if (ChannelId == Guid.Empty || AssetId == Guid.Empty || FrameIndex < 0 ||
            string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 128 ||
            Role?.Length > 128 || !ScientificStyleColor.ValidateColor(Color) ||
            !Enum.IsDefined(NameOrigin) || !IsNameConfirmed ||
            DisplaySettings.ChannelId != ChannelId ||
            !string.Equals(
                ScientificStyleColor.NormalizeColor(DisplaySettings.Color),
                ScientificStyleColor.NormalizeColor(Color),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "多通道成员必须包含已确认的名称、有效的源素材/帧、颜色和显示设置。");
        }

        return this;
    }
}

public sealed record MultiChannelAssetGroup(
    Guid Id,
    string Name,
    Guid ReferenceAssetId,
    IReadOnlyList<ChannelGroupMember> Members,
    bool SameFieldOfViewConfirmed)
{
    public bool RequiresRegistration => !SameFieldOfViewConfirmed;

    public MultiChannelAssetGroup EnsureValid(IReadOnlySet<Guid>? availableAssetIds = null)
    {
        if (Id == Guid.Empty || ReferenceAssetId == Guid.Empty ||
            string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > 128 ||
            Members.Count < 2)
        {
            throw new InvalidOperationException(
                "多通道素材组必须包含有效 ID、名称、参考素材及至少两个通道成员。");
        }

        foreach (ChannelGroupMember member in Members)
        {
            member.EnsureValid();
            if (availableAssetIds is not null && !availableAssetIds.Contains(member.AssetId))
            {
                throw new InvalidOperationException("多通道素材组引用了当前工程中不存在的源素材。");
            }
        }

        if (Members.Count(member => member.AssetId == ReferenceAssetId) != 1 ||
            Members.Select(member => member.ChannelId).Distinct().Count() != Members.Count ||
            Members.Select(member => (member.AssetId, member.FrameIndex)).Distinct().Count() != Members.Count ||
            Members.Select(member => member.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != Members.Count)
        {
            throw new InvalidOperationException(
                "多通道素材组必须恰好包含一个参考素材，且通道、源帧和名称不能重复。");
        }

        return this;
    }
}
