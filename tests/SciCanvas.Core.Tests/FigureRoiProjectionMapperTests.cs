using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FigureRoiProjectionMapperTests
{
    [Fact]
    public void Map_ResolvesCanonicalGeometryThroughReferencedPanelWithoutCopyingIt()
    {
        Guid sourceId = Guid.NewGuid();
        Guid panelId = Guid.NewGuid();
        Guid roiId = Guid.NewGuid();
        var source = new SourceAsset(
            sourceId,
            "source.tif",
            "C:\\source.tif",
            new SourceFingerprint(10, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            new ImageMetadata(new PixelSize64(200, 100), 1, 16, "Gray16"),
            SourceLinkState.Verified);
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(20, 10, 100, 50),
            new PixelRect64(100, 50, 300, 100),
            "a",
            true,
            FrameIndex: 2,
            PanelId: panelId,
            SourceRevision: 7);
        var roi = new RoiObject
        {
            Id = roiId,
            AssetId = sourceId,
            SourceRevision = 7,
            GeometryKind = RoiGeometryKind.Polygon,
            FrameIndex = 2,
            SourceGeometry =
            [
                new MeasurementPoint(20, 10),
                new MeasurementPoint(120, 10),
                new MeasurementPoint(120, 60),
            ],
            Style = RoiStyle.Default with { Label = "cell-1" },
        }.EnsureValid();
        var projection = new RoiFigureProjectionObject
        {
            Id = Guid.NewGuid(),
            RoiId = roiId,
            PanelId = panelId,
            AssetId = sourceId,
            SourceRevision = 7,
        };

        FigureRoiProjectionGeometry mapped = FigureRoiProjectionMapper.Map(
            new FigureRoiProjectionExportItem(projection, roi),
            panel,
            144);

        Assert.Equal(RoiGeometryKind.Polygon, mapped.Kind);
        Assert.Equal(new MeasurementPoint(150, 50), mapped.Points[0]);
        Assert.Equal(new MeasurementPoint(350, 50), mapped.Points[1]);
        Assert.Equal(new MeasurementPoint(350, 150), mapped.Points[2]);
        Assert.Equal("cell-1", mapped.Style.Label);
        Assert.Same(roi, new FigureRoiProjectionExportItem(projection, roi).CanonicalRoi);
        Assert.Equal(roiId, projection.RoiId);
    }

    [Fact]
    public void ValidateRelationship_RejectsStaleSourceRevision()
    {
        Guid sourceId = Guid.NewGuid();
        Guid panelId = Guid.NewGuid();
        Guid roiId = Guid.NewGuid();
        var source = new SourceAsset(
            sourceId,
            "source.tif",
            "C:\\source.tif",
            new SourceFingerprint(10, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            new ImageMetadata(new PixelSize64(100, 100), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(0, 0, 100, 100),
            new PixelRect64(0, 0, 100, 100),
            "a",
            true,
            PanelId: panelId,
            SourceRevision: 8);
        var roi = new RoiObject
        {
            Id = roiId,
            AssetId = sourceId,
            SourceRevision = 7,
            GeometryKind = RoiGeometryKind.Rectangle,
            SourceGeometry = [new MeasurementPoint(10, 10), new MeasurementPoint(20, 20)],
        }.EnsureValid();
        var projection = new RoiFigureProjectionObject
        {
            Id = Guid.NewGuid(),
            RoiId = roiId,
            PanelId = panelId,
            AssetId = sourceId,
            SourceRevision = 7,
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            FigureRoiProjectionMapper.ValidateRelationship(
                new FigureRoiProjectionExportItem(projection, roi),
                panel));

        Assert.Contains("source revision/frame", exception.Message, StringComparison.Ordinal);
    }
}
