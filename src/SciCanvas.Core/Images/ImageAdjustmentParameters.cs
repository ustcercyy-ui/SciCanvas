namespace SciCanvas.Core.Images;

/// <summary>
/// Non-destructive display adjustments for a source image.
/// Values are deliberately bounded so an export can never silently become an
/// unconstrained "auto enhance" operation.
/// </summary>
public sealed record ImageAdjustmentParameters
{
    public double Brightness { get; init; }

    public double Contrast { get; init; }

    public double Gamma { get; init; } = 1;

    public double BlackPoint { get; init; }

    public double WhitePoint { get; init; } = 1;

    public bool Invert { get; init; }

    public bool Grayscale { get; init; }

    /// <summary>rgb, red, green, blue, or alpha.</summary>
    public string Channel { get; init; } = "rgb";

    public bool IsIdentity =>
        Brightness == 0 &&
        Contrast == 0 &&
        Gamma == 1 &&
        BlackPoint == 0 &&
        WhitePoint == 1 &&
        !Invert &&
        !Grayscale &&
        string.Equals(Channel?.Trim(), "rgb", StringComparison.OrdinalIgnoreCase);

    public bool IsValid =>
        double.IsFinite(Brightness) && Brightness is >= -1 and <= 1 &&
        double.IsFinite(Contrast) && Contrast is >= -1 and <= 1 &&
        double.IsFinite(Gamma) && Gamma is >= 0.1 and <= 10 &&
        double.IsFinite(BlackPoint) && BlackPoint is >= 0 and < 1 &&
        double.IsFinite(WhitePoint) && WhitePoint is > 0 and <= 1 &&
        BlackPoint < WhitePoint &&
        Channel?.Trim().ToLowerInvariant() is "rgb" or "red" or "green" or "blue" or "alpha";

    public string ValidationMessage => IsValid
        ? IsIdentity ? "未启用图像处理；导出将直接使用源像素。" : "非破坏性处理参数有效；源图不会被修改。"
        : "处理参数无效：亮度/对比度需在 -100 到 +100（界面百分比），Gamma 需在 0.1 到 10，黑白点必须递增。";

    public ImageAdjustmentParameters Normalize()
    {
        double blackPoint = Math.Clamp(double.IsFinite(BlackPoint) ? BlackPoint : 0, 0, 0.99);
        double whitePoint = Math.Clamp(double.IsFinite(WhitePoint) ? WhitePoint : 1, blackPoint + 0.01, 1);
        string channel = Channel?.Trim().ToLowerInvariant() ?? "rgb";
        if (channel is not ("rgb" or "red" or "green" or "blue" or "alpha"))
        {
            channel = "rgb";
        }

        return this with
        {
            Brightness = Math.Clamp(double.IsFinite(Brightness) ? Brightness : 0, -1, 1),
            Contrast = Math.Clamp(double.IsFinite(Contrast) ? Contrast : 0, -1, 1),
            Gamma = Math.Clamp(double.IsFinite(Gamma) ? Gamma : 1, 0.1, 10),
            BlackPoint = blackPoint,
            WhitePoint = whitePoint,
            Channel = channel,
        };
    }
}
