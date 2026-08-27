using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;
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
                new FigureAnnotationExportItem(
                    "line", 10, 70, 100, 70, string.Empty, "#FF00AA88",
                    7, 1.5, IsBold: false, IsVisible: true, ZIndex: 1),
            ],
            globalStyle: new FigureGlobalStyle(
                "Segoe UI", 8, 1.5, "#FF223344", "#FF00AA88", "#FFFFFFFF"));

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        string svg = await File.ReadAllTextAsync(targetPath);
        Assert.StartsWith("<?xml", svg, StringComparison.Ordinal);
        Assert.Contains("<image ", svg, StringComparison.Ordinal);
        Assert.Contains("<rect ", svg, StringComparison.Ordinal);
        Assert.Contains("<line ", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Segoe UI\"", svg, StringComparison.Ordinal);
        Assert.Contains("#223344", svg, StringComparison.Ordinal);
        Assert.Contains("data-source=\"source.png\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_SvgUsesResolvedLocalTypographyAndIndependentShapeFill()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = Path.Combine(workspace.Root, "styled.svg");
        CreateSolidPng(sourcePath, 20, 20, Colors.Black);
        FigureExportDocument document = new(
            240,
            160,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 20, 20),
                    new PixelRect64(0, 0, 20, 20),
                    new PixelRect64(0, 0, 160, 160),
                    "a",
                    true,
                    new FigureScaleBarExportSpec(1, 5, "µm", ShowLabel: true)),
            ],
            [
                new FigureAnnotationExportItem(
                    "text", 165, 20, 0, 0, "local", "#FF000000", "#FF000000", 0,
                    "#FF663399", "Consolas", 12, 1, true, true, 0),
                new FigureAnnotationExportItem(
                    "rectangle", 165, 60, 230, 140, string.Empty, "#FFFF0000", "#FF00FFFF", 24,
                    "#FF000000", "Arial", 7, 2, false, true, 1),
            ],
            globalStyle: new FigureGlobalStyle(
                "GlobalFont", 7, 1.25, "#FF111111", "#FFE53935", "#FFFFFFFF",
                PanelLabelFontFamily: "Segoe UI",
                PanelLabelFontSizePt: 9,
                PanelLabelTextColor: "#FF112233",
                PanelLabelIsBold: false,
                ScaleBarLabelColor: "#FFFFFF00",
                ScaleBarFontFamily: "Arial",
                ScaleBarFontSizePt: 8,
                ScaleBarLabelIsBold: false,
                ScaleBarThicknessPt: 2));

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        string svg = await File.ReadAllTextAsync(targetPath);
        Assert.Contains("font-family=\"Consolas\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#663399\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#FF0000\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#00FFFF\" fill-opacity=\"0.24\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Segoe UI\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#112233\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Arial\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#FFFF00\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_SvgResolvesPanelLocalLabelAndScaleBarStyles()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "panel-style.png");
        string targetPath = Path.Combine(workspace.Root, "panel-style.svg");
        CreateSolidPng(sourcePath, 20, 20, Colors.Black);
        StyleOverride local = new(
            PanelLabel: new TextStyle("Courier New", 11, false, "#FF123456"),
            ScaleBarText: new TextStyle("Times New Roman", 10, true, "#FFABCDEF"),
            ScaleBar: new ScaleBarStyle(ScaleBarAnchor.BottomRight, 3, "#FF00FF00"));
        FigureExportDocument document = new(
            200,
            160,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 20, 20),
                    new PixelRect64(0, 0, 20, 20),
                    new PixelRect64(0, 0, 160, 160),
                    "a",
                    true,
                    new FigureScaleBarExportSpec(1, 5, "µm", ShowLabel: true),
                    StyleOverride: local),
            ],
            globalStyle: new FigureGlobalStyle(
                "Arial", 7, 1.25, "#FF111111", "#FFE53935", "#FFFFFFFF"));

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        string svg = await File.ReadAllTextAsync(targetPath);
        Assert.Contains("font-family=\"Courier New\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#123456\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Times New Roman\"", svg, StringComparison.Ordinal);
        Assert.Contains("fill=\"#ABCDEF\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#00FF00\" stroke-width=\"12.5\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExportAsync_SvgRendersMultipleUnitConvertedScaleBars()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "multi-scale.png");
        string targetPath = Path.Combine(workspace.Root, "multi-scale.svg");
        CreateSolidPng(sourcePath, 100, 60, Colors.Black);
        FigureScaleBarExportSpec primary = new(0.01, 0.5, "µm", true, "µm", ScaleBarAnchor.BottomRight, Guid.NewGuid());
        FigureScaleBarExportSpec secondary = new(0.01, 250, "nm", true, "µm", ScaleBarAnchor.BottomRight, Guid.NewGuid());
        FigureExportDocument document = new(
            300,
            180,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 100, 60),
                    new PixelRect64(0, 0, 100, 60),
                    new PixelRect64(0, 0, 240, 144),
                    "a",
                    true,
                    ScaleBar: primary,
                    ScaleBars: [primary, secondary]),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        string svg = await File.ReadAllTextAsync(targetPath);
        Assert.Contains(">0.5 µm</text>", svg, StringComparison.Ordinal);
        Assert.Contains(">250 nm</text>", svg, StringComparison.Ordinal);
        Assert.True(svg.Split("stroke-linecap=\"square\"", StringSplitOptions.None).Length - 1 >= 4);
    }
    [Fact]
    public async Task ExportAsync_PngCompositesIndependentRectangleFillOpacity()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "filled.png");
        FigureExportDocument document = new(
            100,
            100,
            300,
            [],
            [
                new FigureAnnotationExportItem(
                    "rectangle", 10, 10, 90, 90, string.Empty, "#FFFF0000", "#FF00FFFF", 25,
                    "#FF000000", "Arial", 7, 1, false, true, 0),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        BitmapSource frame = new FormatConvertedBitmap(
            LoadFirstFrame(targetPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        byte[] pixel = new byte[4];
        frame.CopyPixels(new System.Windows.Int32Rect(50, 50, 1, 1), pixel, 4, 0);
        Assert.InRange(pixel[0], 250, 255);
        Assert.InRange(pixel[1], 250, 255);
        Assert.InRange(pixel[2], 185, 195);
    }

    [Fact]
    public async Task ExportAsync_PdfFallsBackForMissingLocalFontWithoutCrashing()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "missing-font.pdf");
        FigureExportDocument document = new(
            160,
            80,
            300,
            [],
            [
                new FigureAnnotationExportItem(
                    "text", 10, 10, 0, 0, "fallback", "#FF000000", "#FF000000", 0,
                    "#FF123456", "MissingFont123", 11, 1, false, true, 0),
            ]);

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        Assert.True(File.Exists(targetPath));
        Assert.Equal("%PDF-1.7", Encoding.ASCII.GetString(await File.ReadAllBytesAsync(targetPath), 0, 8));
    }

    [Fact]
    public async Task ExportAsync_WritesVectorBorderForInsetPanel()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string targetPath = Path.Combine(workspace.Root, "inset.svg");
        CreateSolidPng(sourcePath, 8, 6, Colors.Red);
        FigureExportDocument document = new(
            120,
            80,
            300,
            [
                new FigurePanelExportItem(
                    CreateAsset(sourcePath, 8, 6),
                    new PixelRect64(0, 0, 8, 6),
                    new PixelRect64(10, 10, 80, 60),
                    string.Empty,
                    true,
                    IsInset: true),
            ],
            globalStyle: new FigureGlobalStyle(
                "Arial", 7, 1, "#FF000000", "#FF123456", "#FFFFFFFF"));

        await new WpfFigureExporter().ExportAsync(document, targetPath);

        string svg = await File.ReadAllTextAsync(targetPath);
        Assert.Equal(2, svg.Split("<rect ", StringSplitOptions.None).Length - 1);
        Assert.Contains("stroke=\"#123456\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke-width=\"2.083\"", svg, StringComparison.Ordinal);
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
    [Fact]
    public async Task ExportAsync_RendersScientificMeasurementOverlayAcrossRasterAndVectorFormats()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string pngPath = Path.Combine(workspace.Root, "overlay.png");
        string svgPath = Path.Combine(workspace.Root, "overlay.svg");
        string pdfPath = Path.Combine(workspace.Root, "overlay.pdf");
        string tiffPath = Path.Combine(workspace.Root, "overlay-16.tif");
        CreateSolidPng(sourcePath, 100, 100, Colors.Black);
        SourceAsset source = CreateAsset(sourcePath, 100, 100);
        Guid panelId = Guid.NewGuid();
        Guid measurementId = Guid.NewGuid();
        var scientificObject = new MeasurementOverlayObject
        {
            Id = Guid.NewGuid(),
            AssetId = source.Id,
            PanelId = panelId,
            SourceRevision = 1,
            MeasurementId = measurementId,
            SourceGeometry = new ScientificMeasurement(
                measurementId,
                source.Id,
                ScientificMeasurementKind.Length,
                new MeasurementPoint(10, 50),
                new MeasurementPoint(90, 50),
                SourceRevision: 1),
            Style = new FigureMeasurementOverlayStyle(
                "#FFFFFF00",
                2,
                "solid",
                "#FFFFFF00",
                0,
                "#FF000000",
                "#FFFFFFFF",
                6,
                true,
                "#FFFFFFFF",
                "Times New Roman",
                9,
                true,
                true),
        };
        var panel = new FigurePanelExportItem(
            source,
            new PixelRect64(0, 0, 100, 100),
            new PixelRect64(0, 0, 200, 200),
            "a",
            true,
            PanelId: panelId);
        var document = new FigureExportDocument(
            200,
            200,
            96,
            [panel],
            measurementOverlays: [new FigureMeasurementOverlayExportItem(scientificObject)]);
        var exporter = new WpfFigureExporter();

        await exporter.ExportAsync(document, pngPath);
        await exporter.ExportAsync(document, svgPath);
        await exporter.ExportAsync(document, pdfPath);
        await exporter.ExportAsync(
            new FigureExportDocument(
                200,
                200,
                96,
                [panel],
                bitDepth: 16,
                measurementOverlays: [new FigureMeasurementOverlayExportItem(scientificObject)]),
            tiffPath);

        BitmapSource png = new FormatConvertedBitmap(
            LoadFirstFrame(pngPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        AssertPixelIsPredominantly(png, 100, 100, Colors.Yellow);
        string svg = await File.ReadAllTextAsync(svgPath);
        Assert.Contains("data-scientific-object=\"measurement-overlay\"", svg, StringComparison.Ordinal);
        Assert.Contains(measurementId.ToString("D"), svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("font-family=\"Times New Roman\"", svg, StringComparison.Ordinal);
        Assert.Equal("%PDF-1.7", Encoding.ASCII.GetString(await File.ReadAllBytesAsync(pdfPath), 0, 8));
        Assert.True(File.Exists(tiffPath));
    }
    [Fact]
    public async Task ExportAsync_RendersCanonicalScientificObjectsAcrossRasterAndVectorFormats()
    {
        using var workspace = new TestWorkspace();
        string pngPath = Path.Combine(workspace.Root, "scientific-objects.png");
        string svgPath = Path.Combine(workspace.Root, "scientific-objects.svg");
        string pdfPath = Path.Combine(workspace.Root, "scientific-objects.pdf");
        string tiffPath = Path.Combine(workspace.Root, "scientific-objects-16.tif");
        FigureScientificObjectExportItem[] objects =
        [
            new(
                Guid.NewGuid(),
                FigureScientificObjectKind.PolygonAnnotation,
                [new FigureScientificPoint(20, 20), new FigureScientificPoint(80, 20), new FigureScientificPoint(60, 70)],
                "Membrane", "#FF00B0FF", "#FF00B0FF", 16, "#FFFFFFFF", "Arial", 7, 1.25, true, true, 0),
            new(
                Guid.NewGuid(),
                FigureScientificObjectKind.Roi,
                [new FigureScientificPoint(100, 20), new FigureScientificPoint(150, 20), new FigureScientificPoint(150, 70), new FigureScientificPoint(100, 70)],
                "ROI", "#FF00E5FF", "#FF00E5FF", 12, "#FFFFFFFF", "Arial", 7, 1.25, true, true, 1),
            new(
                Guid.NewGuid(),
                FigureScientificObjectKind.DirectionMarker,
                [new FigureScientificPoint(20, 120), new FigureScientificPoint(120, 120)],
                "N", "#FFFFFF00", "#FFFFFF00", 0, "#FFFFFFFF", "Arial", 7, 1.25, true, true, 2),
            new(
                Guid.NewGuid(),
                FigureScientificObjectKind.Colorbar,
                [new FigureScientificPoint(185, 20), new FigureScientificPoint(215, 120)],
                "Intensity", "#FFFFFFFF", "#FFFFFFFF", 0, "#FFFFFFFF", "Arial", 7, 1.25, false, true, 3,
                Minimum: 0, Maximum: 4095, Unit: "a.u.", Colormap: "magma"),
            new(
                Guid.NewGuid(),
                FigureScientificObjectKind.ChannelLegend,
                [new FigureScientificPoint(110, 90), new FigureScientificPoint(175, 145)],
                "Channels", "#FFFFFFFF", "#FF000000", 80, "#FFFFFFFF", "Arial", 7, 1.25, false, true, 4,
                ChannelLegendEntries:
                [
                    new FigureChannelLegendEntry("DAPI", "#FF4FC3F7"),
                    new FigureChannelLegendEntry("GFP", "#FF66BB6A"),
                ]),
        ];
        var document = new FigureExportDocument(240, 160, 96, [], scientificObjects: objects);
        var exporter = new WpfFigureExporter();

        await exporter.ExportAsync(document, pngPath);
        await exporter.ExportAsync(document, svgPath);
        await exporter.ExportAsync(document, pdfPath);
        await exporter.ExportAsync(
            new FigureExportDocument(240, 160, 96, [], bitDepth: 16, scientificObjects: objects),
            tiffPath);

        BitmapSource png = new FormatConvertedBitmap(
            LoadFirstFrame(pngPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        byte[] directionPixel = new byte[4];
        png.CopyPixels(new System.Windows.Int32Rect(70, 120, 1, 1), directionPixel, 4, 0);
        Assert.True(directionPixel[1] > 220 && directionPixel[2] > 220 && directionPixel[0] < 80,
            $"方向标记像素不呈现黄色主导：B={directionPixel[0]}, G={directionPixel[1]}, R={directionPixel[2]}。");
        string svg = await File.ReadAllTextAsync(svgPath);
        Assert.Contains("data-scientific-object=\"PolygonAnnotation\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-scientific-object=\"Roi\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-scientific-object=\"DirectionMarker\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-scientific-object=\"Colorbar\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-scientific-object=\"ChannelLegend\"", svg, StringComparison.Ordinal);
        Assert.Contains("scientific-colorbar-", svg, StringComparison.Ordinal);
        Assert.Contains(">DAPI</text>", svg, StringComparison.Ordinal);
        byte[] pdf = await File.ReadAllBytesAsync(pdfPath);
        Assert.Equal("%PDF-1.7", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.Contains("xref", Encoding.ASCII.GetString(pdf), StringComparison.Ordinal);
        BitmapSource tiff = new FormatConvertedBitmap(
            LoadFirstFrame(tiffPath),
            PixelFormats.Bgra32,
            destinationPalette: null,
            alphaThreshold: 0);
        byte[] tiffDirectionPixel = new byte[4];
        tiff.CopyPixels(new System.Windows.Int32Rect(70, 120, 1, 1), tiffDirectionPixel, 4, 0);
        Assert.True(tiffDirectionPixel[1] > 220 && tiffDirectionPixel[2] > 220 && tiffDirectionPixel[0] < 80,
            $"16-bit TIFF 方向标记没有保留黄色主导：B={tiffDirectionPixel[0]}, G={tiffDirectionPixel[1]}, R={tiffDirectionPixel[2]}。");
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
