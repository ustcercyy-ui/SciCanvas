using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Workspace;

public enum QcCategory
{
    Layout,
    Typography,
    Resolution,
    Calibration,
    ScaleBar,
    PanelLabels,
    Source,
    Integrity,
}

public enum QcSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record QcResult(
    string Id,
    string RuleId,
    QcSeverity Severity,
    QcCategory Category,
    string Message,
    Guid? FigureId = null,
    Guid? PanelId = null,
    Guid? AssetId = null,
    Guid? ObjectId = null,
    bool CanAutoFix = false)
{
    public QcIssueLocation Location { get; init; } = new(
        FigureId: FigureId,
        PanelId: PanelId,
        AssetId: AssetId,
        ScientificObjectId: ObjectId);

    public IReadOnlyList<QcIssueLocation> RelatedLocations { get; init; } = [];
}

public sealed record QcConfiguration(
    double MinimumEffectiveDpi = 300,
    double AlignmentToleranceMm = 0.05,
    double SpacingToleranceMm = 0.05,
    double SuspiciousCalibrationAxisRatio = 5,
    bool MissingCalibrationIsError = false)
{
    public void EnsureValid()
    {
        if (!double.IsFinite(MinimumEffectiveDpi) || MinimumEffectiveDpi <= 0 ||
            !double.IsFinite(AlignmentToleranceMm) || AlignmentToleranceMm < 0 ||
            !double.IsFinite(SpacingToleranceMm) || SpacingToleranceMm < 0 ||
            !double.IsFinite(SuspiciousCalibrationAxisRatio) || SuspiciousCalibrationAxisRatio < 1)
        {
            throw new InvalidOperationException("QC 配置无效。" );
        }
    }
}

public sealed record QcContext(
    ScientificProject Project,
    QcConfiguration Configuration,
    IFontCatalog? FontCatalog = null,
    IReadOnlyList<MultiChannelAssetGroup>? MultiChannelGroups = null,
    IReadOnlyList<SpatialLinkGroup>? LinkGroups = null,
    IReadOnlyList<RawCropQcCandidate>? RawCrops = null,
    IReadOnlySet<Guid>? QuantitativeChannelIds = null)
{
    public ScientificAsset? GetAsset(Guid assetId) =>
        Project.Assets.GetValueOrDefault(assetId);

    public ScientificObject? GetScientificObject(Guid objectId) =>
        Project.ScientificObjects.GetValueOrDefault(objectId);

    public IReadOnlyList<MultiChannelAssetGroup> EffectiveMultiChannelGroups => MultiChannelGroups ?? [];

    public IReadOnlyList<SpatialLinkGroup> EffectiveLinkGroups => LinkGroups ?? [];

    public IReadOnlyList<RawCropQcCandidate> EffectiveRawCrops => RawCrops ?? [];
}

public interface IQcRule
{
    string Id { get; }

    QcCategory Category { get; }

    IEnumerable<QcResult> Evaluate(QcContext context);
}

public sealed class QcEngine
{
    private readonly IReadOnlyList<IQcRule> _rules;

