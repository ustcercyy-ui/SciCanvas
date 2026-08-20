namespace SciCanvas.Presentation;

public interface IProjectFilePicker
{
    string? PickProjectToOpen();

    string? PickProjectToSave(string suggestedFileName, string? currentPath);
}
