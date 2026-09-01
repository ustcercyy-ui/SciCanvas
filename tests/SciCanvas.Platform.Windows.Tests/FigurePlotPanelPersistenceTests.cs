using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class FigurePlotPanelPersistenceTests
{
    [Fact]
    public void FigureCanvas_AddMoveExportAndRemovePlotPanel()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);

        FigurePlotPanelViewModel panel = figure.AddPlotPanel(plot, asset);
        panel.Label = "P";
        figure.MovePlotPanel(panel, 81, 93);

        FigurePlotPanelExportItem exported = Assert.Single(figure.CreateExportDocument().PlotPanels);
        Assert.Equal(panel.Id, exported.PanelId);
        Assert.Equal(new PixelRect64(81, 93, panel.Width, panel.Height), exported.DestinationRect);
        Assert.Equal(asset.SourceRevision, exported.Plot.Data.SourceRevision);
        Assert.True(figure.IsPlotReferenced(plot.Id));

        figure.RemoveSelectedPlotPanelCommand.Execute(null);

        Assert.Empty(figure.PlotPanels);
        Assert.False(figure.IsPlotReferenced(plot.Id));
    }

    [Fact]
    public async Task ProjectMapperAndStore_RoundTripPlotPanelGeometryAndStyleCascade()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
        FigurePlotPanelViewModel panel = figure.AddPlotPanel(plot, asset);
        panel.X = 101;
        panel.Y = 123;
        panel.Width = 640;
        panel.Height = 420;
        panel.Label = "b";
        panel.RestoreState(
            isVisible: true,
            isLocked: true,
            new StyleOverride(Annotation: new TextStyle("Calibri", 9, false, "#FF334455")),
            new FigurePlotTypographyOverride(
                Axis: new TextStyle("Times New Roman", 11, true, "#FF112233")));
        Assert.Equal("Times New Roman", panel.PreviewPlot.Typography.Axis.FontFamily);
        Assert.Equal("Calibri", panel.PreviewPlot.Typography.Tick.FontFamily);
        SciCanvasProjectDocument project = CreateProject(figure, asset, plot);

        Assert.Equal("3.0", project.SchemaVersion);
        ProjectFigurePlotPanelSnapshot saved = Assert.Single(project.TemplateSnapshot!.PlotPanels);
        Assert.Equal(plot.Id, saved.PlotId);
        Assert.Equal(new PixelRect64(101, 123, 640, 420),
            ProjectDocumentMapper.ToPixelRect(saved.DestinationRect));
        Assert.True(saved.Locked);
        Assert.Equal("Calibri", saved.StyleOverride!.Annotation!.FontFamily);
        Assert.Equal("Times New Roman", saved.TypographyOverride!.Axis!.FontFamily);

        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scicanvas");
        try
        {
            var store = new JsonProjectStore();
            await store.SaveAsync(path, project);
            SciCanvasProjectDocument restored = await store.LoadAsync(path);
            ProjectFigurePlotPanelSnapshot roundTripped =
                Assert.Single(restored.TemplateSnapshot!.PlotPanels);
            Assert.Equal(saved.Id, roundTripped.Id);
            Assert.Equal(saved.PlotId, roundTripped.PlotId);
            Assert.Equal(saved.DestinationRect.Width, roundTripped.DestinationRect.Width);
            Assert.Equal("Times New Roman", roundTripped.TypographyOverride!.Axis!.FontFamily);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task JsonProjectStore_RejectsTamperedPlotPanelReference()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
        _ = figure.AddPlotPanel(plot, asset);
        SciCanvasProjectDocument project = CreateProject(figure, asset, plot);
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.scicanvas");
        try
        {
            var store = new JsonProjectStore();
            await store.SaveAsync(path, project);
            string json = await File.ReadAllTextAsync(path);
            string marker = $"\"plotId\": \"{plot.Id:D}\"";
            Assert.Contains(marker, json, StringComparison.Ordinal);
            await File.WriteAllTextAsync(
                path,
                json.Replace(
                    marker,
                    $"\"plotId\": \"{Guid.NewGuid():D}\"",
                    StringComparison.Ordinal));

            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Migration_29To30_DefaultsPlotPanelsToEmpty()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var legacy = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.9",
            ProjectId = Guid.NewGuid(),
            CreatedAt = now,
            UpdatedAt = now,
            Canvas = new ProjectCanvasSnapshot { Width = 1200, Height = 900 },
            TemplateSnapshot = new ProjectTemplateSnapshot { TemplateId = "journal-single" },
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(legacy);

        Assert.Equal("3.0", migrated.SchemaVersion);
        Assert.Empty(migrated.TemplateSnapshot!.PlotPanels);
        Assert.Contains(migrated.AuditTrail, entry =>
            entry.Command == "MigrateProject" && Equals(entry.Parameters["from"], "2.9"));
    }

    private static SciCanvasProjectDocument CreateProject(
        FigureCanvasViewModel figure,
        TabularDataAsset asset,
        PlotObject plot) => ProjectDocumentMapper.Create(
        Guid.NewGuid(),
        DateTimeOffset.UtcNow.AddMinutes(-1),
        "Plot panel",
        [],
        null,
        new CropEditorViewModel(),
        figure,
        WorkspaceMode.Figure,
        lockCropSizeAcrossSources: true,
        cropOverlayVisible: true,
        dataAssets: [asset],
        plots: [plot]);

    private static (TabularDataAsset Asset, PlotObject Plot) CreatePlot()
    {
        Guid assetId = Guid.NewGuid();
        Guid xId = Guid.NewGuid();
        Guid yId = Guid.NewGuid();
        var asset = new TabularDataAsset(
            assetId,
            "data",
            null,
            null,
            4,
            [
                new DataColumn(xId, "x", TabularDataType.Numeric, "s", DataColumnRole.X),
                new DataColumn(yId, "y", TabularDataType.Numeric, "mV", DataColumnRole.Y),
            ],
            [
                new TabularDataRow([new TabularDataValue("1", 1), new TabularDataValue("2", 2)]),
                new TabularDataRow([new TabularDataValue("2", 2), new TabularDataValue("4", 4)]),
                new TabularDataRow([new TabularDataValue("3", 3), new TabularDataValue("8", 8)]),
            ],
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = 3,
                InferenceRowCount = 3,
                OriginalHeaders = ["x", "y"],
            }).EnsureValid();
        var plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = "growth",
            PlotType = PlotKind.LineAndSymbol,
            Data = new PlotDataBinding(assetId, asset.SourceRevision, xId, yId),
            XAxis = PlotAxisDefinition.DefaultX with { Title = "Time", Unit = "s" },
            YAxis = PlotAxisDefinition.DefaultY with { Title = "Signal", Unit = "mV" },
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default,
        }.EnsureValid(asset);
        return (asset, plot);
    }
}
