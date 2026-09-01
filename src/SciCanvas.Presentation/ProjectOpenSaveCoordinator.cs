using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using SciCanvas.Persistence;

namespace SciCanvas.Presentation;

public sealed record ProjectSaveExecutionRequest(
    string RequestedPath,
    IReadOnlyCollection<SourceAsset> ProtectedSources,
    SciCanvasProjectDocument Document,
    Guid ProjectId,
    string? PreviousProjectPath);

public sealed record ProjectSaveExecutionResult(
    string ProjectPath,
    SciCanvasProjectDocument Document);

public sealed record ProjectSourceResolutionResult(
    IReadOnlyList<SourceAssetItemViewModel> Sources,
    IReadOnlyDictionary<Guid, SourceAssetItemViewModel> SourceMap,
    int RelinkedSourceCount);

/// <summary>
/// Owns durable project I/O, recovery copies, safe save paths and immutable
/// source fingerprint resolution. Applying a loaded document to UI state stays
/// in the view-model.
/// </summary>
public sealed class ProjectOpenSaveCoordinator
{
    private readonly IProjectStore _projectStore;
    private readonly IProjectRecoveryStore _recoveryStore;
    private readonly IProjectRecoveryPrompt _recoveryPrompt;
    private readonly IPathSafetyPolicy _pathSafetyPolicy;
    private readonly ISourceAssetReader _sourceReader;
    private readonly IImagePreviewLoader _previewLoader;
    private readonly ISourceRelinkFilePicker _sourceRelinkFilePicker;

    public ProjectOpenSaveCoordinator(
        IProjectStore projectStore,
        IProjectRecoveryStore recoveryStore,
        IProjectRecoveryPrompt recoveryPrompt,
        IPathSafetyPolicy pathSafetyPolicy,
        ISourceAssetReader sourceReader,
        IImagePreviewLoader previewLoader,
        ISourceRelinkFilePicker sourceRelinkFilePicker)
    {
        _projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
        _recoveryStore = recoveryStore ?? throw new ArgumentNullException(nameof(recoveryStore));
        _recoveryPrompt = recoveryPrompt ?? throw new ArgumentNullException(nameof(recoveryPrompt));
        _pathSafetyPolicy = pathSafetyPolicy ?? throw new ArgumentNullException(nameof(pathSafetyPolicy));
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        _previewLoader = previewLoader ?? throw new ArgumentNullException(nameof(previewLoader));
        _sourceRelinkFilePicker = sourceRelinkFilePicker ??
            throw new ArgumentNullException(nameof(sourceRelinkFilePicker));
    }

    public async Task<ProjectSaveExecutionResult> SaveAsync(
        ProjectSaveExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
            request.RequestedPath,
            request.ProtectedSources,
            cancellationToken);
        if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
        {
            throw new InvalidOperationException(decision.Message);
        }

        string normalizedPath = Path.ChangeExtension(
            decision.NormalizedTargetPath,
            ".scicanvas");
        await _projectStore.SaveAsync(normalizedPath, request.Document, cancellationToken);
        await _recoveryStore.DeleteAsync(
            request.ProjectId,
            request.PreviousProjectPath,
            cancellationToken);
        if (!string.Equals(
                request.PreviousProjectPath,
                normalizedPath,
                StringComparison.OrdinalIgnoreCase))
        {
            await _recoveryStore.DeleteAsync(request.ProjectId, normalizedPath, cancellationToken);
        }