    public QcEngine(IEnumerable<IQcRule>? rules = null)
    {
        _rules = (rules ?? CreateDefaultRules()).ToArray();
        if (_rules.Select(rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != _rules.Count)
        {
            throw new InvalidOperationException("QC rule ID 必须唯一。" );
        }
    }

    public IReadOnlyList<QcResult> Evaluate(QcContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Project.EnsureValid();
        context.Configuration.EnsureValid();
        return _rules
            .SelectMany(rule => rule.Evaluate(context))
            .OrderByDescending(result => result.Severity)
            .ThenBy(result => result.Category)
            .ThenBy(result => result.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<IQcRule> CreateDefaultRules()
    {
        yield return new ObjectOutsideCanvasRule();
        yield return new PanelAlignmentRule();
        yield return new PanelSpacingRule();
        yield return new TypographyConsistencyRule();
        yield return new FontAvailabilityRule();
        yield return new EffectiveDpiRule();
        yield return new CalibrationRule();
        yield return new ScaleBarRule();
        yield return new PanelLabelRule();
        yield return new SourceTrackingRule();
        yield return new ExactDuplicateSourceRule();
        yield return new DuplicateCropRule();
        yield return new StaleScientificObjectRule();
        yield return new PreciseScientificObjectRule();
        yield return new MultiChannelIntegrityRule();
        yield return new LinkedViewIntegrityRule();
        yield return new ColorbarIntegrityRule();
        yield return new ExactTransformedDuplicateRule();
    }
}

internal abstract class QcRuleBase(string id, QcCategory category) : IQcRule
{
    public string Id { get; } = id;

    public QcCategory Category { get; } = category;

    public abstract IEnumerable<QcResult> Evaluate(QcContext context);

    protected QcResult Issue(
        QcSeverity severity,
        string suffix,
        string message,
        Guid? figureId = null,
        Guid? panelId = null,
        Guid? assetId = null,
        Guid? objectId = null,
        bool canAutoFix = false,
        QcIssueLocation? location = null,
        IReadOnlyList<QcIssueLocation>? relatedLocations = null)
    {
        QcIssueLocation effectiveLocation = location ?? new QcIssueLocation(
            FigureId: figureId,
            PanelId: panelId,
            AssetId: assetId,
            ScientificObjectId: objectId);
        return new QcResult(
            $"{Id}:{suffix}",
            Id,
            severity,
            Category,
            message,
            effectiveLocation.FigureId,
            effectiveLocation.PanelId,
            effectiveLocation.AssetId,
            effectiveLocation.ScientificObjectId,
            canAutoFix)
        {
            Location = effectiveLocation,
            RelatedLocations = relatedLocations ?? [],
        };
    }
}

internal sealed class ObjectOutsideCanvasRule()
    : QcRuleBase("layout.object-outside-canvas", QcCategory.Layout)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            foreach (FigurePanel panel in figure.Panels.Where(panel =>
                         panel.Frame.X < 0 || panel.Frame.Y < 0 ||
                         panel.Frame.Right > figure.WidthMm + 1e-9 ||
                         panel.Frame.Bottom > figure.HeightMm + 1e-9))
            {
                yield return Issue(
                    QcSeverity.Error,
                    panel.Id.ToString("N"),
                    $"Panel {panel.Label} is outside the figure canvas.",
                    figure.Id,
                    panel.Id,
                    panel.AssetId);
            }
        }
    }
}

internal sealed class PanelAlignmentRule()
    : QcRuleBase("layout.panel-alignment", QcCategory.Layout)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        double tolerance = context.Configuration.AlignmentToleranceMm;
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            FigurePanel[] panels = figure.Panels.ToArray();
            for (int index = 0; index < panels.Length; index++)
            {
                for (int otherIndex = index + 1; otherIndex < panels.Length; otherIndex++)
                {
                    FigurePanel first = panels[index];
                    FigurePanel second = panels[otherIndex];
                    bool nearSameRow = Math.Abs(first.Frame.Y - second.Frame.Y) <= tolerance * 4;
                    bool nearSameColumn = Math.Abs(first.Frame.X - second.Frame.X) <= tolerance * 4;
                    if (nearSameRow && Math.Abs(first.Frame.Y - second.Frame.Y) > tolerance)
                    {
                        yield return Issue(
                            QcSeverity.Warning,
                            $"row:{first.Id:N}:{second.Id:N}",
                            $"Panels {first.Label} and {second.Label} have inconsistent top alignment.",
                            figure.Id,
                            second.Id,
                            second.AssetId,
                            canAutoFix: true);
                    }

                    if (nearSameColumn && Math.Abs(first.Frame.X - second.Frame.X) > tolerance)
                    {
                        yield return Issue(
                            QcSeverity.Warning,
                            $"column:{first.Id:N}:{second.Id:N}",
                            $"Panels {first.Label} and {second.Label} have inconsistent left alignment.",
                            figure.Id,
                            second.Id,
                            second.AssetId,
                            canAutoFix: true);
                    }
                }
            }
        }
    }
}

