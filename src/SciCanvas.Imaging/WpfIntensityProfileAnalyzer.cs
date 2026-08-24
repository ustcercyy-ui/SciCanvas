using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

namespace SciCanvas.Imaging;

public sealed class WpfIntensityProfileAnalyzer : IIntensityProfileAnalyzer
{
    public Task<IntensityProfileResult> AnalyzeAsync(
        SourceAsset source,
        MeasurementPoint start,
        MeasurementPoint end,
        SpatialCalibration? calibration,
        int frameIndex = 0,
        int maximumSamples = 2048,
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

        return Task.Run(
            () => AnalyzeCore(source, start, end, calibration, frameIndex, maximumSamples, cancellationToken),
            cancellationToken);
    }

    private static IntensityProfileResult AnalyzeCore(
        SourceAsset source,
        MeasurementPoint start,
        MeasurementPoint end,
        SpatialCalibration? calibration,
        int frameIndex,
        int maximumSamples,
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

        BitmapSource sourceFrame = WpfFigureExporter.LoadExactCrop(
            source.OriginalPath,
            new PixelRect64(0, 0, sourceWidth, sourceHeight),
            frameIndex);
        bool use16Bit = source.Metadata.BitsPerChannel > 8;
        PixelFormat targetFormat = use16Bit ? PixelFormats.Gray16 : PixelFormats.Gray8;
        BitmapSource gray = sourceFrame.Format == targetFormat
            ? sourceFrame
            : new FormatConvertedBitmap(sourceFrame, targetFormat, null, 0);
        gray.Freeze();

        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double pixelLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        int sampleCount = Math.Clamp((int)Math.Ceiling(pixelLength) + 1, 2, maximumSamples);
        double maximumValue = use16Bit ? ushort.MaxValue : byte.MaxValue;
        byte[] pixel = new byte[use16Bit ? 2 : 1];
        var samples = new IntensityProfileSample[sampleCount];
        for (int index = 0; index < sampleCount; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double fraction = index / (double)(sampleCount - 1);
            double pixelX = start.X + deltaX * fraction;
            double pixelY = start.Y + deltaY * fraction;
            int x = Math.Clamp((int)Math.Round(pixelX), 0, gray.PixelWidth - 1);
            int y = Math.Clamp((int)Math.Round(pixelY), 0, gray.PixelHeight - 1);
            gray.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, pixel.Length, 0);
            double raw = use16Bit ? BitConverter.ToUInt16(pixel, 0) : pixel[0];
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
                raw / maximumValue);
        }

        string distanceUnit = calibration?.IsValid == true && calibration.SourceAssetId == source.Id
            ? calibration.Unit
            : "px";
        return new IntensityProfileResult(samples, distanceUnit, use16Bit ? 16 : 8);
    }

    private static bool Inside(MeasurementPoint point, long width, long height) =>
        point.X >= 0 && point.X < width && point.Y >= 0 && point.Y < height;
}
