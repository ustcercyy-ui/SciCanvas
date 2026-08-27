using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class SubmissionPackageBuilderTests
{
    [Fact]
    public async Task BuildAsync_CreatesAuditablePackageWithoutCopyingSourceImage()
    {
        using var workspace = new TestWorkspace();
        string target = Path.Combine(workspace.Root, "SubmissionPackage");
        SourceAssetItemViewModel source = CreateSource();
        source.AddMeasurement(
            ScientificMeasurementKind.Length,
            new MeasurementPoint(1, 1),
            new MeasurementPoint(10, 10));
        var exporter = new RecordingFigureExporter();
        var builder = new SubmissionPackageBuilder(exporter);
        var qc = new FigurePreflightResult(
        [
            new FigurePreflightIssue(
                FigurePreflightSeverity.Warning,
                "FONT_MISSING",
                "Font is not installed."),
        ]);

        SubmissionPackageResult result = await builder.BuildAsync(new SubmissionPackageRequest(
            target,
            new FigureExportDocument(100, 80, 300, []),
            [source],
            qc,
            [],
            "2.3.0-alpha"));

        string[] expected =
        [
            "Figure1/figure1.tif",
            "Figure1/figure1.svg",
            "Figure1/figure1.provenance.json",
            "Figure1/figure1.export-report.html",
            "Data/measurements.csv",
            "Data/measurements.xlsx",
            "Data/analyses.csv",
            "Data/analyses.xlsx",
            "Data/particle-analysis.csv",
            "Audit/project-audit.json",
            "Audit/source-manifest.csv",
            "Audit/qc-report.html",
            "README.txt",
        ];
        Assert.All(expected, relative => Assert.True(File.Exists(Path.Combine(target, relative)), relative));
        Assert.Equal(2, exporter.Targets.Count);
        Assert.Equal(1, result.WarningCount);
        Assert.Contains("SHA256", await File.ReadAllTextAsync(Path.Combine(target, "Audit/source-manifest.csv")), StringComparison.Ordinal);
        Assert.Contains(source.Asset.Fingerprint.Sha256, await File.ReadAllTextAsync(Path.Combine(target, "Audit/source-manifest.csv")), StringComparison.Ordinal);
        Assert.Contains("FONT_MISSING", await File.ReadAllTextAsync(Path.Combine(target, "Audit/qc-report.html")), StringComparison.Ordinal);
        Assert.Contains("sample.png", await File.ReadAllTextAsync(Path.Combine(target, "Data/measurements.csv")), StringComparison.Ordinal);
        Assert.DoesNotContain(
            Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories),
            file => string.Equals(Path.GetFileName(file), "original-source.png", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task BuildAsync_SecondExporterFailureLeavesNoPartialTargetOrStagingDirectory()
    {
        using var workspace = new TestWorkspace();
        string target = Path.Combine(workspace.Root, "SubmissionPackage");
        var builder = new SubmissionPackageBuilder(new FailingSecondFigureExporter());

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(new SubmissionPackageRequest(
            target,
            new FigureExportDocument(100, 80, 300, []),
            [CreateSource()],
            new FigurePreflightResult([]),
            [],
            "2.3.0-alpha")));

        Assert.False(Directory.Exists(target));
        Assert.Empty(Directory.EnumerateDirectories(
            workspace.Root,
            ".SubmissionPackage.scicanvas-staging-*",
            SearchOption.TopDirectoryOnly));
    }
    [Fact]
    public async Task BuildAsync_RejectsExistingNonEmptyDirectoryWithoutOverwrite()
    {
        using var workspace = new TestWorkspace();
        string target = Path.Combine(workspace.Root, "SubmissionPackage");
        Directory.CreateDirectory(target);
        string existing = Path.Combine(target, "keep.txt");
        await File.WriteAllTextAsync(existing, "keep");
        var exporter = new RecordingFigureExporter();
        var builder = new SubmissionPackageBuilder(exporter);

        await Assert.ThrowsAsync<IOException>(() => builder.BuildAsync(new SubmissionPackageRequest(
            target,
            new FigureExportDocument(100, 80, 300, []),
            [CreateSource()],
            new FigurePreflightResult([]),
            [],
            "2.3.0-alpha")));

        Assert.Equal("keep", await File.ReadAllTextAsync(existing));
        Assert.Empty(exporter.Targets);
    }

    [Fact]
    public async Task BuildAsync_QcErrorBlocksBeforeCreatingDirectory()
    {
        using var workspace = new TestWorkspace();
        string target = Path.Combine(workspace.Root, "SubmissionPackage");
        var exporter = new RecordingFigureExporter();
        var builder = new SubmissionPackageBuilder(exporter);

        await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync(new SubmissionPackageRequest(
            target,
            new FigureExportDocument(100, 80, 300, []),
            [CreateSource()],
            new FigurePreflightResult(
            [
                new FigurePreflightIssue(FigurePreflightSeverity.Error, "STALE_ANALYSIS_REVISION", "stale"),
            ]),
            [],
            "2.3.0-alpha")));

        Assert.False(Directory.Exists(target));
        Assert.Empty(exporter.Targets);
    }

    private static SourceAssetItemViewModel CreateSource()
    {
        byte[] pixels = new byte[20 * 16 * 4];
        BitmapSource preview = BitmapSource.Create(
            20,
            16,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            80);
        preview.Freeze();
        var asset = new SourceAsset(
            Guid.NewGuid(),
            "sample.png",
            "C:\\data\\original-source.png",
            new SourceFingerprint(320, DateTimeOffset.UnixEpoch, new string('A', 64), null),
            new CoreImageMetadata(new PixelSize64(20, 16), 4, 8, "Bgra32"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }

    private sealed class FailingSecondFigureExporter : IFigureExporter
    {
        private int _callCount;

        public Task ExportAsync(
            FigureExportDocument document,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            _callCount++;
            if (_callCount == 2)
            {
                throw new InvalidOperationException("Simulated SVG exporter failure.");
            }

            using var stream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.WriteByte(0x01);
            stream.Flush(flushToDisk: true);
            return Task.CompletedTask;
        }
    }
    private sealed class RecordingFigureExporter : IFigureExporter
    {
        public List<string> Targets { get; } = [];

        public async Task ExportAsync(
            FigureExportDocument document,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            Targets.Add(targetPath);
            await using var stream = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true);
            byte[] bytes = Encoding.UTF8.GetBytes(Path.GetExtension(targetPath));
            await stream.WriteAsync(bytes, cancellationToken);
        }
    }
}
