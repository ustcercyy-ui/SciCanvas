using System.Security.Cryptography;
using System.Text;
using SciCanvas.Core.Images;

namespace SciCanvas.Core.Workspace;

public sealed record PreviewOptions(
    int MaximumWidthPixels,
    int MaximumHeightPixels,
    int FrameIndex = 0,
    PanelAdjustments? Adjustments = null)
{
    public void EnsureValid()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumWidthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaximumHeightPixels);
        ArgumentOutOfRangeException.ThrowIfNegative(FrameIndex);
    }
}

public sealed record ImageTransform(
    NormalizedRect Crop,
    PanelFitMode FitMode,
    double RotationDegrees,
    PanelAdjustments Adjustments,
    int FrameIndex = 0);

public sealed record RenderOptions(
    int WidthPixels,
    int HeightPixels,
    string PixelFormat,
    bool PreserveBitDepth = true);

public sealed record PanelExportOptions(
    int Dpi,
    string Format,
    int? BitDepth = null,
    string? ColorSpace = null);

public sealed record PreviewResult(
    string CacheKey,
    int WidthPixels,
    int HeightPixels,
    object Payload);

public sealed record RenderResult(
    int WidthPixels,
    int HeightPixels,
    string PixelFormat,
    object Payload);

public sealed record PanelExportResult(
    string Format,
    int WidthPixels,
    int HeightPixels,
    object Payload);

public interface IImageEngine
{
    Task<ImageMetadata> GetMetadataAsync(
        ScientificAsset asset,
        CancellationToken cancellationToken = default);

    Task<PreviewResult> GeneratePreviewAsync(
        ScientificAsset asset,
        PreviewOptions options,
        CancellationToken cancellationToken = default);

    Task<RenderResult> RenderRegionAsync(
        ScientificAsset asset,
        ImageTransform transform,
        RenderOptions options,
        CancellationToken cancellationToken = default);

    Task<PanelExportResult> ExportPanelAsync(
        FigurePanel panel,
        ScientificAsset asset,
        PanelExportOptions options,
        CancellationToken cancellationToken = default);
}

public static class PreviewCacheKey
{
    public static string Create(ScientificAsset asset, PreviewOptions options)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();
        PanelAdjustments adjustments = options.Adjustments ?? new PanelAdjustments();
        string input = string.Join('|',
            asset.Id.ToString("N"),
            asset.Source.Fingerprint.Sha256,
            asset.Source.SourceRevision,
            asset.Source.Fingerprint.LastWriteTimeUtc.ToUnixTimeMilliseconds(),
            options.MaximumWidthPixels,
            options.MaximumHeightPixels,
            options.FrameIndex,
            adjustments.Brightness.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            adjustments.Contrast.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            adjustments.Gamma.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
    }
}

public sealed record AutoTrimOptions(
    bool TrimWhite,
    bool TrimTransparent,
    double Tolerance,
    int PaddingPixels)
{
    public void EnsureValid()
    {
        if (!TrimWhite && !TrimTransparent)
        {
            throw new InvalidOperationException("Auto Trim 至少需要启用白色或透明背景检测。" );
        }

        if (!double.IsFinite(Tolerance) || Tolerance is < 0 or > 1 || PaddingPixels < 0)
        {
            throw new InvalidOperationException("Auto Trim tolerance 必须为 0–1，padding 不能为负数。" );
        }
    }
}

public sealed record AutoTrimSuggestion(
    NormalizedRect Crop,
    double Confidence,
    string Reason);

public interface IAutoTrimAnalyzer
{
    Task<AutoTrimSuggestion?> SuggestAsync(
        ScientificAsset asset,
        AutoTrimOptions options,
        CancellationToken cancellationToken = default);
}
