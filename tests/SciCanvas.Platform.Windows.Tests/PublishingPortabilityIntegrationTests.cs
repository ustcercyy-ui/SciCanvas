using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

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
}
