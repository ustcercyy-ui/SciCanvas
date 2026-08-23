using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Tests;

public sealed class FigurePreflightTests
{
    [Fact]
    public void Check_FlagsOutOfBoundsAndUnverifiedSource()
    {
        SourceAsset source = CreateSource(SourceLinkState.Modified);
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 50, 50),
                new PixelRect64(80, 80, 30, 30),
                "a",
                true)]);

        FigurePreflightResult result = FigurePreflight.Check(document, [source]);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Issues, issue => issue.Code == "SOURCE_UNVERIFIED");
        Assert.Contains(result.Issues, issue => issue.Code == "PANEL_OUT_OF_BOUNDS");
    }

    [Fact]
    public void Check_FlagsLowResolutionAndUnsavedStateAsWarnings()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            1000,
            1000,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 10, 10),
                new PixelRect64(0, 0, 1000, 1000),
                "a",
                true)]);

        FigurePreflightResult result = FigurePreflight.Check(document, [source], hasUnsavedChanges: true);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, issue => issue.Code == "LOW_EFFECTIVE_DPI");
        Assert.Contains(result.Issues, issue => issue.Code == "UNSAVED_CHANGES");
    }

    private static SourceAsset CreateSource(SourceLinkState linkState) => new(
        Guid.NewGuid(),
        "sample.tif",
        "C:\\sample.tif",
        new SourceFingerprint(100, DateTimeOffset.UnixEpoch, new string('A', 64), null),
        new ImageMetadata(new PixelSize64(100, 100), 3, 8, "Bgr24"),
        linkState);
}
