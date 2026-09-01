using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using LinkGroup = SciCanvas.Core.Linking.LinkGroup;

namespace SciCanvas.Presentation;

public sealed record FigureExportExecutionRequest(
    string RequestedTargetPath,
    FigureExportDocument Document,
    IReadOnlyCollection<SourceAsset> ProtectedSources,
    IReadOnlyCollection<SourceAsset> ProvenanceSources,
    FigurePreflightResult Preflight,
    string SoftwareVersion,
    IReadOnlyDictionary<Guid, long> SourceRevisions,
    IReadOnlyCollection<ScientificImageAnalysisResult> Analyses,
    IReadOnlyCollection<ResolvedFont> FontResolutions,
    IReadOnlyCollection<LinkGroup> LinkGroups,
    IReadOnlyCollection<RoiObject> Rois,
    string? ExportProfileId = null,
    string? ExportProfileName = null,
    bool WriteProvenance = true);

public sealed record FigureExportExecutionResult(
    string TargetPath,
    IReadOnlyList<PdfFontExportOutcome> PdfFontOutcomes,
    string? ProvenanceWarning)
{
    public int OutlineFallbackCount => PdfFontOutcomes.Count(outcome =>
        outcome.Outlined && outcome.FallbackReason is not null);
}

/// <summary>
/// Coordinates source verification and the write-once Figure export transaction.
/// UI state and file pickers remain outside this service.
/// </summary>
public sealed class FigureExportCoordinator
{
    private readonly ISourceAssetReader _sourceReader;
    private readonly IPathSafetyPolicy _pathSafetyPolicy;
    private readonly IFigureExporter _figureExporter;

    public FigureExportCoordinator(
        ISourceAssetReader sourceReader,
        IPathSafetyPolicy pathSafetyPolicy,
        IFigureExporter figureExporter)
    {
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        _pathSafetyPolicy = pathSafetyPolicy ?? throw new ArgumentNullException(nameof(pathSafetyPolicy));
        _figureExporter = figureExporter ?? throw new ArgumentNullException(nameof(figureExporter));
    }

    public async Task VerifySourcesAsync(
        IEnumerable<SourceAsset> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        foreach (SourceAsset source in sources.DistinctBy(item => item.Id))
        {
            SourceVerification verification = await _sourceReader.VerifyAsync(source, cancellationToken);
            if (verification.State != SourceLinkState.Verified)
            {
                throw new InvalidDataException(
                    $"{source.DisplayName}：{verification.Message ?? "源文件验证失败。"}");
            }
        }
    }

    public async Task<FigureExportExecutionResult> ExportNewAsync(
        FigureExportExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
            request.RequestedTargetPath,
            request.ProtectedSources,
            cancellationToken);
        if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
        {
            throw new InvalidOperationException(decision.Message);
        }

        string targetPath = decision.NormalizedTargetPath;
        if (File.Exists(targetPath))
        {
            throw new IOException("目标文件已存在，为保护科研数据未覆盖它。");
        }

        await _figureExporter.ExportAsync(request.Document, targetPath, cancellationToken);
        IReadOnlyList<PdfFontExportOutcome> pdfFontOutcomes =
            (_figureExporter as IPdfFontExportReportProvider)?.LastPdfFontOutcomes ?? [];
        string? provenanceWarning = null;
        if (request.WriteProvenance)
        {
            FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
                request.Document,
                targetPath,
                request.SoftwareVersion,
                request.ProvenanceSources,
                request.Preflight,
                request.ExportProfileId,
                request.ExportProfileName,
                request.SourceRevisions,
                request.Analyses,
                request.FontResolutions,
                request.LinkGroups,
                request.Rois,
                pdfFontOutcomes);
            try
            {
                FigureProvenanceWriter.WriteJson(
                    provenance,
                    Path.ChangeExtension(targetPath, ".provenance.json"));
                FigureProvenanceWriter.WriteHtml(
                    provenance,
                    Path.ChangeExtension(targetPath, ".export-report.html"));
            }
            catch (IOException exception)
            {
                provenanceWarning = $"主图已导出，但溯源报告写入失败：{exception.Message}";
            }
        }

        return new FigureExportExecutionResult(targetPath, pdfFontOutcomes, provenanceWarning);
    }

    public static string CreateVariantTargetPath(
        string folder,
        string templateId,
        FigureExportProfile profile,
        ISet<string> plannedPaths)
    {
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        string templateStem = string.Concat(templateId.Select(
                character => invalidCharacters.Contains(character) ? '_' : character))
            .Trim();
        if (string.IsNullOrWhiteSpace(templateStem))
        {
            templateStem = "figure";
        }

        string suffix = string.Concat(profile.Id.Select(
            character => invalidCharacters.Contains(character) ? '_' : character));
        string baseName = $"figure_{templateStem}_{suffix}";
        string candidate = Path.GetFullPath(Path.Combine(folder, baseName + profile.Extension));
        int attempt = 2;
        while (!plannedPaths.Add(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(folder, $"{baseName}_{attempt++}{profile.Extension}"));
        }

        return candidate;
    }
}
