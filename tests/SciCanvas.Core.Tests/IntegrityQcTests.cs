using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using LinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;

namespace SciCanvas.Core.Tests;

public sealed class IntegrityQcTests
{
    [Fact]
    public void TransformedDuplicate_DetectsRotate90AndCarriesBothPanelLocations()
    {
        RawCropQcCandidate first = Candidate([1, 2, 3, 4, 5, 6], 3, 2, "a");
        RawCropQcCandidate second = Candidate([4, 1, 5, 2, 6, 3], 2, 3, "d");

        TransformedDuplicateMatch match = Assert.Single(
            TransformedDuplicateDetector.FindExactDuplicates([first, second]));

        Assert.Equal(RawCropTransform.Rotate90, match.Transform);
        Assert.Equal(2, match.Locations.Count);
        Assert.Equal(first.Location.PanelId, match.Locations[0].PanelId);
        Assert.Equal(second.Location.PanelId, match.Locations[1].PanelId);
    }

    [Fact]
    public void TransformedDuplicate_DetectsMirrorXButRejectsOneChangedRawPixel()
    {
        RawCropQcCandidate first = Candidate([1, 2, 3, 4, 5, 6], 3, 2, "a");
        RawCropQcCandidate mirrored = Candidate([4, 5, 6, 1, 2, 3], 3, 2, "b");
        RawCropQcCandidate changed = Candidate([4, 5, 7, 1, 2, 3], 3, 2, "c");

        Assert.Equal(RawCropTransform.MirrorX,
            TransformedDuplicateDetector.FindTransform(first.Plane, mirrored.Plane));
        Assert.Null(TransformedDuplicateDetector.FindTransform(first.Plane, changed.Plane));
    }

    [Fact]
    public void TransformedDuplicate_PreservesUInt16SamplesWithoutDownConversion()
    {
        RawCropQcCandidate first = Candidate16([1, 257, 513, 769, 1025, 65535], 3, 2, "a");
        RawCropQcCandidate rotated = Candidate16([769, 1, 1025, 257, 65535, 513], 2, 3, "b");
        RawCropQcCandidate byteEquivalentButDifferent = Candidate16([513, 1, 1025, 257, 65535, 513], 2, 3, "c");

        Assert.Equal(RawCropTransform.Rotate90,
            TransformedDuplicateDetector.FindTransform(first.Plane, rotated.Plane));
        Assert.Null(TransformedDuplicateDetector.FindTransform(first.Plane, byteEquivalentButDifferent.Plane));
    }

    [Fact]
    public void QcEngine_ReportsPreciseAnalysisLocation()
    {
        ScientificAsset asset = Asset(revision: 2);
        var result = new RoiStatisticsResult
        {
            Id = Guid.NewGuid(),
            SourceAssetId = asset.Id,
            SourceRevision = 1,
            AnalyzerId = "test.v1",
            Region = new PixelRect64(1, 2, 3, 4),
        };
        var analysis = new AnalysisResultObject
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            Result = result,
        };
        ScientificProject project = Project([asset], [analysis]);

        QcResult issue = Assert.Single(
            new QcEngine().Evaluate(new QcContext(project, new QcConfiguration())),
            item => item.RuleId == "analysis.revision-stale");

