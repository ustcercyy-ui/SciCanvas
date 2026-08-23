using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Tests;

public sealed class MultiPageFrameTests
{
    [Fact]
    public void PreflightRejectsFrameOutsideSourceMetadata()
    {
        SourceAsset source = CreateSource(frameCount: 2);
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 50, 50),
                new PixelRect64(0, 0, 100, 100),
                "a",
                true,
                FrameIndex: 2)]);

        FigurePreflightResult result = FigurePreflight.Check(document, [source]);

        Assert.Contains(result.Issues, issue => issue.Code == "INVALID_FRAME");
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void ProvenancePreservesSelectedFrame()
    {
        SourceAsset source = CreateSource(frameCount: 4);
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 50, 50),
                new PixelRect64(0, 0, 100, 100),
                "a",
                true,
                FrameIndex: 3)]);

        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
            document,
            "figure.tiff",
            "1.0.0",
            [source],
            new FigurePreflightResult([]),
            "main-tiff",
            "主图 · 无损 TIFF");

        Assert.Equal(3, provenance.Panels[0].FrameIndex);
        Assert.Equal("main-tiff", provenance.ExportProfileId);
        Assert.Equal("主图 · 无损 TIFF", provenance.ExportProfileName);
    }

    private static SourceAsset CreateSource(int frameCount) => new(
        Guid.NewGuid(),
        "source.tif",
        "source.tif",
        new SourceFingerprint(1, DateTimeOffset.UtcNow, new string('a', 64), null),
        new ImageMetadata(new PixelSize64(100, 100), 1, 8, "Gray8", frameCount: frameCount),
        SourceLinkState.Verified);
}
