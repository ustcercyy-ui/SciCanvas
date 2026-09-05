using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class PlotPersistenceTests
{
    [Fact]
    public async Task ProjectStore_RoundTripsDataBoundPlotWithAxesTypographyAndErrorColumns()
    {
        using var workspace = new TestWorkspace();
        TabularDataAsset asset = CreateAsset();
        PlotObject plot = CreatePlot(asset);
        string path = Path.Combine(workspace.Root, "plot-roundtrip.scicanvas");
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Canvas = new ProjectCanvasSnapshot { Width = 600, Height = 400 },
            DataAssets = [TabularDataSnapshotMapper.ToSnapshot(asset)],
            Plots = [PlotSnapshotMapper.ToSnapshot(plot)],
        };
        var store = new JsonProjectStore();

        await store.SaveAsync(path, document);
        SciCanvasProjectDocument restoredDocument = await store.LoadAsync(path);
        TabularDataAsset restoredAsset = TabularDataSnapshotMapper.ToModel(
            Assert.Single(restoredDocument.DataAssets));
        PlotObject restored = PlotSnapshotMapper.ToModel(
            Assert.Single(restoredDocument.Plots),
            restoredAsset);

        Assert.Equal("3.0", restoredDocument.SchemaVersion);
        Assert.Equal(plot.Id, restored.Id);
        Assert.Equal(PlotKind.ErrorBar, restored.PlotType);
        Assert.Equal(asset.Id, restored.Data.DataAssetId);
        Assert.Equal(asset.SourceRevision, restored.Data.SourceRevision);
        Assert.Equal(plot.Data.ErrorBars!.ColumnIds, restored.Data.ErrorBars!.ColumnIds);
        Assert.Equal(PlotAxisScale.Log10, restored.XAxis.Scale);
        Assert.Equal("Times New Roman", restored.Typography.Axis.FontFamily);
        Assert.True(restored.Typography.Axis.IsBold);
        Assert.Equal(PlotLineStyle.DashDot, restored.Style.LineStyle);
        Assert.Equal(PlotMarkerShape.Diamond, restored.Style.MarkerShape);
        PlotDataFilter restoredFilter = Assert.IsType<PlotDataFilter>(restored.Filter);
        PlotDataFilter sourceFilter = Assert.IsType<PlotDataFilter>(plot.Filter);
        Assert.Equal(1, restoredFilter.ExcludedRowCount);
        Assert.Equal(sourceFilter.Expression, restoredFilter.Expression);
        Assert.Equal(2, restored.Transforms.Count);
        Assert.Equal(PlotTransformKind.Offset, restored.Transforms[0].Kind);
        Assert.Equal(PlotTransformKind.MovingAverage, restored.Transforms[1].Kind);
        string json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"plots\"", json, StringComparison.Ordinal);
        Assert.Contains("\"plotType\": \"errorBar\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void PlotSnapshotMapper_RoundTripsQuantitativeHeatmapSettings()
    {
        TabularDataAsset asset = CreateHeatmapAsset();
        PlotObject plot = CreateHeatmap(asset) with
        {
            HeatmapGrid = new HeatmapGridDefinition(
                HeatmapGridKind.IrregularGrid,
                HeatmapDuplicateCellPolicy.Median),
            ColorScale = new PlotColorScale(
                "magma", 10, 100, PlotColorScaleKind.Linear,
                PlotColorClampMode.Clamp, "#00000000", true),
            Colorbar = new PlotColorbarDefinition(
                PlotColorbarBinding.Detached,
                PlotColorbarOrientation.Horizontal,
                PlotColorbarPosition.Bottom,
                0,
                120,
                "a.u.",
                [0, 30, 60, 90, 120],
                PlotTypography.Default.Tick with { FontFamily = "Calibri", FontSizePt = 9 },
                ["Low", "30", "60", "90", "High"]),
        };

        ProjectPlotSnapshot snapshot = PlotSnapshotMapper.ToSnapshot(plot);
        PlotObject restored = PlotSnapshotMapper.ToModel(snapshot, asset);

        Assert.Equal(plot.HeatmapGrid, restored.HeatmapGrid);
        Assert.Equal(plot.ColorScale, restored.ColorScale);
        Assert.Equal(plot.Colorbar!.Binding, restored.Colorbar!.Binding);
        Assert.Equal(plot.Colorbar.Orientation, restored.Colorbar.Orientation);
        Assert.Equal(plot.Colorbar.Position, restored.Colorbar.Position);
        Assert.Equal(plot.Colorbar.Minimum, restored.Colorbar.Minimum);
        Assert.Equal(plot.Colorbar.Maximum, restored.Colorbar.Maximum);
        Assert.Equal(plot.Colorbar.Unit, restored.Colorbar.Unit);
        Assert.Equal(plot.Colorbar.Ticks, restored.Colorbar.Ticks);
        Assert.Equal(plot.Colorbar.TickLabels, restored.Colorbar.TickLabels);
        Assert.Equal(plot.Colorbar.LabelStyle, restored.Colorbar.LabelStyle);
    }

    [Fact]
    public void PlotSnapshotMapper_OldHeatmapWithoutOptionalSettings_UsesCompatibleDefaults()
    {
        TabularDataAsset asset = CreateHeatmapAsset();
        ProjectPlotSnapshot snapshot = PlotSnapshotMapper.ToSnapshot(CreateHeatmap(asset));

        Assert.Null(snapshot.HeatmapGrid);
        Assert.Null(snapshot.ColorScale);
        Assert.Null(snapshot.Colorbar);

        PlotObject restored = PlotSnapshotMapper.ToModel(snapshot, asset);
        HeatmapDomain domain = HeatmapDomainBuilder.Build(
            restored,
            PlotDataProjector.Project(restored, asset));

        Assert.Equal(HeatmapGridKind.Auto, domain.RequestedGridKind);
        Assert.Equal("viridis", domain.Colormap);
        Assert.NotNull(domain.Colorbar);
    }

    [Fact]
    public async Task ProjectStore_RejectsPlotWithMissingDataAssetWithoutWritingFile()
    {
        using var workspace = new TestWorkspace();
        TabularDataAsset asset = CreateAsset();
        PlotObject plot = CreatePlot(asset);
        string path = Path.Combine(workspace.Root, "orphan-plot.scicanvas");
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Canvas = new ProjectCanvasSnapshot { Width = 600, Height = 400 },
            Plots = [PlotSnapshotMapper.ToSnapshot(plot)],
        };

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonProjectStore().SaveAsync(path, document));

        Assert.Contains("Plot", exception.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task ProjectStore_RejectsTamperedFilterExcludedCount()
    {
        using var workspace = new TestWorkspace();
        TabularDataAsset asset = CreateAsset();
        PlotObject plot = CreatePlot(asset);
        ProjectPlotSnapshot snapshot = PlotSnapshotMapper.ToSnapshot(plot);
        ProjectPlotFilterSnapshot filter =
            Assert.IsType<ProjectPlotFilterSnapshot>(snapshot.Filter);
        var tampered = new ProjectPlotSnapshot
        {
            Id = snapshot.Id,
            Name = snapshot.Name,
            PlotType = snapshot.PlotType,
            Data = snapshot.Data,
            XAxis = snapshot.XAxis,
            YAxis = snapshot.YAxis,
            Typography = snapshot.Typography,
            Style = snapshot.Style,
            Filter = new ProjectPlotFilterSnapshot
            {
                ColumnId = filter.ColumnId,
                Operator = filter.Operator,
                Operand = filter.Operand,
                Expression = filter.Expression,
                ExcludedRowCount = filter.ExcludedRowCount + 1,
            },
            Transforms = snapshot.Transforms,
        };
        string path = Path.Combine(workspace.Root, "tampered-filter.scicanvas");
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Canvas = new ProjectCanvasSnapshot { Width = 600, Height = 400 },
            DataAssets = [TabularDataSnapshotMapper.ToSnapshot(asset)],
            Plots = [tampered],
        };

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonProjectStore().SaveAsync(path, document));

        Assert.Contains("Plot", exception.Message);
        Assert.Contains(
            "excluded row count",
            exception.InnerException?.Message,
            StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(path));
    }

    private static PlotObject CreatePlot(TabularDataAsset asset) => new PlotObject
    {
        Id = Guid.NewGuid(),
        Name = "Publication error curve",
        PlotType = PlotKind.ErrorBar,
        Data = new PlotDataBinding(
            asset.Id,
            asset.SourceRevision,
            asset.Columns[0].Id,
            asset.Columns[1].Id,
            new PlotErrorBarBinding(
                PlotErrorBarMode.Asymmetric,
                LowerColumnId: asset.Columns[2].Id,
                UpperColumnId: asset.Columns[3].Id)),
        XAxis = new PlotAxisDefinition(
            "Time",
            "s",
            PlotAxisScale.Log10,
            1,
            10,
            1,
            4),
        YAxis = new PlotAxisDefinition(
            "Signal",
            "mV",
            PlotAxisScale.Linear,
            0,
            20,
            5,
            4),
        Typography = PlotTypography.Default with
        {
            Axis = PlotTypography.Default.Axis with
            {
                FontFamily = "Times New Roman",
                FontSizePt = 10,
                IsBold = true,
            },
        },
        Style = PlotSeriesStyle.Default with
        {
            LineStyle = PlotLineStyle.DashDot,
            MarkerShape = PlotMarkerShape.Diamond,
            LineWidthPt = 2,
        },
        Filter = PlotDataFilter.Create(
            asset,
            asset.Columns[0].Id,
            PlotFilterOperator.GreaterThan,
            "1"),
        Transforms =
        [
            new PlotDataTransform(
                asset.Columns[0].Id,
                PlotTransformKind.Offset,
                Parameter: 1),
            new PlotDataTransform(
                asset.Columns[0].Id,
                PlotTransformKind.MovingAverage,
                WindowSize: 2,
                Alignment: PlotMovingAverageAlignment.Trailing),
        ],
    }.EnsureValid(asset);

    private static PlotObject CreateHeatmap(TabularDataAsset asset) => new PlotObject
    {
        Id = Guid.NewGuid(),
        Name = "Quantitative heatmap",
        PlotType = PlotKind.Heatmap,
        Data = new PlotDataBinding(
            asset.Id,
            asset.SourceRevision,
            asset.Columns[0].Id,
            asset.Columns[1].Id,
            ValueColumnId: asset.Columns[2].Id),
        XAxis = PlotAxisDefinition.DefaultX,
        YAxis = PlotAxisDefinition.DefaultY,
        Typography = PlotTypography.Default,
        Style = PlotSeriesStyle.Default,
    };

    private static TabularDataAsset CreateHeatmapAsset()
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Y", TabularDataType.Numeric, Role: DataColumnRole.Y);
        DataColumn value = new(Guid.NewGuid(), "Intensity", TabularDataType.Numeric);
        DataColumn[] columns = [x, y, value];
        return new TabularDataAsset(
            Guid.NewGuid(),
            "Heatmap",
            null,
            null,
            1,
            columns,
            [
                new TabularDataRow([
                    TabularDataValue.FromNumber("0", 0),
                    TabularDataValue.FromNumber("0", 0),
                    TabularDataValue.FromNumber("10", 10),
                ]),
                new TabularDataRow([
                    TabularDataValue.FromNumber("1", 1),
                    TabularDataValue.FromNumber("0", 0),
                    TabularDataValue.FromNumber("100", 100),
                ]),
            ],
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = 2,
                InferenceRowCount = 2,
                OriginalHeaders = columns.Select(column => column.Name).ToArray(),
            }).EnsureValid();
    }

    private static TabularDataAsset CreateAsset()
    {
        DataColumn x = new(Guid.NewGuid(), "Time", TabularDataType.Numeric, "s", DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Signal", TabularDataType.Numeric, "mV", DataColumnRole.Y);
        DataColumn low = new(Guid.NewGuid(), "Low", TabularDataType.Numeric, "mV", DataColumnRole.YError);
        DataColumn high = new(Guid.NewGuid(), "High", TabularDataType.Numeric, "mV", DataColumnRole.YError);
        DataColumn[] columns = [x, y, low, high];
        return new TabularDataAsset(
            Guid.NewGuid(),
            "Curve",
            null,
            null,
            5,
            columns,
            [
                new TabularDataRow(
                [
                    TabularDataValue.FromNumber("1", 1),
                    TabularDataValue.FromNumber("10", 10),
                    TabularDataValue.FromNumber("1", 1),
                    TabularDataValue.FromNumber("2", 2),
                ]),
                new TabularDataRow(
                [
                    TabularDataValue.FromNumber("10", 10),
                    TabularDataValue.FromNumber("15", 15),
                    TabularDataValue.FromNumber("1.5", 1.5),
                    TabularDataValue.FromNumber("2.5", 2.5),
                ]),
            ],
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = 2,
                InferenceRowCount = 2,
                OriginalHeaders = columns.Select(column => column.Name).ToArray(),
            }).EnsureValid();
    }
}
