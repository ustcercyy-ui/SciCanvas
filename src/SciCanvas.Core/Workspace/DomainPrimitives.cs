using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Workspace;

public enum AssetKind
{
    Sem,
    Tem,
    Stem,
    Ebsd,
    Eds,
    Afm,
    Optical,
    Xrd,
    Graph,
    Schematic,
    Other,
}

public enum ScientificValidityState
{
    Valid,
    Warning,
    Invalid,
    ReviewRequired,
}

public sealed record ScientificValidity(
    ScientificValidityState State,
    IReadOnlyList<string> Reasons)
{
    public static ScientificValidity Valid { get; } = new(
        ScientificValidityState.Valid,
        []);

    public static ScientificValidity Warning(params string[] reasons) => new(
        ScientificValidityState.Warning,
        NormalizeReasons(reasons));

    public static ScientificValidity Invalid(params string[] reasons) => new(
        ScientificValidityState.Invalid,
        NormalizeReasons(reasons));

    public static ScientificValidity ReviewRequired(params string[] reasons) => new(
        ScientificValidityState.ReviewRequired,
        NormalizeReasons(reasons));

    public bool IsScientificallyUsable => State is ScientificValidityState.Valid or ScientificValidityState.Warning;

    private static IReadOnlyList<string> NormalizeReasons(IEnumerable<string> reasons) =>
        reasons
            .Where(reason => !string.IsNullOrWhiteSpace(reason))
            .Select(reason => reason.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
}

public readonly record struct NormalizedPoint
{
    public NormalizedPoint(double u, double v)
    {
        if (!double.IsFinite(u) || !double.IsFinite(v) ||
            u is < 0 or > 1 || v is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(u), "Panel 坐标必须位于 0–1 范围内。");
        }

        U = u;
        V = v;
    }

    public double U { get; }

    public double V { get; }
}

public readonly record struct NormalizedRect
{
    private readonly PixelRect64? _canonicalSourcePixels;
    private readonly long _canonicalSourceWidth;
    private readonly long _canonicalSourceHeight;

    public NormalizedRect(double x, double y, double width, double height)
        : this(x, y, width, height, null, 0, 0)
    {
    }

    private NormalizedRect(
        double x,
        double y,
        double width,
        double height,
        PixelRect64? canonicalSourcePixels,
        long canonicalSourceWidth,
        long canonicalSourceHeight)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            x < 0 || y < 0 || width <= 0 || height <= 0 ||
            x + width > 1 + 1e-9 || y + height > 1 + 1e-9)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "标准化裁剪区域必须完全位于 0–1 范围内。" );
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
        _canonicalSourcePixels = canonicalSourcePixels;
        _canonicalSourceWidth = canonicalSourceWidth;
        _canonicalSourceHeight = canonicalSourceHeight;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Equals(NormalizedRect other) =>
        X.Equals(other.X) &&
        Y.Equals(other.Y) &&
        Width.Equals(other.Width) &&
        Height.Equals(other.Height);

    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    public static NormalizedRect Full { get; } = new(0, 0, 1, 1);

    public PixelRect64 ToSourcePixels(long sourceWidth, long sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);

        if (_canonicalSourcePixels is PixelRect64 canonical &&
            _canonicalSourceWidth == sourceWidth &&
            _canonicalSourceHeight == sourceHeight)
        {
            return canonical;
        }

        long left = Math.Clamp((long)Math.Floor(X * sourceWidth), 0, sourceWidth - 1);
        long top = Math.Clamp((long)Math.Floor(Y * sourceHeight), 0, sourceHeight - 1);
        long right = Math.Clamp((long)Math.Ceiling(Right * sourceWidth), left + 1, sourceWidth);
        long bottom = Math.Clamp((long)Math.Ceiling(Bottom * sourceHeight), top + 1, sourceHeight);
        return new PixelRect64(left, top, right - left, bottom - top);
    }

    public static NormalizedRect FromSourcePixels(
        PixelRect64 rect,
        long sourceWidth,
        long sourceHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceHeight);
        if (rect.Right > sourceWidth || rect.Bottom > sourceHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(rect), "源图裁剪区域超出图像边界。" );
        }

        return new NormalizedRect(
            rect.X / (double)sourceWidth,
            rect.Y / (double)sourceHeight,
            rect.Width / (double)sourceWidth,
            rect.Height / (double)sourceHeight,
            rect,
            sourceWidth,
            sourceHeight);
    }
}

public readonly record struct FigurePointMm
{
    public FigurePointMm(double x, double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Figure 坐标必须是有限数值。" );
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }
}

public readonly record struct FigureRectMm
{
    public FigureRectMm(double x, double y, double width, double height)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            x < 0 || y < 0 || width <= 0 || height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), "Figure frame 必须使用有效的毫米坐标和正尺寸。" );
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public FigureRectMm WithPosition(double x, double y) => new(x, y, Width, Height);

    public FigureRectMm WithSize(double width, double height) => new(X, Y, width, height);
}

public enum PanelFitMode
{
    Fit,
    Fill,
    Manual,
}
