using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;
using SciCanvas.Core.Data;
using SciCanvas.Core.Sources;
using SciCanvas.Persistence;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ScientificDataWorkspaceTests
{
    [Fact]
    public async Task PreviewThenConfirm_CreatesAssetOnlyAfterColumnReview()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "xrd.csv",
            Encoding.UTF8.GetBytes("2θ (deg),Intensity (counts)\n10.0,125\n10.5,250\n"));
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(path));
        var assets = new ObservableCollection<TabularDataAsset>();
        var viewModel = new ScientificDataWorkspaceViewModel(
            assets,
            new TabularDataImporter());

        await viewModel.PreviewFileAsync(path);

        Assert.True(viewModel.IsPreviewReady);
        Assert.Empty(assets);
        Assert.Equal(2, viewModel.PreviewColumns.Count);
        Assert.Equal(2, viewModel.PreviewRows.Count);
        Assert.Equal("deg", viewModel.PreviewColumns[0].Unit);
        Assert.Equal("counts", viewModel.PreviewColumns[1].Unit);
        viewModel.PreviewColumns[0].Role = DataColumnRole.X;
        viewModel.PreviewColumns[1].Role = DataColumnRole.Y;
        viewModel.AssetName = "XRD scan 01";

        TabularDataAsset? imported = await viewModel.ConfirmImportAsync();

        Assert.NotNull(imported);
        Assert.Same(imported, Assert.Single(assets));
        Assert.Equal(DataColumnRole.X, imported.Columns[0].Role);
        Assert.Equal(DataColumnRole.Y, imported.Columns[1].Role);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(path)));
        Assert.Contains("来源保持只读", viewModel.StatusText);

        viewModel.RemoveSelectedAssetCommand.Execute(null);

        Assert.Empty(assets);
        Assert.Contains("外部来源文件未修改", viewModel.StatusText);
    }

    [Fact]
    public async Task ConfirmAfterSourceChange_DoesNotCreateAssetAndKeepsPreviewActionable()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "curve.tsv",
            Encoding.UTF8.GetBytes("Time\tValue\n0\t1\n"));
        var assets = new ObservableCollection<TabularDataAsset>();
        var viewModel = new ScientificDataWorkspaceViewModel(
            assets,
            new TabularDataImporter());
        await viewModel.PreviewFileAsync(path);
        await File.AppendAllTextAsync(path, "1\t2\n", Encoding.UTF8);

        TabularDataAsset? imported = await viewModel.ConfirmImportAsync();

        Assert.Null(imported);
        Assert.Empty(assets);
        Assert.True(viewModel.IsPreviewReady);
        Assert.Contains("重新预览", viewModel.StatusText);
    }

    [Fact]
    public async Task XlsxDescriptionSheetFailure_KeepsDiscoveredSheetsForUserSelection()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile("multi-sheet.xlsx", [1, 2, 3]);
        var viewModel = new ScientificDataWorkspaceViewModel(
            new ObservableCollection<TabularDataAsset>(),
            new SheetSelectingImporter(path));

        await viewModel.PreviewFileAsync(path);

        Assert.False(viewModel.IsPreviewReady);
        Assert.Equal(["Overview", "Data"], viewModel.AvailableSheets);
        Assert.Equal("Overview", viewModel.SelectedSheetName);
        Assert.Contains("预览失败", viewModel.StatusText);

        viewModel.SelectedSheetName = "Data";
        await viewModel.RefreshPreviewAsync();

        Assert.True(viewModel.IsPreviewReady);
        Assert.Single(viewModel.PreviewColumns);
        Assert.Single(viewModel.PreviewRows);
    }

    private sealed class SheetSelectingImporter(string path) : ITabularDataImporter
    {
        private readonly SourceFingerprint _fingerprint = new(
            3,
            DateTimeOffset.UnixEpoch,
            new string('A', 64),
            null);

        public Task<IReadOnlyList<string>> DiscoverSheetsAsync(
            string sourcePath,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(["Overview", "Data"]);

        public Task<TabularDataImportPreview> PreviewAsync(
            string sourcePath,
            TabularDataImportOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.Equals(options?.SheetName, "Data", StringComparison.Ordinal))
            {
                throw new InvalidDataException("说明页不包含数据行。");
            }

            DataColumn column = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
            return Task.FromResult(new TabularDataImportPreview(
                path,
                _fingerprint,
                TabularDataFormat.Xlsx,
                "OOXML",
                null,
                ["Overview", "Data"],
                "Data",
                "A1:A2",
                1,
                [column],
                [new TabularDataRow([TabularDataValue.FromNumber("1", 1)])],
                1,
                1,
                ["Value"]));
        }

        public Task<TabularDataAsset> ImportAsync(
            TabularDataImportPreview preview,
            TabularDataImportConfirmation confirmation,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
