using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class FigurePlotPanelExportItemTests
{
    [Fact]
    public void Create_FreezesProjectionInsteadOfRasterScreenshot()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();

        FigurePlotPanelExportItem item = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(10, 20, 400, 300),
            "a");

        Assert.Equal(plot.Id, item.Plot.Id);
        Assert.Equal(asset.Id, item.Plot.Data.DataAssetId);
        Assert.Equal(asset.SourceRevision, item.Plot.Data.SourceRevision);
        Assert.Equal(3, item.Projection.SourceRowCount);
        Assert.Equal(3, item.Projection.IncludedRowCount);
        Assert.Equal([1d, 2d, 3d], item.Projection.Rows.Select(row => row.Y));
    }

    [Fact]
    public void ResolveTypography_UsesFigureThenPanelThenPlotObject()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();
        var figure = new FigureGlobalStyle(
            "FigureFace", 7, 1, "#FF111111", "#FF222222", "#FFFFFFFF");
        var panelText = new TextStyle("PanelFace", 8, false, "#FF333333");
        var plotAxis = new TextStyle("PlotFace", 9, true, "#FF444444");
        FigurePlotPanelExportItem item = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(0, 0, 400, 300),
            "a",
            styleOverride: new StyleOverride(Annotation: panelText),
            typographyOverride: new FigurePlotTypographyOverride(Axis: plotAxis));

        ResolvedFigurePlotTypography resolved = item.ResolveTypography(figure);

        Assert.Equal("PlotFace", resolved.Axis.Value.FontFamily);
        Assert.Equal(StyleInheritanceSource.Object, resolved.Axis.Source);
        Assert.Equal("PanelFace", resolved.Tick.Value.FontFamily);
        Assert.Equal(StyleInheritanceSource.Panel, resolved.Tick.Source);
        Assert.Equal("PanelFace", resolved.Legend.Value.FontFamily);
        Assert.Equal("PanelFace", resolved.Annotation.Value.FontFamily);
    }

    [Fact]
    public void DocumentFontResolutionAndProfile_PreservePlotPanelAsVectorData()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();
        FigurePlotPanelExportItem item = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(10, 20, 400, 300),
            "a",
            typographyOverride: new FigurePlotTypographyOverride(
                Axis: new TextStyle("MissingFace", 9, false, "#FF111111")));
        var document = new FigureExportDocument(
            800,
            600,
            300,
            [],
            plotPanels: [item]);

        FigureExportDocument scaled = new FigureExportProfile(
            "double", "Double", "svg", 300, scale: 2).Apply(document);
        ResolvedFigureExportDocument resolved = FigureExportFontResolver.Resolve(
            scaled,
            [new FontSubstitutionRule("MissingFace", "Arial")],
            new FixedFontCatalog(["Arial"]));

        FigurePlotPanelExportItem output = Assert.Single(resolved.Document.PlotPanels);
        Assert.Equal(new PixelRect64(20, 40, 800, 600), output.DestinationRect);
        Assert.Equal("Arial", output.TypographyOverride!.Axis!.FontFamily);
        Assert.Equal(item.Projection, output.Projection);
        Assert.Contains(FontUsageCollector.Collect(resolved.Document), usage =>
            usage.UsageKind == FontUsageKind.PlotAxis && usage.RequestedFont == "Arial");
    }

    [Fact]
    public void Preflight_AllowsPlotOnlyFigureAndChecksSharedPanelOverlapAndLabelFont()
    {
        (TabularDataAsset asset, PlotObject plot) = CreatePlot();
        FigurePlotPanelExportItem first = FigurePlotPanelExportItem.Create(
            plot, asset, new PixelRect64(0, 0, 400, 300), "a");
        FigurePlotPanelExportItem second = FigurePlotPanelExportItem.Create(
            plot, asset, new PixelRect64(300, 200, 400, 300), "b");
        var document = new FigureExportDocument(
            800, 600, 300, [], plotPanels: [first, second]);

        FigurePreflightResult result = FigurePreflight.Check(document, []);

        Assert.DoesNotContain(result.Issues, issue => issue.Code == "NO_PANELS");
        Assert.Contains(result.Issues, issue => issue.Code == "PANEL_OVERLAP");
        Assert.Contains(FontUsageCollector.Collect(document), usage =>
            usage.UsageKind == FontUsageKind.PanelLabel && usage.PanelLabel == "a");

        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
            document,
            "figure.svg",
            "test",
            [],
            result);
        Assert.Equal(2, provenance.PlotPanels!.Count);
        FigureProvenancePlotPanel plotRecord = provenance.PlotPanels[0];
        Assert.Equal(plot.Id, plotRecord.PlotId);
        Assert.Equal(asset.Id, plotRecord.DataAssetId);
        Assert.Equal(3, plotRecord.IncludedRowCount);
    }

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
                new DataColumn(xId, "x", TabularDataType.Numeric, Role: DataColumnRole.X),
                new DataColumn(yId, "y", TabularDataType.Numeric, Role: DataColumnRole.Y),
            ],
            [
                new TabularDataRow([new TabularDataValue("1", 1), new TabularDataValue("1", 1)]),
                new TabularDataRow([new TabularDataValue("2", 2), new TabularDataValue("2", 2)]),
                new TabularDataRow([new TabularDataValue("3", 3), new TabularDataValue("3", 3)]),
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
            });
        var plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = "line",
            PlotType = PlotKind.Line,
            Data = new PlotDataBinding(assetId, 4, xId, yId),
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default,
        };
        return (asset.EnsureValid(), plot.EnsureValid(asset));
    }
}
