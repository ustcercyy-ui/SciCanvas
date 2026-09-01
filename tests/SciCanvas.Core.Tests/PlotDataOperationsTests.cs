using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;

namespace SciCanvas.Core.Tests;

public sealed class PlotDataOperationsTests
{
    [Fact]
    public void Filter_StoresCanonicalExpressionAndRecomputedExcludedRowCount()
    {
        TestTable table = CreateTable();
        PlotDataFilter filter = PlotDataFilter.Create(
            table.Asset,
            table.X.Id,
            PlotFilterOperator.GreaterThan,
            "2.0");
        PlotObject plot = CreatePlot(table) with { Filter = filter };

        PlotDataProjection projection = PlotDataProjector.Project(plot, table.Asset);

        Assert.Contains(table.X.Id.ToString("D"), filter.Expression);
        Assert.Contains("X", filter.Expression);
        Assert.Equal(2, filter.ExcludedRowCount);
        Assert.Equal(2, projection.ExcludedRowCount);
        Assert.Equal(3, projection.IncludedRowCount);
        Assert.Equal([2, 3, 4], projection.Rows.Select(row => row.SourceRowIndex));
        Assert.Throws<InvalidDataException>(() =>
            (plot with
            {
                Filter = filter with { ExcludedRowCount = 1 },
            }).EnsureValid(table.Asset));
    }

    [Fact]
    public void TransformPipeline_IsOrderedNonDestructiveAndKeepsEveryIncludedRow()
    {
        TestTable table = CreateTable();
        PlotObject plot = CreatePlot(table) with
        {
            Transforms =
            [
                new PlotDataTransform(table.Y.Id, PlotTransformKind.NormalizeMinMax),
                new PlotDataTransform(table.Y.Id, PlotTransformKind.Offset, Parameter: 1),
                new PlotDataTransform(table.Y.Id, PlotTransformKind.Log10),
                new PlotDataTransform(
                    table.Y.Id,
                    PlotTransformKind.MovingAverage,
                    WindowSize: 2,
                    Alignment: PlotMovingAverageAlignment.Trailing),
            ],
        };
        double[] originalValues = table.Asset.Rows
            .Select(row => row.Values[1].NumericValue!.Value)
            .ToArray();

        PlotDataProjection projection = PlotDataProjector.Project(plot, table.Asset);

        Assert.Equal(5, projection.Rows.Count);
        Assert.Equal(0, projection.ExcludedRowCount);
        Assert.Equal(0, projection.UnplottableRowCount);
        Assert.Equal(4, projection.AppliedTransforms.Count);
        Assert.Equal(0.0, projection.Rows[0].Y!.Value, 10);
        Assert.Equal(
            (Math.Log10(1.75) + Math.Log10(2)) / 2,
            projection.Rows[^1].Y!.Value,
            10);
        Assert.Equal(originalValues, table.Asset.Rows
            .Select(row => row.Values[1].NumericValue!.Value));
        Assert.Equal(originalValues, projection.Rows.Select(row => row.OriginalY!.Value));
    }

    [Fact]
    public void LogTransform_RejectsNonPositiveValuesWithoutHiddenRemoval()
    {
        TestTable table = CreateTable();
        TabularDataAsset asset = table.Asset with
        {
            Rows =
            [
                table.Asset.Rows[0] with
                {
                    Values =
                    [
                        table.Asset.Rows[0].Values[0],
                        TabularDataValue.FromNumber("0", 0),
                        .. table.Asset.Rows[0].Values.Skip(2),
                    ],
                },
                .. table.Asset.Rows.Skip(1),
            ],
        };
        PlotObject plot = CreatePlot(table) with
        {
            Transforms =
            [
                new PlotDataTransform(table.Y.Id, PlotTransformKind.Log10),
            ],
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => plot.EnsureValid(asset));

        Assert.Contains("不会静默移除", exception.Message);
        Assert.Equal(5, asset.Rows.Count);
    }

    [Fact]
    public void ErrorBarTransform_RefusesUnspecifiedErrorPropagation()
    {
        TestTable table = CreateTable();
        PlotObject plot = CreatePlot(table, PlotKind.ErrorBar) with
        {
            Data = new PlotDataBinding(
                table.Asset.Id,
                table.Asset.SourceRevision,
                table.X.Id,
                table.Y.Id,
                new PlotErrorBarBinding(
                    PlotErrorBarMode.Symmetric,
                    SymmetricColumnId: table.Error.Id)),
            Transforms =
            [
                new PlotDataTransform(table.Y.Id, PlotTransformKind.NormalizeMinMax),
            ],
        };

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => plot.EnsureValid(table.Asset));

