using System.Collections.ObjectModel;
using System.Windows.Media;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Imaging;

public sealed class SystemFontCatalog : IFontCatalog
{
    public static SystemFontCatalog Instance { get; } = new();

    private readonly string[] _installedFontFamilies;
    private readonly HashSet<string> _lookup;

    private SystemFontCatalog()
    {
        _installedFontFamilies = Fonts.SystemFontFamilies
            .Select(font => font.Source)
            .Where(source => !string.IsNullOrWhiteSpace(source))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(source => source, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _lookup = _installedFontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        InstalledFontFamilies = new ReadOnlyCollection<string>(_installedFontFamilies);
    }

    public IReadOnlyList<string> InstalledFontFamilies { get; }

    public bool IsInstalled(string? fontFamily) =>
        !string.IsNullOrWhiteSpace(fontFamily) && _lookup.Contains(fontFamily.Trim());

    public IReadOnlyList<string> CreateChoices(string? currentFontFamily)
    {
        string current = currentFontFamily?.Trim() ?? string.Empty;
        if (current.Length == 0 || IsInstalled(current))
        {
            return InstalledFontFamilies;
        }

        return _installedFontFamilies
            .Append(current)
            .OrderBy(source => source, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
