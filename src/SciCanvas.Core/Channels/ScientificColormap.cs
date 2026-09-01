using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Channels;

public static class ScientificColormap
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<ScientificColorValue>> Palettes =
        new Dictionary<string, IReadOnlyList<ScientificColorValue>>(StringComparer.OrdinalIgnoreCase)
        {
            ["viridis"] = Palette((68, 1, 84), (59, 82, 139), (33, 145, 140), (94, 201, 98), (253, 231, 37)),
            ["magma"] = Palette((0, 0, 4), (115, 20, 117), (252, 136, 97), (252, 253, 191)),
            ["plasma"] = Palette((13, 8, 135), (126, 3, 168), (204, 71, 120), (248, 149, 64), (240, 249, 33)),
            ["inferno"] = Palette((0, 0, 4), (87, 16, 110), (188, 55, 84), (249, 142, 8), (252, 255, 164)),
            ["cividis"] = Palette((0, 32, 77), (40, 72, 110), (87, 108, 116), (145, 143, 111), (253, 234, 69)),
            ["turbo"] = Palette((48, 18, 59), (50, 104, 210), (44, 203, 128), (245, 210, 65), (180, 4, 38)),
            ["grayscale"] = Palette((0, 0, 0), (255, 255, 255)),
        };

    private static readonly string[] SupportedValues =
    [
        "viridis",
        "magma",
        "plasma",
        "inferno",
        "cividis",
        "turbo",
        "grayscale",
    ];

    public static IReadOnlyList<string> Supported { get; } =
        Array.AsReadOnly(SupportedValues);

    public static bool IsSupported(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        SupportedValues.Contains(value.Trim(), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim().ToLowerInvariant();
        if (!IsSupported(normalized))
        {
            throw new InvalidOperationException($"不支持的 scientific colormap：{value}。");
        }

        return normalized;
    }

    public static IReadOnlyList<ScientificColorValue> GetStops(string value) =>
        Palettes[Normalize(value)];

    public static ScientificColorValue Sample(string value, double normalizedValue)
    {
        if (!double.IsFinite(normalizedValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(normalizedValue),
                "Colormap sample position must be finite.");
        }

        IReadOnlyList<ScientificColorValue> stops = GetStops(value);
        double position = Math.Clamp(normalizedValue, 0, 1) * (stops.Count - 1);
        int lowerIndex = Math.Min((int)Math.Floor(position), stops.Count - 1);
        int upperIndex = Math.Min(lowerIndex + 1, stops.Count - 1);
        double fraction = position - lowerIndex;
        ScientificColorValue lower = stops[lowerIndex];
        ScientificColorValue upper = stops[upperIndex];
        return new ScientificColorValue(
            Interpolate(lower.Alpha, upper.Alpha, fraction),
            Interpolate(lower.Red, upper.Red, fraction),
            Interpolate(lower.Green, upper.Green, fraction),
            Interpolate(lower.Blue, upper.Blue, fraction));
    }

    private static byte Interpolate(byte lower, byte upper, double fraction) =>
        (byte)Math.Round(lower + (upper - lower) * fraction, MidpointRounding.AwayFromZero);

    private static IReadOnlyList<ScientificColorValue> Palette(
        params (byte Red, byte Green, byte Blue)[] colors) =>
        Array.AsReadOnly(colors
            .Select(color => new ScientificColorValue(byte.MaxValue, color.Red, color.Green, color.Blue))
            .ToArray());
}
