using System.IO;
using System.Windows.Media.Imaging;

namespace SciCanvas.Imaging;

public sealed class WpfImagePreviewLoader : IImagePreviewLoader
{
    public Task<BitmapSource> LoadAsync(
        string path,
        int maximumPixelWidth,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPixelWidth);
        cancellationToken.ThrowIfCancellationRequested();

        string fullPath = Path.GetFullPath(path);
        using FileStream source = new(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.SequentialScan
        });

        BitmapImage preview = new();
        preview.BeginInit();
        preview.CacheOption = BitmapCacheOption.OnLoad;
        preview.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        preview.DecodePixelWidth = maximumPixelWidth;
        preview.StreamSource = source;
        preview.EndInit();
        preview.Freeze();

        return Task.FromResult<BitmapSource>(preview);
    }
}
