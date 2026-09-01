using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class HeatmapFigureAuditTests
{
    [Fact]
    public void FigureAudit_RecordsAggregationColorbarRangeTicksAndFont()
    {
        (TabularDataAsset asset, PlotObject plot) = CreateDuplicateHeatmap(
            HeatmapDuplicateCellPolicy.Mean);
        FigurePlotPanelExportItem panel = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(0, 0, 500, 350),
            "a");
        var document = new FigureExportDocument(
            500,
            350,
            300,
            [],
            plotPanels: [panel]);

        FigurePreflightResult preflight = FigurePreflight.Check(document, []);
        ResolvedFigureExportDocument resolved = FigureExportFontResolver.Resolve(
            document,
            [new FontSubstitutionRule("HeatmapFace", "Arial")],
            new FixedFontCatalog(["Arial"]));
        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
            document,
            "heatmap.svg",
            "test",
            [],
            preflight);

        FigurePreflightIssue aggregation = Assert.Single(
            preflight.Issues,
            issue => issue.Code == HeatmapQcCodes.DuplicateCell);
        Assert.Equal(FigurePreflightSeverity.Info, aggregation.Severity);
        Assert.Contains(FontUsageCollector.Collect(document), usage =>
            usage.UsageKind == FontUsageKind.PlotColorbar &&
            usage.RequestedFont == "HeatmapFace");
        Assert.Equal(
            "Arial",
            Assert.Single(resolved.Document.PlotPanels).Plot.Colorbar!.LabelStyle!.FontFamily);
        Assert.Equal("HeatmapFace", panel.Plot.Colorbar!.LabelStyle!.FontFamily);
        HeatmapScientificProvenance heatmap = Assert.IsType<HeatmapScientificProvenance>(
            Assert.Single(provenance.PlotPanels!).Heatmap);
        Assert.Equal(nameof(HeatmapDuplicateCellPolicy.Mean), heatmap.DuplicateCellPolicy);
        Assert.True(heatmap.DuplicateAggregationApplied);
        Assert.Equal("viridis", heatmap.Colormap);
        Assert.Equal(10, heatmap.Minimum);
        Assert.Equal(100, heatmap.Maximum);
        Assert.Equal([20d, 40d, 60d, 80d, 100d], heatmap.Colorbar!.Ticks);
        Assert.Equal(["Low", "40", "60", "80", "High"], heatmap.Colorbar.TickLabels);
        Assert.Equal(10, heatmap.Colorbar.Minimum);
        Assert.Equal(100, heatmap.Colorbar.Maximum);
    }

    [Fact]
    public void Preflight_DuplicateCellWithDefaultPolicy_UsesDedicatedErrorCode()
    {
        (TabularDataAsset asset, PlotObject configured) = CreateDuplicateHeatmap(
            HeatmapDuplicateCellPolicy.Mean);
        PlotObject plot = configured with { HeatmapGrid = null };
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);
        var panel = new FigurePlotPanelExportItem(
            Guid.NewGuid(),
            plot,
            projection,
            new PixelRect64(0, 0, 500, 350),
            "a",
            true);
        var document = new FigureExportDocument(
            500,
            350,
            300,
            [],
            plotPanels: [panel]);

        FigurePreflightResult preflight = FigurePreflight.Check(document, []);

        FigurePreflightIssue issue = Assert.Single(
            preflight.Issues,
            candidate => candidate.Code == HeatmapQcCodes.DuplicateCell);
        Assert.Equal(FigurePreflightSeverity.Error, issue.Severity);
        Assert.Equal(plot.Id, issue.ObjectId);
    }

    private static (TabularDataAsset Asset, PlotObject Plot) CreateDuplicateHeatmap(
        HeatmapDuplicateCellPolicy duplicatePolicy)
    {
        Guid assetId = Guid.NewGuid();
        Guid xId = Guid.NewGuid();
        Guid yId = Guid.NewGuid();
        Guid valueId = Guid.NewGuid();
        DataColumn[] columns =
        [
            new DataColumn(xId, "x", TabularDataType.Numeric, Role: DataColumnRole.X),
            new DataColumn(yId, "y", TabularDataType.Numeric, Role: DataColumnRole.Y),
            new DataColumn(valueId, "intensity", TabularDataType.Numeric),
        ];
        var asset = new TabularDataAsset(
            assetId,
            "duplicate heatmap",
            null,
            null,
            1,
            columns,
            [
                Row(0, 0, 10),
                Row(0, 0, 20),
                Row(1, 0, 100),
            ],
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = 3,
                InferenceRowCount = 3,
                OriginalHeaders = columns.Select(column => column.Name).ToArray(),
            }).EnsureValid();
        var plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = "Audited heatmap",
            PlotType = PlotKind.Heatmap,
            Data = new PlotDataBinding(asset.Id, asset.SourceRevision, xId, yId, ValueColumnId: valueId),
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default,
            HeatmapGrid = new HeatmapGridDefinition(HeatmapGridKind.PointCloud, duplicatePolicy),
            ColorScale = new PlotColorScale("viridis", 10, 100),
            Colorbar = new PlotColorbarDefinition(
                Ticks: [20, 40, 60, 80, 100],
                Unit: "a.u.",
                LabelStyle: new TextStyle("HeatmapFace", 9, false, "#FF112233"),
                TickLabels: ["Low", "40", "60", "80", "High"]),
        };
        return (asset, plot);
    }

    private static TabularDataRow Row(double x, double y, double value) => new(
    [
        TabularDataValue.FromNumber(x.ToString(System.Globalization.CultureInfo.InvariantCulture), x),
        TabularDataValue.FromNumber(y.ToString(System.Globalization.CultureInfo.InvariantCulture), y),
        TabularDataValue.FromNumber(value.ToString(System.Globalization.CultureInfo.InvariantCulture), value),
    ]);
}
