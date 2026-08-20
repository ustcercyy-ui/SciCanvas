using System.Windows.Media.Imaging;

namespace SciCanvas.Imaging;

public interface IImagePreviewLoader
{
    Task<BitmapSource> LoadAsync(
        string path,
        int maximumPixelWidth,
        CancellationToken cancellationToken = default);
}

