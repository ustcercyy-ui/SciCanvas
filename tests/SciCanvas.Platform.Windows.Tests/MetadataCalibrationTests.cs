using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class MetadataCalibrationTests
{
    [Fact]
    public void AcceptRevision_DoesNotOverwriteManualCalibration()
    {
        Guid sourceId = Guid.NewGuid();
        SourceAssetItemViewModel item = CreateSourceItem(
            sourceId,
            CreateMetadata(0.5, 0.5, "µm"));
        item.Calibration.Restore(new SpatialCalibration(
            sourceId,
            2,
            3,
            "nm",
            CalibrationOrigin.Manual));

        item.AcceptRevision(
            CreateSource(sourceId, CreateMetadata(0.25, 0.25, "µm")),
            CreatePreview());

        Assert.Equal(2, item.SourceRevision);
        Assert.Equal(CalibrationOrigin.Manual, item.Calibration.Origin);
        Assert.Equal(2, item.Calibration.UnitsPerPixelX);
        Assert.Equal(3, item.Calibration.UnitsPerPixelY);
        Assert.Equal("nm", item.Calibration.Unit);
    }

    [Fact]
    public void AcceptRevision_DoesNotOverwriteLinkedCalibration()
    {
        Guid sourceId = Guid.NewGuid();
        SourceAssetItemViewModel item = CreateSourceItem(
            sourceId,
            CreateMetadata(0.5, 0.5, "µm"));
        item.Calibration.Restore(new SpatialCalibration(
            sourceId,
            1.25,
            1.5,
            "nm",
            CalibrationOrigin.Linked));

        item.AcceptRevision(
            CreateSource(sourceId, CreateMetadata(0.25, 0.25, "µm")),
            CreatePreview());

        Assert.Equal(2, item.SourceRevision);
        Assert.Equal(CalibrationOrigin.Linked, item.Calibration.Origin);
        Assert.Equal(1.25, item.Calibration.UnitsPerPixelX);
        Assert.Equal(1.5, item.Calibration.UnitsPerPixelY);
        Assert.Equal("nm", item.Calibration.Unit);
    }

    [Fact]
    public void AcceptRevision_RefreshesMetadataCalibrationFromNewOmeRevision()
    {
        Guid sourceId = Guid.NewGuid();
        SourceAssetItemViewModel item = CreateSourceItem(
            sourceId,
            CreateOmeOnlyMetadata(0.5, "µm", 0.75, "µm"));

        Assert.Equal(CalibrationOrigin.Metadata, item.Calibration.Origin);
        Assert.Equal(0.5, item.Calibration.UnitsPerPixelX);
        Assert.Equal(0.75, item.Calibration.UnitsPerPixelY);

        item.AcceptRevision(
            CreateSource(sourceId, CreateOmeOnlyMetadata(0.25, "µm", 0.4, "µm")),
            CreatePreview());

        Assert.Equal(2, item.SourceRevision);
        Assert.Equal(CalibrationOrigin.Metadata, item.Calibration.Origin);
        Assert.Equal(0.25, item.Calibration.UnitsPerPixelX);
        Assert.Equal(0.4, item.Calibration.UnitsPerPixelY);
        Assert.Equal("µm", item.Calibration.Unit);
    }

    [Fact]
    public void OmeCalibration_DoesNotGuessWhenAxisUnitsAreIncompatible()
    {
        Guid sourceId = Guid.NewGuid();
        SourceAssetItemViewModel item = CreateSourceItem(
            sourceId,
            CreateOmeOnlyMetadata(1, "pixel-x-unit", 2, "pixel-y-unit"));

        Assert.False(item.Calibration.IsCalibrated);
        Assert.Equal(CalibrationOrigin.None, item.Calibration.Origin);
    }

    [Fact]
    public void ReferenceLineDirection_CanBeAdjustedAfterDrawing()
    {
        var calibration = new CalibrationEditorViewModel(
            Guid.NewGuid(),
            metadataUnitsPerPixelX: null,
            metadataUnitsPerPixelY: null,
            metadataUnit: null,
            sourceWidth: 200,
            sourceHeight: 160);
        calibration.BeginReferenceLine(50, 50);
        calibration.UpdateReferenceLine(150, 50);
        calibration.CompleteReferenceLine();
        double originalLength = calibration.ReferencePixelLength;

        calibration.ReferenceAngleDegrees = 90;

        Assert.Equal(90, calibration.ReferenceAngleDegrees, 8);
        Assert.Equal(calibration.ReferenceStartX, calibration.ReferenceEndX, 8);
        Assert.Equal(originalLength, calibration.ReferencePixelLength, 8);
        Assert.True(calibration.SetReferenceHorizontalCommand.CanExecute(null));
    }

    private static CoreImageMetadata CreateMetadata(double x, double y, string unit) => new(
        new PixelSize64(2, 2),
        1,
        8,
        "Gray8",
        physicalSizeX: x,
        physicalSizeY: y,
        physicalUnit: unit);

    private static CoreImageMetadata CreateOmeOnlyMetadata(
        double x,
        string xUnit,
        double y,
        string yUnit) => new(
        new PixelSize64(2, 2),
        1,
        8,
        "Gray8",
        ome: new OmeImageMetadata(
            "XYZCT",
            "uint8",
            1,
            1,
            1,
            x,
            y,
            null,
            xUnit,
            yUnit,
            null,
            null,
            null,
            [],
            new string('A', 64)));

    private static SourceAssetItemViewModel CreateSourceItem(Guid id, CoreImageMetadata metadata) =>
        new(CreateSource(id, metadata), CreatePreview());

    private static SourceAsset CreateSource(Guid id, CoreImageMetadata metadata) => new(
        id,
        "ome.tif",
        "ome.tif",
        new SourceFingerprint(4, DateTimeOffset.UtcNow, new string('B', 64), null),
        metadata,
        SourceLinkState.Verified);

    private static BitmapSource CreatePreview()
    {
        BitmapSource preview = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Gray8,
            null,
            new byte[4],
            2);
        preview.Freeze();
        return preview;
    }
}
