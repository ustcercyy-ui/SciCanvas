using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

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
            Guid panelId = Guid.NewGuid();
            Guid roiId = Guid.NewGuid();
            var roi = new RoiObject
            {
                Id = roiId,
                AssetId = source.Id,
                SourceRevision = 3,
                GeometryKind = RoiGeometryKind.Rectangle,
                SourceGeometry =
                [
                    new MeasurementPoint(5, 5),
                    new MeasurementPoint(20, 20),
                ],
                Style = RoiStyle.Default with { Label = "tracked-cell" },
            }.EnsureValid();
            var projection = new RoiFigureProjectionObject
            {
                Id = Guid.NewGuid(),
                RoiId = roiId,
                PanelId = panelId,
                AssetId = source.Id,
                SourceRevision = 3,
                ZIndex = 2,
            };
            var export = new FigureExportDocument(
                200,
                100,
                300,
                [
                    new FigurePanelExportItem(
                        source,
                        new PixelRect64(0, 0, 50, 50),
                        new PixelRect64(0, 0, 100, 100),
                        "a",
                        true,
                        PanelId: panelId,
                        SourceRevision: 3),
                ],
                roiProjections: [new FigureRoiProjectionExportItem(projection, roi)]);
            FigurePreflightResult preflight = FigurePreflight.Check(export, [source]);
            var analysis = new RoiStatisticsResult
            {
                SourceAssetId = source.Id,
                SourceRevision = 3,
                AnalyzerId = "scicanvas.roi.v2",
                SourceBitDepth = 16,
                Region = new PixelRect64(0, 0, 50, 50),
                ClippedToImage = true,
                CoverageFraction = 0.82,
                Validity = AnalysisResultValidity.ReviewRequired("ROI clipped."),
                Histogram = new IntensityHistogram([], 0, 0, 0),
            };
            var particles = new AssistedRegionAnalysisResult(
                new AssistedRegionAnalysisOptions(
                    AssistedRegionMode.BrightParticles,
                    new PixelRect64(0, 0, 50, 50),
                    MinimumAreaPixels: 4),
                [],
                0.5,
                0,
                2500)
            {
                SourceAssetId = source.Id,
                SourceRevision = 3,
                AnalyzerId = "scicanvas.connected-components.v3",
                SourceBitDepth = 16,
                ResourcePolicy = new AnalysisResourcePolicy(
                    MaxPixels: 25_000,
                    MaxComponentsSafety: 500,
                    MaxBoundaryPoints: 2_000,
                    MemoryBudgetBytes: 64_000_000),
            };
            FigureProvenanceDocument document = FigureProvenanceWriter.Create(
                export,
                "figure.tif",
                "0.9.0",
                [source],
                preflight,
                sourceRevisions: new Dictionary<Guid, long> { [source.Id] = 3 },
                analyses: [analysis, particles]);
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
            Assert.Contains("\"clippedToImage\": true", json, StringComparison.Ordinal);
            Assert.Contains("\"coverageFraction\": 0.82", json, StringComparison.Ordinal);
            Assert.Contains("\"resultLimit\": \"complete-or-AnalysisTooComplex\"", json, StringComparison.Ordinal);
            Assert.Contains("\"maxPixels\": 25000", json, StringComparison.Ordinal);
            Assert.Contains("\"maxComponentsSafety\": 500", json, StringComparison.Ordinal);
            Assert.Contains("\"maxBoundaryPoints\": 2000", json, StringComparison.Ordinal);
            Assert.Contains("\"memoryBudgetBytes\": 64000000", json, StringComparison.Ordinal);
            Assert.Contains($"\"projectionId\": \"{projection.Id:D}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"roiId\": \"{roiId:D}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains($"\"panelId\": \"{panelId:D}\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"geometryKind\": \"Rectangle\"", json, StringComparison.Ordinal);
            Assert.Single(document.RoiProjections!);
            Assert.Contains("投稿导出报告", File.ReadAllText(htmlPath));
            Assert.Throws<IOException>(() => FigureProvenanceWriter.WriteJson(document, jsonPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
