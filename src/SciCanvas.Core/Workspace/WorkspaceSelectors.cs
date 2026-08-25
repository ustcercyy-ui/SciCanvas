namespace SciCanvas.Core.Workspace;

public sealed record AssetUsage(
    Guid FigureId,
    string FigureName,
    Guid PanelId,
    string PanelLabel);

public static class WorkspaceSelectors
{
    public static IReadOnlyList<AssetUsage> GetAssetUsages(
        ScientificProject project,
        Guid assetId)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.Figures.Values
            .SelectMany(figure => figure.Panels
                .Where(panel => panel.AssetId == assetId)
                .Select(panel => new AssetUsage(
                    figure.Id,
                    figure.Name,
                    panel.Id,
                    panel.Label)))
            .OrderBy(usage => usage.FigureName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(usage => usage.PanelLabel, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static ScientificValidity GetPanelScientificValidity(
        ScientificProject project,
        FigurePanel panel)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(panel);
        if (!project.Assets.TryGetValue(panel.AssetId, out ScientificAsset? asset))
        {
            return ScientificValidity.Invalid("Panel references a missing asset." );
        }

        List<string> invalid = [];
        List<string> review = [];
        List<string> warnings = [];
        if (asset.LinkState == Sources.SourceLinkState.Missing)
        {
            invalid.Add("Source file is missing." );
        }
        else if (asset.LinkState == Sources.SourceLinkState.Modified)
        {
            warnings.Add("Source file changed since import." );
        }

        foreach (ScientificObject item in panel.ScientificObjectIds
                     .Select(project.ScientificObjects.GetValueOrDefault)
                     .Where(item => item is not null)!)
        {
            switch (item.Validity.State)
            {
                case ScientificValidityState.Invalid:
                    invalid.AddRange(item.Validity.Reasons);
                    break;
                case ScientificValidityState.ReviewRequired:
                    review.AddRange(item.Validity.Reasons);
                    break;
                case ScientificValidityState.Warning:
                    warnings.AddRange(item.Validity.Reasons);
                    break;
            }
        }

        if (invalid.Count > 0)
        {
            return ScientificValidity.Invalid(invalid.ToArray());
        }

        if (review.Count > 0)
        {
            return ScientificValidity.ReviewRequired(review.ToArray());
        }

        return warnings.Count > 0
            ? ScientificValidity.Warning(warnings.ToArray())
            : ScientificValidity.Valid;
    }

    public static ResolvedProjectStyle ResolveStyle(
        ScientificProject project,
        ScientificFigure figure,
        FigurePanel? panel = null,
        ScientificObject? scientificObject = null) =>
        ProjectStyleResolver.Resolve(
            project.Style,
            figure.StyleOverride,
            panel?.StyleOverride,
            scientificObject?.StyleOverride);
}
