using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Export;

public interface IImageCropExporter
{
    Task ExportAsync(
        string sourcePath,
        string targetPath,
        PixelRect64 crop,
        CancellationToken cancellationToken = default);
}
