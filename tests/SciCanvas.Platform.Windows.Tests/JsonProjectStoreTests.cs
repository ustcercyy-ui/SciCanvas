using System.Security.Cryptography;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class JsonProjectStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsProjectWithoutChangingSource()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("source.tif", [1, 2, 3, 4, 5, 6]);
        string projectPath = Path.Combine(workspace.Root, "roundtrip.scicanvas");
        byte[] sourceBefore = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        SciCanvasProjectDocument expected = CreateDocument(sourcePath, "Roundtrip");
        var store = new JsonProjectStore();

        await store.SaveAsync(projectPath, expected);
        SciCanvasProjectDocument actual = await store.LoadAsync(projectPath);

        Assert.Equal(expected.ProjectId, actual.ProjectId);
        Assert.Equal(expected.Title, actual.Title);
        Assert.Equal(expected.Canvas.Width, actual.Canvas.Width);
        Assert.Single(actual.Sources);
        Assert.Equal(expected.Sources[0].Fingerprint.Sha256, actual.Sources[0].Fingerprint.Sha256);
        Assert.Equal(sourceBefore, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task SavingExistingProject_CreatesBackupOfPreviousVersion()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("source.tif", [7, 8, 9]);
        string projectPath = Path.Combine(workspace.Root, "backup.scicanvas");
        var store = new JsonProjectStore();

        await store.SaveAsync(projectPath, CreateDocument(sourcePath, "Version 1"));
        await store.SaveAsync(projectPath, CreateDocument(sourcePath, "Version 2"));
        await store.SaveAsync(projectPath, CreateDocument(sourcePath, "Version 3"));

        Assert.True(File.Exists(projectPath + ".bak"));
        Assert.Equal("Version 3", (await store.LoadAsync(projectPath)).Title);

        string backupJson = await File.ReadAllTextAsync(projectPath + ".bak");
        Assert.Contains("Version 2", backupJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LoadAsync_RejectsUnsupportedSchemaVersion()
    {
        using var workspace = new TestWorkspace();
        string path = workspace.CreateFile(
            "future.scicanvas",
            System.Text.Encoding.UTF8.GetBytes("""
            {
              "schemaVersion": "9.0",
              "projectId": "11111111-1111-1111-1111-111111111111",
              "createdAt": "2026-08-20T00:00:00Z",
              "updatedAt": "2026-08-20T00:00:00Z",
              "canvas": { "width": 1, "height": 1, "background": "white" },
              "sources": [], "layers": [], "cropPresets": [], "guides": [], "exportProfiles": []
            }
            """));

        await Assert.ThrowsAsync<NotSupportedException>(
            () => new JsonProjectStore().LoadAsync(path));
    }

    [Fact]
    public async Task SaveAsync_RejectsRoiProjectionWithMismatchedAssetReference()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("projection-source.tif", [1, 2, 3, 4]);
        string projectPath = Path.Combine(workspace.Root, "invalid-projection.scicanvas");
        Guid assetId = Guid.NewGuid();
        Guid panelId = Guid.NewGuid();
        Guid roiId = Guid.NewGuid();
        byte[] bytes = File.ReadAllBytes(sourcePath);
        var document = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            Canvas = new ProjectCanvasSnapshot { Width = 100, Height = 100 },
            Sources =
            [
                new ProjectSourceSnapshot
                {
                    Id = assetId,
                    DisplayName = Path.GetFileName(sourcePath),
                    OriginalPath = sourcePath,
                    SourceRevision = 1,
                    Fingerprint = new ProjectFingerprintSnapshot
                    {
                        ByteLength = bytes.Length,
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath),
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    },
                    Metadata = new ProjectImageMetadataSnapshot
                    {
                        Width = 2,
                        Height = 2,
                        Channels = 1,
                        BitsPerChannel = 8,
                        PixelFormat = "Gray8",
                    },
                    LinkState = "verified",
                },
            ],
            Layers =
            [
                new ProjectImageLayerSnapshot
                {
                    Id = panelId,
                    Name = "Panel A",
                    SourceAssetId = assetId,
                    SourceRect = new ProjectPixelRectSnapshot { Width = 2, Height = 2 },
                },
            ],
            Rois =
            [
                new ProjectRoiSnapshot
                {
                    Id = roiId,
                    AssetId = assetId,
                    SourceRevision = 1,
                    SourceGeometry =
                    [
                        new ProjectMeasurementPointSnapshot { X = 0, Y = 0 },
                        new ProjectMeasurementPointSnapshot { X = 1, Y = 0 },
                        new ProjectMeasurementPointSnapshot { X = 0, Y = 1 },
                    ],
                },
            ],
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                RoiProjections =
                [
                    new ProjectRoiFigureProjectionSnapshot
                    {
                        Id = Guid.NewGuid(),
                        RoiId = roiId,
                        PanelId = panelId,
                        AssetId = Guid.NewGuid(),
                        SourceRevision = 1,
                    },
                ],
            },
        };

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonProjectStore().SaveAsync(projectPath, document));
        Assert.False(File.Exists(projectPath));
    }

    private static SciCanvasProjectDocument CreateDocument(string sourcePath, string title)
    {
        byte[] bytes = File.ReadAllBytes(sourcePath);
        return new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            UpdatedAt = DateTimeOffset.UtcNow,
            Title = title,
            Canvas = new ProjectCanvasSnapshot
            {
                Width = 2161,
                Height = 2008,
                Background = "white",
            },
            Sources =
            [
                new ProjectSourceSnapshot
                {
                    Id = Guid.NewGuid(),
                    DisplayName = Path.GetFileName(sourcePath),
                    OriginalPath = sourcePath,
                    Fingerprint = new ProjectFingerprintSnapshot
                    {
                        ByteLength = bytes.Length,
                        LastWriteTimeUtc = File.GetLastWriteTimeUtc(sourcePath),
                        Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
                    },
                    Metadata = new ProjectImageMetadataSnapshot
                    {
                        Width = 2,
                        Height = 2,
                        Channels = 1,
                        BitsPerChannel = 8,
                        PixelFormat = "Gray8",
                    },
                    LinkState = "verified",
                },
            ],
            Layers = [],
            CropPresets = [],
            Guides = [],
            ExportProfiles = [],
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                TemplateId = "materials.multiscale-morphology.nature-double",
            },
            AuditTrail = [],
        };
    }
}
