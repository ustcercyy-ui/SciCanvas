using System.Globalization;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;

namespace SciCanvas.Core.Tests;

public sealed class HeatmapDomainTests
{
    [Fact]
    public void Build_ThreeByTwoRegularGrid_HasExactlySixCells()
    {
        (TabularDataAsset asset, PlotObject plot) = Create(
            (0, 0, 10), (1, 0, 20), (2, 0, 30),
            (0, 1, 40), (1, 1, 50), (2, 1, 60));

        HeatmapDomain domain = Build(asset, plot);

        Assert.Equal(HeatmapGridKind.RegularGrid, domain.EffectiveGridKind);
        Assert.Equal(6, domain.Cells.Count);
        Assert.All(domain.Cells, cell => Assert.False(cell.IsNoData));
    }

    [Fact]
    public void Build_NonuniformCoordinates_UsesMidpointBoundaries()
    {
        (TabularDataAsset asset, PlotObject plot) = Create(
            (0, 0, 10), (1, 0, 20), (3, 0, 30),
            (0, 1, 40), (1, 1, 50), (3, 1, 60));

        HeatmapDomain domain = Build(asset, plot);
        HeatmapDomainCell[] bottomRow = domain.Cells.Where(cell => cell.Y == 0).ToArray();

        Assert.Equal(HeatmapGridKind.IrregularGrid, domain.EffectiveGridKind);
        Assert.Equal([(-0.5, 0.5), (0.5, 2.0), (2.0, 4.0)],
            bottomRow.Select(cell => (cell.Left, cell.Right)));
        Assert.Contains(domain.Issues, issue => issue.Code == HeatmapQcCodes.IrregularGrid);
    }

