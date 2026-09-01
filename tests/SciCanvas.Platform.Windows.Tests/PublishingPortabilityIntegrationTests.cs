using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

[Collection(WpfTestCollection.Name)]
public sealed class PublishingPortabilityIntegrationTests
{
    [Fact]
    public void ExportProfileSnapshot_RoundTripsPdfFontStrategy()
    {
        var snapshot = new ProjectExportProfileSnapshot
        {
            Id = Guid.NewGuid(),
            Name = "PDF portable",
            Format = "pdf",
            Dpi = 600,
            BitDepth = 8,
            PdfFontStrategy = "preferEmbeddedWithOutlineFallback",
        };

        FigureExportProfile profile = ExportProfileEditorViewModel.FromSnapshot(snapshot).ToModel();

        Assert.Equal(PdfFontStrategy.PreferEmbeddedWithOutlineFallback, profile.PdfFontStrategy);
    }

    [Fact]
    public void ProjectMapper_PersistsPresetSnapshotAndFontSubstitution()
    {
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
        var preset = new JournalExportPreset(
            "team-custom",
            "Team Custom",
            89,
            null,
            600,
            "pdf",
            ["pdf", "tiff"],
            "RGB",
            description: "Project snapshot");
        var substitution = new FontSubstitutionRule("MissingFont123", "Arial");

        SciCanvasProjectDocument document = ProjectDocumentMapper.Create(
            Guid.NewGuid(),
            DateTimeOffset.Parse("2026-08-29T00:00:00Z"),
            "Publishing portability",
            [],
            null,
            new CropEditorViewModel(),
            figure,
            WorkspaceMode.Figure,
            false,
            false,
            journalPresetSnapshots: [preset],
            fontSubstitutions: [substitution]);

        ProjectJournalPresetSnapshot presetSnapshot = Assert.Single(document.JournalPresetSnapshots);
        ProjectFontSubstitutionSnapshot fontSnapshot = Assert.Single(document.FontSubstitutions);
        JournalExportPreset restoredPreset = ProjectDocumentMapper.ToJournalPreset(presetSnapshot);
        FontSubstitutionRule restoredFont = ProjectDocumentMapper.ToFontSubstitution(fontSnapshot);
        Assert.Equal(preset.Id, restoredPreset.Id);
        Assert.Equal(preset.AllowedFormats, restoredPreset.AllowedFormats);
        Assert.Equal("MissingFont123", restoredFont.RequestedFontFamily);
        Assert.Equal("Arial", restoredFont.SubstituteFontFamily);
    }

    [Fact]
    public void PublishingWorkspace_SubstitutionResolvesRenderSnapshotWithoutChangingFigureFont()
    {
        WpfTestHost.Invoke(() =>
        {
            var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
            figure.GlobalFontFamily = "MissingFont123";
            using var workspace = new PublishingPortabilityWorkspaceViewModel(figure);
            string installed = Assert.Single(workspace.InstalledFonts.Take(1));
            workspace.RequestedFont = "MissingFont123";
            workspace.SubstituteFont = installed;

            workspace.SetSubstitutionCommand.Execute(null);
            ResolvedFigureExportDocument resolved = workspace.ResolveFonts(figure.CreateExportDocument());

            Assert.Equal("MissingFont123", figure.GlobalFontFamily);
            Assert.Equal(installed, resolved.Document.GlobalStyle.FontFamily);
            Assert.Contains(resolved.FontResolutions, item =>
                item.RequestedFamily == "MissingFont123" &&
                item.ResolutionKind == FontResolutionKind.ExplicitSubstitution);
        });
    }

    [Fact]
    public void PublishingWorkspace_MissingFontsUsesCollectorForPanelLocalAndMeasurementOverlay()
    {
        WpfTestHost.Invoke(() =>
        {
            Guid sourceId = Guid.NewGuid();
            Guid panelId = Guid.NewGuid();
            Guid overlayId = Guid.NewGuid();
            Guid measurementId = Guid.NewGuid();
            var source = new SourceAsset(
                sourceId,
                "source.tif",
                "C:\\source.tif",
                new SourceFingerprint(10, DateTimeOffset.UnixEpoch, new string('A', 64), null),
                new ImageMetadata(new PixelSize64(100, 100), 1, 8, "Gray8"),
                SourceLinkState.Verified);
            var panel = new FigurePanelExportItem(
                source,
                new PixelRect64(0, 0, 100, 100),
                new PixelRect64(0, 0, 100, 100),
                "a",
                true,
                new FigureScaleBarExportSpec(1, 20, "px", true),
                StyleOverride: new StyleOverride(
                    PanelLabel: new TextStyle("MissingPanelLocal_FontUsageCollector", 8, true, "#FF000000"),
                    ScaleBarText: new TextStyle("MissingScaleLocal_FontUsageCollector", 7, false, "#FFFFFFFF")),
                PanelId: panelId);
            var overlay = new FigureMeasurementOverlayExportItem(new MeasurementOverlayObject
            {
                Id = overlayId,
                AssetId = sourceId,
                PanelId = panelId,
                SourceRevision = 1,
                MeasurementId = measurementId,
                SourceGeometry = new ScientificMeasurement(
                    measurementId,
                    sourceId,
                    ScientificMeasurementKind.Length,
                    new MeasurementPoint(10, 10),
                    new MeasurementPoint(40, 40),
                    SourceRevision: 1),
                Style = new FigureMeasurementOverlayStyle(
                    "#FFFFFFFF", 1, "solid", "#00000000", 0,
                    "#FFFFFFFF", "#FF000000", 6, true,
                    "#FFFFFFFF", "MissingOverlay_FontUsageCollector", 7, false, true),
            });
            var document = new FigureExportDocument(
                100,
                100,
                96,
                [panel],
                measurementOverlays: [overlay]);
            var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
            using var workspace = new PublishingPortabilityWorkspaceViewModel(figure, () => document);

            MissingFontItemViewModel panelMissing = Assert.Single(
                workspace.MissingFonts,
                item => item.RequestedFontFamily == "MissingPanelLocal_FontUsageCollector");
            MissingFontItemViewModel scaleMissing = Assert.Single(
                workspace.MissingFonts,
                item => item.RequestedFontFamily == "MissingScaleLocal_FontUsageCollector");
            MissingFontItemViewModel overlayMissing = Assert.Single(
                workspace.MissingFonts,
                item => item.RequestedFontFamily == "MissingOverlay_FontUsageCollector");

            Assert.Contains("PanelLabel", panelMissing.UsedBy, StringComparison.Ordinal);
            Assert.Contains(panelId.ToString(), panelMissing.UsedBy, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ScaleBarText", scaleMissing.UsedBy, StringComparison.Ordinal);
            Assert.Contains("MeasurementOverlayLabel", overlayMissing.UsedBy, StringComparison.Ordinal);
            Assert.Contains(overlayId.ToString(), overlayMissing.UsedBy, StringComparison.OrdinalIgnoreCase);
        });
    }
}
