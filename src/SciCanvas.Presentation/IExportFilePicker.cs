namespace SciCanvas.Presentation;

public interface IExportFilePicker
{
    string? PickNewExportPath(string suggestedFileName);

    string? PickNewFigureExportPath(string suggestedFileName) =>
        PickNewExportPath(suggestedFileName);
}
