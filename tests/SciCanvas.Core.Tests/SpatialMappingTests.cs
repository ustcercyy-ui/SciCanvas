using SciCanvas.Core.Geometry;
using SciCanvas.Core.Linking;

namespace SciCanvas.Core.Tests;

public sealed class SpatialMappingTests
{
    [Fact]
    public void Translation_UsesTargetEqualsMatrixTimesSourceConvention()
    {
        Guid sourceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        SpatialMapping mapping = SpatialMapping.CreateTranslation(
            sourceId,
            targetId,
            sourceRevision: 3,
            targetRevision: 7,
            offsetX: 12.5,
            offsetY: -4,
            createdAt: DateTimeOffset.Parse("2026-08-28T00:00:00Z"));

        Assert.Equal(new SpatialPoint(22.5, 16), mapping.MapForward(new SpatialPoint(10, 20)));
        Assert.Equal(new SpatialPoint(10, 20), mapping.MapReverse(new SpatialPoint(22.5, 16)));
        Assert.True(mapping.MatchesRevisions(3, 7));
        Assert.False(mapping.MatchesRevisions(3, 8));
    }

    [Fact]
    public void LinkGroup_MapsBetweenNonReferenceAssetsWithoutChangingIdentity()
    {
        Guid referenceId = Guid.NewGuid();
        Guid titaniumId = Guid.NewGuid();
        Guid oxygenId = Guid.NewGuid();
        DateTimeOffset createdAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z");
        var group = new LinkGroup(
            Guid.NewGuid(),
            "EDS linked views",
            referenceId,
            [referenceId, titaniumId, oxygenId],
            LinkSyncOptions.Crop | LinkSyncOptions.Roi | LinkSyncOptions.ColorScale,
            [
                SpatialMapping.CreateTranslation(referenceId, titaniumId, 1, 2, 10, -5, createdAt),
                SpatialMapping.CreateTranslation(referenceId, oxygenId, 1, 4, -3, 8, createdAt),
            ]).EnsureValid();

        SpatialPoint mapped = group.MapPoint(titaniumId, oxygenId, new SpatialPoint(110, 95));

        Assert.Equal(new SpatialPoint(97, 108), mapped);
        Assert.True(group.AreMappingsCurrent(new Dictionary<Guid, long>
        {
            [referenceId] = 1,
            [titaniumId] = 2,
            [oxygenId] = 4,
        }));
    }

    [Fact]
    public void MapCrop_UsesHalfOpenCornersAndBoundingRectangle()
    {
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        var group = new LinkGroup(
            Guid.NewGuid(),
            "fractional translation",
            referenceId,
            [referenceId, targetId],
            LinkSyncOptions.Crop,
            [
                SpatialMapping.CreateTranslation(
                    referenceId,
                    targetId,
                    1,
                    1,
                    0.5,
                    -1.25,
                    DateTimeOffset.Parse("2026-08-28T00:00:00Z")),
            ]).EnsureValid();

        PixelRect64 mapped = group.MapCrop(
            referenceId,
            targetId,
            new PixelRect64(10, 20, 100, 50));

        Assert.Equal(new PixelRect64(10, 18, 101, 51), mapped);
    }

    [Fact]
    public void EnsureValid_RejectsMissingTargetMapping()
    {
        Guid referenceId = Guid.NewGuid();
        Guid firstTarget = Guid.NewGuid();
        Guid secondTarget = Guid.NewGuid();
        var group = new LinkGroup(
            Guid.NewGuid(),
            "incomplete",
            referenceId,
            [referenceId, firstTarget, secondTarget],
            LinkSyncOptions.Crop,
            [
                SpatialMapping.CreateIdentity(
                    referenceId,
                    firstTarget,
                    1,
                    1,
                    DateTimeOffset.Parse("2026-08-28T00:00:00Z")),
            ]);

        Assert.Throws<InvalidOperationException>(() => group.EnsureValid());
    }
}
