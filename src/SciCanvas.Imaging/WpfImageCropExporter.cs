using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Imaging;

public sealed class WpfImageCropExporter : IImageCropExporter
{
    public Task ExportAsync(
        string sourcePath,
        string targetPath,
        PixelRect64 crop,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        return Task.Run(
            () => ExportCore(sourcePath, targetPath, crop, cancellationToken),
            cancellationToken);
    }

    private static void ExportCore(
        string sourcePath,
        string targetPath,
        PixelRect64 crop,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (crop.X > int.MaxValue || crop.Y > int.MaxValue ||
            crop.Width > int.MaxValue || crop.Height > int.MaxValue)
        {
            throw new NotSupportedException("当前 Windows 图像编码器不支持超过 32 位范围的单边裁剪坐标。");
        }

        BitmapFrame frame;
        using (var input = new FileStream(
                   sourcePath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 1024 * 1024,
                   useAsync: false))
        {
            BitmapDecoder decoder = BitmapDecoder.Create(
                input,
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            frame = decoder.Frames[0];
            frame.Freeze();
        }

        if (crop.Right > frame.PixelWidth || crop.Bottom > frame.PixelHeight)
        {
            throw new InvalidOperationException("裁剪区域超出当前源图像边界，导出已停止。");
        }

        var cropped = new CroppedBitmap(
            frame,
            new Int32Rect((int)crop.X, (int)crop.Y, (int)crop.Width, (int)crop.Height));
        cropped.Freeze();

        BitmapEncoder encoder = CreateEncoder(Path.GetExtension(targetPath));
        encoder.Frames.Add(BitmapFrame.Create(cropped));

        bool targetCreated = false;
        try
        {
            using var output = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: false);
            targetCreated = true;
            encoder.Save(output);
            output.Flush(flushToDisk: true);
        }
        catch
        {
            if (targetCreated)
            {
                TryDeleteIncompleteTarget(targetPath);
            }

            throw;
        }
    }

    private static BitmapEncoder CreateEncoder(string extension) => extension.ToLowerInvariant() switch
    {
        ".tif" or ".tiff" => new TiffBitmapEncoder
        {
            Compression = TiffCompressOption.Zip,
        },
        ".png" => new PngBitmapEncoder(),
        ".bmp" => new BmpBitmapEncoder(),
        ".jpg" or ".jpeg" => new JpegBitmapEncoder
        {
            QualityLevel = 95,
        },
        _ => throw new NotSupportedException("导出格式仅支持 TIFF、PNG、BMP 与 JPEG。"),
    };

    private static void TryDeleteIncompleteTarget(string targetPath)
    {
        try
        {
            File.Delete(targetPath);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
