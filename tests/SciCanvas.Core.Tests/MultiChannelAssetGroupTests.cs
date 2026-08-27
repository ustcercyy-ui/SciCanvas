using SciCanvas.Core.Channels;

namespace SciCanvas.Core.Tests;

public sealed class MultiChannelAssetGroupTests
{
    [Fact]
    public void EnsureValid_PreservesConfirmedEdsMemberIdentityAndDisplaySettings()
    {
        Guid referenceAssetId = Guid.NewGuid();
        Guid titaniumAssetId = Guid.NewGuid();
        ChannelGroupMember reference = CreateMember(
            referenceAssetId,
            "HAADF",
            "Reference",
            "#FFFFFFFF",
            65535);
        ChannelGroupMember titaniumBase = CreateMember(
            titaniumAssetId,
            "Ti",
            "ElementalMap",
            "#FFFF3B30",
            65535);
        ChannelGroupMember titanium = titaniumBase with
        {
            NameOrigin = ChannelNameOrigin.FilenameSuggestion,
            DisplaySettings = CreateDisplay(
                titaniumBase.ChannelId,
                "#FFFF3B30",
                100,
                60000,
                opacity: 0.7,
                gamma: 1.4,
                invert: true),
        };
        var group = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS same field",
            referenceAssetId,
            [reference, titanium],
            SameFieldOfViewConfirmed: true);

        MultiChannelAssetGroup validated = group.EnsureValid(
            new HashSet<Guid> { referenceAssetId, titaniumAssetId });

        Assert.Same(group, validated);
        Assert.False(validated.RequiresRegistration);
        ChannelGroupMember restoredTitanium = validated.Members[1];
        Assert.Equal(ChannelNameOrigin.FilenameSuggestion, restoredTitanium.NameOrigin);
        Assert.True(restoredTitanium.IsNameConfirmed);
        Assert.Equal(100, restoredTitanium.DisplaySettings.DisplayMinimum);
        Assert.Equal(60000, restoredTitanium.DisplaySettings.DisplayMaximum);
        Assert.Equal(0.7, restoredTitanium.DisplaySettings.Opacity);
        Assert.Equal(1.4, restoredTitanium.DisplaySettings.Gamma);
        Assert.True(restoredTitanium.DisplaySettings.Invert);
    }

    [Fact]
    public void EnsureValid_UnconfirmedFilenameSuggestionIsNotAcceptedAsScientificFact()
    {
        Guid referenceAssetId = Guid.NewGuid();
        Guid mapAssetId = Guid.NewGuid();
        ChannelGroupMember unconfirmed = CreateMember(
            mapAssetId,
            "Ti-map",
            "ElementalMap",
            "#FFFF3B30",
            255) with
        {
            NameOrigin = ChannelNameOrigin.FilenameSuggestion,
            IsNameConfirmed = false,
        };
        var group = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS",
            referenceAssetId,
            [
                CreateMember(referenceAssetId, "HAADF", "Reference", "#FFFFFFFF", 255),
                unconfirmed,
            ],
            SameFieldOfViewConfirmed: true);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => group.EnsureValid());

        Assert.Contains("已确认", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsureValid_RejectsDuplicateSourceFrameAndMissingProjectAsset()
    {
        Guid referenceAssetId = Guid.NewGuid();
        ChannelGroupMember reference = CreateMember(
            referenceAssetId,
            "HAADF",
            "Reference",
            "#FFFFFFFF",
            255);
        ChannelGroupMember duplicateFrame = CreateMember(
            referenceAssetId,
            "Ti",
            "ElementalMap",
            "#FFFF3B30",
            255);
        var duplicateGroup = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS",
            referenceAssetId,
            [reference, duplicateFrame],
            SameFieldOfViewConfirmed: false);

        Assert.Throws<InvalidOperationException>(() => duplicateGroup.EnsureValid());

        Guid missingAssetId = Guid.NewGuid();
        var missingGroup = duplicateGroup with
        {
            Members =
            [
                reference,
                CreateMember(missingAssetId, "Ti", "ElementalMap", "#FFFF3B30", 255),
            ],
        };
        Assert.Throws<InvalidOperationException>(() => missingGroup.EnsureValid(
            new HashSet<Guid> { referenceAssetId }));
    }

    private static ChannelGroupMember CreateMember(
        Guid assetId,
        string name,
        string role,
        string color,
        double maximum)
    {
        Guid channelId = Guid.NewGuid();
        return new ChannelGroupMember(
            channelId,
            assetId,
            FrameIndex: 0,
            name,
            role,
            color,
            ChannelNameOrigin.User,
            IsNameConfirmed: true,
            CreateDisplay(channelId, color, 0, maximum));
    }

    private static ChannelDisplaySettings CreateDisplay(
        Guid channelId,
        string color,
        double minimum,
        double maximum,
        double opacity = 1,
        double gamma = 1,
        bool invert = false) => new(
            channelId,
            Visible: true,
            color,
            opacity,
            minimum,
            maximum,
            gamma,
            invert);
}
