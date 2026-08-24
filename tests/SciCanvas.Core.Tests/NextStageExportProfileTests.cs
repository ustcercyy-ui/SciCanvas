using SciCanvas.Core.Export;

namespace SciCanvas.Core.Tests;

public sealed class NextStageExportProfileTests
{
    [Fact]
    public void MainPublicationProfileUsesTrueSixteenBitTiff()
    {
        FigureExportProfile profile = FigureExportProfile.BuiltIns.Single(item => item.Id == "main-tiff");

        Assert.Equal("tiff", profile.Format);
        Assert.Equal(16, profile.BitDepth);
    }

    [Fact]
    public void SixteenBitProfileRejectsLossyOrVectorFormats()
    {
        Assert.Throws<ArgumentException>(() => new FigureExportProfile(
            "invalid",
            "invalid",
            "png",
            300,
            bitDepth: 16));
    }

    [Fact]
    public void ApplyCarriesBitDepthIntoExportSnapshot()
    {
        var source = new FigureExportDocument(10, 10, 300, []);
        var profile = new FigureExportProfile("custom", "16-bit", "tiff", 300, bitDepth: 16);

        FigureExportDocument result = profile.Apply(source);

        Assert.Equal(16, result.BitDepth);
    }
}
