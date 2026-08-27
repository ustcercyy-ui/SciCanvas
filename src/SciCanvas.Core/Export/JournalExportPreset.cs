namespace SciCanvas.Core.Export;

/// <summary>
/// Publisher-neutral submission constraints. Presets are data, not hard-coded
/// journal claims, so future catalog updates do not change saved projects.
/// </summary>
public sealed record JournalExportPreset
{
    private static readonly HashSet<string> SupportedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "tiff", "png", "jpg", "pdf", "svg",
    };

    public JournalExportPreset(
        string id,
        string name,
        double figureWidthMm,
        double? figureHeightMm,
        int minimumDpi,
        string preferredFormat,
        IEnumerable<string> allowedFormats,
        string colorMode,
        double? maximumFileSizeMb = null)
    {
        Id = NormalizeRequired(id, nameof(id));
        Name = NormalizeRequired(name, nameof(name));
        if (!double.IsFinite(figureWidthMm) || figureWidthMm <= 0 || figureWidthMm > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(figureWidthMm));
        }

        if (figureHeightMm is not null &&
            (!double.IsFinite(figureHeightMm.Value) || figureHeightMm.Value <= 0 || figureHeightMm.Value > 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(figureHeightMm));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumDpi);
        if (minimumDpi > 4800)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumDpi));
        }

        string[] formats = allowedFormats
            .Select(NormalizeFormat)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (formats.Length == 0)
        {
            throw new ArgumentException("至少需要一种允许的导出格式。", nameof(allowedFormats));
        }

        string preferred = NormalizeFormat(preferredFormat);
        if (!formats.Contains(preferred, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException("首选格式必须包含在允许格式中。", nameof(preferredFormat));
        }

        string normalizedColorMode = NormalizeRequired(colorMode, nameof(colorMode)).ToUpperInvariant();
        if (normalizedColorMode is not ("RGB" or "GRAYSCALE" or "CMYK"))
        {
            throw new ArgumentException("颜色模式只支持 RGB、Grayscale 或 CMYK。", nameof(colorMode));
        }

        if (maximumFileSizeMb is not null &&
            (!double.IsFinite(maximumFileSizeMb.Value) || maximumFileSizeMb.Value <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumFileSizeMb));
        }

        FigureWidthMm = figureWidthMm;
        FigureHeightMm = figureHeightMm;
        MinimumDpi = minimumDpi;
        PreferredFormat = preferred;
        AllowedFormats = formats;
        ColorMode = normalizedColorMode;
        MaximumFileSizeMb = maximumFileSizeMb;
    }

    public string Id { get; }

    public string Name { get; }

    public double FigureWidthMm { get; }

    public double? FigureHeightMm { get; }

    public int MinimumDpi { get; }

    public string PreferredFormat { get; }

    public IReadOnlyList<string> AllowedFormats { get; }

    public string ColorMode { get; }

    public double? MaximumFileSizeMb { get; }

    public FigureExportProfile CreateProfile(string? id = null, string? name = null, int bitDepth = 8)
    {
        int widthPixels = MillimetersToPixels(FigureWidthMm, MinimumDpi);
        int? heightPixels = FigureHeightMm is double height
            ? MillimetersToPixels(height, MinimumDpi)
            : null;
        return new FigureExportProfile(
            id ?? Id,
            name ?? Name,
            PreferredFormat,
            MinimumDpi,
            widthPixels: widthPixels,
            heightPixels: heightPixels,
            bitDepth: bitDepth);
    }

    public static IReadOnlyList<JournalExportPreset> BuiltIns { get; } =
    [
        new("generic-single-column", "Single Column", 89, null, 300, "tiff", ["tiff", "png", "pdf", "svg"], "RGB"),
        new("generic-double-column", "Double Column", 183, null, 300, "tiff", ["tiff", "png", "pdf", "svg"], "RGB"),
        new("generic-full-page", "Full Page", 183, 247, 300, "tiff", ["tiff", "png", "pdf"], "RGB"),
        new("generic-line-art", "High Resolution Line Art", 183, null, 1200, "tiff", ["tiff", "pdf", "svg"], "GRAYSCALE"),
    ];

    private static int MillimetersToPixels(double millimeters, int dpi) =>
        checked((int)Math.Round(millimeters / 25.4 * dpi, MidpointRounding.AwayFromZero));

    private static string NormalizeFormat(string value)
    {
        string format = NormalizeRequired(value, nameof(value)).TrimStart('.').ToLowerInvariant();
        format = format switch
        {
            "tif" => "tiff",
            "jpeg" => "jpg",
            _ => format,
        };
        return SupportedFormats.Contains(format)
            ? format
            : throw new ArgumentException($"不支持的投稿格式：{value}。", nameof(value));
    }

    private static string NormalizeRequired(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("值不能为空。", parameterName)
            : value.Trim();
}
