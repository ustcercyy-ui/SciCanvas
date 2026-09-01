using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FigurePreflightTests
{
    [Fact]
    public void Check_ReportsPanelLocalMissingFontAndMixedTypography()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        StyleOverride local = new(
            PanelLabel: new TextStyle("Missing Panel Font", 9, true, "#FF112233"),
            ScaleBarText: new TextStyle("Missing Scale Font", 8, false, "#FFFFFFFF"));
        var document = new FigureExportDocument(
            300,
            120,
            300,
            [
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 50, 50),
                    new PixelRect64(0, 0, 100, 100),
                    "a",
                    true),
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(50, 0, 50, 50),
                    new PixelRect64(150, 0, 100, 100),
                    "b",
                    true,
                    StyleOverride: local),
            ],
            globalStyle: new FigureGlobalStyle(
                "Arial", 7, 1.25, "#FF111111", "#FFE53935", "#FFFFFFFF"));

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(
                document,
                FontCatalog: new FixedFontCatalog(["Arial"])),
            [source]);

        Assert.Contains(result.Issues, issue =>
            issue.Code == "FONT_MISSING" && issue.Message.Contains("Missing Panel Font", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Code == "MIXED_PANEL_LABEL_FONT");
        Assert.Contains(result.Issues, issue => issue.Code == "MIXED_SCALE_BAR_FONT");
    }

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

    [Theory]
    [InlineData("#00FFFFFF", true)]
    [InlineData("#80FFFFFF", true)]
    [InlineData("#FFFFFFFF", false)]
    public void Check_SixteenBitTiffBlocksEveryNonOpaqueBackground(
        string background,
        bool expectedError)
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
                true)],
            backgroundColor: background,
            bitDepth: 16);

        var profile = new FigureExportProfile(
            "main-tiff",
            "Main TIFF",
            "tiff",
            300,
            bitDepth: 16);
        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(document, profile.Format, profile),
            [source]);

        Assert.Equal(
            expectedError,
            result.Issues.Any(issue =>
                issue.Code == "TRANSPARENT_BACKGROUND_UNSUPPORTED" &&
                issue.Severity == FigurePreflightSeverity.Error));
    }

    [Fact]
    public void Check_SixteenBitTiffBlocksAlphaChannelView()
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
                true,
                Adjustments: new ImageAdjustmentParameters { Channel = "alpha" })],
            bitDepth: 16);

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(document, "tiff"),
            [source]);

        FigurePreflightIssue issue = Assert.Single(
            result.Issues,
            item => item.Code == "ALPHA_CHANNEL_UNSUPPORTED_16BIT");
        Assert.Equal(FigurePreflightSeverity.Error, issue.Severity);
        Assert.Equal(
            "16-bit RGB TIFF cannot represent the selected alpha-channel view.",
            issue.Message);
    }
    [Theory]
    [InlineData(PanelLabelScheme.Numeric, "1", "2")]
    [InlineData(PanelLabelScheme.None, "", "")]
    public void Check_UsesConfiguredPanelLabelSchemeForExportConsistency(
        PanelLabelScheme scheme,
        string firstLabel,
        string secondLabel)
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        var document = new FigureExportDocument(
            200,
            100,
            300,
            [
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(0, 0, 100, 100),
                    firstLabel,
                    true),
                new FigurePanelExportItem(
                    source,
                    new PixelRect64(0, 0, 100, 100),
                    new PixelRect64(100, 0, 100, 100),
                    secondLabel,
                    true),
            ]);

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(document, LabelScheme: scheme),
            [source]);

        Assert.DoesNotContain(result.Issues, issue =>
            issue.Code is "MISSING_LABEL" or "DUPLICATE_LABEL" or "LABEL_SEQUENCE");
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
    public void Check_ValidatesRoiProjectionRelationshipAndResolvedFontBeforeExport()
    {
        SourceAsset source = CreateSource(SourceLinkState.Verified);
        Guid panelId = Guid.NewGuid();
        Guid roiId = Guid.NewGuid();
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(0, 0, 100, 100),
            new PixelRect64(0, 0, 100, 100),
            "a",
            true,
            PanelId: panelId,
            SourceRevision: 4);
        var roi = new RoiObject
        {
            Id = roiId,
            AssetId = source.Id,
            SourceRevision = 4,
            GeometryKind = RoiGeometryKind.Rectangle,
            SourceGeometry =
            [
                new MeasurementPoint(10, 10),
                new MeasurementPoint(30, 30),
            ],
            Style = RoiStyle.Default with { Label = "cell" },
        }.EnsureValid();
        var validProjection = new RoiFigureProjectionObject
        {
            Id = Guid.NewGuid(),
            RoiId = roiId,
            PanelId = panelId,
            AssetId = source.Id,
            SourceRevision = 4,
            StyleOverride = new StyleOverride(
                Annotation: new TextStyle("Missing ROI Font", 8, false, "#FFFFFFFF")),
        };
        var staleProjection = validProjection with
        {
            Id = Guid.NewGuid(),
            SourceRevision = 3,
        };
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [panel],
            roiProjections:
            [
                new FigureRoiProjectionExportItem(validProjection, roi),
                new FigureRoiProjectionExportItem(staleProjection, roi),
            ]);

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(
                document,
                TargetFormat: "svg",
                FontCatalog: new FixedFontCatalog(["Arial"])),
            [source],
            configuration: new FigurePreflightConfiguration { MinimumEffectiveDpi = 1 });

        FigurePreflightIssue invalid = Assert.Single(
            result.Issues,
            issue => issue.Code == "INVALID_ROI_PROJECTION");
        Assert.Equal(staleProjection.Id, invalid.ObjectId);
        FigurePreflightIssue missingFont = Assert.Single(
            result.Issues,
            issue => issue.Code == "FONT_MISSING" && issue.ObjectId == validProjection.Id);
        Assert.Contains("Missing ROI Font", missingFont.Message, StringComparison.Ordinal);
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

    [Theory]
    [InlineData(PdfFontStrategy.EmbedSubsetWhenPermitted, FontEmbeddingPermission.Restricted, true, FigurePreflightSeverity.Error, "PDF_FONT_EMBEDDING_UNAVAILABLE")]
    [InlineData(PdfFontStrategy.EmbedSubsetWhenPermitted, FontEmbeddingPermission.BitmapOnly, true, FigurePreflightSeverity.Error, "PDF_FONT_EMBEDDING_UNAVAILABLE")]
    [InlineData(PdfFontStrategy.EmbedSubsetWhenPermitted, FontEmbeddingPermission.Editable, false, FigurePreflightSeverity.Error, "PDF_FONT_EMBEDDING_UNAVAILABLE")]
    [InlineData(PdfFontStrategy.PreferEmbeddedWithOutlineFallback, FontEmbeddingPermission.Restricted, true, FigurePreflightSeverity.Warning, "PDF_FONT_OUTLINE_FALLBACK")]
    [InlineData(PdfFontStrategy.PreferEmbeddedWithOutlineFallback, FontEmbeddingPermission.Editable, false, FigurePreflightSeverity.Warning, "PDF_FONT_OUTLINE_FALLBACK")]
    public void PdfFontPreflight_RespectsEmbeddingRightsAndStrategy(
        PdfFontStrategy strategy,
        FontEmbeddingPermission permission,
        bool subsettingPermitted,
        FigurePreflightSeverity expectedSeverity,
        string expectedCode)
    {
        Guid annotationId = Guid.NewGuid();
        var annotation = new FigureAnnotationExportItem(
            "text", 10, 10, 0, 0, "font", "#FF000000", "#00000000", 0,
            "#FF000000", "Arial", 8, 1, false, true, 0)
        {
            Id = annotationId,
        };
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [],
            [annotation],
            pdfFontStrategy: strategy);
        var provider = new FixedPdfFontCapabilityProvider(permission, subsettingPermitted);

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(
                document,
                TargetFormat: "pdf",
                PdfFontCapabilityProvider: provider),
            []);

        FigurePreflightIssue issue = Assert.Single(
            result.Issues,
            item => item.Code == expectedCode && item.ObjectId == annotationId);
        Assert.Equal(expectedSeverity, issue.Severity);
    }

    [Fact]
    public void PdfFontPreflight_PermittedTrueTypeSubsetHasNoFontStrategyIssue()
    {
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [],
            pdfFontStrategy: PdfFontStrategy.EmbedSubsetWhenPermitted);

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(
                document,
                TargetFormat: "pdf",
                PdfFontCapabilityProvider: new FixedPdfFontCapabilityProvider(
                    FontEmbeddingPermission.Editable,
                    subsettingPermitted: true)),
            []);

        Assert.DoesNotContain(result.Issues, issue => issue.Code.StartsWith("PDF_FONT_", StringComparison.Ordinal));
    }

    [Fact]
    public void PdfFontPreflight_OutlineDoesNotRequireCapabilityProvider()
    {
        var document = new FigureExportDocument(
            100,
            100,
            300,
            [],
            pdfFontStrategy: PdfFontStrategy.OutlineText);

        FigurePreflightResult result = FigurePreflight.Check(
            new FigurePreflightContext(document, TargetFormat: "pdf"),
            []);

        Assert.DoesNotContain(result.Issues, issue => issue.Code.StartsWith("PDF_FONT_", StringComparison.Ordinal));
    }

    private static SourceAsset CreateSource(SourceLinkState linkState) => new(
        Guid.NewGuid(),
        "sample.tif",
        "C:\\sample.tif",
        new SourceFingerprint(100, DateTimeOffset.UnixEpoch, new string('A', 64), null),
        new ImageMetadata(new PixelSize64(100, 100), 3, 8, "Bgr24"),
        linkState);

    private sealed class FixedPdfFontCapabilityProvider(
        FontEmbeddingPermission permission,
        bool subsettingPermitted) : IPdfFontCapabilityProvider
    {
        public PdfFontCapability GetCapability(string effectiveFont, bool isBold) => new(
            effectiveFont,
            effectiveFont,
            IsInstalled: true,
            IsSupportedFontFormat: true,
            permission,
            subsettingPermitted,
            EmbeddingImplementationAvailable: true);
    }
}