internal sealed class PanelSpacingRule()
    : QcRuleBase("layout.panel-spacing", QcCategory.Layout)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        double tolerance = context.Configuration.SpacingToleranceMm;
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            double[] horizontalGaps = FindHorizontalGaps(figure.Panels, tolerance).ToArray();
            double[] verticalGaps = FindVerticalGaps(figure.Panels, tolerance).ToArray();
            foreach ((string axis, double[] gaps) in new[]
                     {
                         ("horizontal", horizontalGaps),
                         ("vertical", verticalGaps),
                     })
            {
                if (gaps.Length < 2)
                {
                    continue;
                }

                double median = gaps.OrderBy(value => value).ElementAt(gaps.Length / 2);
                if (gaps.Any(value => Math.Abs(value - median) > tolerance))
                {
                    yield return Issue(
                        QcSeverity.Warning,
                        $"{figure.Id:N}:{axis}",
                        $"Figure has inconsistent {axis} panel spacing.",
                        figure.Id,
                        canAutoFix: true);
                }
            }
        }
    }

    private static IEnumerable<double> FindHorizontalGaps(
        IReadOnlyList<FigurePanel> panels,
        double tolerance)
    {
        foreach (IGrouping<long, FigurePanel> row in panels.GroupBy(panel =>
                     (long)Math.Round(panel.Frame.Y / Math.Max(0.001, tolerance))))
        {
            FigurePanel[] ordered = row.OrderBy(panel => panel.Frame.X).ToArray();
            for (int index = 1; index < ordered.Length; index++)
            {
                double gap = ordered[index].Frame.X - ordered[index - 1].Frame.Right;
                if (gap >= 0)
                {
                    yield return gap;
                }
            }
        }
    }

    private static IEnumerable<double> FindVerticalGaps(
        IReadOnlyList<FigurePanel> panels,
        double tolerance)
    {
        foreach (IGrouping<long, FigurePanel> column in panels.GroupBy(panel =>
                     (long)Math.Round(panel.Frame.X / Math.Max(0.001, tolerance))))
        {
            FigurePanel[] ordered = column.OrderBy(panel => panel.Frame.Y).ToArray();
            for (int index = 1; index < ordered.Length; index++)
            {
                double gap = ordered[index].Frame.Y - ordered[index - 1].Frame.Bottom;
                if (gap >= 0)
                {
                    yield return gap;
                }
            }
        }
    }
}

internal sealed class TypographyConsistencyRule()
    : QcRuleBase("typography.panel-label-consistency", QcCategory.Typography)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            ResolvedProjectStyle[] resolved = figure.Panels
                .Select(panel => ProjectStyleResolver.Resolve(
                    context.Project.Style,
                    figure.StyleOverride,
                    panel.StyleOverride))
                .ToArray();
            if (resolved.Select(item => item.PanelLabel.Value.FontFamily)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"{figure.Id:N}:font",
                    "Figure uses mixed panel-label fonts.",
                    figure.Id);
            }

            if (resolved.Select(item => Math.Round(item.PanelLabel.Value.FontSizePt, 3)).Distinct().Count() > 1)
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"{figure.Id:N}:size",
                    "Figure uses mixed panel-label sizes.",
                    figure.Id);
            }

            foreach (FigurePanel panel in figure.Panels.Where(panel => panel.StyleOverride?.PanelLabel is not null))
            {
                yield return Issue(
                    QcSeverity.Info,
                    $"override:{panel.Id:N}",
                    $"Panel {panel.Label} has a local panel-label style override.",
                    figure.Id,
                    panel.Id,
                    panel.AssetId,
                    canAutoFix: true);
            }
        }
    }
}

