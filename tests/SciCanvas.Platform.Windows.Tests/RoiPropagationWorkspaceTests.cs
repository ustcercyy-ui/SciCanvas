using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using LinkingLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkingLinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using LinkingSpatialMapping = SciCanvas.Core.Linking.SpatialMapping;

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
        LinkingSpatialMapping translation = LinkingSpatialMapping.CreateTranslation(
            referenceSource.Asset.Id,
            targetSource.Asset.Id,
            referenceSource.SourceRevision,
            targetSource.SourceRevision,
            1,
            0,
            DateTimeOffset.UnixEpoch);
        figure.LinkGroups.Clear();
        figure.LinkGroups.Add(new LinkingLinkGroup(
            Guid.NewGuid(),
            "translated raw targets",
            referenceSource.Asset.Id,
            [referenceSource.Asset.Id, targetSource.Asset.Id],
            LinkingLinkSyncOptions.Crop | LinkingLinkSyncOptions.Roi,
            [translation]).EnsureValid());
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
            PolygonText = "0,0\n8,0\n0,8",
            Label = "Grain A",
        };

        workspace.CreateAndPropagateCommand.Execute(null);

        Assert.Equal(2, workspace.Rois.Count);
        RoiObjectItemViewModel referenceRoi = workspace.Rois.Single(item => item.Model.Propagation is null);
        RoiObjectItemViewModel targetRoi = workspace.Rois.Single(item => item.Model.Propagation is not null);
        Assert.Equal("Grain A", referenceRoi.Label);
        Assert.Equal(
            referenceRoi.Model.SourceGeometry
                .Select(point => new MeasurementPoint(point.X + 1, point.Y))
                .ToArray(),
            targetRoi.Model.SourceGeometry);
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
        Assert.Equal(
            180,
            Assert.IsType<RoiStatisticsResult>(Assert.Single(targetSource.AnalysisResults)).Mean);
        Assert.All(referenceSource.AnalysisResults.Concat(targetSource.AnalysisResults), result =>
        {
            var roi = Assert.IsType<SciCanvas.Core.Science.RoiStatisticsResult>(result);
            Assert.Equal(SciCanvas.Core.Workspace.PolygonRoiStatisticsCalculator.AnalyzerVersion, roi.AnalyzerId);
            Assert.NotNull(roi.ScientificChannelId);
            Assert.NotEmpty(roi.PolygonMask);
        });
        Assert.Equal(2, reader.RequestedAssets.Count);
        Assert.Contains(targetSource.Asset.Id, reader.RequestedAssets);
    }

    [Fact]
    public async Task Workspace_SameTargetAssetMultipleFramesNeverUsesAssetOnlyDictionaryIdentity()
    {
        SourceAssetItemViewModel targetSource = CreateSource("stack.tif", 10, 10, frameCount: 2);
        SourceAssetItemViewModel referenceSource = CreateSource("reference.tif", 10, 10);
        var sources = new ObservableCollection<SourceAssetItemViewModel>
        {
            referenceSource,
            targetSource,
        };
        var channels = new MultiChannelWorkspaceViewModel(sources);
        Guid referenceChannelId = Guid.NewGuid();
        Guid frame0ChannelId = Guid.NewGuid();
        Guid frame1ChannelId = Guid.NewGuid();
        ChannelGroupMember Member(
            Guid channelId,
            SourceAssetItemViewModel source,
            ChannelPlaneSelector selector,
            string name) => new(
                channelId,
                source.Asset.Id,
                selector,
                name,
                null,
                "#FFFFFFFF",
                ChannelNameOrigin.User,
                true,
                new ChannelDisplaySettings(channelId, true, "#FFFFFFFF", 1, 0, 255, 1, false))
            {
                SourceRevision = source.SourceRevision,
            };
        var channelGroup = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "Reference + stack frames",
            referenceSource.Asset.Id,
            [
                Member(referenceChannelId, referenceSource, ChannelPlaneSelector.ExternalAsset(0), "Reference"),
                Member(frame0ChannelId, targetSource, ChannelPlaneSelector.FramePlane(0), "Frame 0"),
                Member(frame1ChannelId, targetSource, ChannelPlaneSelector.FramePlane(1), "Frame 1"),
            ],
            SameFieldOfViewConfirmed: true).EnsureValid();
        channels.Restore([channelGroup]);
        FigureCanvasViewModel figure = CreateFigure();
        FigurePanelViewModel targetPanel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(targetSource, new PixelRect64(0, 0, 10, 10)));
        FigurePanelViewModel referencePanel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(referenceSource, new PixelRect64(0, 0, 10, 10)));
        figure.SelectPanel(targetPanel, toggle: false);
        figure.SelectPanel(referencePanel, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);
        var reader = new RecordingRawPlaneReader();
        using var workspace = new RoiPropagationWorkspaceViewModel(sources, channels, figure, reader)
        {
            PolygonText = "0,0\n8,0\n0,8",
        };

        workspace.CreateAndPropagateCommand.Execute(null);

        Assert.True(
            workspace.Rois.Count == 3,
            $"Expected three plane-bound ROIs. Workspace status: {workspace.StatusText}");
        Assert.Equal(
            [0, 1],
            workspace.Rois
                .Where(item => item.AssetId == targetSource.Asset.Id)
                .Select(item => item.Model.FrameIndex)
                .Order()
                .ToArray());

        await workspace.AnalyzeAcrossChannelsAsync();

        Assert.Equal(3, workspace.Statistics.Count);
        Assert.Equal(
            [0, 1],
            reader.Requests
                .Where(request => request.AssetId == targetSource.Asset.Id)
                .Select(request => request.Selector.FrameIndex)
                .Order()
                .ToArray());
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

    [Fact]
    public void DirectRoiEditing_ValidatesEveryMutationAndNeverClampsOrPartiallyApplies()
    {
        SourceAssetItemViewModel source = CreateSource("source.tif", 100, 80);
        var sources = new ObservableCollection<SourceAssetItemViewModel> { source };
        var channels = new MultiChannelWorkspaceViewModel(sources);
        FigureCanvasViewModel figure = CreateFigure();
        using var workspace = new RoiPropagationWorkspaceViewModel(sources, channels, figure);

        workspace.AddDirectRoi(
            source,
            RoiGeometryKind.Rectangle,
            [new MeasurementPoint(5, 5), new MeasurementPoint(25, 20)]);
        workspace.AddDirectRoi(
            source,
            RoiGeometryKind.Ellipse,
            [new MeasurementPoint(30, 5), new MeasurementPoint(50, 25)]);
        RoiObjectItemViewModel polygon = workspace.AddDirectRoi(
            source,
            RoiGeometryKind.Polygon,
            [
                new MeasurementPoint(10, 30),
                new MeasurementPoint(40, 30),
                new MeasurementPoint(40, 60),
                new MeasurementPoint(10, 60),
            ],
            "editable");
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(0, 0, 100, 80)));
        FigureRoiProjectionViewModel projection = figure.AddRoiProjection(polygon.Model, panel);
        workspace.AddDirectRoi(
            source,
            RoiGeometryKind.Polyline,
            [new MeasurementPoint(55, 10), new MeasurementPoint(70, 20)]);

        Assert.Equal(
            [
                RoiGeometryKind.Rectangle,
                RoiGeometryKind.Ellipse,
                RoiGeometryKind.Polygon,
                RoiGeometryKind.Polyline,
            ],
            workspace.Rois.Select(item => item.Model.GeometryKind));
        Assert.All(workspace.Rois, item =>
            Assert.Equal(ScientificValidityState.Valid, item.Model.Validity.State));

        workspace.SelectedRoi = polygon;
        Assert.True(workspace.TryInsertSelectedPolygonVertex(new MeasurementPoint(25, 30)));
        Assert.Equal(5, polygon.Model.SourceGeometry.Count);
        Assert.Same(polygon.Model, projection.CanonicalRoi);
        Assert.Equal(
            polygon.Model.SourceGeometry,
            projection.CreateExportItem().CanonicalRoi.SourceGeometry);
        Assert.True(workspace.TryDeleteSelectedPolygonVertex(1));
        Assert.Equal(4, polygon.Model.SourceGeometry.Count);

        IReadOnlyList<MeasurementPoint> beforeSelfIntersection =
            polygon.Model.SourceGeometry.ToArray();
        Assert.False(workspace.TryUpdateSelectedRoiVertex(1, new MeasurementPoint(20, 70)));
        Assert.Equal(beforeSelfIntersection, polygon.Model.SourceGeometry);
        Assert.Contains("拒绝", workspace.StatusText, StringComparison.Ordinal);

        Assert.True(workspace.TryMoveSelectedRoi(5, 0));
        IReadOnlyList<MeasurementPoint> beforeOutsideMove = polygon.Model.SourceGeometry.ToArray();
        Assert.False(workspace.TryMoveSelectedRoi(100, 0));
        Assert.Equal(beforeOutsideMove, polygon.Model.SourceGeometry);
        Assert.Contains("拒绝", workspace.StatusText, StringComparison.Ordinal);

        panel.ApplyLinkedCrop(new PixelRect64(0, 0, 48, 80));
        IReadOnlyList<MeasurementPoint> beforePanelOutsideMove = polygon.Model.SourceGeometry.ToArray();
        Assert.False(workspace.TryMoveSelectedRoi(5, 0));
        Assert.Equal(beforePanelOutsideMove, polygon.Model.SourceGeometry);
        Assert.Same(polygon.Model, projection.CanonicalRoi);
        PixelRect64 acceptedPanelCrop = panel.SourceRect;
        panel.ApplyLinkedCrop(new PixelRect64(0, 0, 40, 80));
        Assert.Equal(acceptedPanelCrop, panel.SourceRect);
        Assert.Contains(
            "Panel 修改已回滚",
            figure.LinkSynchronizationStatusText,
            StringComparison.Ordinal);

        while (polygon.Model.SourceGeometry.Count > 3)
        {
            Assert.True(workspace.TryDeleteSelectedPolygonVertex(0));
        }
        Assert.False(workspace.TryDeleteSelectedPolygonVertex(0));
        Assert.Equal(3, polygon.Model.SourceGeometry.Count);
    }

    private static FigureCanvasViewModel CreateFigure() => new(
        new BuiltInTemplateCatalog().LoadAll().Single(
            template => template.Id == "materials.multiscale-morphology.nature-double"));

    private static SourceAssetItemViewModel CreateSource(
        string name,
        int width,
        int height,
        int frameCount = 1)
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
            new CoreImageMetadata(
                new PixelSize64(width, height),
                1,
                8,
                "Gray8",
                frameCount: frameCount),
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
                ChannelPlaneSelector.ExternalAsset(frameIndex: 0),
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

    private sealed class RecordingRawPlaneReader : IImagePlaneReader
    {
        public List<ScientificPlaneRef> Requests { get; } = [];

        public ValueTask<ImagePlane> ReadAsync(
            SourceAsset source,
            ImagePlaneRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request.PlaneRef);
            int count = checked((int)(request.Region.Width * request.Region.Height));
            byte value = (byte)(10 + request.PlaneSelector.FrameIndex * 20);
            return ValueTask.FromResult(new ImagePlane(
                source.Id,
                request.SourceRevision,
                request.FrameIndex,
                request.Region,
                request.ChannelSelector,
                new UInt8ImagePlaneSamples(Enumerable.Repeat(value, count))));
        }
    }
}
