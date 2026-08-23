using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfFigureExporterTests
{
    [Fact]
    public async Task ExportAsync_ComposesExactCanvasAndLeavesSourcesUnchanged()
    {
        using var workspace = new TestWorkspace();
        string firstPath = Path.Combine(workspace.Root, "first.png");
        string secondPath = Path.Combine(workspace.Root, "second.png");
        string targetPath = Path.Combine(workspace.Root, "figure.png");
        CreateSolidPng(firstPath, 8, 6, Colors.Red);
        CreateSolidPng(secondPath, 8, 6, Colors.Blue);
        byte[] firstHash = SHA256.HashData(await File.ReadAllBytesAsync(firstPath));
        byte[] secondHash = SHA256.HashData(await File.ReadAllBytesAsync(secondPath));

        FigureExportDocument document = new(
            120,
            80,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(firstPath, 8, 6),
                    new PixelRect64(1, 1, 6, 4),
                    new PixelRect64(5, 5, 50, 70),
                    "a",
                    true),
                new FigurePanelExportItem(
                    CreateAsset(secondPath, 8, 6),
                    new PixelRect64(1, 1, 6, 4),
                    new PixelRect64(65, 5, 50, 70),
                    "b",
                    true),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapFrame frame = LoadFirstFrame(targetPath);
        Assert.Equal(120, frame.PixelWidth);
        Assert.Equal(80, frame.PixelHeight);
        BitmapSource converted = new FormatConvertedBitmap(
            frame,
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        AssertPixelIsPredominantly(converted, 45, 45, Colors.Red);
        AssertPixelIsPredominantly(converted, 105, 45, Colors.Blue);
        Assert.Equal(firstHash, SHA256.HashData(await File.ReadAllBytesAsync(firstPath)));
        Assert.Equal(secondHash, SHA256.HashData(await File.ReadAllBytesAsync(secondPath)));
    }

    [Fact]
    public async Task ExportAsync_RefusesToOverwriteExistingFigure()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = workspace.CreateFile("existing.png", [1, 3, 5, 7]);
        CreateSolidPng(sourcePath, 4, 4, Colors.Green);
        FigureExportDocument document = new(
            40,
            40,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 4, 4),
                    new PixelRect64(0, 0, 4, 4),
                    new PixelRect64(0, 0, 40, 40),
                    "a",
                    true),
            ]);

        await Assert.ThrowsAsync<IOException>(
            () => new WpfFigureExporter().ExportAsync(document, targetPath));
        Assert.Equal(new byte[] { 1, 3, 5, 7 }, await File.ReadAllBytesAsync(targetPath));
    }

    [Fact]
    public async Task ExportAsync_RendersConfiguredCanvasBackground()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "background.png");
        FigureExportDocument document = new(
            32,
            24,
            300,
            [],
            [],
            "#FF123456");

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapSource converted = new FormatConvertedBitmap(
            LoadFirstFrame(targetPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        AssertPixelIsPredominantly(converted, 16, 12, Color.FromRgb(0x12, 0x34, 0x56));
    }

    [Fact]
    public async Task ExportAsync_RendersCalibratedScaleBarAtSourcePixelLength()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "black.png");
        string targetPath = Path.Combine(workspace.Root, "scale-bar.png");
        CreateSolidPng(sourcePath, 100, 50, Colors.Black);
        FigureExportDocument document = new(
            200,
            100,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 100, 50),
                    new PixelRect64(0, 0, 100, 50),
                    new PixelRect64(0, 0, 200, 100),
                    string.Empty,
                    true,
                    new FigureScaleBarExportSpec(1, 20, "µm", ShowLabel: false)),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapSource frame = new FormatConvertedBitmap(
            LoadFirstFrame(targetPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        const int sampleWidth = 60;
        const int sampleHeight = 20;
        byte[] pixels = new byte[sampleWidth * sampleHeight * 4];
        frame.CopyPixels(
            new System.Windows.Int32Rect(135, 75, sampleWidth, sampleHeight),
            pixels,
            sampleWidth * 4,
            0);
        int whitePixelCount = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 220 && pixels[index + 1] > 220 && pixels[index + 2] > 220)
            {
                whitePixelCount++;
            }
        }

        Assert.True(whitePixelCount > 80, $"比例尺区域仅检测到 {whitePixelCount} 个白色像素。");
    }

    [Fact]
    public async Task ExportAsync_RejectsScaleBarLongerThanEightyPercentOfSourceCrop()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = Path.Combine(workspace.Root, "invalid.png");
        CreateSolidPng(sourcePath, 10, 10, Colors.Black);
        FigureExportDocument document = new(
            100,
            100,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 10, 10),
                    new PixelRect64(0, 0, 10, 10),
                    new PixelRect64(0, 0, 100, 100),
                    "a",
                    true,
                    new FigureScaleBarExportSpec(1, 9, "µm", ShowLabel: true)),
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WpfFigureExporter().ExportAsync(document, targetPath));
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task ExportAsync_RendersArrowAnnotationInFinalPixelCoordinates()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "annotation.png");
        FigureExportDocument document = new(
            200,
            100,
            300,
            [],
            [
                new FigureAnnotationExportItem(
                    "arrow",
                    20,
                    50,
                    180,
                    50,
                    string.Empty,
                    "#FFFF0000",
                    7,
                    2,
                    IsBold: false,
                    IsVisible: true,
                    ZIndex: 0),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapSource converted = new FormatConvertedBitmap(
            LoadFirstFrame(targetPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        AssertPixelIsPredominantly(converted, 100, 50, Colors.Red);
    }

    [Fact]
    public async Task ExportAsync_RendersRectangleAndEllipseAsVectorLikeOutlines()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "shapes.png");
        FigureExportDocument document = new(
            220,
            120,
            300,
            [],
            [
                new FigureAnnotationExportItem(
                    "rectangle", 20, 20, 90, 100, string.Empty, "#FFFF0000",
                    7, 2, IsBold: false, IsVisible: true, ZIndex: 0),
                new FigureAnnotationExportItem(
                    "ellipse", 120, 20, 200, 100, string.Empty, "#FF1E88E5",
                    7, 2, IsBold: false, IsVisible: true, ZIndex: 1),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapSource converted = new FormatConvertedBitmap(
            LoadFirstFrame(targetPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        AssertPixelIsPredominantly(converted, 20, 60, Colors.Red);
        AssertPixelIsPredominantly(converted, 160, 20, Color.FromRgb(0x1E, 0x88, 0xE5));
        AssertPixelIsPredominantly(converted, 55, 60, Colors.White);
    }

    [Fact]
    public async Task ExportAsync_RejectsAnnotationOutsideCanvas()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "outside.png");
        FigureExportDocument document = new(
            100,
            100,
            300,
            [],
            [
                new FigureAnnotationExportItem(
                    "text",
                    -1,
                    20,
                    0,
                    0,
                    "invalid",
                    "#FF000000",
                    7,
                    1,
                    IsBold: false,
                    IsVisible: true,
                    ZIndex: 0),
            ]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WpfFigureExporter().ExportAsync(document, targetPath));
        Assert.False(File.Exists(targetPath));
    }

    [Fact]
    public async Task ExportAsync_WritesEditableSvgWithIndependentImageAndAnnotationObjects()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = Path.Combine(workspace.Root, "figure.svg");
        CreateSolidPng(sourcePath, 8, 6, Colors.Red);
        FigureExportDocument document = new(
            120,
            80,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 8, 6),
                    new PixelRect64(0, 0, 8, 6),
                    new PixelRect64(5, 5, 50, 70),
                    "a",
                    true),
            ],
            [
                new FigureAnnotationExportItem(
                    "rectangle", 20, 20, 80, 60, string.Empty, "#FFFF0000",
                    7, 2, IsBold: false, IsVisible: true, ZIndex: 0),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        string svg = await File.ReadAllTextAsync(targetPath);
        Assert.StartsWith("<?xml", svg, StringComparison.Ordinal);
        Assert.Contains("<image ", svg, StringComparison.Ordinal);
        Assert.Contains("<rect ", svg, StringComparison.Ordinal);
        Assert.Contains("data-source=\"source.png\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_WritesPdfWithIndependentImageXObjectAndVectorContent()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = Path.Combine(workspace.Root, "figure.pdf");
        CreateSolidPng(sourcePath, 8, 6, Colors.Blue);
        FigureExportDocument document = new(
            120,
            80,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 8, 6),
                    new PixelRect64(0, 0, 8, 6),
                    new PixelRect64(5, 5, 50, 70),
                    "a",
                    true),
            ],
            [
                new FigureAnnotationExportItem(
                    "arrow", 20, 40, 90, 40, string.Empty, "#FF00AA00",
                    7, 2, IsBold: false, IsVisible: true, ZIndex: 0),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        byte[] pdf = await File.ReadAllBytesAsync(targetPath);
        string header = Encoding.ASCII.GetString(pdf, 0, 8);
        string body = Encoding.ASCII.GetString(pdf);
        Assert.Equal("%PDF-1.7", header);
        Assert.Contains("/Subtype /Image", body, StringComparison.Ordinal);
        Assert.Contains("xref", body, StringComparison.Ordinal);
    }
    private static SourceAsset CreateAsset(string path, int width, int height) => new(
        Guid.NewGuid(),
        Path.GetFileName(path),
        path,
        new SourceFingerprint(
            new FileInfo(path).Length,
            File.GetLastWriteTimeUtc(path),
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
            windowsFileId: null),
        new CoreImageMetadata(new PixelSize64(width, height), 4, 8, "Bgra32"),
        SourceLinkState.Verified);

    private static void CreateSolidPng(string path, int width, int height, Color color)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = color.B;
            pixels[index + 1] = color.G;
            pixels[index + 2] = color.R;
            pixels[index + 3] = color.A;
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

    private static void AssertPixelIsPredominantly(BitmapSource source, int x, int y, Color color)
    {
        byte[] pixel = new byte[4];
        source.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), pixel, 4, 0);
        Assert.InRange(pixel[0], Math.Max(0, color.B - 10), Math.Min(255, color.B + 10));
        Assert.InRange(pixel[1], Math.Max(0, color.G - 10), Math.Min(255, color.G + 10));
        Assert.InRange(pixel[2], Math.Max(0, color.R - 10), Math.Min(255, color.R + 10));
    }
}
