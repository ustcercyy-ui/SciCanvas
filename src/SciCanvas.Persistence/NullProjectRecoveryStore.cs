namespace SciCanvas.Persistence;

public sealed class NullProjectRecoveryStore : IProjectRecoveryStore
{
    public Task SaveAsync(
        Guid projectId,
        string? originalProjectPath,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<ProjectRecoveryCandidate?> FindForProjectAsync(
        string originalProjectPath,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProjectRecoveryCandidate?>(null);

    public Task<ProjectRecoveryCandidate?> FindLatestUnsavedAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ProjectRecoveryCandidate?>(null);

    public Task DeleteAsync(
        Guid projectId,
        string? originalProjectPath,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task DeleteCandidateAsync(
        ProjectRecoveryCandidate candidate,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
