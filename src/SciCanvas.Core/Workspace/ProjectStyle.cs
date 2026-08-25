namespace SciCanvas.Core.Workspace;

public sealed record TextStyle(
    string FontFamily,
    double FontSizePt,
    bool IsBold,
    string Color)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 128 ||
            !double.IsFinite(FontSizePt) || FontSizePt is < 4 or > 72 ||
            !StyleColor.IsValid(Color))
        {
            throw new InvalidOperationException("文字样式无效：字体、字号或颜色不符合要求。" );
        }
    }
}

public sealed record LineStyle(double StandardWidthPt, double BorderWidthPt, string Color)
{
    public void EnsureValid()
    {
        if (!double.IsFinite(StandardWidthPt) || StandardWidthPt is < 0.25 or > 10 ||
            !double.IsFinite(BorderWidthPt) || BorderWidthPt is < 0 or > 10 ||
            !StyleColor.IsValid(Color))
        {
            throw new InvalidOperationException("线条样式无效。" );
        }
    }
}

public sealed record PanelLayoutStyle(double DefaultGapMm)
{
    public void EnsureValid()
    {
        if (!double.IsFinite(DefaultGapMm) || DefaultGapMm is < 0 or > 100)
        {
            throw new InvalidOperationException("Panel 默认间距必须是 0–100 mm 的有限数值。" );
        }
    }
}

public enum ScaleBarAnchor
{
    BottomLeft,
    BottomRight,
    TopLeft,
    TopRight,
    Custom,
}

public sealed record ScaleBarStyle(
    ScaleBarAnchor DefaultPosition,
    double BarThicknessPt,
    string Color)
{
    public void EnsureValid()
    {
        if (!double.IsFinite(BarThicknessPt) || BarThicknessPt is < 0.25 or > 10 ||
            !StyleColor.IsValid(Color))
        {
            throw new InvalidOperationException("比例尺样式无效。" );
        }
    }
}

public sealed record ProjectStyle(
    TextStyle PanelLabel,
    TextStyle Annotation,
    TextStyle ScaleBarText,
    LineStyle Lines,
    PanelLayoutStyle Panel,
    ScaleBarStyle ScaleBar)
{
    public static ProjectStyle Default { get; } = new(
        new TextStyle("Arial", 8, true, "#FF111111"),
        new TextStyle("Arial", 7, false, "#FF111111"),
        new TextStyle("Arial", 7, true, "#FFFFFFFF"),
        new LineStyle(1.0, 0.5, "#FF111111"),
        new PanelLayoutStyle(2.0),
        new ScaleBarStyle(ScaleBarAnchor.BottomRight, 2.0, "#FFFFFFFF"));

    public void EnsureValid()
    {
        PanelLabel.EnsureValid();
        Annotation.EnsureValid();
        ScaleBarText.EnsureValid();
        Lines.EnsureValid();
        Panel.EnsureValid();
        ScaleBar.EnsureValid();
    }
}

public sealed record StyleOverride(
    TextStyle? PanelLabel = null,
    TextStyle? Annotation = null,
    TextStyle? ScaleBarText = null,
    LineStyle? Lines = null,
    PanelLayoutStyle? Panel = null,
    ScaleBarStyle? ScaleBar = null)
{
    public bool IsEmpty =>
        PanelLabel is null && Annotation is null && ScaleBarText is null &&
        Lines is null && Panel is null && ScaleBar is null;

    public StyleOverride ResetPanelLabel() => this with { PanelLabel = null };

    public StyleOverride ResetAnnotation() => this with { Annotation = null };

    public StyleOverride ResetScaleBar() => this with
    {
        ScaleBarText = null,
        ScaleBar = null,
    };

    public void EnsureValid()
    {
        PanelLabel?.EnsureValid();
        Annotation?.EnsureValid();
        ScaleBarText?.EnsureValid();
        Lines?.EnsureValid();
        Panel?.EnsureValid();
        ScaleBar?.EnsureValid();
    }
}

public enum StyleInheritanceSource
{
    Project,
    Figure,
    Panel,
    Object,
}

public sealed record ResolvedStyleValue<T>(T Value, StyleInheritanceSource Source)
{
    public bool IsOverride => Source != StyleInheritanceSource.Project;
}

public sealed record ResolvedProjectStyle(
    ResolvedStyleValue<TextStyle> PanelLabel,
    ResolvedStyleValue<TextStyle> Annotation,
    ResolvedStyleValue<TextStyle> ScaleBarText,
    ResolvedStyleValue<LineStyle> Lines,
    ResolvedStyleValue<PanelLayoutStyle> Panel,
    ResolvedStyleValue<ScaleBarStyle> ScaleBar)
{
    public ProjectStyle Value => new(
        PanelLabel.Value,
        Annotation.Value,
        ScaleBarText.Value,
        Lines.Value,
        Panel.Value,
        ScaleBar.Value);
}

public static class ProjectStyleResolver
{
    public static ResolvedProjectStyle Resolve(
        ProjectStyle project,
        StyleOverride? figure = null,
        StyleOverride? panel = null,
        StyleOverride? scientificObject = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        project.EnsureValid();
        figure?.EnsureValid();
        panel?.EnsureValid();
        scientificObject?.EnsureValid();

        return new ResolvedProjectStyle(
            ResolveValue(project.PanelLabel, figure?.PanelLabel, panel?.PanelLabel, scientificObject?.PanelLabel),
            ResolveValue(project.Annotation, figure?.Annotation, panel?.Annotation, scientificObject?.Annotation),
            ResolveValue(project.ScaleBarText, figure?.ScaleBarText, panel?.ScaleBarText, scientificObject?.ScaleBarText),
            ResolveValue(project.Lines, figure?.Lines, panel?.Lines, scientificObject?.Lines),
            ResolveValue(project.Panel, figure?.Panel, panel?.Panel, scientificObject?.Panel),
            ResolveValue(project.ScaleBar, figure?.ScaleBar, panel?.ScaleBar, scientificObject?.ScaleBar));
    }

    public static StyleOverride CopyVisualStyle(StyleOverride? source) => source ?? new StyleOverride();

    private static ResolvedStyleValue<T> ResolveValue<T>(
        T project,
        T? figure,
        T? panel,
        T? scientificObject)
        where T : class
    {
        if (scientificObject is not null)
        {
            return new(scientificObject, StyleInheritanceSource.Object);
        }

        if (panel is not null)
        {
            return new(panel, StyleInheritanceSource.Panel);
        }

        if (figure is not null)
        {
            return new(figure, StyleInheritanceSource.Figure);
        }

        return new(project, StyleInheritanceSource.Project);
    }
}

internal static class StyleColor
{
    public static bool IsValid(string? value)
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }
}
