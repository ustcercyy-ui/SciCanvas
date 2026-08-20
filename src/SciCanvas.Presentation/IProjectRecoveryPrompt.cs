using SciCanvas.Persistence;

namespace SciCanvas.Presentation;

public interface IProjectRecoveryPrompt
{
    bool ShouldRestore(ProjectRecoveryCandidate candidate);
}

internal sealed class DeclineProjectRecoveryPrompt : IProjectRecoveryPrompt
{
    public bool ShouldRestore(ProjectRecoveryCandidate candidate) => false;
}
