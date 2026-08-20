namespace SciCanvas.Presentation;

public interface IExportFilePicker
{
    string? PickNewExportPath(string suggestedFileName);
}
