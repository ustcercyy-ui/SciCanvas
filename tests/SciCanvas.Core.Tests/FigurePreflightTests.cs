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

    [Fact]
    public void Check_UsesProjectSpecificEffectiveDpiThreshold()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 100, 100),
                new PixelRect64(0, 0, 100, 100),
                "a",
                true)]);

        FigurePreflightResult relaxed = FigurePreflight.Check(
            document,
            [source],
            configuration: new FigurePreflightConfiguration { MinimumEffectiveDpi = 250 });
        FigurePreflightResult strict = FigurePreflight.Check(
            document,
            [source],
            configuration: new FigurePreflightConfiguration { MinimumEffectiveDpi = 350 });

        Assert.DoesNotContain(relaxed.Issues, issue => issue.Code == "LOW_EFFECTIVE_DPI");
        Assert.Contains(strict.Issues, issue => issue.Code == "LOW_EFFECTIVE_DPI");
    }

    [Fact]
    public void Check_FlagsPanelOverlapInvalidScaleBarAndTransparentBackground()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            500,
            400,
            300,
            [
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(20, 20, 200, 200),
                    "a",
                    true,
                    new FigureScaleBarExportSpec(0, 10, "µm", true)),
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(180, 160, 200, 200),
                    "b",
                    true),
            ],
            backgroundColor: "#00FFFFFF");

        FigurePreflightResult result = FigurePreflight.Check(document, [source]);

        Assert.Contains(result.Issues, issue => issue.Code == "TRANSPARENT_BACKGROUND");
        Assert.Contains(result.Issues, issue => issue.Code == "INVALID_SCALE_BAR");
        Assert.Contains(result.Issues, issue => issue.Code == "PANEL_OVERLAP");
    }

    [Fact]
    public void Check_DoesNotTreatAllHiddenLabelsAsDuplicates()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            500,
            400,
            300,
            [
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(0, 0, 100, 100),
                    string.Empty,
                    true),
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(200, 0, 100, 100),
                    string.Empty,
                    true),
            ]);

        FigurePreflightResult result = FigurePreflight.Check(document, [source]);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "DUPLICATE_LABEL");
        Assert.DoesNotContain(result.Issues, issue => issue.Code == "MISSING_LABEL");
    }

    [Fact]
    public void AssistedReview_ReportsStyleContrastAndIntegrityRisksWithExplainableCodes()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            500,
            400,
            300,
            [
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 10, 10),
                    new PixelRect64(0, 0, 100, 100),
                    "a",
                    true,
                    Adjustments: new ImageAdjustmentParameters { Contrast = 0.8 }),
            ],
            [
                new FigureAnnotationExportItem(
                    "text", 10, 10, 0, 0, "α", "#FF000000", 10, 1,
                    IsBold: false, IsVisible: true, ZIndex: 0),
            ],
            backgroundColor: "#FFFFFFFF",
            globalStyle: new FigureGlobalStyle(
                "Arial", 8, 1, "#FFFFFFFF", "#FFFFFFFF", "#FF000000"));

        FigurePreflightResult result = FigureAssistance.Review(document, [source]);

        Assert.Contains(result.Issues, issue => issue.Code == "STYLE_HARMONIZATION");
        Assert.Contains(result.Issues, issue => issue.Code == "LOW_COLOR_CONTRAST");
        Assert.Contains(result.Issues, issue => issue.Code == "INTEGRITY_EXTREME_ADJUSTMENT");
        Assert.Contains(result.Issues, issue => issue.Code == "INTEGRITY_NARROW_CROP");
        Assert.Contains(result.Issues, issue => issue.Code == "INTEGRITY_NON_GENERATIVE_PIPELINE");
    }

    [Fact]
    public void AssistedReview_FlagsDifferentAdjustmentsForSameSource()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            300,
            120,
            300,
            [
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(0, 0, 100, 100),
                    "a",
                    true),
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(150, 0, 100, 100),
                    "b",
                    true,
                    Adjustments: new ImageAdjustmentParameters { Brightness = 0.1 }),
            ]);

        FigurePreflightResult result = FigureAssistance.Review(document, [source]);

        Assert.Contains(result.Issues, issue => issue.Code == "INTEGRITY_INCONSISTENT_ADJUSTMENT");
    }

    private static SourceAsset CreateSource(SourceLinkState linkState) => new(
        Guid.NewGuid(),
        "sample.tif",
        "C:\\sample.tif",
        new SourceFingerprint(100, DateTimeOffset.UnixEpoch, new string('A', 64), null),
        new ImageMetadata(new PixelSize64(100, 100), 3, 8, "Bgr24"),
        linkState);
}
