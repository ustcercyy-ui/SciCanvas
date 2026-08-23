using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Images;

public sealed record ImageMetadata
{
    public ImageMetadata(
        PixelSize64 pixelSize,
        int channels,
        int bitsPerChannel,
        string pixelFormat,
        double? dpiX = null,
        double? dpiY = null,
        double? physicalSizeX = null,
        double? physicalSizeY = null,
        string? physicalUnit = null,
        string? iccProfileName = null,
        int frameCount = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(channels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bitsPerChannel);
        ArgumentException.ThrowIfNullOrWhiteSpace(pixelFormat);

        PixelSize = pixelSize;
        Channels = channels;
        BitsPerChannel = bitsPerChannel;
        PixelFormat = pixelFormat;
        DpiX = dpiX;
        DpiY = dpiY;
        PhysicalSizeX = physicalSizeX;
        PhysicalSizeY = physicalSizeY;
        PhysicalUnit = physicalUnit;
        IccProfileName = iccProfileName;
        FrameCount = Math.Max(1, frameCount);
    }

    public PixelSize64 PixelSize { get; }

    public int Channels { get; }

    public int BitsPerChannel { get; }

    public string PixelFormat { get; }

    public double? DpiX { get; }

    public double? DpiY { get; }

    public double? PhysicalSizeX { get; }

    public double? PhysicalSizeY { get; }

    public string? PhysicalUnit { get; }

    public string? IccProfileName { get; }

    public int FrameCount { get; }
}

