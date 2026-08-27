namespace SciCanvas.Core.Workspace;

public interface IFontCatalog
{
    IReadOnlyList<string> InstalledFontFamilies { get; }

    bool IsInstalled(string? fontFamily);
}

public sealed class FixedFontCatalog(IEnumerable<string> fontFamilies) : IFontCatalog
{
    private readonly string[] _fontFamilies = fontFamilies
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
        .ToArray();

    public IReadOnlyList<string> InstalledFontFamilies => _fontFamilies;

    public bool IsInstalled(string? fontFamily) =>
        !string.IsNullOrWhiteSpace(fontFamily) &&
        _fontFamilies.Contains(fontFamily.Trim(), StringComparer.OrdinalIgnoreCase);
}
