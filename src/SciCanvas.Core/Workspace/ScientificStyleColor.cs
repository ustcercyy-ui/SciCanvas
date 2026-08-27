using System.Globalization;

namespace SciCanvas.Core.Workspace;

public readonly record struct ScientificColorValue(byte Alpha, byte Red, byte Green, byte Blue)
{
    public string ToHex() => $"#{Alpha:X2}{Red:X2}{Green:X2}{Blue:X2}";
}

public static class ScientificStyleColor
{
    public static string NormalizeColor(string value)
    {
        if (!TryParseColor(value, out ScientificColorValue color))
        {
            throw new FormatException("颜色必须使用 #RRGGBB 或 #AARRGGBB。");
        }

        return color.ToHex();
    }

    public static bool ValidateColor(string? value) => TryParseColor(value, out _);

    public static bool TryParseColor(string? value, out ScientificColorValue color)
    {
        color = default;
        string hex = value?.Trim() ?? string.Empty;
        if (!hex.StartsWith('#') || hex.Length is not (7 or 9))
        {
            return false;
        }

        ReadOnlySpan<char> digits = hex.AsSpan(1);
        byte alpha = byte.MaxValue;
        int offset = 0;
        if (digits.Length == 8)
        {
            if (!TryByte(digits[..2], out alpha))
            {
                return false;
            }

            offset = 2;
        }

        if (!TryByte(digits.Slice(offset, 2), out byte red) ||
            !TryByte(digits.Slice(offset + 2, 2), out byte green) ||
            !TryByte(digits.Slice(offset + 4, 2), out byte blue))
        {
            return false;
        }

        color = new ScientificColorValue(alpha, red, green, blue);
        return true;
    }

    private static bool TryByte(ReadOnlySpan<char> digits, out byte value) =>
        byte.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
}