internal sealed class FontAvailabilityRule()
    : QcRuleBase("typography.font-availability", QcCategory.Typography)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        if (context.FontCatalog is null)
        {
            yield break;
        }

        foreach (FontUsage usage in FontUsageCollector.Collect(context.Project)
                     .Where(usage => !context.FontCatalog.IsInstalled(usage.RequestedFont)))
        {
            yield return Issue(
                QcSeverity.Warning,
                $"{usage.RequestedFont}:{usage.UsageKind}:{usage.FigureId:N}:{usage.PanelId:N}:{usage.ObjectId:N}",
                $"{usage.UsageKind} font “{usage.RequestedFont}” is not installed on this system. Export will use a fallback font.",
                usage.FigureId,
                usage.PanelId,
                objectId: usage.ObjectId);
        }
    }
}

internal sealed class EffectiveDpiRule()
    : QcRuleBase("resolution.effective-dpi", QcCategory.Resolution)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            foreach (FigurePanel panel in figure.Panels)
            {
                ScientificAsset? asset = context.GetAsset(panel.AssetId);
                if (asset is null)
                {
                    continue;
                }

                double dpi = EffectiveDpiCalculator.Calculate(
                    asset.Image.PixelSize.Width,
                    asset.Image.PixelSize.Height,
                    panel.Crop,
                    panel.Frame.Width,
                    panel.Frame.Height);
                if (dpi < context.Configuration.MinimumEffectiveDpi)
                {
                    yield return Issue(
                        QcSeverity.Warning,
                        panel.Id.ToString("N"),
                        $"Panel {panel.Label} effective DPI is {dpi:0}, below the configured {context.Configuration.MinimumEffectiveDpi:0} DPI threshold.",
                        figure.Id,
                        panel.Id,
                        panel.AssetId);
                }
            }
        }
    }
}

internal sealed class CalibrationRule()
    : QcRuleBase("calibration.asset", QcCategory.Calibration)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        HashSet<Guid> usedAssets = context.Project.Figures.Values
            .SelectMany(figure => figure.Panels)
            .Select(panel => panel.AssetId)
            .Where(id => id != Guid.Empty)
            .ToHashSet();
        foreach (Guid assetId in usedAssets)
        {
            ScientificAsset? asset = context.GetAsset(assetId);
            if (asset is null || !RequiresCalibration(asset.Kind))
            {
                continue;
            }

            if (asset.Calibration is null)
            {
                yield return Issue(
                    context.Configuration.MissingCalibrationIsError ? QcSeverity.Error : QcSeverity.Warning,
                    $"missing:{asset.Id:N}",
                    $"Asset {asset.Name} is missing calibration.",
                    assetId: asset.Id);
                continue;
            }

            if (!asset.Calibration.IsValid)
            {
                yield return Issue(
                    QcSeverity.Error,
                    $"invalid:{asset.Id:N}",
                    $"Asset {asset.Name} has invalid calibration: {asset.Calibration.ValidationMessage}",
                    assetId: asset.Id);
                continue;
            }

            double ratio = Math.Max(
                asset.Calibration.UnitsPerPixelX / asset.Calibration.UnitsPerPixelY,
                asset.Calibration.UnitsPerPixelY / asset.Calibration.UnitsPerPixelX);
            if (ratio > context.Configuration.SuspiciousCalibrationAxisRatio)
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"axis-ratio:{asset.Id:N}",
                    $"Asset {asset.Name} has suspicious X/Y calibration ratio {ratio:0.###}.",
                    assetId: asset.Id);
            }
        }
    }

    private static bool RequiresCalibration(AssetKind kind) => kind is
        AssetKind.Sem or AssetKind.Tem or AssetKind.Stem or AssetKind.Ebsd or
        AssetKind.Eds or AssetKind.Afm or AssetKind.Optical;
}

