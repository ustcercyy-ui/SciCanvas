using System.Collections.ObjectModel;
using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class PlotWorkspaceViewModelTests
{
    [Fact]
    public void Constructor_SelectsRoleColumnsAndPreservesAssetRevision()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));

        Assert.Same(table.Asset, viewModel.SelectedDataAsset);
        Assert.Equal(table.X.Id, viewModel.SelectedXColumn?.Id);
        Assert.Equal(table.Y.Id, viewModel.SelectedYColumn?.Id);
        Assert.Equal("Time", viewModel.XAxis.Title);
        Assert.Equal("s", viewModel.XAxis.Unit);

        PlotObject? plot = viewModel.SavePlot();

        Assert.NotNull(plot);
        Assert.Equal(table.Asset.Id, plot.Data.DataAssetId);
        Assert.Equal(7, plot.Data.SourceRevision);
        Assert.Same(plot, Assert.Single(viewModel.Plots));
    }

    [Fact]
    public void SavePlot_CreatesAllSevenKindsWithExplicitColumnBindings()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));
        var created = new List<PlotObject>();

        foreach (PlotKind kind in Enum.GetValues<PlotKind>())
        {
            viewModel.BeginNewPlot();
            viewModel.SelectedPlotKind = kind;
            viewModel.PlotName = kind.ToString();
            viewModel.SelectedXColumn = kind == PlotKind.BoxPlot
                ? table.Category
                : table.X;
            viewModel.SelectedYColumn = table.Y;
            viewModel.SelectedValueColumn = table.Value;
            viewModel.SelectedSymmetricErrorColumn = table.Error;
            PlotObject? plot = viewModel.SavePlot();
            Assert.NotNull(plot);
            created.Add(plot);
        }

        Assert.Equal(7, viewModel.Plots.Count);
        Assert.Equal(Enum.GetValues<PlotKind>(), created.Select(plot => plot.PlotType));
        Assert.Null(created.Single(plot => plot.PlotType == PlotKind.Histogram).Data.XColumnId);
        Assert.Equal(
            table.Value.Id,
            created.Single(plot => plot.PlotType == PlotKind.Heatmap).Data.ValueColumnId);
    }

    [Fact]
    public void ErrorBarEditor_StoresSymmetricAndAsymmetricSourceColumnIds()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));
        viewModel.SelectedPlotKind = PlotKind.ErrorBar;
        viewModel.PlotName = "Symmetric";
        viewModel.SelectedSymmetricErrorColumn = table.Error;

        PlotObject symmetric = Assert.IsType<PlotObject>(viewModel.SavePlot());

        Assert.Equal([table.Error.Id], symmetric.Data.ErrorBars!.ColumnIds);

        viewModel.BeginNewPlot();
        viewModel.SelectedPlotKind = PlotKind.ErrorBar;
        viewModel.PlotName = "Asymmetric";
        viewModel.SelectedErrorBarMode = PlotErrorBarMode.Asymmetric;
        viewModel.SelectedLowerErrorColumn = table.ErrorLow;
        viewModel.SelectedUpperErrorColumn = table.ErrorHigh;

        PlotObject asymmetric = Assert.IsType<PlotObject>(viewModel.SavePlot());

        Assert.Equal(
            [table.ErrorLow.Id, table.ErrorHigh.Id],
            asymmetric.Data.ErrorBars!.ColumnIds);
    }

    [Fact]
    public void HeatmapEditor_SavesAndReloadsQuantitativeDomainAndColorbarSettings()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));
        viewModel.SelectedPlotKind = PlotKind.Heatmap;
        viewModel.PlotName = "Quantitative heatmap";
        viewModel.SelectedXColumn = table.X;
        viewModel.SelectedYColumn = table.Y;
        viewModel.SelectedValueColumn = table.Value;
        viewModel.Heatmap.GridKind = HeatmapGridKind.PointCloud;
        viewModel.Heatmap.DuplicateCellPolicy = HeatmapDuplicateCellPolicy.Median;
        viewModel.Heatmap.Colormap = "viridis";
        viewModel.Heatmap.Minimum = 20;
        viewModel.Heatmap.Maximum = 100;
        viewModel.Heatmap.NoDataColor = "#00000000";
        viewModel.Heatmap.ColorbarTicks = "20, 40, 60, 80, 100";
        viewModel.Heatmap.ColorbarTickLabels = "Low | 40 | 60 | 80 | High";
        viewModel.Heatmap.ColorbarUnit = "a.u.";
        viewModel.Heatmap.UseCustomColorbarFont = true;
        viewModel.Heatmap.ColorbarFont.FontFamily = "Times New Roman";
        viewModel.Heatmap.ColorbarFont.FontSizePt = 9;

        PlotObject plot = Assert.IsType<PlotObject>(viewModel.SavePlot());

        Assert.Equal(HeatmapGridKind.PointCloud, plot.HeatmapGrid!.Kind);
        Assert.Equal(HeatmapDuplicateCellPolicy.Median, plot.HeatmapGrid.DuplicateCellPolicy);
        Assert.Equal(20, plot.ColorScale!.Minimum);
        Assert.Equal(100, plot.ColorScale.Maximum);
        Assert.Equal("#00000000", plot.ColorScale.NoDataColor);
        Assert.Equal([20d, 40d, 60d, 80d, 100d], plot.Colorbar!.Ticks);
        Assert.Equal(["Low", "40", "60", "80", "High"], plot.Colorbar.TickLabels);
        Assert.Equal("Times New Roman", plot.Colorbar.LabelStyle!.FontFamily);

        viewModel.BeginNewPlot();
        viewModel.SelectedPlot = plot;

        Assert.True(viewModel.UsesHeatmapSettings);
        Assert.Equal(HeatmapGridKind.PointCloud, viewModel.Heatmap.GridKind);
        Assert.Equal("20, 40, 60, 80, 100", viewModel.Heatmap.ColorbarTicks);
        Assert.Equal("Low | 40 | 60 | 80 | High", viewModel.Heatmap.ColorbarTickLabels);
        Assert.True(viewModel.Heatmap.UseCustomColorbarFont);
        Assert.Equal("Times New Roman", viewModel.Heatmap.ColorbarFont.FontFamily);
    }

    [Fact]
    public void SavePlot_InvalidAxisOrMissingColumnDoesNotMutateProject()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));
        viewModel.XAxis.Scale = PlotAxisScale.Log10;
        viewModel.XAxis.Minimum = 0;

        Assert.Null(viewModel.SavePlot());
        Assert.Empty(viewModel.Plots);
        Assert.Contains("Plot 校验失败", viewModel.StatusText);

        viewModel.XAxis.Minimum = 1;
        viewModel.SelectedYColumn = null;

        Assert.Null(viewModel.SavePlot());
        Assert.Empty(viewModel.Plots);
    }

    [Fact]
    public void EditPlot_RetainsIdentityAndUpdatesCanonicalTypographyAndSeriesStyle()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));
        PlotObject original = Assert.IsType<PlotObject>(viewModel.SavePlot());
        int changed = 0;
        viewModel.Changed += (_, _) => changed++;
        viewModel.SelectedPlot = original;
        viewModel.PlotName = "Publication curve";
        viewModel.AxisFont.FontFamily = "Times New Roman";
        viewModel.AxisFont.FontSizePt = 11;
        viewModel.AxisFont.IsBold = true;
        viewModel.AxisFont.Color = "#FF102030";
        viewModel.SeriesStyle.LineStyle = PlotLineStyle.Dash;
        viewModel.SeriesStyle.LineWidthPt = 2.5;
        viewModel.SeriesStyle.MarkerShape = PlotMarkerShape.Diamond;

        PlotObject updated = Assert.IsType<PlotObject>(viewModel.SavePlot());

        Assert.Single(viewModel.Plots);
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal("Publication curve", updated.Name);
        Assert.Equal("Times New Roman", updated.Typography.Axis.FontFamily);
        Assert.Equal(11, updated.Typography.Axis.FontSizePt);
        Assert.True(updated.Typography.Axis.IsBold);
        Assert.Equal(PlotLineStyle.Dash, updated.Style.LineStyle);
        Assert.Equal(2.5, updated.Style.LineWidthPt);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void FilterAndTransforms_SaveExplicitExpressionCountsAndExecutionOrder()
    {
        TestTable table = CreateTable();
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]));
        viewModel.IsFilterEnabled = true;
        viewModel.SelectedFilterColumn = table.X;
        viewModel.SelectedFilterOperator = PlotFilterOperator.GreaterThan;
        viewModel.FilterOperand = "1";

        viewModel.PreviewFilterCommand.Execute(null);

        Assert.Equal(1, viewModel.ExcludedRowCount);
        Assert.Contains(table.X.Id.ToString("D"), viewModel.FilterExpression);

        viewModel.NewTransformColumn = table.Y;
        viewModel.NewTransformKind = PlotTransformKind.NormalizeMinMax;
        viewModel.AddTransformCommand.Execute(null);
        viewModel.NewTransformKind = PlotTransformKind.Offset;
        viewModel.NewTransformParameter = 5;
        viewModel.AddTransformCommand.Execute(null);
        viewModel.MoveTransformUpCommand.Execute(null);

        PlotObject plot = Assert.IsType<PlotObject>(viewModel.SavePlot());
        PlotScientificProvenance provenance =
            PlotScientificProvenanceBuilder.Create(plot, table.Asset);

        Assert.Equal(1, plot.Filter!.ExcludedRowCount);
        Assert.Equal(viewModel.FilterExpression, plot.Filter.Expression);
        Assert.Equal(PlotTransformKind.Offset, plot.Transforms[0].Kind);
        Assert.Equal(PlotTransformKind.NormalizeMinMax, plot.Transforms[1].Kind);
        Assert.Equal(1, provenance.ExcludedRowCount);
        Assert.Equal(2, provenance.IncludedRowCount);
        Assert.Equal(2, provenance.Transforms.Count);
        Assert.Equal(
            [10.0, 12.0, 14.0],
            table.Asset.Rows.Select(row => row.Values[1].NumericValue!.Value));
    }

    [Fact]
    public void SelectingSavedPlot_RestoresFilterAndTransformEditors()
    {
        TestTable table = CreateTable();
        PlotDataFilter filter = PlotDataFilter.Create(
            table.Asset,
            table.X.Id,
            PlotFilterOperator.GreaterThanOrEqual,
            "2");
        PlotObject plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = "Restored operations",
            PlotType = PlotKind.Line,
            Data = new PlotDataBinding(
                table.Asset.Id,
                table.Asset.SourceRevision,
                table.X.Id,
                table.Y.Id),
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default,
            Filter = filter,
            Transforms =
            [
                new PlotDataTransform(
                    table.Y.Id,
                    PlotTransformKind.MovingAverage,
                    WindowSize: 3,
                    Alignment: PlotMovingAverageAlignment.Centered),
            ],
        }.EnsureValid(table.Asset);
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]),
            new ObservableCollection<PlotObject>([plot]));

        viewModel.SelectedPlot = plot;

        Assert.True(viewModel.IsFilterEnabled);
        Assert.Equal(table.X.Id, viewModel.SelectedFilterColumn?.Id);
        Assert.Equal(filter.Expression, viewModel.FilterExpression);
        PlotTransformEditorViewModel transform = Assert.Single(viewModel.Transforms);
        Assert.Equal(table.Y.Id, transform.Column.Id);
        Assert.Equal(PlotTransformKind.MovingAverage, transform.Kind);
        Assert.Equal(3, transform.WindowSize);
        Assert.Equal(PlotMovingAverageAlignment.Centered, transform.Alignment);
    }

    [Fact]
    public void AddToFigure_UsesValidatedAssetAndReferencedPlotCannotBeRemoved()
    {
        TestTable table = CreateTable();
        PlotObject? addedPlot = null;
        TabularDataAsset? addedAsset = null;
        using var viewModel = new PlotWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>([table.Asset]),
            addToFigure: (plot, asset) =>
            {
                addedPlot = plot;
                addedAsset = asset;
            },
            canRemovePlot: _ => false);
        PlotObject plot = Assert.IsType<PlotObject>(viewModel.SavePlot());

        viewModel.AddSelectedPlotToFigureCommand.Execute(null);

        Assert.Same(plot, addedPlot);
        Assert.Same(table.Asset, addedAsset);
        Assert.False(viewModel.RemoveSelectedPlotCommand.CanExecute(null));
        Assert.Contains("添加到 Figure", viewModel.StatusText);
    }

    private static TestTable CreateTable()
    {
        DataColumn x = new(Guid.NewGuid(), "Time", TabularDataType.Numeric, "s", DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Signal", TabularDataType.Numeric, "mV", DataColumnRole.Y);
        DataColumn error = new(Guid.NewGuid(), "Error", TabularDataType.Numeric, "mV", DataColumnRole.YError);
        DataColumn errorLow = new(Guid.NewGuid(), "Error low", TabularDataType.Numeric, "mV", DataColumnRole.YError);
        DataColumn errorHigh = new(Guid.NewGuid(), "Error high", TabularDataType.Numeric, "mV", DataColumnRole.YError);
        DataColumn value = new(Guid.NewGuid(), "Intensity", TabularDataType.Numeric);
        DataColumn category = new(Guid.NewGuid(), "Group", TabularDataType.Text, Role: DataColumnRole.Category);
        DataColumn[] columns = [x, y, error, errorLow, errorHigh, value, category];
        var asset = new TabularDataAsset(
            Guid.NewGuid(),
            "Experiment",
            null,
            null,
            7,
            columns,
            [
                new TabularDataRow(
                [
                    TabularDataValue.FromNumber("1", 1),
                    TabularDataValue.FromNumber("10", 10),
                    TabularDataValue.FromNumber("0.5", 0.5),
                    TabularDataValue.FromNumber("0.3", 0.3),
                    TabularDataValue.FromNumber("0.7", 0.7),
                    TabularDataValue.FromNumber("42", 42),
                    TabularDataValue.FromText("Control"),
                ]),
                new TabularDataRow(
                [
                    TabularDataValue.FromNumber("2", 2),
                    TabularDataValue.FromNumber("12", 12),
                    TabularDataValue.FromNumber("0.5", 0.5),
                    TabularDataValue.FromNumber("0.3", 0.3),
                    TabularDataValue.FromNumber("0.7", 0.7),
                    TabularDataValue.FromNumber("45", 45),
                    TabularDataValue.FromText("Treatment"),
                ]),
                new TabularDataRow(
                [
                    TabularDataValue.FromNumber("3", 3),
                    TabularDataValue.FromNumber("14", 14),
                    TabularDataValue.FromNumber("0.5", 0.5),
                    TabularDataValue.FromNumber("0.3", 0.3),
                    TabularDataValue.FromNumber("0.7", 0.7),
                    TabularDataValue.FromNumber("48", 48),
                    TabularDataValue.FromText("Treatment"),
                ]),
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
            });

        return new TestTable(asset, x, y, error, errorLow, errorHigh, value, category);
    }

    private sealed record TestTable(
        TabularDataAsset Asset,
        DataColumn X,
        DataColumn Y,
        DataColumn Error,
        DataColumn ErrorLow,
        DataColumn ErrorHigh,
        DataColumn Value,
        DataColumn Category);
}
