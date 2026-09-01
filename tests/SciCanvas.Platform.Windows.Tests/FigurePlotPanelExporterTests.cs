using System.Globalization;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class FigurePlotPanelExporterTests
{
    [Theory]
    [InlineData(PlotKind.Line)]
    [InlineData(PlotKind.Scatter)]
    [InlineData(PlotKind.LineAndSymbol)]
    [InlineData(PlotKind.ErrorBar)]
    [InlineData(PlotKind.Histogram)]
    [InlineData(PlotKind.BoxPlot)]
    [InlineData(PlotKind.Heatmap)]
    public async Task ExportSvg_AllPlotKindsRemainNativeVector(PlotKind kind)
    {
        using var workspace = new TestWorkspace();
        (TabularDataAsset asset, PlotObject plot) = CreatePlot(kind);
        FigurePlotPanelExportItem panel = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(10, 10, 620, 440),
            "a",
            typographyOverride: FigurePlotTypographyOverride.FromPlot(plot.Typography));
        var document = new FigureExportDocument(
            640,
            460,
            96,
            [],
            plotPanels: [panel]);
        string path = Path.Combine(workspace.Root, $"{kind}.svg");

        await new WpfFigureExporter().ExportAsync(document, path);

        string svg = await File.ReadAllTextAsync(path);
        Assert.Contains("data-plot-vector=\"true\"", svg, StringComparison.Ordinal);
        Assert.Contains($"data-plot-kind=\"{kind}\"", svg, StringComparison.Ordinal);
        Assert.Contains($"data-data-asset-id=\"{asset.Id:D}\"", svg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains($"data-source-revision=\"{asset.SourceRevision}\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("<image ", svg, StringComparison.Ordinal);
        Assert.Contains("<line ", svg, StringComparison.Ordinal);
        Assert.Contains("<text ", svg, StringComparison.Ordinal);
        if (kind is PlotKind.Scatter or PlotKind.LineAndSymbol or PlotKind.ErrorBar)
        {
            Assert.Contains("<ellipse ", svg, StringComparison.Ordinal);
        }
        if (kind is PlotKind.Histogram or PlotKind.BoxPlot or PlotKind.Heatmap)
        {
            Assert.True(Count(svg, "<rect ") > 1, "Plot geometry should add vector rectangles beyond the canvas background.");
        }
    }

    [Fact]
    public async Task ExportPlotPanel_RendersRasterTiffAndPdfWithoutPdfImageXObject()
    {
        using var workspace = new TestWorkspace();
        (TabularDataAsset asset, PlotObject plot) = CreatePlot(PlotKind.LineAndSymbol);
        plot = plot with
        {
            Typography = new PlotTypography(
                new TextStyle("Arial", 9, true, "#FF111111"),
                new TextStyle("Arial", 8, false, "#FF111111"),
                new TextStyle("Arial", 8, false, "#FF111111"),
                new TextStyle("Arial", 7, false, "#FF111111")),
        };
        FigurePlotPanelExportItem panel = FigurePlotPanelExportItem.Create(
            plot,
            asset,
            new PixelRect64(10, 10, 620, 440),
            "a",
            typographyOverride: FigurePlotTypographyOverride.FromPlot(plot.Typography));
        var document = new FigureExportDocument(
            640,
            460,
            96,
            [],
            pdfFontStrategy: PdfFontStrategy.EmbedSubsetWhenPermitted,
            plotPanels: [panel]);
        var exporter = new WpfFigureExporter();
        string png = Path.Combine(workspace.Root, "plot.png");
        string tiff = Path.Combine(workspace.Root, "plot.tif");
        string tiff16 = Path.Combine(workspace.Root, "plot-16.tif");
        string svg = Path.Combine(workspace.Root, "plot.svg");
        string pdf = Path.Combine(workspace.Root, "plot.pdf");

        await exporter.ExportAsync(document, png);
        await exporter.ExportAsync(document, tiff);
        await exporter.ExportAsync(
            new FigureExportDocument(
                640,
                460,
                96,
                [],
                bitDepth: 16,
                plotPanels: [panel]),
            tiff16);
        await exporter.ExportAsync(document, svg);
        await exporter.ExportAsync(document, pdf);

        Assert.Equal(32, LoadFirstFrame(png).Format.BitsPerPixel);
        Assert.Equal(32, LoadFirstFrame(tiff).Format.BitsPerPixel);
        Assert.Equal(48, LoadFirstFrame(tiff16).Format.BitsPerPixel);
        Assert.True(ContainsNonWhitePixel(LoadFirstFrame(png)));
        Assert.True(ContainsNonWhitePixel(LoadFirstFrame(tiff16)));
        string svgBody = await File.ReadAllTextAsync(svg);
        Assert.Contains("data-plot-vector=\"true\"", svgBody, StringComparison.Ordinal);
        Assert.DoesNotContain("<image ", svgBody, StringComparison.Ordinal);
        string pdfBody = Encoding.ASCII.GetString(await File.ReadAllBytesAsync(pdf));
        Assert.StartsWith("%PDF-1.7", pdfBody, StringComparison.Ordinal);
        Assert.Contains("% SciCanvas vector plot", pdfBody, StringComparison.Ordinal);
        Assert.DoesNotContain("/Subtype /Image", pdfBody, StringComparison.Ordinal);
        Assert.Contains(exporter.LastPdfFontOutcomes, outcome =>
            outcome.EffectiveFont == "Arial" && outcome.Embedded && !outcome.Outlined);
    }

    private static (TabularDataAsset Asset, PlotObject Plot) CreatePlot(PlotKind kind)
    {
        DataColumn x = new(Guid.NewGuid(), "X", TabularDataType.Numeric, Role: DataColumnRole.X);
        DataColumn y = new(Guid.NewGuid(), "Y", TabularDataType.Numeric, Role: DataColumnRole.Y);
        DataColumn error = new(Guid.NewGuid(), "Error", TabularDataType.Numeric, Role: DataColumnRole.YError);
        DataColumn value = new(Guid.NewGuid(), "Value", TabularDataType.Numeric);
        DataColumn category = new(Guid.NewGuid(), "Group", TabularDataType.Text, Role: DataColumnRole.Category);
        DataColumn[] columns = [x, y, error, value, category];
        TabularDataRow[] rows =
        [
            Row(1, 2, 0.2, 10, "Control"),
            Row(2, 4, 0.3, 20, "Control"),
            Row(3, 3, 0.25, 30, "Treatment"),
            Row(4, 6, 0.4, 40, "Treatment"),
        ];
        var asset = new TabularDataAsset(
            Guid.NewGuid(),
            "Plot data",
            null,
            null,
            7,
            columns,
            rows,
            new TabularImportMetadata
            {
                Format = TabularDataFormat.Csv,
                ImportedAt = DateTimeOffset.UnixEpoch,
                EncodingName = "UTF-8",
                Delimiter = ',',
                DataRowCount = rows.Length,
                InferenceRowCount = rows.Length,
                OriginalHeaders = columns.Select(column => column.Name).ToArray(),
            }).EnsureValid();
        Guid? xColumn = kind switch
        {
            PlotKind.Histogram => null,
            PlotKind.BoxPlot => category.Id,
            _ => x.Id,
        };
        PlotErrorBarBinding? errors = kind == PlotKind.ErrorBar
            ? new PlotErrorBarBinding(PlotErrorBarMode.Symmetric, SymmetricColumnId: error.Id)
            : null;
        Guid? valueColumn = kind == PlotKind.Heatmap ? value.Id : null;
        var plot = new PlotObject
        {
            Id = Guid.NewGuid(),
            Name = $"{kind} vector export",
            PlotType = kind,
            Data = new PlotDataBinding(
                asset.Id,
                asset.SourceRevision,
                xColumn,
                y.Id,
                errors,
                valueColumn),
            XAxis = PlotAxisDefinition.DefaultX,
            YAxis = PlotAxisDefinition.DefaultY,
            Typography = PlotTypography.Default,
            Style = PlotSeriesStyle.Default with
            {
                MarkerShape = PlotMarkerShape.Circle,
                MarkerFill = "#FFFFFFFF",
            },
        };
        return (asset, plot.EnsureValid(asset));
    }

    private static TabularDataRow Row(double x, double y, double error, double value, string category) => new(
    [
        TabularDataValue.FromNumber(x.ToString(CultureInfo.InvariantCulture), x),
        TabularDataValue.FromNumber(y.ToString(CultureInfo.InvariantCulture), y),
        TabularDataValue.FromNumber(error.ToString(CultureInfo.InvariantCulture), error),
        TabularDataValue.FromNumber(value.ToString(CultureInfo.InvariantCulture), value),
        TabularDataValue.FromText(category),
    ]);

    private static BitmapSource LoadFirstFrame(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapFrame frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private static bool ContainsNonWhitePixel(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] < 245 || pixels[index + 1] < 245 || pixels[index + 2] < 245)
            {
                return true;
            }
        }
        return false;
    }

    private static int Count(string value, string token)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }
        return count;
    }
}
