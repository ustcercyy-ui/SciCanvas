namespace SciCanvas.Core.Export;

public interface IFigureExporter
{
    Task ExportAsync(
        FigureExportDocument document,
        string targetPath,
        CancellationToken cancellationToken = default);
}
