using SciCanvas.Core.Export;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Workspace;

public enum UnifiedQcIssueOrigin
{
    FigurePreflight,
    ScientificQcEngine,
    Supplemental,
}

public sealed record UnifiedQcIssue(
    FigurePreflightSeverity Severity,
    string Code,
    string Message,
    string? PanelLabel = null,
    Guid? SourceId = null,
    Guid? ObjectId = null,
    UnifiedQcIssueOrigin Origin = UnifiedQcIssueOrigin.FigurePreflight,
    QcIssueLocation? Location = null,
    IReadOnlyList<QcIssueLocation>? RelatedLocations = null)
{
    public IReadOnlyList<QcIssueLocation> EffectiveRelatedLocations => RelatedLocations ?? [];

    public FigurePreflightIssue ToFigurePreflightIssue() => new(
        Severity,
        Code,
        Message,
        PanelLabel,
        SourceId,
        ObjectId);

    public static UnifiedQcIssue FromFigurePreflight(
        FigurePreflightIssue issue,
        UnifiedQcIssueOrigin origin = UnifiedQcIssueOrigin.FigurePreflight)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new UnifiedQcIssue(
            issue.Severity,
            issue.Code,
            issue.Message,
            issue.PanelLabel,
            issue.SourceId,
            issue.ObjectId,
            origin,
            new QcIssueLocation(
                AssetId: issue.SourceId,
                ScientificObjectId: issue.ObjectId));
    }
}

public sealed class UnifiedQcReport
{
    public UnifiedQcReport(IEnumerable<UnifiedQcIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);
        Issues = issues
            .DistinctBy(issue => (
                issue.Severity,
                issue.Code,
                issue.Message,
                issue.PanelLabel,
                issue.SourceId,
                issue.ObjectId))
            .OrderByDescending(issue => issue.Severity)
            .ThenBy(issue => issue.Code, StringComparer.Ordinal)
            .ThenBy(issue => issue.PanelLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<UnifiedQcIssue> Issues { get; }

    public bool HasErrors => Issues.Any(issue => issue.Severity == FigurePreflightSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == FigurePreflightSeverity.Warning);

    public string Summary => HasErrors
        ? $"发现 {Issues.Count(issue => issue.Severity == FigurePreflightSeverity.Error)} 个错误，导出已阻止。"
        : HasWarnings
            ? $"检查通过，但有 {Issues.Count(issue => issue.Severity == FigurePreflightSeverity.Warning)} 个提醒。"
            : "投稿预检通过。";

    public FigurePreflightResult ToFigurePreflightResult() => new(
        Issues.Select(issue => issue.ToFigurePreflightIssue()).ToArray());

    public static UnifiedQcReport FromFigurePreflight(FigurePreflightResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new UnifiedQcReport(result.Issues.Select(issue =>
            UnifiedQcIssue.FromFigurePreflight(issue)));
    }
}

public sealed record ScientificQcRequest(
    FigurePreflightContext FigureContext,
    IReadOnlyCollection<SourceAsset> Sources,
    QcContext ScientificContext,
    bool HasUnsavedChanges = false,
    FigurePreflightConfiguration? FigureConfiguration = null,
    IReadOnlyList<FigurePreflightIssue>? SupplementalIssues = null);

/// <summary>
/// The single orchestration layer for figure preflight and scientific integrity QC.
/// GUI, CLI, submission packages and batch exports consume its unified report.
/// </summary>
public sealed class ScientificQcCoordinator
{
    private readonly QcEngine _engine;

    public ScientificQcCoordinator(QcEngine? engine = null)
    {
        _engine = engine ?? new QcEngine();
    }

    public UnifiedQcReport Run(ScientificQcRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.FigureContext);
        ArgumentNullException.ThrowIfNull(request.Sources);
        ArgumentNullException.ThrowIfNull(request.ScientificContext);

        FigurePreflightResult preflight = FigurePreflight.Check(
            request.FigureContext,
            request.Sources,
            request.HasUnsavedChanges,
            request.FigureConfiguration);
        IEnumerable<UnifiedQcIssue> preflightIssues = preflight.Issues
            .Select(issue => UnifiedQcIssue.FromFigurePreflight(issue));
        IEnumerable<UnifiedQcIssue> supplemental = (request.SupplementalIssues ?? [])
            .Select(issue => UnifiedQcIssue.FromFigurePreflight(
                issue,
                UnifiedQcIssueOrigin.Supplemental));
        IEnumerable<UnifiedQcIssue> scientific = _engine
            .Evaluate(request.ScientificContext)
            .Select(result => ToUnifiedIssue(result, request.ScientificContext.Project));

        return new UnifiedQcReport(preflightIssues.Concat(supplemental).Concat(scientific));
    }

    private static UnifiedQcIssue ToUnifiedIssue(QcResult result, ScientificProject project)
    {
        string? panelLabel = result.Location.PanelId is Guid panelId
            ? project.Figures.Values
                .SelectMany(figure => figure.Panels)
                .FirstOrDefault(panel => panel.Id == panelId)?.Label
            : null;
        FigurePreflightSeverity severity = result.Severity switch
        {
            QcSeverity.Info => FigurePreflightSeverity.Info,
            QcSeverity.Warning => FigurePreflightSeverity.Warning,
            QcSeverity.Error => FigurePreflightSeverity.Error,
            _ => throw new ArgumentOutOfRangeException(nameof(result)),
        };
        return new UnifiedQcIssue(
            severity,
            result.RuleId,
            result.Message,
            panelLabel,
            result.Location.AssetId,
            result.Location.ScientificObjectId,
            UnifiedQcIssueOrigin.ScientificQcEngine,
            result.Location,
            result.RelatedLocations);
    }
}
