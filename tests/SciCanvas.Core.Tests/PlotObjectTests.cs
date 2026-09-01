using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class PlotObjectTests
{
    [Theory]
    [InlineData(PlotKind.Line)]
    [InlineData(PlotKind.Scatter)]
    [InlineData(PlotKind.LineAndSymbol)]
    public void EnsureValid_AcceptsXyPlotKinds(PlotKind plotKind)
    {
        TestTable table = CreateTable();
        PlotObject plot = CreatePlot(
            table,
            plotKind,
            new PlotDataBinding(table.Asset.Id, 3, table.X.Id, table.Y.Id));

        Assert.Same(plot, plot.EnsureValid(table.Asset));
        Assert.Equal(table.X.Id, plot.Data.XColumnId);
        Assert.Equal(table.Y.Id, plot.Data.YColumnId);
    }

    [Fact]
    public void EnsureValid_AcceptsHistogramBoxPlotAndHeatmapBindings()
    {
        TestTable table = CreateTable();
        PlotObject histogram = CreatePlot(
            table,
            PlotKind.Histogram,
            new PlotDataBinding(table.Asset.Id, 3, null, table.Y.Id));
        PlotObject boxPlot = CreatePlot(
            table,
            PlotKind.BoxPlot,
            new PlotDataBinding(table.Asset.Id, 3, table.Category.Id, table.Y.Id));
        PlotObject heatmap = CreatePlot(
            table,
            PlotKind.Heatmap,
            new PlotDataBinding(
                table.Asset.Id,
                3,
                table.X.Id,
                table.Y.Id,
                ValueColumnId: table.Value.Id));

        Assert.Same(histogram, histogram.EnsureValid(table.Asset));
        Assert.Same(boxPlot, boxPlot.EnsureValid(table.Asset));
        Assert.Same(heatmap, heatmap.EnsureValid(table.Asset));
    }

    [Fact]
    public void ErrorBarBinding_AcceptsSymmetricAndAsymmetricOriginalColumns()
    {
        TestTable table = CreateTable();
        PlotObject symmetric = CreatePlot(
            table,
            PlotKind.ErrorBar,
            new PlotDataBinding(
                table.Asset.Id,
                3,
                table.X.Id,
                table.Y.Id,
                new PlotErrorBarBinding(
                    PlotErrorBarMode.Symmetric,
                    SymmetricColumnId: table.Error.Id)));
        PlotObject asymmetric = CreatePlot(
            table,
            PlotKind.ErrorBar,
            new PlotDataBinding(
                table.Asset.Id,
                3,
                table.X.Id,
                table.Y.Id,
                new PlotErrorBarBinding(
                    PlotErrorBarMode.Asymmetric,
                    LowerColumnId: table.ErrorLow.Id,
                    UpperColumnId: table.ErrorHigh.Id)));

        symmetric.EnsureValid(table.Asset);
        asymmetric.EnsureValid(table.Asset);

        Assert.Equal([table.Error.Id], symmetric.Data.ErrorBars!.ColumnIds);
        Assert.Equal(
            [table.ErrorLow.Id, table.ErrorHigh.Id],
            asymmetric.Data.ErrorBars!.ColumnIds);
    }

    [Fact]
    public void ErrorBarBinding_RejectsMissingForeignAndNonNumericColumns()
    {
        TestTable table = CreateTable();
        var missing = new PlotDataBinding(
            table.Asset.Id,
            3,
            table.X.Id,
            table.Y.Id);
        var foreign = missing with
        {
            ErrorBars = new PlotErrorBarBinding(
                PlotErrorBarMode.Symmetric,
                SymmetricColumnId: Guid.NewGuid()),
        };
        var nonNumeric = missing with
        {
            ErrorBars = new PlotErrorBarBinding(
                PlotErrorBarMode.Symmetric,
                SymmetricColumnId: table.Category.Id),
        };

        Assert.Throws<InvalidDataException>(() =>
            CreatePlot(table, PlotKind.ErrorBar, missing).EnsureValid(table.Asset));
        Assert.Throws<InvalidDataException>(() =>
            CreatePlot(table, PlotKind.ErrorBar, foreign).EnsureValid(table.Asset));
        Assert.Throws<InvalidDataException>(() =>
            CreatePlot(table, PlotKind.ErrorBar, nonNumeric).EnsureValid(table.Asset));
    }

    [Fact]
    public void EnsureValid_RejectsRevisionMismatchAndIncompatiblePlotColumns()
    {
        TestTable table = CreateTable();
        var revisionMismatch = new PlotDataBinding(
            table.Asset.Id,
            2,
            table.X.Id,
            table.Y.Id);
        var invalidHistogram = new PlotDataBinding(
            table.Asset.Id,
            3,
            table.X.Id,
            table.Y.Id);
        var invalidHeatmap = new PlotDataBinding(
            table.Asset.Id,
            3,
            table.X.Id,
            table.Y.Id,
            ValueColumnId: table.Category.Id);

        Assert.Throws<InvalidDataException>(() =>
            CreatePlot(table, PlotKind.Line, revisionMismatch).EnsureValid(table.Asset));
        Assert.Throws<InvalidDataException>(() =>
            CreatePlot(table, PlotKind.Histogram, invalidHistogram).EnsureValid(table.Asset));
        Assert.Throws<InvalidDataException>(() =>
            CreatePlot(table, PlotKind.Heatmap, invalidHeatmap).EnsureValid(table.Asset));
    }

    [Fact]
    public void AxisTypographyAndSeriesStyle_UseCanonicalValidation()
    {
        TestTable table = CreateTable();
        PlotObject valid = CreatePlot(
            table,
            PlotKind.Line,
            new PlotDataBinding(table.Asset.Id, 3, table.X.Id, table.Y.Id)) with
        {
            XAxis = new PlotAxisDefinition(
                "Time",
                "s",
                PlotAxisScale.Log10,
                0.1,
                100,
                10,
                8),
            Typography = new PlotTypography(
                new TextStyle("Times New Roman", 10, true, "#FF112233"),
                new TextStyle("Arial", 8, false, "#FF223344"),
                new TextStyle("Arial", 8, false, "#FF334455"),
                new TextStyle("Arial", 7, true, "#FF445566")),
            Style = new PlotSeriesStyle(
                "#FF1122AA",
                2,
                PlotLineStyle.DashDot,
                PlotMarkerShape.Diamond,
                6,
                "#FFFFFFFF",
                "#FF1122AA"),
        };

        Assert.Same(valid, valid.EnsureValid(table.Asset));
        Assert.Throws<InvalidDataException>(() =>
            (valid with
            {
                XAxis = valid.XAxis with { Minimum = 0 },
            }).EnsureValid(table.Asset));
        Assert.Throws<InvalidOperationException>(() =>
            (valid with
            {
                Typography = valid.Typography with
                {
                    Axis = valid.Typography.Axis with { FontSizePt = 2 },
                },
            }).EnsureValid(table.Asset));
        Assert.Throws<InvalidDataException>(() =>
            (valid with
            {
                Style = valid.Style with { MarkerFill = "not-a-color" },
            }).EnsureValid(table.Asset));
    }

    [Fact]
    public void LogAxisAndErrorBars_DoNotSilentlyDiscardInvalidSourceValues()
    {
        TestTable table = CreateTable();
        TabularDataAsset zeroX = table.Asset with
        {
            Rows =
            [
                table.Asset.Rows[0] with
                {
                    Values =
                    [
                        TabularDataValue.FromNumber("0", 0),
                        .. table.Asset.Rows[0].Values.Skip(1),
                    ],
                },
            ],
        };
        PlotObject logPlot = CreatePlot(
            table,
            PlotKind.Line,
            new PlotDataBinding(zeroX.Id, 3, table.X.Id, table.Y.Id)) with
        {
            XAxis = PlotAxisDefinition.DefaultX with { Scale = PlotAxisScale.Log10 },
        };
        TabularDataAsset negativeError = table.Asset with
        {
            Rows =
            [
                table.Asset.Rows[0] with
                {
                    Values =
                    [
                        .. table.Asset.Rows[0].Values.Take(2),
                        TabularDataValue.FromNumber("-0.5", -0.5),
                        .. table.Asset.Rows[0].Values.Skip(3),
                    ],
                },
            ],
        };
        PlotObject errorPlot = CreatePlot(
            table,
            PlotKind.ErrorBar,
            new PlotDataBinding(
                negativeError.Id,
                3,
                table.X.Id,
                table.Y.Id,
                new PlotErrorBarBinding(
                    PlotErrorBarMode.Symmetric,
                    SymmetricColumnId: table.Error.Id)));

        InvalidDataException logException = Assert.Throws<InvalidDataException>(
            () => logPlot.EnsureValid(zeroX));
        InvalidDataException errorException = Assert.Throws<InvalidDataException>(
            () => errorPlot.EnsureValid(negativeError));

        Assert.Contains("不会静默移除", logException.Message);
        Assert.Contains("不能包含负值", errorException.Message);
    }

    private static PlotObject CreatePlot(
        TestTable table,
        PlotKind kind,
        PlotDataBinding binding) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{kind} plot",
        PlotType = kind,
        Data = binding,
        XAxis = PlotAxisDefinition.DefaultX,
        YAxis = PlotAxisDefinition.DefaultY,
        Typography = PlotTypography.Default,
        Style = PlotSeriesStyle.Default,
    };

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
            3,
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
            ],
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = 1,
                InferenceRowCount = 1,
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
