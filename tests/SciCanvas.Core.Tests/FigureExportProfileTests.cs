using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Tests;

public sealed class FigureExportProfileTests
{
    [Fact]
    public void SingleTargetWidth_PreservesAspectRatioAndScalesLayout()
    {
        var source = new SourceAsset(
            Guid.NewGuid(),
            "source.tif",
            "source.tif",
            new SourceFingerprint(1, DateTimeOffset.UtcNow, new string('a', 64), null),
            new ImageMetadata(new PixelSize64(800, 400), 3, 8, "rgb"),
            SourceLinkState.Verified);
        var document = new FigureExportDocument(
            800,
            400,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 200, 100),
                new PixelRect64(100, 50, 400, 200),
                "a",
                true)],
            [new FigureAnnotationExportItem(
                "text",
                100,
                50,
                100,
                50,
                "note",
                "#FFFFFFFF",
                8,
                1,
                false,
                true,
                0)]);

        FigureExportDocument variant = new FigureExportProfile(
            "thumbnail",
            "Thumbnail",
            "png",
            150,
            widthPixels: 1200)
            .Apply(document);

        Assert.Equal(1200, variant.WidthPixels);
        Assert.Equal(600, variant.HeightPixels);
        Assert.Equal(150, variant.Dpi);
        Assert.Equal(new PixelRect64(150, 75, 600, 300), variant.Panels[0].DestinationRect);
        Assert.Equal(150, variant.Annotations[0].X);
        Assert.Equal(75, variant.Annotations[0].Y);
    }

    [Fact]
    public void InvalidFormatAndDimensionsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new FigureExportProfile("x", "X", "webp", 96));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FigureExportProfile("x", "X", "png", 96, widthPixels: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FigureExportProfile("x", "X", "png", 96, scale: 0));
    }

    [Fact]
    public void BuiltInsExposeRepeatableSubmissionVariants()
    {
        Assert.Equal(3, FigureExportProfile.BuiltIns.Count);
        Assert.Equal("tiff", FigureExportProfile.BuiltIns[0].Format);
        Assert.Equal("png", FigureExportProfile.BuiltIns[1].Format);
        Assert.Equal(1200, FigureExportProfile.BuiltIns[2].WidthPixels);
    }
}
