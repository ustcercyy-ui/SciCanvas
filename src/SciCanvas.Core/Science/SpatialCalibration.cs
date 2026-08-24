namespace SciCanvas.Core.Science;

public enum CalibrationOrigin
{
    None,
    Metadata,
    Manual,
    Linked,
}

public sealed record SpatialCalibration(
    Guid SourceAssetId,
    double UnitsPerPixelX,
    double UnitsPerPixelY,
    string Unit,
    CalibrationOrigin Origin,
    double? ReferencePixelLength = null,
    double? ReferencePhysicalLength = null)
{
    public bool IsValid =>
        SourceAssetId != Guid.Empty &&
        double.IsFinite(UnitsPerPixelX) && UnitsPerPixelX > 0 &&
        double.IsFinite(UnitsPerPixelY) && UnitsPerPixelY > 0 &&
        !string.IsNullOrWhiteSpace(Unit) &&
        Origin != CalibrationOrigin.None;

    public bool IsAnisotropic => IsValid &&
        Math.Abs(UnitsPerPixelX - UnitsPerPixelY) /
        Math.Max(UnitsPerPixelX, UnitsPerPixelY) > 0.001;

    public double ConvertDistance(double deltaPixelsX, double deltaPixelsY)
    {
        EnsureValid();
        return Math.Sqrt(
            Math.Pow(deltaPixelsX * UnitsPerPixelX, 2) +
            Math.Pow(deltaPixelsY * UnitsPerPixelY, 2));
    }

    public (double Width, double Height) ConvertRectangle(
        double widthPixels,
        double heightPixels)
    {
        EnsureValid();
        return (
            Math.Abs(widthPixels) * UnitsPerPixelX,
            Math.Abs(heightPixels) * UnitsPerPixelY);
    }

    public static SpatialCalibration Uncalibrated(Guid sourceAssetId) => new(
        sourceAssetId,
        0,
        0,
        string.Empty,
        CalibrationOrigin.None);

    public static SpatialCalibration FromReference(
        Guid sourceAssetId,
        double referencePixelLength,
        double referencePhysicalLength,
        string unit)
    {
        if (!double.IsFinite(referencePixelLength) || referencePixelLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(referencePixelLength),
                "参考线像素长度必须大于 0。");
        }

        if (!double.IsFinite(referencePhysicalLength) || referencePhysicalLength <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(referencePhysicalLength),
                "参考线真实长度必须大于 0。");
        }

        string normalizedUnit = ScientificLengthUnits.Normalize(unit);
        double unitsPerPixel = referencePhysicalLength / referencePixelLength;
        return new SpatialCalibration(
            sourceAssetId,
            unitsPerPixel,
            unitsPerPixel,
            normalizedUnit,
            CalibrationOrigin.Manual,
            referencePixelLength,
            referencePhysicalLength);
    }

    public string ValidationMessage
    {
        get
        {
            if (SourceAssetId == Guid.Empty)
            {
                return "标定缺少源图像 ID。";
            }

            if (!double.IsFinite(UnitsPerPixelX) || UnitsPerPixelX <= 0 ||
                !double.IsFinite(UnitsPerPixelY) || UnitsPerPixelY <= 0)
            {
                return "X/Y 每像素物理尺寸必须为大于 0 的有限数值。";
            }

            if (string.IsNullOrWhiteSpace(Unit))
            {
                return "标定必须指定物理单位。";
            }

            if (Origin == CalibrationOrigin.None)
            {
                return "尚未建立标定。";
            }

            return IsAnisotropic
                ? "标定有效，但 X/Y 尺度不同；比例尺与测量将分别使用对应轴尺度。"
                : "标定有效。";
        }
    }

    private void EnsureValid()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException(ValidationMessage);
        }
    }
}

public static class ScientificLengthUnits
{
    private static readonly IReadOnlyDictionary<string, double> MicrometresPerUnit =
        new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["Å"] = 0.0001,
            ["nm"] = 0.001,
            ["µm"] = 1,
            ["mm"] = 1000,
        };

    public static IReadOnlyList<string> Supported { get; } = ["Å", "nm", "µm", "mm"];

    public static string Normalize(string? unit)
    {
        string normalized = (unit ?? string.Empty).Trim()
            .Replace('μ', 'µ');
        return normalized.ToLowerInvariant() switch
        {
            "a" or "angstrom" or "ångström" => "Å",
            "nm" => "nm",
            "um" or "µm" => "µm",
            "mm" => "mm",
            _ when !string.IsNullOrWhiteSpace(normalized) => normalized,
            _ => throw new ArgumentException("物理单位不能为空。", nameof(unit)),
        };
    }

    public static double Convert(double value, string fromUnit, string toUnit)
    {
        string from = Normalize(fromUnit);
        string to = Normalize(toUnit);
        if (!MicrometresPerUnit.TryGetValue(from, out double fromFactor) ||
            !MicrometresPerUnit.TryGetValue(to, out double toFactor))
        {
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            throw new NotSupportedException($"无法在自定义单位 {from} 与 {to} 之间换算。");
        }

        return value * fromFactor / toFactor;
    }
}