        Assert.Contains("显式误差传播", exception.Message);
    }

    [Fact]
    public void Provenance_RecordsIdentityFilterTransformsStyleAndRowAccounting()
    {
        TestTable table = CreateTable();
        PlotDataFilter filter = PlotDataFilter.Create(
            table.Asset,
            table.Category.Id,
            PlotFilterOperator.Equal,
            "Treatment");
        PlotObject plot = CreatePlot(table) with
        {
            Filter = filter,
            Transforms =
            [
                new PlotDataTransform(table.Y.Id, PlotTransformKind.Offset, Parameter: -5),
            ],
        };

        PlotScientificProvenance provenance =
            PlotScientificProvenanceBuilder.Create(plot, table.Asset);

        Assert.Equal(plot.Id, provenance.PlotId);
        Assert.Equal(table.Asset.Id, provenance.DataAssetId);
        Assert.Equal(table.Asset.SourceRevision, provenance.SourceRevision);
        Assert.Equal(table.X.Id, provenance.XColumnId);
        Assert.Equal(table.Y.Id, provenance.YColumnId);
        Assert.Empty(provenance.ErrorColumnIds);
        Assert.Equal(filter.Expression, provenance.FilterExpression);
        Assert.Equal(3, provenance.ExcludedRowCount);
        Assert.Equal(2, provenance.IncludedRowCount);
        Assert.Single(provenance.Transforms);
        Assert.Contains("offset", provenance.Transforms[0]);
        Assert.Equal(plot.Style, provenance.Style);
        Assert.Equal(PlotKind.Line, provenance.PlotType);
    }

    [Fact]
    public void MissingBoundValue_IsReportedAsUnplottableNotFiltered()
    {
        TestTable table = CreateTable();
        TabularDataAsset asset = table.Asset with
        {
            Rows =
            [
                table.Asset.Rows[0] with
                {
                    Values =
                    [
                        table.Asset.Rows[0].Values[0],
                        TabularDataValue.Missing,
                        .. table.Asset.Rows[0].Values.Skip(2),
                    ],
                },
                .. table.Asset.Rows.Skip(1),
            ],
        };
        PlotObject plot = CreatePlot(table);

        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);

        Assert.Equal(0, projection.ExcludedRowCount);
        Assert.Equal(5, projection.IncludedRowCount);
        Assert.Equal(1, projection.UnplottableRowCount);
        Assert.Null(projection.Rows[0].Y);
        Assert.Equal(0, projection.Rows[0].SourceRowIndex);
    }

    [Fact]
    public void ExplicitFilterCanEnableLogAxisButPostTransformInvalidityStillBlocks()
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
                .. table.Asset.Rows.Skip(1),
            ],
        };
        PlotDataFilter positiveOnly = PlotDataFilter.Create(
            zeroX,
            table.X.Id,
            PlotFilterOperator.GreaterThan,
            "0");
        PlotObject filteredLog = CreatePlot(table) with
        {
            XAxis = PlotAxisDefinition.DefaultX with { Scale = PlotAxisScale.Log10 },
            Filter = positiveOnly,
        };

        PlotDataProjection projection = PlotDataProjector.Project(filteredLog, zeroX);

        Assert.Equal(1, projection.ExcludedRowCount);
        Assert.All(projection.Rows, row => Assert.True(row.X > 0));

        PlotObject invalidAfterOffset = filteredLog with
        {
            Transforms =
            [
                new PlotDataTransform(
                    table.X.Id,
                    PlotTransformKind.Offset,
                    Parameter: -10),
            ],
        };
        InvalidDataException transformException = Assert.Throws<InvalidDataException>(
            () => invalidAfterOffset.EnsureValid(zeroX));
        Assert.Contains("Transform 后", transformException.Message);

        PlotObject empty = CreatePlot(table) with
        {
            Filter = PlotDataFilter.Create(
                table.Asset,
                table.X.Id,
                PlotFilterOperator.GreaterThan,
                "100"),
        };
        InvalidDataException emptyException = Assert.Throws<InvalidDataException>(
            () => empty.EnsureValid(table.Asset));
        Assert.Contains("excluded every", emptyException.Message);
    }

    private static PlotObject CreatePlot(
        TestTable table,
        PlotKind kind = PlotKind.Line) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Auditable plot",
        PlotType = kind,
        Data = new PlotDataBinding(
            table.Asset.Id,
            table.Asset.SourceRevision,
            table.X.Id,
            table.Y.Id),
        XAxis = PlotAxisDefinition.DefaultX,
        YAxis = PlotAxisDefinition.DefaultY,
        Typography = PlotTypography.Default,
        Style = PlotSeriesStyle.Default,
    };

    private static TestTable CreateTable()
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Y", TabularDataType.Numeric, Role: DataColumnRole.Y);
        DataColumn error = new(Guid.NewGuid(), "Error", TabularDataType.Numeric, Role: DataColumnRole.YError);
        DataColumn category = new(Guid.NewGuid(), "Group", TabularDataType.Text, Role: DataColumnRole.Category);
        DataColumn[] columns = [x, y, error, category];
        TabularDataRow[] rows = Enumerable.Range(1, 5)
            .Select(value => new TabularDataRow(
            [
                TabularDataValue.FromNumber(value.ToString(), value),
                TabularDataValue.FromNumber((value * 10).ToString(), value * 10),
                TabularDataValue.FromNumber("1", 1),
                TabularDataValue.FromText(value % 2 == 0 ? "Treatment" : "Control"),
            ]))
            .ToArray();
        var asset = new TabularDataAsset(
            Guid.NewGuid(),
            "Operations",
            null,
            null,
            9,
            columns,
            rows,
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = rows.Length,
                InferenceRowCount = rows.Length,
                OriginalHeaders = columns.Select(column => column.Name).ToArray(),
            }).EnsureValid();
        return new TestTable(asset, x, y, error, category);
    }

    private sealed record TestTable(
        TabularDataAsset Asset,
        DataColumn X,
        DataColumn Y,
        DataColumn Error,
        DataColumn Category);
}
