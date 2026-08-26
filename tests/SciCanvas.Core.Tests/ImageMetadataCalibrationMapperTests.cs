using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Tests;

public sealed class ImageMetadataCalibrationMapperTests
{
    [Fact]
    public void Map_PromotesCompatibleOmeAxesIntoCanonicalMetadataCalibration()
    {
        Guid sourceId = Guid.NewGuid();
        var metadata = new ImageMetadata(
            new PixelSize64(100, 80),
            1,
            16,
            "Gray16",
            ome: CreateOme(0.5, "µm", 500, "nm"));

        MetadataCalibrationMapping mapping = ImageMetadataCalibrationMapper.Map(sourceId, metadata);

        Assert.Equal(MetadataCalibrationState.Available, metadata.MetadataCalibrationState);
        Assert.Equal(0.5, metadata.PhysicalSizeX);
        Assert.Equal(0.5, metadata.PhysicalSizeY);
        Assert.Equal("µm", metadata.PhysicalUnit);
        Assert.True(mapping.IsAvailable);
        Assert.Equal(CalibrationOrigin.Metadata, mapping.Calibration.Origin);
        Assert.Equal(sourceId, mapping.Calibration.SourceAssetId);
        Assert.Equal(0.5, mapping.Calibration.UnitsPerPixelX);
        Assert.Equal(0.5, mapping.Calibration.UnitsPerPixelY);
    }

    [Theory]
    [InlineData(1d, "custom-x", 1d, "custom-y")]
    [InlineData(1d, "µm", null, "µm")]
    [InlineData(-1d, "µm", 1d, "µm")]
    public void Map_RequiresTwoValidCompatibleAxes(
        double? x,
        string xUnit,
        double? y,
        string yUnit)
    {
        Guid sourceId = Guid.NewGuid();
        var metadata = new ImageMetadata(
            new PixelSize64(100, 80),
            1,
            16,
            "Gray16",
            ome: CreateOme(x, xUnit, y, yUnit));

        MetadataCalibrationMapping mapping = ImageMetadataCalibrationMapper.Map(sourceId, metadata);

        Assert.Equal(MetadataCalibrationState.ReviewRequired, mapping.State);
        Assert.False(mapping.Calibration.IsValid);
        Assert.Equal(CalibrationOrigin.None, mapping.Calibration.Origin);
        Assert.False(string.IsNullOrWhiteSpace(mapping.ReviewMessage));
        Assert.Null(metadata.PhysicalSizeX);
        Assert.Null(metadata.PhysicalSizeY);
    }

    private static OmeImageMetadata CreateOme(
        double? x,
        string? xUnit,
        double? y,
        string? yUnit) => new(
        "XYZCT",
        "uint16",
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
        new string('A', 64));
}
