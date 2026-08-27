using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FigureMeasurementOverlayMapperTests
{
    [Fact]
    public void Map_UsesSourceRectThenContainedPanelImageRectWithoutChangingScientificIdentity()
    {
        Guid sourceId = Guid.NewGuid();
        Guid panelId = Guid.NewGuid();
        Guid measurementId = Guid.NewGuid();
        var source = new SourceAsset(
            sourceId,
            "source.tif",
            "C:\\source.tif",
            new SourceFingerprint(10, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            new ImageMetadata(new PixelSize64(200, 100), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(20, 10, 100, 50),
            new PixelRect64(100, 50, 300, 100),
            "a",
            true,
            PanelId: panelId);
        var overlay = new MeasurementOverlayObject
        {
            Id = Guid.NewGuid(),
            AssetId = sourceId,
            PanelId = panelId,
            SourceRevision = 7,
            MeasurementId = measurementId,
            SourceGeometry = new ScientificMeasurement(
                measurementId,
                sourceId,
                ScientificMeasurementKind.Length,
                new MeasurementPoint(20, 10),
                new MeasurementPoint(120, 60),
                SourceRevision: 7),
            Style = new FigureMeasurementOverlayStyle(
                "#FFFFFF00", 2, "solid", "#FFFFFF00", 0,
                "#FF000000", "#FFFFFFFF", 6, true,
                "#FFFFFFFF", "Arial", 9, false, true),
        };

        FigureMeasurementOverlayGeometry mapped = FigureMeasurementOverlayMapper.Map(overlay, panel);

        Assert.Equal(150, mapped.ImageRect.X);
        Assert.Equal(50, mapped.ImageRect.Y);
        Assert.Equal(200, mapped.ImageRect.Width);
        Assert.Equal(100, mapped.ImageRect.Height);
        Assert.Equal(new MeasurementPoint(150, 50), mapped.PointA);
        Assert.Equal(new MeasurementPoint(350, 150), mapped.PointB);
        Assert.Equal(measurementId, overlay.MeasurementId);
        Assert.Equal(sourceId, overlay.SourceGeometry.SourceAssetId);
        Assert.Equal(7, overlay.SourceGeometry.SourceRevision);
    }
}