using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FigureScaleBarSemanticsTests
{
    [Fact]
    public void SourcePixelLength_ConvertsDisplayUnitThroughExplicitCalibrationUnit()
    {
        var scaleBar = new FigureScaleBarExportSpec(
            PhysicalUnitsPerSourcePixel: 0.01,
            PhysicalLength: 500,
            Unit: "nm",
            ShowLabel: true,
            CalibrationUnit: "µm");

        Assert.Equal(50, scaleBar.SourcePixelLength, precision: 8);
        Assert.Equal("500 nm", scaleBar.Label);
    }

    [Fact]
    public void Layout_StacksBarsAtSharedAnchorWithoutChangingTheirPhysicalWidths()
    {
        FigureScaleBarExportSpec primary = new(0.01, 1, "µm", true, "µm", ScaleBarAnchor.BottomRight, Guid.NewGuid());
        FigureScaleBarExportSpec secondary = new(0.01, 500, "nm", true, "µm", ScaleBarAnchor.BottomRight, Guid.NewGuid());
        FigureImageRect image = new(0, 0, 400, 200);

        IReadOnlyList<FigureScaleBarGeometry> geometry = FigureScaleBarLayout.Calculate(
            [primary, secondary],
            new PixelRect64(0, 0, 1_000, 500),
            image,
            dpi: 300,
            thicknessPixels: 5,
            labelFontPixels: 16);

        Assert.Equal(2, geometry.Count);
        Assert.Equal(40, geometry[0].Right - geometry[0].Left, precision: 8);
        Assert.Equal(20, geometry[1].Right - geometry[1].Left, precision: 8);
        Assert.True(geometry[0].Y > geometry[1].Y, "Bottom-right bars should stack upward.");
        Assert.Equal(geometry[0].Right, geometry[1].Right, precision: 8);
    }

    [Fact]
    public void SourcePixelLength_RejectsIncompatibleDisplayAndCalibrationUnits()
    {
        var scaleBar = new FigureScaleBarExportSpec(1, 1, "nm", true, "custom-unit");

        Assert.Throws<NotSupportedException>(() => _ = scaleBar.SourcePixelLength);
    }
}