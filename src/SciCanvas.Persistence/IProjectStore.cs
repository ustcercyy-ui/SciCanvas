namespace SciCanvas.Persistence;

public interface IProjectStore
{
    Task<SciCanvasProjectDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string path,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default);
}