internal sealed class ScaleBarRule()
    : QcRuleBase("scale-bar.validity", QcCategory.ScaleBar)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScaleBarObject scaleBar in context.Project.ScientificObjects.Values.OfType<ScaleBarObject>())
        {
            ScientificAsset? asset = scaleBar.AssetId is Guid assetId
                ? context.GetAsset(assetId)
                : null;
            if (asset?.HasValidCalibration != true)
            {
                yield return Issue(
                    QcSeverity.Error,
                    scaleBar.Id.ToString("N"),
                    "Scale bar has no valid calibration.",
                    panelId: scaleBar.PanelId,
                    assetId: scaleBar.AssetId,
                    objectId: scaleBar.Id);
            }
            else if (scaleBar.Validity.State is ScientificValidityState.Invalid or ScientificValidityState.ReviewRequired)
            {
                yield return Issue(
                    QcSeverity.Error,
                    $"state:{scaleBar.Id:N}",
                    "Scale bar is invalid after a source or calibration revision.",
                    panelId: scaleBar.PanelId,
                    assetId: scaleBar.AssetId,
                    objectId: scaleBar.Id);
            }
        }
    }
}

internal sealed class PanelLabelRule()
    : QcRuleBase("panel-label.sequence", QcCategory.PanelLabels)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            if (figure.LabelScheme == PanelLabelScheme.None)
            {
                continue;
            }

            FigurePanel[] ordered = figure.Panels
                .OrderBy(panel => panel.Frame.Y)
                .ThenBy(panel => panel.Frame.X)
                .ToArray();
            foreach (FigurePanel panel in ordered.Where(panel => string.IsNullOrWhiteSpace(panel.Label)))
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"missing:{panel.Id:N}",
                    "Panel label is missing.",
                    figure.Id,
                    panel.Id,
                    panel.AssetId,
                    canAutoFix: true);
            }

            string[] labels = ordered
                .Select(panel => NormalizeLabel(panel.Label))
                .Where(label => label.Length > 0)
                .ToArray();
            foreach (string duplicate in labels
                         .GroupBy(label => label, StringComparer.OrdinalIgnoreCase)
                         .Where(group => group.Count() > 1)
                         .Select(group => group.Key))
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"duplicate:{figure.Id:N}:{duplicate}",
                    $"Duplicate panel label ({duplicate}).",
                    figure.Id,
                    canAutoFix: true);
            }

            if (figure.LabelScheme is PanelLabelScheme.Custom)
            {
                continue;
            }

            for (int index = 0; index < ordered.Length; index++)
            {
                string expected = PanelLabelGenerator.Generate(index, figure.LabelScheme);
                string observed = PanelLabelGenerator.NormalizeForComparison(ordered[index].Label);
                if (!string.Equals(observed, expected, StringComparison.Ordinal))
                {
                    yield return Issue(
                        QcSeverity.Warning,
                        $"sequence:{figure.Id:N}:{index}",
                        $"Panel label sequence expects ({expected}) at position {index + 1}.",
                        figure.Id,
                        ordered[index].Id,
                        ordered[index].AssetId,
                        canAutoFix: true);
                }
            }
        }
    }

    private static string NormalizeLabel(string? label) =>
        PanelLabelGenerator.NormalizeForComparison(label);
}

internal sealed class SourceTrackingRule()
    : QcRuleBase("source.tracking", QcCategory.Source)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificAsset asset in context.Project.Assets.Values)
        {
            if (asset.LinkState == SourceLinkState.Missing)
            {
                yield return Issue(
                    QcSeverity.Error,
                    $"missing:{asset.Id:N}",
                    $"Asset {asset.Name} source is missing.",
                    assetId: asset.Id);
            }
            else if (asset.LinkState == SourceLinkState.Modified)
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"changed:{asset.Id:N}",
                    $"Asset {asset.Name} source changed since import.",
                    assetId: asset.Id);
            }
            else if (asset.LinkState == SourceLinkState.Unverified)
            {
                yield return Issue(
                    QcSeverity.Warning,
                    $"unverified:{asset.Id:N}",
                    $"Asset {asset.Name} source has not been verified.",
                    assetId: asset.Id);
            }
        }
    }
}

