using System.Security.Cryptography;
using System.Text;
using SciCanvas.Core.Data;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class TabularDataPersistenceTests
{
    [Fact]
    public async Task ProjectStore_RoundTripsTypedDataAssetAndLeavesTableSourceUnchanged()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile(
            "hardness.csv",
            Encoding.UTF8.GetBytes("Load (N),Hardness (HV),Valid\n1,120.5,true\n2,125.25,false\n"));
        byte[] sourceHash = SHA256.HashData(File.ReadAllBytes(sourcePath));
        var importer = new TabularDataImporter();
        TabularDataImportPreview preview = await importer.PreviewAsync(sourcePath);
        TabularDataAsset asset = await importer.ImportAsync(
            preview,
            new TabularDataImportConfirmation("Hardness series"));
        string projectPath = Path.Combine(workspace.Root, "tabular-roundtrip.scicanvas");
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Title = "Tabular roundtrip",
            Canvas = new ProjectCanvasSnapshot { Width = 100, Height = 100 },
            DataAssets = [TabularDataSnapshotMapper.ToSnapshot(asset)],
        };
        var store = new JsonProjectStore();

        await store.SaveAsync(projectPath, document);
        SciCanvasProjectDocument restoredDocument = await store.LoadAsync(projectPath);
        TabularDataAsset restored = TabularDataSnapshotMapper.ToModel(
            Assert.Single(restoredDocument.DataAssets));

        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, restoredDocument.SchemaVersion);
        Assert.Equal(asset.Id, restored.Id);
        Assert.Equal(asset.Fingerprint, restored.Fingerprint);
        Assert.Equal(TabularDataType.Numeric, restored.Columns[0].DataType);
        Assert.Equal("N", restored.Columns[0].Unit);
        Assert.Equal(TabularDataType.Boolean, restored.Columns[2].DataType);
        Assert.Equal(125.25, restored.Rows[1].Values[1].NumericValue);
        Assert.False(restored.Rows[1].Values[2].BooleanValue);
        Assert.Equal(sourceHash, SHA256.HashData(File.ReadAllBytes(sourcePath)));
        string json = await File.ReadAllTextAsync(projectPath);
        Assert.Contains("\"schemaVersion\": \"3.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"dataAssets\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectStore_RejectsTypedValueThatDoesNotMatchColumn()
    {
        using var workspace = new TestWorkspace();
        string projectPath = Path.Combine(workspace.Root, "invalid-data.scicanvas");
        Guid columnId = Guid.NewGuid();
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Canvas = new ProjectCanvasSnapshot { Width = 10, Height = 10 },
            DataAssets =
            [
                new ProjectTabularDataAssetSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = "Invalid",
                    Columns =
                    [
                        new ProjectDataColumnSnapshot
                        {
                            Id = columnId,
                            Name = "Value",
                            DataType = "numeric",
                        },
                    ],
                    Rows =
                    [
                        new ProjectTabularDataRowSnapshot
                        {
                            Values =
                            [
                                new ProjectTabularDataValueSnapshot { RawText = "text" },
                            ],
                        },
                    ],
                    ImportMetadata = new ProjectTabularImportMetadataSnapshot
                    {
                        Format = "csv",
                        ImportedAt = DateTimeOffset.UtcNow,
                        EncodingName = "UTF-8",
                        Delimiter = ",",
                        HeaderRow = 1,
                        DataRowCount = 1,
                        InferenceRowCount = 1,
                        OriginalHeaders = ["Value"],
                    },
                },
            ],
        };

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new JsonProjectStore().SaveAsync(projectPath, document));

        Assert.Contains("表格数据资产", exception.Message);
        Assert.False(File.Exists(projectPath));
    }
}
