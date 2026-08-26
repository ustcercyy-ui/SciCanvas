namespace SciCanvas.Core.Geometry;

/// <summary>
/// Integer source-pixel rectangle with half-open bounds
/// [X, X + Width) × [Y, Y + Height).
/// </summary>
public readonly record struct PixelRect64
{
    public PixelRect64(long x, long y, long width, long height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(x);
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        _ = checked(x + width);
        _ = checked(y + height);

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public long X { get; }

    public long Y { get; }

    public long Width { get; }

    public long Height { get; }

    public long Right => checked(X + Width);

    public long Bottom => checked(Y + Height);

    public PixelRect64 MoveBy(long deltaX, long deltaY)
    {
        return new PixelRect64(
            checked(X + deltaX),
            checked(Y + deltaY),
            Width,
            Height);
    }
}
