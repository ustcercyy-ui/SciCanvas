using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using SciCanvas.Core.Export;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;
using SciCanvas.Persistence;

namespace SciCanvas.Presentation;

public sealed record SubmissionPackageRequest(
    string TargetDirectory,
    FigureExportDocument Figure,
    IReadOnlyCollection<SourceAssetItemViewModel> Sources,
    UnifiedQcReport QcResult,
    IReadOnlyList<ProjectAuditEntrySnapshot> AuditTrail,
    string SoftwareVersion,
    string FigureBaseName = "figure1",
    IReadOnlyList<ResolvedFont>? FontResolutions = null,
    IReadOnlyList<SpatialLinkGroup>? LinkGroups = null,
    IReadOnlyList<RoiObject>? Rois = null)
{
    public SubmissionPackageRequest(
        string targetDirectory,
        FigureExportDocument figure,
        IReadOnlyCollection<SourceAssetItemViewModel> sources,
        FigurePreflightResult qcResult,
        IReadOnlyList<ProjectAuditEntrySnapshot> auditTrail,
        string softwareVersion,
        string figureBaseName = "figure1",
        IReadOnlyList<ResolvedFont>? fontResolutions = null,
        IReadOnlyList<SpatialLinkGroup>? linkGroups = null,
        IReadOnlyList<RoiObject>? rois = null)
        : this(
            targetDirectory,
            figure,
            sources,
            UnifiedQcReport.FromFigurePreflight(qcResult),
            auditTrail,
            softwareVersion,
            figureBaseName,
            fontResolutions,
            linkGroups,
            rois)
    {
    }
}

public sealed record SubmissionPackageResult(
    string RootDirectory,
    IReadOnlyList<string> CreatedFiles,
    int WarningCount);

