using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Tests;

public sealed class FigureProvenanceWriterTests
{
    [Fact]
    public void WriteJsonAndHtml_CreateAuditableSidecars()
    {
        string root = Path.Combine(Path.GetTempPath(), "scicanvas-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            SourceAsset source = new(
                Guid.NewGuid(),
                "source.tif",
                Path.Combine(root, "source.tif"),
                new SourceFingerprint(42, DateTimeOffset.UnixEpoch, new string('A', 64), null),
                new ImageMetadata(new PixelSize64(100, 80), 1, 16, "Gray16"),
                SourceLinkState.Verified);
            var export = new FigureExportDocument(
                200,
                100,
                300,
                [new FigurePanelExportItem(source, new PixelRect64(0, 0, 50, 50), new PixelRect64(0, 0, 100, 100), "a", true)]);
            FigurePreflightResult preflight = FigurePreflight.Check(export, [source]);
            var analysis = new RoiStatisticsResult
            {
                SourceAssetId = source.Id,
                SourceRevision = 3,
                AnalyzerId = "scicanvas.roi.v2",
                SourceBitDepth = 16,
                Region = new PixelRect64(0, 0, 50, 50),
                Histogram = new IntensityHistogram([], 0, 0, 0),
            };
            FigureProvenanceDocument document = FigureProvenanceWriter.Create(
                export,
                "figure.tif",
                "0.9.0",
                [source],
                preflight,
                sourceRevisions: new Dictionary<Guid, long> { [source.Id] = 3 },
                analyses: [analysis]);
            string jsonPath = Path.Combine(root, "figure.provenance.json");
            string htmlPath = Path.Combine(root, "figure.export-report.html");

            FigureProvenanceWriter.WriteJson(document, jsonPath);
            FigureProvenanceWriter.WriteHtml(document, htmlPath);

            string json = File.ReadAllText(jsonPath);
            Assert.Contains("source.tif", json);
            Assert.Contains("Gray16", json);
            Assert.Contains("\"sourceRevision\": 3", json, StringComparison.Ordinal);
            Assert.Contains("scicanvas.roi.v2", json, StringComparison.Ordinal);
            Assert.Contains("\"algorithmVersion\": \"2\"", json, StringComparison.Ordinal);
            Assert.Contains("histogramBinCount", json, StringComparison.Ordinal);
            Assert.Contains("投稿导出报告", File.ReadAllText(htmlPath));
            Assert.Throws<IOException>(() => FigureProvenanceWriter.WriteJson(document, jsonPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
