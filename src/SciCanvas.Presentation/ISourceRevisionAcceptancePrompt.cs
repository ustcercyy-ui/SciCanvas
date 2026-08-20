using SciCanvas.Core.Sources;

namespace SciCanvas.Presentation;

public sealed record SourceRevisionAcceptanceRequest(
    string DisplayName,
    string Path,
    SourceFingerprint PreviousFingerprint,
    SourceFingerprint ProposedFingerprint,
    long PreviousWidth,
    long PreviousHeight,
    long ProposedWidth,
    long ProposedHeight);

public interface ISourceRevisionAcceptancePrompt
{
    bool ConfirmAcceptance(SourceRevisionAcceptanceRequest request);
}

internal sealed class DeclineSourceRevisionAcceptancePrompt : ISourceRevisionAcceptancePrompt
{
    public bool ConfirmAcceptance(SourceRevisionAcceptanceRequest request) => false;
}