    [Fact]
    public void Build_DuplicateCoordinate_DefaultPolicyIsExplicitError()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (0, 0, 20));

        HeatmapDomainException exception = Assert.Throws<HeatmapDomainException>(() => Build(asset, plot));

        Assert.Equal(HeatmapQcCodes.DuplicateCell, exception.Code);
    }

    [Fact]
    public void Build_DuplicateCoordinate_WithMeanPolicyAggregatesAndAudits()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (0, 0, 20));
        plot = plot with
        {
            HeatmapGrid = new HeatmapGridDefinition(
                HeatmapGridKind.PointCloud,
                HeatmapDuplicateCellPolicy.Mean),
        };

        HeatmapDomain domain = Build(asset, plot);

        Assert.Equal(15, Assert.Single(domain.Cells).Value);
        Assert.Contains(domain.Issues, issue => issue.Code == HeatmapQcCodes.DuplicateCell);
    }

    [Fact]
    public void Build_MissingCoordinateOnExplicitGrid_IsTransparentNoDataNotZero()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (1, 0, 20), (0, 1, 30));
        plot = plot with
        {
            HeatmapGrid = new HeatmapGridDefinition(HeatmapGridKind.RegularGrid),
        };

        HeatmapDomain domain = Build(asset, plot);
        HeatmapDomainCell missing = Assert.Single(domain.Cells, cell => cell.X == 1 && cell.Y == 1);

        Assert.Equal(4, domain.Cells.Count);
        Assert.True(missing.IsNoData);
        Assert.Null(missing.Value);
        Assert.Null(missing.NormalizedValue);
        Assert.Null(missing.Fill);
        Assert.Contains(domain.Issues, issue => issue.Code == HeatmapQcCodes.GridIncomplete);
    }

    [Fact]
    public void Build_IncompleteAutoGrid_RendersObservedPointsWithoutInterpolation()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (1, 0, 20), (0, 1, 30));

        HeatmapDomain domain = Build(asset, plot);

        Assert.Equal(HeatmapGridKind.PointCloud, domain.EffectiveGridKind);
        Assert.Equal(3, domain.Cells.Count);
        Assert.All(domain.Cells, cell =>
        {
            Assert.Equal(cell.X, cell.Left);
            Assert.Equal(cell.X, cell.Right);
            Assert.Equal(cell.Y, cell.Bottom);
            Assert.Equal(cell.Y, cell.Top);
        });
    }

    [Fact]
    public void Build_ColorbarAndCellsShareConfiguredTenToOneHundredRange()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (1, 0, 100));
        plot = plot with
        {
            ColorScale = new PlotColorScale(Minimum: 10, Maximum: 100),
            Colorbar = new PlotColorbarDefinition(Ticks: [10, 40, 70, 100]),
        };

        HeatmapDomain domain = Build(asset, plot);

        Assert.Equal(10, domain.Minimum);
        Assert.Equal(100, domain.Maximum);
        Assert.Equal(10, domain.Colorbar!.Minimum);
        Assert.Equal(100, domain.Colorbar.Maximum);
        Assert.Equal(0, domain.Cells.Single(cell => cell.Value == 10).NormalizedValue);
        Assert.Equal(1, domain.Cells.Single(cell => cell.Value == 100).NormalizedValue);
    }

    [Fact]
    public void Build_LogScaleWithNonpositiveValue_IsExplicitError()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 0), (1, 0, 10));
        plot = plot with { ColorScale = new PlotColorScale(Scale: PlotColorScaleKind.Log10) };

        HeatmapDomainException exception = Assert.Throws<HeatmapDomainException>(() => Build(asset, plot));

        Assert.Equal(HeatmapQcCodes.LogNonpositive, exception.Code);
    }

    [Fact]
    public void Build_LogScaleAutoColorbarTicks_AreLogSpaced()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 1), (1, 0, 100));
        plot = plot with { ColorScale = new PlotColorScale(Scale: PlotColorScaleKind.Log10) };

        HeatmapDomain domain = Build(asset, plot);

        Assert.Equal(1, domain.Colorbar!.Ticks[0], 10);
        Assert.Equal(10, domain.Colorbar.Ticks[2], 10);
        Assert.Equal(100, domain.Colorbar.Ticks[^1], 10);
    }

    [Fact]
    public void Build_MissingValueBinding_IsExplicitNoValueColumnError()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (1, 0, 20));
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);
        plot = plot with { Data = plot.Data with { ValueColumnId = null } };

        HeatmapDomainException exception = Assert.Throws<HeatmapDomainException>(
            () => HeatmapDomainBuilder.Build(plot, projection));

        Assert.Equal(HeatmapQcCodes.NoValueColumn, exception.Code);
    }

    [Fact]
    public void Build_InvalidConfiguredRange_UsesDedicatedErrorCode()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (1, 0, 20));
        plot = plot with { ColorScale = new PlotColorScale(Minimum: 100, Maximum: 10) };

        HeatmapDomainException exception = Assert.Throws<HeatmapDomainException>(() => Build(asset, plot));

        Assert.Equal(HeatmapQcCodes.InvalidRange, exception.Code);
    }

    [Fact]
    public void Build_LinkedColorbarWithIndependentRange_IsMismatchError()
    {
        (TabularDataAsset asset, PlotObject plot) = Create((0, 0, 10), (1, 0, 100));
        plot = plot with
        {
            Colorbar = new PlotColorbarDefinition(Minimum: 0, Maximum: 100),
        };

        HeatmapDomainException exception = Assert.Throws<HeatmapDomainException>(() => Build(asset, plot));

        Assert.Equal(HeatmapQcCodes.ColorbarMismatch, exception.Code);
    }

    [Fact]
    public void ScientificColormap_SamplesCorePaletteDeterministically()
    {
        Assert.Equal("#FF440154", ScientificColormap.Sample("Viridis", 0).ToHex());
        Assert.Equal("#FFFDE725", ScientificColormap.Sample("viridis", 1).ToHex());
        Assert.Equal("#FF808080", ScientificColormap.Sample("grayscale", 0.5).ToHex());
    }

    private static HeatmapDomain Build(TabularDataAsset asset, PlotObject plot) =>
        HeatmapDomainBuilder.Build(plot, PlotDataProjector.Project(plot, asset));

    private static (TabularDataAsset Asset, PlotObject Plot) Create(
        params (double X, double Y, double? Value)[] samples)
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Y", TabularDataType.Numeric, Role: DataColumnRole.Y);
        DataColumn value = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
        DataColumn[] columns = [x, y, value];
        TabularDataRow[] rows = samples.Select(sample => new TabularDataRow(
        [
            Number(sample.X),
            Number(sample.Y),
            sample.Value.HasValue ? Number(sample.Value.Value) : TabularDataValue.Missing,
        ])).ToArray();
        var asset = new TabularDataAsset(
            Guid.NewGuid(),
            "Heatmap domain",
            null,
            null,
            1,
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
        var plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = "Quantitative heatmap",
            PlotType = PlotKind.Heatmap,
            Data = new PlotDataBinding(asset.Id, asset.SourceRevision, x.Id, y.Id, ValueColumnId: value.Id),
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default,
        };
        return (asset, plot);
    }

    private static TabularDataValue Number(double value) =>
        TabularDataValue.FromNumber(value.ToString(CultureInfo.InvariantCulture), value);
}
