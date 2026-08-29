namespace SciCanvas.Core.Workspace;

public enum FontResolutionKind
{
    Exact,
    ExplicitSubstitution,
    SystemFallback,
}

public sealed record FontSubstitutionRule(
    string RequestedFontFamily,
    string SubstituteFontFamily)
{
    public FontSubstitutionRule EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(RequestedFontFamily) || RequestedFontFamily.Trim().Length > 128 ||
            string.IsNullOrWhiteSpace(SubstituteFontFamily) || SubstituteFontFamily.Trim().Length > 128 ||
            string.Equals(RequestedFontFamily.Trim(), SubstituteFontFamily.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("字体替换必须包含两个不同的有效字体族名称。");
        }

        return this;
    }
}

public sealed record ResolvedFont(
    string RequestedFamily,
    string EffectiveFamily,
    FontResolutionKind ResolutionKind,
    FontSubstitutionRule? SubstitutionRule = null,
    string? Warning = null);

/// <summary>
/// Resolves an effective render font without mutating the requested family saved
/// in object/project style.
/// </summary>
public sealed class FontResolutionService(
    IFontCatalog fontCatalog,
    IEnumerable<string>? fallbackFamilies = null)
{
    private readonly IFontCatalog _fontCatalog = fontCatalog ?? throw new ArgumentNullException(nameof(fontCatalog));
    private readonly string[] _fallbackFamilies = (fallbackFamilies ?? ["Arial", "Segoe UI", "sans-serif"])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public ResolvedFont Resolve(
        string requestedFontFamily,
        IEnumerable<FontSubstitutionRule>? substitutions = null)
    {
        if (string.IsNullOrWhiteSpace(requestedFontFamily) || requestedFontFamily.Trim().Length > 128)
        {
            throw new ArgumentException("Requested font family 无效。", nameof(requestedFontFamily));
        }

        string requested = requestedFontFamily.Trim();
        if (_fontCatalog.IsInstalled(requested))
        {
            return new ResolvedFont(requested, requested, FontResolutionKind.Exact);
        }

        FontSubstitutionRule? rule = (substitutions ?? [])
            .Select(item => item.EnsureValid())
            .SingleOrDefault(item => string.Equals(
                item.RequestedFontFamily.Trim(),
                requested,
                StringComparison.OrdinalIgnoreCase));
        if (rule is not null && _fontCatalog.IsInstalled(rule.SubstituteFontFamily))
        {
            return new ResolvedFont(
                requested,
                rule.SubstituteFontFamily.Trim(),
                FontResolutionKind.ExplicitSubstitution,
                rule);
        }

        string fallback = _fallbackFamilies.FirstOrDefault(_fontCatalog.IsInstalled) ??
                          _fontCatalog.InstalledFontFamilies.FirstOrDefault() ??
                          throw new InvalidOperationException("当前系统没有可用字体，无法解析 render font。");
        string warning = rule is null
            ? $"Requested font “{requested}” is missing; using system fallback “{fallback}”."
            : $"Requested font “{requested}” and explicit substitute “{rule.SubstituteFontFamily}” are missing; using system fallback “{fallback}”.";
        return new ResolvedFont(
            requested,
            fallback,
            FontResolutionKind.SystemFallback,
            rule,
            warning);
    }
}
