using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Science;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkingSpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using LinkingSpatialMappingKind = SciCanvas.Core.Linking.SpatialMappingKind;
using LinkingLinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class CanonicalRoiTests
{
    [Fact]
    public void PolygonMask_UsesOnlyRawPixelsWhoseCentersAreInsidePolygon()
    {
        Guid assetId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        var channel = new ScientificChannelDescriptor(
            channelId,
            0,
            "HAADF",
            ScientificChannelSourceKind.ExternalAsset,
            ScientificSampleType.UInt8,
            8);
        var plane = new ImagePlane(
            assetId,
            1,
            0,
            new PixelRect64(0, 0, 10, 10),
            channel,
            new UInt8ImagePlaneSamples(Enumerable.Range(0, 100).Select(value => (byte)value)));
        RoiObject roi = CreatePolygon(
            assetId,
            1,
            [new(0, 0), new(10, 0), new(0, 10)]);

        var result = PolygonRoiStatisticsCalculator.AnalyzeRawPlane(plane, roi, histogramBinCount: 16);

        Assert.Equal(55, result.PixelCount);
        Assert.Equal(0, result.Minimum);
        Assert.Equal(90, result.Maximum);
        Assert.Equal(1815, result.IntegratedIntensity);
        Assert.Equal(33, result.Mean, 10);
        Assert.NotEqual(result.Region.Width * result.Region.Height, result.PixelCount);
        Assert.True(result.IsValid);
        Assert.Equal(roi.SourceGeometry, result.PolygonMask);
        Assert.Equal(channelId, result.ScientificChannelId);
    }

    [Fact]
    public void PolygonPropagation_MapsEveryVertexAndPersistsProvenance()
    {
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        LinkingSpatialMapping mapping = SpatialRegistrationSolver.Solve(
            referenceId,
            targetId,
            1,
            2,
            LinkingSpatialMappingKind.Affine,
            [
                Pair(0, 0, 5, 7),
                Pair(10, 0, 25, 7),
                Pair(0, 10, 5, 37),
            ],
            DateTimeOffset.Parse("2026-08-28T00:00:00Z")).Mapping;
        var group = new SpatialLinkGroup(
            Guid.NewGuid(),
            "SEM / EDS",
            referenceId,
            [referenceId, targetId],
            LinkingLinkSyncOptions.Roi,
            [mapping]).EnsureValid();
        RoiObject reference = CreatePolygon(
            referenceId,
            1,
            [new(1, 2), new(4, 2), new(1, 5)]);

        RoiObject target = Assert.Single(RoiPropagationService.PropagatePolygon(
            reference,
            group,
            new Dictionary<Guid, long> { [referenceId] = 1, [targetId] = 2 }));

        Assert.Equal(targetId, target.AssetId);
        Assert.Equal(2, target.SourceRevision);
        Assert.Equal(new MeasurementPoint(7, 13), target.SourceGeometry[0]);
        Assert.Equal(new MeasurementPoint(13, 13), target.SourceGeometry[1]);
        Assert.Equal(new MeasurementPoint(7, 22), target.SourceGeometry[2]);
        Assert.Equal(reference.Id, target.Propagation!.ReferenceRoiId);
        Assert.Equal(target.Id, target.Propagation.TargetRoiId);
        Assert.Equal(group.Id, target.Propagation.LinkGroupId);
        Assert.Equal(mapping.Id, target.Propagation.MappingId);
    }

    [Fact]
    public void PolygonPropagation_RejectsStaleRegistration()
    {
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        var group = new SpatialLinkGroup(
            Guid.NewGuid(),
            "stale",
            referenceId,
            [referenceId, targetId],
            LinkingLinkSyncOptions.Roi,
            [LinkingSpatialMapping.CreateIdentity(
                referenceId,
                targetId,
                1,
                1,
                DateTimeOffset.Parse("2026-08-28T00:00:00Z"))]).EnsureValid();
        RoiObject reference = CreatePolygon(
            referenceId,
            1,
            [new(0, 0), new(4, 0), new(0, 4)]);

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RoiPropagationService.PropagatePolygon(
                reference,
                group,
                new Dictionary<Guid, long> { [referenceId] = 1, [targetId] = 2 }));

        Assert.Contains("mapping-revision-stale", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CrossChannelStatistics_ReadsEachExternalAssetsRawPlane()
    {
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        SourceAsset referenceAsset = CreateSource(referenceId, "HAADF");
        SourceAsset targetAsset = CreateSource(targetId, "Ti");
        LinkingSpatialMapping mapping = LinkingSpatialMapping.CreateIdentity(
            referenceId,
            targetId,
            1,
            1,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
        var linkGroup = new SpatialLinkGroup(
            Guid.NewGuid(),
            "HAADF / Ti",
            referenceId,
            [referenceId, targetId],
            LinkingLinkSyncOptions.Roi,
            [mapping]).EnsureValid();
        Guid referenceChannelId = Guid.NewGuid();
        Guid targetChannelId = Guid.NewGuid();
        var channelGroup = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS",
            referenceId,
            [
                Member(referenceChannelId, referenceId, "HAADF"),
                Member(targetChannelId, targetId, "Ti"),
            ],
            SameFieldOfViewConfirmed: true).EnsureValid();
        RoiObject referenceRoi = CreatePolygon(
            referenceId,
            1,
            [new(0, 0), new(4, 0), new(0, 4)]);
        IReadOnlyList<RoiObject> propagated = RoiPropagationService.PropagatePolygon(
            referenceRoi,
            linkGroup,
            new Dictionary<Guid, long> { [referenceId] = 1, [targetId] = 1 });
        var reader = new ConstantRawPlaneReader(new Dictionary<Guid, byte>
        {
            [referenceId] = 10,
            [targetId] = 200,
        });

        IReadOnlyList<CrossChannelRoiStatisticsEntry> results =
            await CrossChannelRoiStatisticsService.AnalyzeAsync(
                referenceRoi,
                propagated,
                linkGroup,
                channelGroup,
                new Dictionary<Guid, RoiAnalysisSource>
                {
                    [referenceId] = new(referenceAsset, 1),
                    [targetId] = new(targetAsset, 1),
                },
                reader,
                histogramBinCount: 16);

        Assert.Equal(2, results.Count);
        Assert.Equal(10, results.Single(item => item.ChannelMember.ChannelId == referenceChannelId).Statistics.Mean);
        Assert.Equal(200, results.Single(item => item.ChannelMember.ChannelId == targetChannelId).Statistics.Mean);
        Assert.All(results, result =>
        {
            Assert.Equal(result.ChannelMember.ChannelId, result.Statistics.ScientificChannelId);
            Assert.Equal(PolygonRoiStatisticsCalculator.AnalyzerVersion, result.Statistics.AnalyzerId);
            Assert.True(result.Statistics.IsValid);
        });
        Assert.Equal(2, reader.RequestedAssets.Count);
    }

    private static RoiObject CreatePolygon(
        Guid assetId,
        long revision,
        IReadOnlyList<MeasurementPoint> points) => new RoiObject
        {
            Id = Guid.NewGuid(),
            AssetId = assetId,
            SourceRevision = revision,
            SourceGeometry = points,
            GeometryKind = RoiGeometryKind.Polygon,
            FrameIndex = 0,
        }.EnsureValid();

    private static RegistrationLandmarkPair Pair(
        double sourceX,
        double sourceY,
        double targetX,
        double targetY) => new(
            Guid.NewGuid(),
            new SpatialPoint(sourceX, sourceY),
            new SpatialPoint(targetX, targetY));

    private static SourceAsset CreateSource(Guid id, string name) => new(
        id,
        name,
        $"{name}.tif",
        new SourceFingerprint(0, DateTimeOffset.UtcNow, new string('0', 64), null),
        new ImageMetadata(new PixelSize64(10, 10), 1, 8, "Gray8"),
        SourceLinkState.Verified);

    private static ChannelGroupMember Member(Guid channelId, Guid assetId, string name) => new ChannelGroupMember(
        channelId,
        assetId,
        0,
        name,
        null,
        "#FFFFFFFF",
        ChannelNameOrigin.User,
        true,
new ChannelDisplaySettings(
            channelId,
            Visible: true,
            "#FFFFFFFF",
            Opacity: 1,
            DisplayMinimum: 0,
            DisplayMaximum: 255,
            Gamma: 1,
            Invert: false)).EnsureValid();

    private sealed class ConstantRawPlaneReader(IReadOnlyDictionary<Guid, byte> values) : IImagePlaneReader
    {
        public List<Guid> RequestedAssets { get; } = [];

        public ValueTask<ImagePlane> ReadAsync(
            SourceAsset source,
            ImagePlaneRequest request,
            CancellationToken cancellationToken = default)
        {
            request.EnsureValid();
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