        Assert.Equal(project.Id, issue.Location.ProjectId);
        Assert.Equal(asset.Id, issue.Location.AssetId);
        Assert.Equal(analysis.Id, issue.Location.ScientificObjectId);
        Assert.Equal(result.Id, issue.Location.AnalysisResultId);
        Assert.Equal(result.Region, issue.Location.SourceRegion);
    }

    [Fact]
    public void QcEngine_ReportsExplicitWarningForImageClippedRoiStatistics()
    {
        ScientificAsset asset = Asset();
        var result = new RoiStatisticsResult
        {
            Id = Guid.NewGuid(),
            SourceAssetId = asset.Id,
            SourceRevision = 1,
            AnalyzerId = "test.roi.v2",
            Region = new PixelRect64(0, 0, 10, 10),
            ClippedToImage = true,
            CoverageFraction = 0.5,
            Validity = AnalysisResultValidity.ReviewRequired("ROI clipped."),
        };
        var analysis = new AnalysisResultObject
        {
            Id = Guid.NewGuid(),
            AssetId = asset.Id,
            Result = result,
        };
        ScientificProject project = Project([asset], [analysis]);

        QcResult warning = Assert.Single(
            new QcEngine().Evaluate(new QcContext(project, new QcConfiguration())),
            item => item.RuleId == "analysis.roi-clipped-to-image");

        Assert.Equal(QcSeverity.Warning, warning.Severity);
        Assert.Contains("0.5", warning.Message, StringComparison.Ordinal);
        Assert.Equal(result.Id, warning.Location.AnalysisResultId);
    }

    [Fact]
    public void QcEngine_ReportsChannelAndMappingIntegrityRules()
    {
        ScientificAsset reference = Asset();
        ScientificAsset target = Asset(revision: 2);
        Guid firstChannel = Guid.NewGuid();
        Guid secondChannel = Guid.NewGuid();
        var group = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS",
            reference.Id,
            [
                Member(firstChannel, reference.Id, "Ti", sourceRevision: 0),
                Member(secondChannel, target.Id, "Ti", sourceRevision: 1, visible: false, invalidRange: true),
            ],
            SameFieldOfViewConfirmed: false);
        var mapping = SpatialMapping.CreateIdentity(
            reference.Id,
            target.Id,
            sourceRevision: 1,
            targetRevision: 1,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
        var links = new SpatialLinkGroup(
            Guid.NewGuid(),
            "linked",
            reference.Id,
            [reference.Id, target.Id],
            LinkSyncOptions.Crop,
            [mapping]);
        ScientificProject project = Project([reference, target], []);

        QcResult[] issues = new QcEngine().Evaluate(new QcContext(
            project,
            new QcConfiguration(),
            MultiChannelGroups: [group],
            LinkGroups: [links])).ToArray();

        Assert.Contains(issues, issue => issue.RuleId == "multichannel.channel-revision-stale" &&
                                                issue.Location.ChannelId == secondChannel);
        Assert.Contains(issues, issue => issue.RuleId == "multichannel.display-range-invalid" &&
                                                issue.Location.ChannelId == secondChannel);
        Assert.Contains(issues, issue => issue.RuleId == "multichannel.duplicate-channel-name");
        Assert.Contains(issues, issue => issue.RuleId == "linked-view.mapping-revision-stale" &&
                                                issue.Location.MappingId == mapping.Id &&
                                                issue.Location.LinkGroupId == links.Id);
        Assert.DoesNotContain(issues, issue => issue.RuleId == "linked-view.crop-bounding-box");
    }

    [Fact]
    public void QcEngine_EmitsTransformedDuplicateWarningWithRelatedLocations()
    {
        ScientificProject project = Project([], []);
        RawCropQcCandidate first = Candidate([1, 2, 3, 4], 2, 2, "a");
        RawCropQcCandidate second = Candidate([3, 1, 4, 2], 2, 2, "d");

        QcResult issue = Assert.Single(
            new QcEngine().Evaluate(new QcContext(
                project,
                new QcConfiguration(),
                RawCrops: [first, second])),
            item => item.RuleId == "integrity.exact-transformed-duplicate");

        Assert.Equal(QcSeverity.Warning, issue.Severity);
        Assert.Contains("Rotate90", issue.Message, StringComparison.Ordinal);
        Assert.Equal(2, issue.RelatedLocations.Count);
    }

    private static RawCropQcCandidate Candidate(byte[] values, int width, int height, string name)
    {
        Guid assetId = Guid.NewGuid();
        return new RawCropQcCandidate(
            new ImagePlane(
                assetId,
                1,
                0,
                new PixelRect64(0, 0, width, height),
                Channel(ScientificSampleType.UInt8, 8),
                new UInt8ImagePlaneSamples(values)),
            new QcIssueLocation(
                FigureId: Guid.NewGuid(),
                PanelId: Guid.NewGuid(),
                AssetId: assetId,
                SourceRegion: new PixelRect64(0, 0, width, height)),
            name);
    }

    private static RawCropQcCandidate Candidate16(ushort[] values, int width, int height, string name)
    {
        Guid assetId = Guid.NewGuid();
        return new RawCropQcCandidate(
            new ImagePlane(
                assetId,
                1,
                0,
                new PixelRect64(0, 0, width, height),
                Channel(ScientificSampleType.UInt16, 16),
                new UInt16ImagePlaneSamples(values)),
            new QcIssueLocation(PanelId: Guid.NewGuid(), AssetId: assetId),
            name);
    }

    private static ScientificChannelDescriptor Channel(ScientificSampleType type, int depth) => new(
        Guid.NewGuid(),
        0,
        "raw",
        ScientificChannelSourceKind.ExternalAsset,
        type,
        depth);

    private static ChannelGroupMember Member(
        Guid channelId,
        Guid assetId,
        string name,
        long sourceRevision,
        bool visible = true,
        bool invalidRange = false) => new(
            channelId,
            assetId,
            ChannelPlaneSelector.ExternalAsset(frameIndex: 0),
            name,
            null,
            "#FFFFFFFF",
            ChannelNameOrigin.User,
            true,
            new ChannelDisplaySettings(
                channelId,
                visible,
                "#FFFFFFFF",
                1,
                0,
                invalidRange ? 0 : 255,
                1,
                false))
        { SourceRevision = sourceRevision };

    private static ScientificProject Project(
        IEnumerable<ScientificAsset> assets,
        IEnumerable<ScientificObject> objects)
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        return new ScientificProject(
            ScientificProject.CurrentSchemaVersion,
            Guid.NewGuid(),
            "Integrity QC",
            assets.ToDictionary(asset => asset.Id),
            new Dictionary<Guid, ScientificFigure>(),
            ProjectStyle.Default,
            objects.ToDictionary(item => item.Id),
            now,
            now);
    }

    private static ScientificAsset Asset(long revision = 1)
    {
        Guid id = Guid.NewGuid();
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        return new ScientificAsset(
            id,
            $"asset-{id:N}",
            new AssetSourceReference(
                $"C:\\data\\{id:N}.tif",
                $"{id:N}.tif",
                new SourceFingerprint(100, now, new string('A', 64), null),
                revision),
            new ImageMetadata(new PixelSize64(100, 80), 1, 16, "Gray16"),
            AssetKind.Sem,
            null,
            new Dictionary<string, object?>(),
            [],
            null,
            SourceLinkState.Verified,
            now,
            now);
    }
}
