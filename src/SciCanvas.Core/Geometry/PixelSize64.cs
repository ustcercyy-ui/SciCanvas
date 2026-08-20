namespace SciCanvas.Core.Geometry;

public readonly record struct PixelSize64
{
    public PixelSize64(long width, long height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;
    }

    public long Width { get; }

    public long Height { get; }
}