        return new ProjectSaveExecutionResult(normalizedPath, request.Document);
    }

    public Task<SciCanvasProjectDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default) =>
        _projectStore.LoadAsync(path, cancellationToken);

    public Task<ProjectRecoveryCandidate?> FindForProjectAsync(
        string projectPath,
        CancellationToken cancellationToken = default) =>
        _recoveryStore.FindForProjectAsync(projectPath, cancellationToken);

    public Task<ProjectRecoveryCandidate?> FindLatestUnsavedAsync(
        CancellationToken cancellationToken = default) =>
        _recoveryStore.FindLatestUnsavedAsync(cancellationToken);

    public bool ShouldRestore(ProjectRecoveryCandidate candidate) =>
        _recoveryPrompt.ShouldRestore(candidate);

    public Task DeleteCandidateAsync(
        ProjectRecoveryCandidate candidate,
        CancellationToken cancellationToken = default) =>
        _recoveryStore.DeleteCandidateAsync(candidate, cancellationToken);

    public Task SaveRecoveryAsync(
        Guid projectId,
        string? projectPath,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default) =>
        _recoveryStore.SaveAsync(projectId, projectPath, document, cancellationToken);

    public Task DeleteRecoveryAsync(
        Guid projectId,
        string? projectPath,
        CancellationToken cancellationToken = default) =>
        _recoveryStore.DeleteAsync(projectId, projectPath, cancellationToken);

    public async Task<ProjectSourceResolutionResult> ResolveSourcesAsync(
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        List<SourceAssetItemViewModel> restoredSources = [];
        Dictionary<Guid, SourceAssetItemViewModel> sourceMap = [];
        List<string> errors = [];
        int relinkedSourceCount = 0;
        foreach (ProjectSourceSnapshot snapshot in document.Sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                (SourceAssetItemViewModel item, bool relinked) =
                    await ResolveSourceAsync(snapshot, cancellationToken);
                item.RestoreSourceRevision(snapshot.SourceRevision);
                restoredSources.Add(item);
                sourceMap.Add(snapshot.Id, item);
                if (relinked)
                {
                    relinkedSourceCount++;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                errors.Add($"{snapshot.DisplayName}：{exception.Message}");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidDataException(
                "工程未打开，因为以下源文件未通过验证：" +
                Environment.NewLine +
                string.Join(Environment.NewLine, errors));
        }

        return new ProjectSourceResolutionResult(
            restoredSources,
            sourceMap,
            relinkedSourceCount);
    }

    private async Task<(SourceAssetItemViewModel Item, bool Relinked)> ResolveSourceAsync(
        ProjectSourceSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        string originalFailure;
        try
        {
            SourceAsset original = await _sourceReader.ImportAsync(
                snapshot.OriginalPath,
                cancellationToken);
            if (string.Equals(
                    original.Fingerprint.Sha256,
                    snapshot.Fingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                var preview = await _previewLoader.LoadAsync(
                    snapshot.OriginalPath,
                    1400,
                    cancellationToken);
                SourceAsset restored = original with
                {
                    Id = snapshot.Id,
                    DisplayName = snapshot.DisplayName,
                    OriginalPath = Path.GetFullPath(snapshot.OriginalPath),
                    LinkState = SourceLinkState.Verified,
                };
                return (new SourceAssetItemViewModel(restored, preview), false);
            }

            originalFailure = "原路径文件内容与保存工程时不同";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            originalFailure = exception.Message;
        }

        string? replacementPath = _sourceRelinkFilePicker.PickReplacement(
            snapshot.DisplayName,
            snapshot.OriginalPath,
            snapshot.Fingerprint.Sha256);
        if (replacementPath is null)
        {
            throw new InvalidDataException($"{originalFailure}；未选择重新链接文件。");
        }

        SourceAsset replacement = await _sourceReader.ImportAsync(
            replacementPath,
            cancellationToken);
        if (!string.Equals(
                replacement.Fingerprint.Sha256,
                snapshot.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"所选替代文件 SHA-256 不匹配；需要 {snapshot.Fingerprint.Sha256[..12]}，" +
                $"实际为 {replacement.Fingerprint.Sha256[..12]}。");
        }

        var replacementPreview = await _previewLoader.LoadAsync(
            replacementPath,
            1400,
            cancellationToken);
        SourceAsset relinked = replacement with
        {
            Id = snapshot.Id,
            DisplayName = snapshot.DisplayName,
            OriginalPath = Path.GetFullPath(replacementPath),
            LinkState = SourceLinkState.Relocated,
        };
        return (new SourceAssetItemViewModel(relinked, replacementPreview), true);
    }
}
