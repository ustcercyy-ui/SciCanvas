using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Imaging;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfImageCropExporterTests
{
    [Fact]
    public async Task ExportAsync_WritesExactCropAndLeavesSourceUnchanged()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = Path.Combine(workspace.Root, "crop.png");
        CreateTestPng(sourcePath, width: 6, height: 4);
        byte[] sourceHashBefore = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));

        var exporter = new WpfImageCropExporter();
        await exporter.ExportAsync(sourcePath, targetPath, new PixelRect64(2, 1, 3, 2));

        BitmapFrame exported = LoadFirstFrame(targetPath);
        Assert.Equal(3, exported.PixelWidth);
        Assert.Equal(2, exported.PixelHeight);
        Assert.Equal(sourceHashBefore, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task ExportAsync_RefusesToOverwriteExistingTarget()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = workspace.CreateFile("existing.png", [9, 8, 7, 6]);
        CreateTestPng(sourcePath, width: 4, height: 3);

        var exporter = new WpfImageCropExporter();

        await Assert.ThrowsAsync<IOException>(() => exporter.ExportAsync(
            sourcePath,
            targetPath,
            new PixelRect64(0, 0, 2, 2)));
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, await File.ReadAllBytesAsync(targetPath));
    }

    private static void CreateTestPng(string path, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = (byte)(index % 251);
            pixels[index + 1] = 90;
            pixels[index + 2] = 180;
            pixels[index + 3] = 255;
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapFrame LoadFirstFrame(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapDecoder decoder = BitmapDecoder.Create(
            input,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }
}
