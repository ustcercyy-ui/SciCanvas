using SciCanvas.Core.Science;

namespace SciCanvas.Core.Images;

public enum MetadataCalibrationState
{
    Unavailable,
    Available,
    ReviewRequired,
}

public sealed record PhysicalPixelSizeMetadata(
    double? UnitsPerPixelX,
    double? UnitsPerPixelY,
    string? Unit,
    MetadataCalibrationState State,
    string? ReviewMessage)
{
    public bool IsAvailable => State == MetadataCalibrationState.Available;
}

public sealed record MetadataCalibrationMapping(
    SpatialCalibration Calibration,
    MetadataCalibrationState State,
    string? ReviewMessage)
{
    public bool IsAvailable =>
        State == MetadataCalibrationState.Available && Calibration.IsValid;
}

/// <summary>
/// The single mapping boundary from imported image metadata, including OME-XML,
/// to a source-bound spatial calibration. Incomplete or incompatible metadata is
/// never guessed.
/// </summary>
public static class ImageMetadataCalibrationMapper
{
    public static MetadataCalibrationMapping Map(Guid sourceAssetId, ImageMetadata metadata)
    {
        if (sourceAssetId == Guid.Empty)
        {
            throw new ArgumentException("源图像 ID 不能为空。", nameof(sourceAssetId));
        }

        ArgumentNullException.ThrowIfNull(metadata);
        SpatialCalibration calibration = metadata.MetadataCalibrationState == MetadataCalibrationState.Available &&
                                         metadata.PhysicalSizeX is double x &&
                                         metadata.PhysicalSizeY is double y &&
                                         !string.IsNullOrWhiteSpace(metadata.PhysicalUnit)
            ? new SpatialCalibration(
                sourceAssetId,
                x,
                y,
                metadata.PhysicalUnit,
                CalibrationOrigin.Metadata)
            : SpatialCalibration.Uncalibrated(sourceAssetId);
        return new MetadataCalibrationMapping(
            calibration,
            metadata.MetadataCalibrationState,
            metadata.MetadataCalibrationReviewMessage);
    }

    public static PhysicalPixelSizeMetadata Resolve(
        double? physicalSizeX,
        double? physicalSizeY,
        string? physicalUnit,
        OmeImageMetadata? ome)
    {
        bool hasExplicitMetadata = physicalSizeX is not null ||
                                   physicalSizeY is not null ||
                                   !string.IsNullOrWhiteSpace(physicalUnit);
        if (hasExplicitMetadata)
        {
            return ResolveAxes(
                physicalSizeX,
                physicalUnit,
                physicalSizeY,
                physicalUnit,
                "图像物理像素尺寸");
        }

        if (ome is null)
        {
            return Unavailable();
        }

        return ResolveAxes(
            ome.PhysicalSizeX,
            ome.PhysicalSizeXUnit,
            ome.PhysicalSizeY,
            ome.PhysicalSizeYUnit,
            "OME PhysicalSizeX/Y");
    }

    private static PhysicalPixelSizeMetadata ResolveAxes(
        double? x,
        string? xUnit,
        double? y,
        string? yUnit,
        string sourceName)
    {
        bool hasAny = x is not null || y is not null ||
                      !string.IsNullOrWhiteSpace(xUnit) ||
                      !string.IsNullOrWhiteSpace(yUnit);
        if (!hasAny)
        {
            return Unavailable();
        }

        if (x is not (> 0) || !double.IsFinite(x.Value) ||
            y is not (> 0) || !double.IsFinite(y.Value) ||
            string.IsNullOrWhiteSpace(xUnit) || string.IsNullOrWhiteSpace(yUnit))
        {
            return ReviewRequired($"{sourceName} 信息不完整或数值无效，未自动建立标定。");
        }

        try
        {
            string canonicalUnit = ScientificLengthUnits.Normalize(xUnit);
            double canonicalY = ScientificLengthUnits.Convert(y.Value, yUnit, canonicalUnit);
            if (!double.IsFinite(canonicalY) || canonicalY <= 0)
            {
                return ReviewRequired($"{sourceName} 的 Y 轴单位换算结果无效，未自动建立标定。");
            }

            return new PhysicalPixelSizeMetadata(
                x.Value,
                canonicalY,
                canonicalUnit,
                MetadataCalibrationState.Available,
                null);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            return ReviewRequired($"{sourceName} 的 X/Y 单位不兼容，未自动建立标定。");
        }
    }

    private static PhysicalPixelSizeMetadata Unavailable() => new(
        null,
        null,
        null,
        MetadataCalibrationState.Unavailable,
        null);

    private static PhysicalPixelSizeMetadata ReviewRequired(string message) => new(
        null,
        null,
        null,
        MetadataCalibrationState.ReviewRequired,
        message);
}
