using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Workspace;
using SciCanvas.Persistence;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;

namespace SciCanvas.Presentation;

public sealed record SubmissionExecutionRequest(
    string TargetDirectory,
    FigureExportDocument Figure,
    IReadOnlyCollection<SourceAssetItemViewModel> Sources,
    UnifiedQcReport Qc,
    IReadOnlyList<ProjectAuditEntrySnapshot> ExistingAuditTrail,
    string SoftwareVersion,
    IReadOnlyList<ResolvedFont> FontResolutions,
    IReadOnlyList<SpatialLinkGroup> LinkGroups,
    IReadOnlyList<RoiObject> Rois);

public sealed record SubmissionExecutionResult(
    SubmissionPackageResult Package,
    ProjectAuditEntrySnapshot AuditEntry);

/// <summary>
/// Owns the complete submission-package transaction after the UI chooses a
/// destination and resolves the immutable export document.
/// </summary>
public sealed class SubmissionCoordinator
{
    private readonly FigureExportCoordinator _figureExportCoordinator;
    private readonly SubmissionPackageBuilder _builder;

    public SubmissionCoordinator(
        FigureExportCoordinator figureExportCoordinator,
        SubmissionPackageBuilder builder)
    {
        _figureExportCoordinator = figureExportCoordinator ??
            throw new ArgumentNullException(nameof(figureExportCoordinator));
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    public async Task<SubmissionExecutionResult> BuildAsync(
        SubmissionExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Qc.HasErrors)
        {
            throw new InvalidOperationException(
                "Submission Package 已阻止：Figure QC、源文件验证或科学对象有效性仍有 Error。");
        }

        await _figureExportCoordinator.VerifySourcesAsync(
            request.Sources.Select(source => source.Asset),
            cancellationToken);
        var auditEntry = new ProjectAuditEntrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Command = "BuildSubmissionPackage",
            Parameters = new Dictionary<string, object?>
            {
                ["targetDirectory"] = Path.GetFullPath(request.TargetDirectory),
                ["sourceCount"] = request.Sources.Count,
                ["panelCount"] = request.Figure.Panels.Count,
                ["warningCount"] = request.Qc.Issues.Count(
                    issue => issue.Severity == FigurePreflightSeverity.Warning),
            },
        };
        SubmissionPackageResult package = await _builder.BuildAsync(
            new SubmissionPackageRequest(
                request.TargetDirectory,
                request.Figure,
                request.Sources,
                request.Qc,
                request.ExistingAuditTrail.Concat([auditEntry]).ToArray(),
                request.SoftwareVersion,
                FontResolutions: request.FontResolutions,
                LinkGroups: request.LinkGroups,
                Rois: request.Rois),
            cancellationToken);
        return new SubmissionExecutionResult(package, auditEntry);
    }
}
