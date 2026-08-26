using System.IO;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

public sealed class WpfIntensityProfileAnalyzer : IIntensityProfileAnalyzer
{
    public const string AnalyzerVersion = "scicanvas.line-profile.v2";

    public Task<IntensityProfileResult> AnalyzeAsync(
        SourceAsset source,
        MeasurementPoint start,
        MeasurementPoint end,
        SpatialCalibration? calibration,
        int frameIndex = 0,
        int maximumSamples = 2048,
        ImageAnalysisChannel channel = ImageAnalysisChannel.Luminance,
        long sourceRevision = 1,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!start.IsFinite || !end.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "强度剖面端点必须为有限坐标。");
        }

        if (maximumSamples is < 2 or > 16384)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSamples), "采样点数上限必须为 2–16384。");
        }

        if (sourceRevision < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sourceRevision));
        }

        if (frameIndex < 0 || frameIndex >= Math.Max(1, source.Metadata.FrameCount))
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        return Task.Run(
            () => AnalyzeCore(
                source,
                start,
                end,
                calibration,
                frameIndex,
                maximumSamples,
                channel,
                sourceRevision,
                cancellationToken),
            cancellationToken);
    }

    private static IntensityProfileResult AnalyzeCore(
        SourceAsset source,
        MeasurementPoint start,
        MeasurementPoint end,
        SpatialCalibration? calibration,
        int frameIndex,
        int maximumSamples,
        ImageAnalysisChannel channel,
        long sourceRevision,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long sourceWidth = source.Metadata.PixelSize.Width;
        long sourceHeight = source.Metadata.PixelSize.Height;
        if (sourceWidth > int.MaxValue || sourceHeight > int.MaxValue)
        {
            throw new NotSupportedException("强度剖面暂不支持单边超过 Int32 范围的图像。");
        }

        if (!Inside(start, sourceWidth, sourceHeight) || !Inside(end, sourceWidth, sourceHeight))
        {
            throw new InvalidDataException("强度剖面端点超出源图范围。");
        }

        long left = Math.Clamp((long)Math.Floor(Math.Min(start.X, end.X)), 0, sourceWidth - 1);
        long top = Math.Clamp((long)Math.Floor(Math.Min(start.Y, end.Y)), 0, sourceHeight - 1);
        long right = Math.Clamp((long)Math.Ceiling(Math.Max(start.X, end.X)) + 1, left + 1, sourceWidth);
        long bottom = Math.Clamp((long)Math.Ceiling(Math.Max(start.Y, end.Y)) + 1, top + 1, sourceHeight);
        var region = new PixelRect64(left, top, right - left, bottom - top);
        ScientificPixelBuffer pixels = WpfScientificPixelReader.ReadRegion(
            source,
            region,
            frameIndex,
            channel,
            cancellationToken);

        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double pixelLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        int sampleCount = Math.Clamp((int)Math.Ceiling(pixelLength) + 1, 2, maximumSamples);
        var samples = new IntensityProfileSample[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double fraction = index / (double)(sampleCount - 1);
            double pixelX = start.X + deltaX * fraction;
            double pixelY = start.Y + deltaY * fraction;
            int x = Math.Clamp((int)Math.Round(pixelX - region.X), 0, pixels.Width - 1);
            int y = Math.Clamp((int)Math.Round(pixelY - region.Y), 0, pixels.Height - 1);
            double raw = pixels.Values[y * pixels.Width + x];
            double? physicalDistance = calibration?.IsValid == true &&
                                       calibration.SourceAssetId == source.Id
                ? calibration.ConvertDistance(deltaX * fraction, deltaY * fraction)
                : null;
            samples[index] = new IntensityProfileSample(
                index + 1,
                pixelX,
                pixelY,
                pixelLength * fraction,
                physicalDistance,
                raw / pixels.MaximumValue)
            {
                RawIntensity = raw,
            };
        }

        string distanceUnit = calibration?.IsValid == true && calibration.SourceAssetId == source.Id
            ? calibration.Unit
            : "px";
        return new IntensityProfileResult(samples, distanceUnit, pixels.SourceBitDepth)
        {
            Id = Guid.NewGuid(),
            SourceAssetId = source.Id,
            SourceRevision = sourceRevision,
            FrameIndex = frameIndex,
            Channel = channel,
            AnalyzerId = AnalyzerVersion,
            AnalyzedAt = DateTimeOffset.UtcNow,
        };
    }

    private static bool Inside(MeasurementPoint point, long width, long height) =>
        point.X >= 0 && point.X < width && point.Y >= 0 && point.Y < height;
}
