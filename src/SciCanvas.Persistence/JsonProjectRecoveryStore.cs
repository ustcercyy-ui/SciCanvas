namespace SciCanvas.Persistence;

public sealed class JsonProjectRecoveryStore : IProjectRecoveryStore
{
    private const string RecoverySuffix = ".autosave.scicanvas";
    private readonly string _recoveryDirectory;
    private readonly IProjectStore _projectStore;

    public JsonProjectRecoveryStore(
        string? recoveryDirectory = null,
        IProjectStore? projectStore = null)
    {
        _recoveryDirectory = Path.GetFullPath(
            recoveryDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SciCanvas",
                "Recovery"));
        _projectStore = projectStore ?? new JsonProjectStore();
    }

    public async Task SaveAsync(
        Guid projectId,
        string? originalProjectPath,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(projectId, Guid.Empty);
        ArgumentNullException.ThrowIfNull(document);

        string recoveryPath = GetRecoveryPath(projectId, originalProjectPath);
        string? directory = Path.GetDirectoryName(recoveryPath);
        if (directory is null)
        {
            throw new InvalidOperationException("自动保存路径缺少父目录。");
        }

        Directory.CreateDirectory(directory);
        await _projectStore.SaveAsync(recoveryPath, document, cancellationToken);
    }

    public Task<ProjectRecoveryCandidate?> FindForProjectAsync(
        string originalProjectPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(originalProjectPath);

        string originalFullPath = Path.GetFullPath(originalProjectPath);
        string recoveryPath = originalFullPath + RecoverySuffix;
        if (!File.Exists(recoveryPath))
        {
            return Task.FromResult<ProjectRecoveryCandidate?>(null);
        }

        DateTime recoveryWriteTime = File.GetLastWriteTimeUtc(recoveryPath);
        if (File.Exists(originalFullPath) &&
            recoveryWriteTime <= File.GetLastWriteTimeUtc(originalFullPath))
        {
            return Task.FromResult<ProjectRecoveryCandidate?>(null);
        }

        return Task.FromResult<ProjectRecoveryCandidate?>(new ProjectRecoveryCandidate(
            recoveryPath,
            originalFullPath,
            new DateTimeOffset(recoveryWriteTime, TimeSpan.Zero)));
    }

    public Task<ProjectRecoveryCandidate?> FindLatestUnsavedAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Directory.Exists(_recoveryDirectory))
        {
            return Task.FromResult<ProjectRecoveryCandidate?>(null);
        }

        FileInfo? latest = new DirectoryInfo(_recoveryDirectory)
            .EnumerateFiles($"*{RecoverySuffix}", SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return Task.FromResult<ProjectRecoveryCandidate?>(latest is null
            ? null
            : new ProjectRecoveryCandidate(
                latest.FullName,
                OriginalProjectPath: null,
                new DateTimeOffset(latest.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    public Task DeleteAsync(
        Guid projectId,
        string? originalProjectPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TryDeleteRecoveryFiles(GetRecoveryPath(projectId, originalProjectPath));
        return Task.CompletedTask;
    }

    public Task DeleteCandidateAsync(
        ProjectRecoveryCandidate candidate,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(candidate);
        TryDeleteRecoveryFiles(Path.GetFullPath(candidate.RecoveryPath));
        return Task.CompletedTask;
    }

    internal string GetRecoveryPath(Guid projectId, string? originalProjectPath) =>
        string.IsNullOrWhiteSpace(originalProjectPath)
            ? Path.Combine(_recoveryDirectory, $"{projectId:N}{RecoverySuffix}")
            : Path.GetFullPath(originalProjectPath) + RecoverySuffix;

    private static void TryDeleteRecoveryFiles(string recoveryPath)
    {
        TryDeleteFile(recoveryPath);
        TryDeleteFile(recoveryPath + ".bak");
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
