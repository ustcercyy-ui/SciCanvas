namespace SciCanvas.Core.Export;

public sealed record ScientificColorDefinition(Guid Id, string Name, string Color)
{
    public bool IsValid =>
        Id != Guid.Empty &&
        !string.IsNullOrWhiteSpace(Name) &&
        Name.Trim().Length <= 64 &&
        IsHexColor(Color);

    internal static bool IsHexColor(string? color)
    {
        string hex = color?.Trim().TrimStart('#') ?? string.Empty;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }
}

public sealed record ScientificColorPaletteReview(
    IReadOnlyList<string> Warnings)
{
    public bool IsValid => Warnings.Count == 0;
}

public static class ScientificColorPalette
{
    public static IReadOnlyList<ScientificColorDefinition> Default =>
    [
        new(Guid.Parse("C1A10000-0000-4000-8000-000000000001"), "α phase", "#FF0072B2"),
        new(Guid.Parse("C1A10000-0000-4000-8000-000000000002"), "β phase", "#FFE69F00"),
        new(Guid.Parse("C1A10000-0000-4000-8000-000000000003"), "GB", "#FF000000"),
        new(Guid.Parse("C1A10000-0000-4000-8000-000000000004"), "TB", "#FFD55E00"),
    ];

    public static ScientificColorPaletteReview Review(
        IEnumerable<ScientificColorDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ScientificColorDefinition[] colors = definitions.ToArray();
        var warnings = new List<string>();
        if (colors.Any(color => !color.IsValid))
        {
            warnings.Add("存在名称为空、过长或 HEX 无效的颜色条目。");
        }

        if (colors.GroupBy(color => color.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            warnings.Add("同一物理对象名称出现多次，请合并后再用于全项目。");
        }

        for (int first = 0; first < colors.Length; first++)
        {
            for (int second = first + 1; second < colors.Length; second++)
            {
                if (!colors[first].IsValid || !colors[second].IsValid)
                {
                    continue;
                }

                double distance = SimulatedDeuteranopiaDistance(colors[first].Color, colors[second].Color);
                if (distance < 0.12)
                {
                    warnings.Add($"{colors[first].Name} 与 {colors[second].Name} 在红绿色觉缺陷模拟下可能难以区分。");
                }
            }
        }

        return new ScientificColorPaletteReview(warnings.Distinct().ToArray());
    }

    private static double SimulatedDeuteranopiaDistance(string first, string second)
    {
        (double r, double g, double b) a = Simulate(Parse(first));
        (double r, double g, double b) b = Simulate(Parse(second));
        return Math.Sqrt(
            Math.Pow(a.r - b.r, 2) +
            Math.Pow(a.g - b.g, 2) +
            Math.Pow(a.b - b.b, 2));
    }

    private static (double r, double g, double b) Simulate((double r, double g, double b) color) =>
        (
            0.625 * color.r + 0.375 * color.g,
            0.700 * color.r + 0.300 * color.g,
            0.300 * color.g + 0.700 * color.b);

    private static (double r, double g, double b) Parse(string value)
    {
        string hex = value.Trim().TrimStart('#');
        if (hex.Length == 8)
        {
            hex = hex[2..];
        }

        return (
            Convert.ToByte(hex[0..2], 16) / 255d,
            Convert.ToByte(hex[2..4], 16) / 255d,
            Convert.ToByte(hex[4..6], 16) / 255d);
    }
}
