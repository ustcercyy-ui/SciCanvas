namespace SciCanvas.Presentation;

public interface ISourceRelinkFilePicker
{
    string? PickReplacement(
        string displayName,
        string originalPath,
        string expectedSha256);
}

internal sealed class NullSourceRelinkFilePicker : ISourceRelinkFilePicker
{
    public string? PickReplacement(
        string displayName,
        string originalPath,
        string expectedSha256) => null;
}
