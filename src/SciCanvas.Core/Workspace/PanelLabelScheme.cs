using System.Globalization;

namespace SciCanvas.Core.Workspace;

public enum PanelLabelScheme
{
    LowerAlpha,
    UpperAlpha,
    Numeric,
    Custom,
    None,
}

public static class PanelLabelGenerator
{
    public static string Generate(int index, PanelLabelScheme scheme)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return scheme switch
        {
            PanelLabelScheme.LowerAlpha => GenerateAlphabetic(index, uppercase: false),
            PanelLabelScheme.UpperAlpha => GenerateAlphabetic(index, uppercase: true),
            PanelLabelScheme.Numeric => (index + 1).ToString(CultureInfo.InvariantCulture),
            PanelLabelScheme.None => string.Empty,
            PanelLabelScheme.Custom => throw new InvalidOperationException(
                "Custom panel labels must be supplied explicitly and cannot be auto-generated."),
            _ => throw new ArgumentOutOfRangeException(nameof(scheme)),
        };
    }

    public static PanelLabelScheme FromLegacySettings(
        string? sequence,
        bool showLabels = true,
        bool autoLabels = true)
    {
        if (!showLabels)
        {
            return PanelLabelScheme.None;
        }

        if (!autoLabels)
        {
            return PanelLabelScheme.Custom;
        }

        return sequence?.Trim().ToLowerInvariant() switch
        {
            "uppercase" => PanelLabelScheme.UpperAlpha,
            "numeric" => PanelLabelScheme.Numeric,
            _ => PanelLabelScheme.LowerAlpha,
        };
    }

    public static string ToLegacySequence(PanelLabelScheme scheme) => scheme switch
    {
        PanelLabelScheme.UpperAlpha => "uppercase",
        PanelLabelScheme.Numeric => "numeric",
        _ => "lowercase",
    };

    public static string NormalizeForComparison(string? label) =>
        (label ?? string.Empty).Trim().Trim('(', ')');

    private static string GenerateAlphabetic(int index, bool uppercase)
    {
        int value = index;
        Span<char> buffer = stackalloc char[16];
        int position = buffer.Length;
        char first = uppercase ? 'A' : 'a';
        do
        {
            int digit = value % 26;
            buffer[--position] = (char)(first + digit);
            value = value / 26 - 1;
        }
        while (value >= 0);

        return new string(buffer[position..]);
    }
}
