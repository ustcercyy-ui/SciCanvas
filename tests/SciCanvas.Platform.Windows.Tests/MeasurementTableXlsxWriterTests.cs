using System.IO.Compression;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class MeasurementTableXlsxWriterTests
{
    [Fact]
    public void WriteNew_CreatesOpenXmlWorkbookWithMeasurementAreaAndUnits()
    {
        using var workspace = new TestWorkspace();
        string targetPath = Path.Combine(workspace.Root, "measurements.xlsx");
        SourceAssetItemViewModel source = CreateSource();
        source.Calibration.Restore(
            new SpatialCalibration(source.Asset.Id, 0.1, 0.1, "µm", CalibrationOrigin.Manual),
            0,
            0,
            10,
            0);
        source.AddMeasurement(
            ScientificMeasurementKind.CircleRoi,
            new MeasurementPoint(10, 10),
            new MeasurementPoint(50, 50));

        MeasurementTableXlsxWriter.WriteNew(targetPath, source);

        using ZipArchive archive = ZipFile.OpenRead(targetPath);
        Assert.NotNull(archive.GetEntry("[Content_Types].xml"));
        Assert.NotNull(archive.GetEntry("xl/workbook.xml"));
        ZipArchiveEntry sheet = Assert.IsType<ZipArchiveEntry>(archive.GetEntry("xl/worksheets/sheet1.xml"));
        using var reader = new StreamReader(sheet.Open());
        string xml = reader.ReadToEnd();
        Assert.Contains("Measurements", ReadEntry(archive, "xl/workbook.xml"), StringComparison.Ordinal);
        Assert.Contains("圆形测量", xml, StringComparison.Ordinal);
        Assert.Contains("AreaUnit", xml, StringComparison.Ordinal);
        Assert.Contains("µm²", xml, StringComparison.Ordinal);
    }

    private static string ReadEntry(ZipArchive archive, string path)
    {
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(path));
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static SourceAssetItemViewModel CreateSource()
    {
        byte[] pixels = new byte[100 * 80 * 4];
        BitmapSource preview = BitmapSource.Create(
            100,
            80,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            400);
        preview.Freeze();
        var asset = new SourceAsset(
            Guid.NewGuid(),
            "sample.png",
            "sample.png",
            new SourceFingerprint(1, DateTimeOffset.UtcNow, new string('A', 64), null),
            new CoreImageMetadata(new PixelSize64(100, 80), 4, 8, "Bgra32"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }
}
