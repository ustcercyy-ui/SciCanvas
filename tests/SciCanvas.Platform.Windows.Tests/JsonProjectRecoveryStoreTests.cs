using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class JsonProjectRecoveryStoreTests
{
    [Fact]
    public async Task UnsavedRecovery_RoundTripsAndDeletesOnlyRecoveryFiles()
    {
        using var workspace = new TestWorkspace();
        string recoveryDirectory = Path.Combine(workspace.Root, "recovery");
        Guid projectId = Guid.NewGuid();
        SciCanvasProjectDocument document = CreateDocument(projectId, "未命名恢复测试");
        var store = new JsonProjectRecoveryStore(recoveryDirectory);

        await store.SaveAsync(projectId, originalProjectPath: null, document);
        ProjectRecoveryCandidate candidate = Assert.IsType<ProjectRecoveryCandidate>(
            await store.FindLatestUnsavedAsync());
        SciCanvasProjectDocument restored = await new JsonProjectStore().LoadAsync(candidate.RecoveryPath);

        Assert.Null(candidate.OriginalProjectPath);
        Assert.Equal(projectId, restored.ProjectId);
        Assert.Equal(document.Title, restored.Title);

        await store.DeleteCandidateAsync(candidate);

        Assert.False(File.Exists(candidate.RecoveryPath));
        Assert.Null(await store.FindLatestUnsavedAsync());
    }

    [Fact]
    public async Task SavedRecovery_IsOfferedOnlyWhenNewerThanManualProject()
    {
        using var workspace = new TestWorkspace();
        string projectPath = Path.Combine(workspace.Root, "research.scicanvas");
        Guid projectId = Guid.NewGuid();
        SciCanvasProjectDocument document = CreateDocument(projectId, "版本 1");
        await new JsonProjectStore().SaveAsync(projectPath, document);
        var recoveryStore = new JsonProjectRecoveryStore(Path.Combine(workspace.Root, "recovery"));

        await recoveryStore.SaveAsync(
            projectId,
            projectPath,
            CreateDocument(projectId, "自动保存版本"));
        string recoveryPath = projectPath + ".autosave.scicanvas";
        DateTime now = DateTime.UtcNow;
        File.SetLastWriteTimeUtc(projectPath, now.AddMinutes(-2));
        File.SetLastWriteTimeUtc(recoveryPath, now);

        ProjectRecoveryCandidate candidate = Assert.IsType<ProjectRecoveryCandidate>(
            await recoveryStore.FindForProjectAsync(projectPath));
        Assert.Equal(projectPath, candidate.OriginalProjectPath);

        File.SetLastWriteTimeUtc(projectPath, now.AddMinutes(1));
        Assert.Null(await recoveryStore.FindForProjectAsync(projectPath));
    }

    private static SciCanvasProjectDocument CreateDocument(Guid projectId, string title) => new()
    {
        ProjectId = projectId,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        UpdatedAt = DateTimeOffset.UtcNow,
        Title = title,
        Canvas = new ProjectCanvasSnapshot
        {
            Width = 1200,
            Height = 900,
            Background = "white",
        },
        Sources = [],
        Layers = [],
        CropPresets = [],
        Guides = [],
        ExportProfiles = [],
        AuditTrail = [],
    };
}
