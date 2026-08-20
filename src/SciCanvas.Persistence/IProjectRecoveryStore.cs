namespace SciCanvas.Persistence;

public sealed record ProjectRecoveryCandidate(
    string RecoveryPath,
    string? OriginalProjectPath,
    DateTimeOffset LastWriteTimeUtc);

public interface IProjectRecoveryStore
{
    Task SaveAsync(
        Guid projectId,
        string? originalProjectPath,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default);

    Task<ProjectRecoveryCandidate?> FindForProjectAsync(
        string originalProjectPath,
        CancellationToken cancellationToken = default);

    Task<ProjectRecoveryCandidate?> FindLatestUnsavedAsync(
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid projectId,
        string? originalProjectPath,
        CancellationToken cancellationToken = default);

    Task DeleteCandidateAsync(
        ProjectRecoveryCandidate candidate,
        CancellationToken cancellationToken = default);
}
