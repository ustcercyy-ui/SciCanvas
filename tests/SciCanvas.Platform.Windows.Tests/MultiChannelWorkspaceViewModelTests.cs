using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class MultiChannelWorkspaceViewModelTests
{
    [Fact]
    public void EdsWizard_RequiresExplicitNameAndFieldOfViewConfirmation()
    {
        ObservableCollection<SourceAssetItemViewModel> sources =
        [
            CreateSource("HAADF.tif", 16),
            CreateSource("Ti.tif", 16),
            CreateSource("Al.tif", 12),
            CreateSource("V.tif", 16),
            CreateSource("O.tif", 16),
        ];
        var workspace = new MultiChannelWorkspaceViewModel(sources);

        workspace.StartEdsGroupWizardCommand.Execute(null);
        Assert.True(workspace.IsWizardOpen);
        Assert.Equal(1, workspace.WizardStep);
        Assert.True(workspace.EdsCandidates[0].IsReference);
        Assert.False(workspace.EdsCandidates[0].CanExclude);

        workspace.NextWizardStepCommand.Execute(null);
        Assert.Equal(2, workspace.WizardStep);
        workspace.NextWizardStepCommand.Execute(null);
        Assert.Equal(3, workspace.WizardStep);
        Assert.False(workspace.NextWizardStepCommand.CanExecute(null));
        Assert.All(workspace.EdsCandidates, candidate => Assert.False(candidate.IsNameConfirmed));

        workspace.ConfirmSuggestedNamesCommand.Execute(null);
        Assert.All(workspace.EdsCandidates, candidate => Assert.True(candidate.IsNameConfirmed));
        workspace.NextWizardStepCommand.Execute(null);
        Assert.Equal(4, workspace.WizardStep);
        workspace.NextWizardStepCommand.Execute(null);
        Assert.Equal(5, workspace.WizardStep);
        Assert.False(workspace.NextWizardStepCommand.CanExecute(null));

        workspace.SameFieldOfViewConfirmed = true;
        workspace.NextWizardStepCommand.Execute(null);
        Assert.Equal(6, workspace.WizardStep);
        workspace.GroupName = "HAADF + Ti Al V O";
        workspace.CreateGroupCommand.Execute(null);

        MultiChannelAssetGroup group = Assert.Single(workspace.CreateModels());
        Assert.False(workspace.IsWizardOpen);
        Assert.Equal("HAADF + Ti Al V O", group.Name);
        Assert.True(group.SameFieldOfViewConfirmed);
        Assert.Equal(5, group.Members.Count);
        Assert.Equal(sources[0].Asset.Id, group.ReferenceAssetId);
        Assert.Equal("Reference", group.Members[0].Role);
        Assert.Equal("HAADF", group.Members[0].Name);
        Assert.Equal(ChannelNameOrigin.FilenameSuggestion, group.Members[1].NameOrigin);
        Assert.Equal(ushort.MaxValue, group.Members[1].DisplaySettings.DisplayMaximum);
        Assert.Equal(4095, group.Members[2].DisplaySettings.DisplayMaximum);
        Assert.Equal(5, group.Members.Select(member => member.ChannelId).Distinct().Count());
    }

    [Fact]
    public void PendingRegistrationGroup_RestoresAndValidDisplayEditsRaiseChange()
    {
        SourceAssetItemViewModel reference = CreateSource("SEM.tif", 8);
        SourceAssetItemViewModel map = CreateSource("Fe.tif", 8);
        ObservableCollection<SourceAssetItemViewModel> sources = [reference, map];
        ChannelGroupMember referenceMember = CreateMember(reference.Asset.Id, "SEM", "#FFFFFFFF");
        ChannelGroupMember mapMember = CreateMember(map.Asset.Id, "Fe", "#FFFF3B30");
        var model = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "SEM / Fe",
            reference.Asset.Id,
            [referenceMember, mapMember],
            SameFieldOfViewConfirmed: false);
        var workspace = new MultiChannelWorkspaceViewModel(sources);
        int changed = 0;
        workspace.Changed += (_, _) => changed++;

        workspace.Restore([model]);
        MultiChannelAssetGroupViewModel group = Assert.Single(workspace.Groups);
        Assert.Contains("registration required", group.Summary, StringComparison.Ordinal);
        Assert.Equal(0, changed);

        ChannelGroupMemberViewModel member = group.Members[1];
        member.Color = "invalid";
        Assert.Equal(0, changed);
        Assert.Contains("颜色格式无效", workspace.WorkflowStatus, StringComparison.Ordinal);

        member.Color = "#FF00FFFF";
        Assert.Equal(1, changed);
        member.DisplayMinimum = 10;
        member.DisplayMaximum = 240;
        member.Opacity = 0.5;
        member.Gamma = 1.8;
        member.Invert = true;

        MultiChannelAssetGroup edited = Assert.Single(workspace.CreateModels());
        ChannelGroupMember editedMember = edited.Members[1];
        Assert.True(edited.RequiresRegistration);
        Assert.Equal("#FF00FFFF", editedMember.Color);
        Assert.Equal(10, editedMember.DisplaySettings.DisplayMinimum);
        Assert.Equal(240, editedMember.DisplaySettings.DisplayMaximum);
        Assert.Equal(0.5, editedMember.DisplaySettings.Opacity);
        Assert.Equal(1.8, editedMember.DisplaySettings.Gamma);
        Assert.True(editedMember.DisplaySettings.Invert);
    }

    [Fact]
    public void MemberEditor_PreservesAndEditsExplicitInterleavedPlaneSelector()
    {
        SourceAssetItemViewModel reference = CreateSource("reference.tif", 8);
        SourceAssetItemViewModel rgb = CreateSource("rgb.tif", 8, channels: 3, frameCount: 2);
        ChannelGroupMember referenceMember = CreateMember(reference.Asset.Id, "Reference", "#FFFFFFFF");
        ChannelGroupMember rgbMember = CreateMember(rgb.Asset.Id, "Blue", "#FF0000FF") with
        {
            PlaneSelector = ChannelPlaneSelector.InterleavedComponent(0, 2),
        };
        var workspace = new MultiChannelWorkspaceViewModel([reference, rgb]);
        workspace.Restore(
        [
            new MultiChannelAssetGroup(
                Guid.NewGuid(),
                "RGB selector",
                reference.Asset.Id,
                [referenceMember, rgbMember],
                SameFieldOfViewConfirmed: true),
        ]);
        ChannelGroupMemberViewModel editor = Assert.Single(workspace.Groups).Members[1];

        Assert.Equal(ScientificChannelSourceKind.InterleavedComponent, editor.SourceKind);
        Assert.Equal(2, editor.ComponentIndex);
        editor.FrameIndex = 1;
        editor.ComponentIndex = 1;

        ChannelGroupMember restored = Assert.Single(workspace.CreateModels()).Members[1];
        Assert.Equal(ChannelPlaneSelector.InterleavedComponent(1, 1), restored.PlaneSelector);
    }

    private static SourceAssetItemViewModel CreateSource(
        string displayName,
        int bitDepth,
        int channels = 1,
        int frameCount = 1)
    {
        var asset = new SourceAsset(
            Guid.NewGuid(),
            displayName,
            Path.Combine("C:\\science", displayName),
            new SourceFingerprint(1, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            new SciCanvas.Core.Images.ImageMetadata(
                new PixelSize64(10, 10),
                channels,
                bitDepth,
                channels == 1
                    ? bitDepth == 16 ? "Gray16" : "Gray8"
                    : bitDepth == 16 ? "Rgb48" : "Rgb24",
                frameCount: frameCount),
            SourceLinkState.Verified);
        byte[] pixels = [0];
        BitmapSource preview = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Gray8,
            palette: null,
            pixels,
            stride: 1);
        preview.Freeze();
        return new SourceAssetItemViewModel(asset, preview);
    }

    private static ChannelGroupMember CreateMember(Guid assetId, string name, string color)
    {
        Guid channelId = Guid.NewGuid();
        return new ChannelGroupMember(
            channelId,
            assetId,
            ChannelPlaneSelector.ExternalAsset(frameIndex: 0),
            name,
            name == "SEM" ? "Reference" : "ElementalMap",
            color,
            ChannelNameOrigin.User,
            IsNameConfirmed: true,
            new ChannelDisplaySettings(channelId, true, color, 1, 0, 255, 1, false));
    }
}