public sealed class SubmissionPackageBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IFigureExporter _figureExporter;

    public SubmissionPackageBuilder(IFigureExporter figureExporter)
    {
        _figureExporter = figureExporter ?? throw new ArgumentNullException(nameof(figureExporter));
    }

    public async Task<SubmissionPackageResult> BuildAsync(
        SubmissionPackageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDirectory);
        ArgumentNullException.ThrowIfNull(request.Figure);
        ArgumentNullException.ThrowIfNull(request.Sources);
        ArgumentNullException.ThrowIfNull(request.QcResult);
        if (request.QcResult.HasErrors)
        {
            throw new InvalidOperationException("Submission Package 已阻止：Figure QC、源文件验证或科学对象有效性仍有 Error。 ");
        }

        string root = Path.GetFullPath(request.TargetDirectory);
        EnsureEmptyTarget(root);
        string stagingRoot = CreateStagingDirectory(root);
        try
        {
            string figureDirectory = Path.Combine(stagingRoot, "Figure1");
            string dataDirectory = Path.Combine(stagingRoot, "Data");
            string auditDirectory = Path.Combine(stagingRoot, "Audit");
            Directory.CreateDirectory(figureDirectory);
            Directory.CreateDirectory(Path.Combine(stagingRoot, "Supplement"));
            Directory.CreateDirectory(dataDirectory);
            Directory.CreateDirectory(auditDirectory);

            var created = new List<string>();
            string baseName = SanitizeBaseName(request.FigureBaseName);
            string tiffPath = Path.Combine(figureDirectory, baseName + ".tif");
            string svgPath = Path.Combine(figureDirectory, baseName + ".svg");
            cancellationToken.ThrowIfCancellationRequested();
            await _figureExporter.ExportAsync(request.Figure, tiffPath, cancellationToken);
            created.Add(tiffPath);
            await _figureExporter.ExportAsync(request.Figure, svgPath, cancellationToken);
            created.Add(svgPath);

            FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
                request.Figure,
                tiffPath,
                request.SoftwareVersion,
                request.Sources.Select(source => source.Asset).ToArray(),
                request.QcResult.ToFigurePreflightResult(),
                exportProfileId: "submission-package",
                exportProfileName: "Submission Package",
                sourceRevisions: request.Sources.ToDictionary(source => source.Asset.Id, source => source.SourceRevision),
                analyses: request.Sources.SelectMany(source => source.AnalysisResults),
                fontResolutions: request.FontResolutions,
                linkGroups: request.LinkGroups,
                rois: request.Rois,
                pdfFontOutcomes: (_figureExporter as IPdfFontExportReportProvider)?.LastPdfFontOutcomes);
            string provenancePath = Path.Combine(figureDirectory, baseName + ".provenance.json");
            string exportReportPath = Path.Combine(figureDirectory, baseName + ".export-report.html");
            FigureProvenanceWriter.WriteJson(provenance, provenancePath);
            FigureProvenanceWriter.WriteHtml(provenance, exportReportPath);
            created.Add(provenancePath);
            created.Add(exportReportPath);

            SourceAssetItemViewModel[] sources = request.Sources.ToArray();
            string measurementsCsvPath = Path.Combine(dataDirectory, "measurements.csv");
            string measurementsXlsxPath = Path.Combine(dataDirectory, "measurements.xlsx");
            WriteMeasurementsCsv(measurementsCsvPath, sources);
            MeasurementTableXlsxWriter.WriteNew(measurementsXlsxPath, sources);
            created.Add(measurementsCsvPath);
            created.Add(measurementsXlsxPath);

            ScientificImageAnalysisResult[] analyses = sources
                .SelectMany(source => source.AnalysisResults)
                .ToArray();
            string analysesCsvPath = Path.Combine(dataDirectory, "analyses.csv");
            string analysesXlsxPath = Path.Combine(dataDirectory, "analyses.xlsx");
            WriteNewText(analysesCsvPath, ScientificAnalysisTable.CreateCsv(analyses), emitBom: true);
            AnalysisTableXlsxWriter.WriteNew(analysesXlsxPath, analyses);
            created.Add(analysesCsvPath);
            created.Add(analysesXlsxPath);

            string particlesPath = Path.Combine(dataDirectory, "particle-analysis.csv");
            WriteNewText(
                particlesPath,
                ScientificAnalysisTable.CreateCsv(analyses.OfType<AssistedRegionAnalysisResult>()),
                emitBom: true);
            created.Add(particlesPath);

            string auditPath = Path.Combine(auditDirectory, "project-audit.json");
            WriteNewText(
                auditPath,
                JsonSerializer.Serialize(new
                {
                    software = "SciCanvas",
                    version = request.SoftwareVersion,
                    packagedAt = DateTimeOffset.UtcNow,
                    entries = request.AuditTrail,
                }, JsonOptions));
            created.Add(auditPath);

            string manifestPath = Path.Combine(auditDirectory, "source-manifest.csv");
            SourceManifestWriter.WriteNew(manifestPath, sources);
            created.Add(manifestPath);

            string qcReportPath = Path.Combine(auditDirectory, "qc-report.html");
            SubmissionQcReportWriter.WriteNew(qcReportPath, request.QcResult);
            created.Add(qcReportPath);

            int warningCount = request.QcResult.Issues.Count(issue =>
                issue.Severity == FigurePreflightSeverity.Warning);
            string readmePath = Path.Combine(stagingRoot, "README.txt");
            WriteNewText(readmePath, CreateReadme(request, warningCount));
            created.Add(readmePath);

            CommitStagingDirectory(stagingRoot, root);
            return new SubmissionPackageResult(
                root,
                created.Select(path => Path.Combine(root, Path.GetRelativePath(stagingRoot, path))).ToArray(),
                warningCount);
        }
        catch
        {
            CleanupStagingDirectory(stagingRoot);
            throw;
        }
    }

    private static string CreateStagingDirectory(string targetRoot)
    {
        string parent = Path.GetDirectoryName(targetRoot)
            ?? throw new IOException("投稿包目标缺少父目录。");
        Directory.CreateDirectory(parent);
        string name = $".{Path.GetFileName(targetRoot)}.scicanvas-staging-{Guid.NewGuid():N}";
        string stagingRoot = Path.Combine(parent, name);
        Directory.CreateDirectory(stagingRoot);
        return stagingRoot;
    }

    private static void CommitStagingDirectory(string stagingRoot, string targetRoot)
    {
        if (Directory.Exists(targetRoot))
        {
            if (Directory.EnumerateFileSystemEntries(targetRoot).Any())
            {
                throw new IOException("投稿包目标文件夹在提交前已被写入；已保留原目录且未覆盖。");
            }

            Directory.Delete(targetRoot);
        }

        Directory.Move(stagingRoot, targetRoot);
    }

    private static void CleanupStagingDirectory(string stagingRoot)
    {
        try
        {
            if (Directory.Exists(stagingRoot))
            {
                Directory.Delete(stagingRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
    private static void EnsureEmptyTarget(string root)
    {
        if (File.Exists(root))
        {
            throw new IOException("投稿包目标必须是新文件夹，不能是已有文件。 ");
        }

        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new IOException("投稿包目标文件夹必须为空；SciCanvas 不会覆盖已有文件。 ");
        }
    }

    private static void WriteMeasurementsCsv(
        string path,
        IReadOnlyCollection<SourceAssetItemViewModel> sources)
    {
        const string header = "Image,ID,Type,Value,Unit,PixelValue,Area,AreaUnit,Perimeter,PerimeterUnit";
        var combined = new StringBuilder().AppendLine(header);
        foreach (SourceAssetItemViewModel source in sources)
        {
            using var reader = new StringReader(source.CreateMeasurementCsv());
            _ = reader.ReadLine();
            while (reader.ReadLine() is { } line)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    combined.AppendLine(line);
                }
            }
        }

        WriteNewText(path, combined.ToString(), emitBom: true);
    }

    private static void WriteNewText(string path, string content, bool emitBom = false)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(emitBom));
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static string CreateReadme(SubmissionPackageRequest request, int warningCount) =>
        $"""
        SciCanvas Submission Package
        ============================

        Generated by SciCanvas {request.SoftwareVersion}
        Generated at: {DateTimeOffset.UtcNow:O}

        Figure1 contains the lossless TIFF, editable SVG, provenance JSON and export report.
        Data contains measurement and analysis tables in CSV/XLSX form.
        Audit contains the project audit trail, source manifest and QC report.
        Supplement is reserved for explicitly selected supplementary outputs.

        QC summary: {request.QcResult.Summary}
        Warnings retained in Audit/qc-report.html: {warningCount}

        Original source images are intentionally NOT copied into this package.
        Source paths and SHA-256 fingerprints are recorded in Audit/source-manifest.csv.
        SciCanvas never modifies the original source images.
        """;

    private static string SanitizeBaseName(string value)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "figure1" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            normalized = normalized.Replace(invalid, '_');
        }

        return normalized.Length == 0 ? "figure1" : normalized;
    }
}
