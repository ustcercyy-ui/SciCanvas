namespace SciCanvas.Core.Channels;

public static class ScientificColormap
{
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
}
