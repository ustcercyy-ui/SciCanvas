using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class RoiPropagationWorkspaceTests
{
    [Fact]
    public async Task Workspace_PropagatesCanonicalPolygonAndStoresRawStatisticsPerChannel()
    {
        SourceAssetItemViewModel targetSource = CreateSource("Ti.tif", 10, 10);
        SourceAssetItemViewModel referenceSource = CreateSource("HAADF.tif", 10, 10);
        var sources = new ObservableCollection<SourceAssetItemViewModel>
        {
            targetSource,
            referenceSource,
        };
        var channels = new MultiChannelWorkspaceViewModel(sources);
        MultiChannelAssetGroup channelGroup = CreateChannelGroup(referenceSource, targetSource);
        channels.Restore([channelGroup]);
        FigureCanvasViewModel figure = CreateFigure();
        FigurePanelViewModel target = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(targetSource, new PixelRect64(0, 0, 10, 10)));
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(referenceSource, new PixelRect64(0, 0, 10, 10)));
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);
        var reader = new ConstantRawPlaneReader(new Dictionary<Guid, byte>
        {
            [referenceSource.Asset.Id] = 20,
            [targetSource.Asset.Id] = 180,
        });
        using var workspace = new RoiPropagationWorkspaceViewModel(
            sources,
            channels,
            figure,
            reader)
        {
            PolygonText = "0,0\n10,0\n0,10",
            Label = "Grain A",
        };

        workspace.CreateAndPropagateCommand.Execute(null);

        Assert.Equal(2, workspace.Rois.Count);
        RoiObjectItemViewModel referenceRoi = workspace.Rois.Single(item => item.Model.Propagation is null);
        RoiObjectItemViewModel targetRoi = workspace.Rois.Single(item => item.Model.Propagation is not null);
        Assert.Equal("Grain A", referenceRoi.Label);
        Assert.Equal(referenceRoi.Model.SourceGeometry, targetRoi.Model.SourceGeometry);
        Assert.Equal(referenceRoi.Model.Id, targetRoi.Model.Propagation!.ReferenceRoiId);
        Assert.Equal(figure.LinkGroups[0].Id, targetRoi.Model.Propagation.LinkGroupId);
        Assert.Equal(2, workspace.CreateModels().Count);

        await workspace.AnalyzeAcrossChannelsAsync();

        Assert.Equal(2, workspace.Statistics.Count);
        Assert.Contains(workspace.Statistics, item =>
            item.ChannelName == "HAADF" && item.Summary.Contains("mean=20", StringComparison.Ordinal));
        Assert.Contains(workspace.Statistics, item =>
            item.ChannelName == "Ti" && item.Summary.Contains("mean=180", StringComparison.Ordinal));
        Assert.Single(referenceSource.AnalysisResults);
        Assert.Single(targetSource.AnalysisResults);
        Assert.All(referenceSource.AnalysisResults.Concat(targetSource.AnalysisResults), result =>
        {
            var roi = Assert.IsType<SciCanvas.Core.Science.RoiStatisticsResult>(result);
            Assert.Equal(SciCanvas.Core.Workspace.PolygonRoiStatisticsCalculator.AnalyzerVersion, roi.AnalyzerId);
            Assert.NotNull(roi.ScientificChannelId);
            Assert.NotEmpty(roi.PolygonMask);
        });
        Assert.Equal(2, reader.RequestedAssets.Count);
    }

    [Fact]
    public void Workspace_StaleRegistrationBlocksPropagation()
    {
        SourceAssetItemViewModel targetSource = CreateSource("Ti.tif", 10, 10);
        SourceAssetItemViewModel referenceSource = CreateSource("HAADF.tif", 10, 10);
        var sources = new ObservableCollection<SourceAssetItemViewModel> { targetSource, referenceSource };
        var channels = new MultiChannelWorkspaceViewModel(sources);
        channels.Restore([CreateChannelGroup(referenceSource, targetSource)]);
        FigureCanvasViewModel figure = CreateFigure();
        FigurePanelViewModel target = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(targetSource, new PixelRect64(0, 0, 10, 10)));
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(referenceSource, new PixelRect64(0, 0, 10, 10)));
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);
        targetSource.RestoreSourceRevision(2);
        using var workspace = new RoiPropagationWorkspaceViewModel(sources, channels, figure)
        {
            PolygonText = "0,0\n8,0\n0,8",
        };

        workspace.CreateAndPropagateCommand.Execute(null);

        Assert.Empty(workspace.Rois);
        Assert.Contains("mapping-revision-stale", workspace.StatusText, StringComparison.Ordinal);
    }

    private static FigureCanvasViewModel CreateFigure() => new(
        new BuiltInTemplateCatalog().LoadAll().Single(
            template => template.Id == "materials.multiscale-morphology.nature-double"));

    private static SourceAssetItemViewModel CreateSource(string name, int width, int height)
    {
        int stride = width;
        byte[] pixels = new byte[stride * height];
        BitmapSource preview = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Gray8,
            palette: null,
            pixels,
            stride);
        preview.Freeze();
        var asset = new SourceAsset(
            Guid.NewGuid(),
            name,
            name,
            new SourceFingerprint(0, DateTimeOffset.UtcNow, new string('0', 64), null),
            new CoreImageMetadata(new PixelSize64(width, height), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }

    private static MultiChannelAssetGroup CreateChannelGroup(
        SourceAssetItemViewModel reference,
        SourceAssetItemViewModel target)
    {
        ChannelGroupMember Member(SourceAssetItemViewModel source, string name)
        {
            Guid channelId = Guid.NewGuid();
            return new ChannelGroupMember(
                channelId,
                source.Asset.Id,
                0,
                name,
                null,
                "#FFFFFFFF",
                ChannelNameOrigin.User,
                true,
                new ChannelDisplaySettings(
                    channelId,
                    true,
                    "#FFFFFFFF",
                    1,
                    0,
                    255,
                    1,
                    false)).EnsureValid();
        }

        return new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "HAADF + Ti",
            reference.Asset.Id,
            [Member(reference, "HAADF"), Member(target, "Ti")],
            SameFieldOfViewConfirmed: true).EnsureValid();
    }

    private sealed class ConstantRawPlaneReader(IReadOnlyDictionary<Guid, byte> values) : IImagePlaneReader
    {
        public List<Guid> RequestedAssets { get; } = [];

        public ValueTask<ImagePlane> ReadAsync(
            SourceAsset source,
            ImagePlaneRequest request,
            CancellationToken cancellationToken = default)
        {
            RequestedAssets.Add(source.Id);
            int count = checked((int)(request.Region.Width * request.Region.Height));
            return ValueTask.FromResult(new ImagePlane(
                source.Id,
                request.SourceRevision,
                request.FrameIndex,
                request.Region,
                request.ChannelSelector,
                new UInt8ImagePlaneSamples(Enumerable.Repeat(values[source.Id], count))));
        }
    }
}