internal sealed class ExactDuplicateSourceRule()
    : QcRuleBase("integrity.exact-duplicate-source", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (IGrouping<string, ScientificAsset> group in context.Project.Assets.Values
                     .Where(asset => !string.IsNullOrWhiteSpace(asset.Source.Fingerprint.Sha256))
                     .GroupBy(asset => asset.Source.Fingerprint.Sha256, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            ScientificAsset[] duplicates = group.OrderBy(asset => asset.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            yield return Issue(
                QcSeverity.Warning,
                group.Key,
                $"Assets {string.Join(", ", duplicates.Select(asset => asset.Name))} reference exactly the same source content.",
                assetId: duplicates[0].Id);
        }
    }
}

internal sealed class DuplicateCropRule()
    : QcRuleBase("integrity.crop-reuse", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificFigure figure in context.Project.Figures.Values)
        {
            FigurePanel[] panels = figure.Panels.ToArray();
            for (int index = 0; index < panels.Length; index++)
            {
                for (int otherIndex = index + 1; otherIndex < panels.Length; otherIndex++)
                {
                    FigurePanel first = panels[index];
                    FigurePanel second = panels[otherIndex];
                    if (first.AssetId == Guid.Empty || first.AssetId != second.AssetId ||
                        first.FrameIndex != second.FrameIndex ||
                        context.GetAsset(first.AssetId) is not { } asset)
                    {
                        continue;
                    }

                    var firstRect = first.ResolveSourcePixels(asset.Image.PixelSize.Width, asset.Image.PixelSize.Height);
                    var secondRect = second.ResolveSourcePixels(asset.Image.PixelSize.Width, asset.Image.PixelSize.Height);
                    if (firstRect == secondRect)
                    {
                        yield return Issue(
                            QcSeverity.Warning,
                            $"exact:{first.Id:N}:{second.Id:N}",
                            $"Panels ({first.Label}) and ({second.Label}) use the exact same source crop.",
                            figure.Id,
                            second.Id,
                            second.AssetId);
                        continue;
                    }

                    long left = Math.Max(firstRect.X, secondRect.X);
                    long top = Math.Max(firstRect.Y, secondRect.Y);
                    long right = Math.Min(firstRect.Right, secondRect.Right);
                    long bottom = Math.Min(firstRect.Bottom, secondRect.Bottom);
                    long intersection = Math.Max(0, right - left) * Math.Max(0, bottom - top);
                    long smallerArea = Math.Min(firstRect.Width * firstRect.Height, secondRect.Width * secondRect.Height);
                    if (smallerArea > 0 && intersection / (double)smallerArea > 0.90)
                    {
                        yield return Issue(
                            QcSeverity.Warning,
                            $"overlap:{first.Id:N}:{second.Id:N}",
                            $"Panels ({first.Label}) and ({second.Label}) reuse more than 90% of the same source crop.",
                            figure.Id,
                            second.Id,
                            second.AssetId);
                    }
                }
            }
        }
    }
}

internal sealed class StaleScientificObjectRule()
    : QcRuleBase("integrity.source-revision", QcCategory.Integrity)
{
    public override IEnumerable<QcResult> Evaluate(QcContext context)
    {
        foreach (ScientificObject scientificObject in context.Project.ScientificObjects.Values)
        {
            if (scientificObject.AssetId is not Guid assetId ||
                context.GetAsset(assetId) is not { } asset)
            {
                continue;
            }

            long? capturedRevision = scientificObject switch
            {
                AnalysisResultObject analysis => analysis.Result.SourceRevision,
                _ => scientificObject.SourceRevision,
            };
            if (capturedRevision.HasValue && capturedRevision.Value != asset.Source.SourceRevision)
            {
                yield return Issue(
                    QcSeverity.Error,
                    scientificObject.Id.ToString("N"),
                    $"{scientificObject.Kind} uses source revision {capturedRevision}; current revision is {asset.Source.SourceRevision}. Review or recompute it before submission.",
                    panelId: scientificObject.PanelId,
                    assetId: assetId,
                    objectId: scientificObject.Id);
            }
        }
    }
}
