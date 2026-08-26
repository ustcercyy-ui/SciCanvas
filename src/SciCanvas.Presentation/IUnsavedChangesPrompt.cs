namespace SciCanvas.Presentation;

public enum UnsavedChangesDecision
{
    Save,
    Discard,
    Cancel,
}

public interface IUnsavedChangesPrompt
{
    UnsavedChangesDecision ConfirmProjectReplacement(string actionLabel, string currentProjectDisplayName);
}

internal sealed class CancelUnsavedChangesPrompt : IUnsavedChangesPrompt
{
    public UnsavedChangesDecision ConfirmProjectReplacement(
        string actionLabel,
        string currentProjectDisplayName) => UnsavedChangesDecision.Cancel;
}
