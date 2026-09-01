using SciCanvas.Core.Geometry;
using SciCanvas.Core.Linking;

namespace SciCanvas.Core.Channels;

public enum RegisteredInterpolation
{
    Nearest,
    Bilinear,
}

public enum RegisteredBorderPolicy
{
    Transparent,
    Zero,
    NoData,
}

public enum RegisteredPlaneSemantic
{
    ContinuousDisplay,
    LabelOrMask,
}

/// <summary>
/// Immutable output grid for pull-based registration resampling. Region is expressed
/// in reference-source pixel coordinates and therefore remains separate from the
/// target-source crop that is loaded for interpolation.
/// </summary>
public sealed record RegisteredReferenceGrid(
    ScientificPlaneRef PlaneRef,
    PixelRect64 Region)
{
    public int Width => checked((int)Region.Width);

    public int Height => checked((int)Region.Height);

    public RegisteredReferenceGrid EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(PlaneRef);
        PlaneRef.EnsureValid();
        if (PlaneRef.SourceRevision is null ||
            Region.Width > int.MaxValue || Region.Height > int.MaxValue)
        {
            throw new InvalidOperationException(
                "配准参考网格必须包含固定源修订及 Int32 范围内的有效像素区域。");
        }

        return this;
    }
}

/// <summary>
/// Complete display-only registration sampling contract. SpatialMapping uses the
/// repository convention target = M × reference. The resampler performs inverse
/// (pull) rasterization by visiting each output reference pixel and evaluating M to
/// locate the corresponding raw target-source coordinate; it never forward-splats
/// or mutates target samples.
/// </summary>
public sealed record RegisteredPlaneResamplingSpec(
    SpatialMapping Mapping,
    RegisteredReferenceGrid ReferenceGrid,
    PixelSize64 TargetPixelSize,
    RegisteredInterpolation Interpolation = RegisteredInterpolation.Bilinear,
    RegisteredBorderPolicy BorderPolicy = RegisteredBorderPolicy.Transparent,
    RegisteredPlaneSemantic Semantic = RegisteredPlaneSemantic.ContinuousDisplay)
{
    public RegisteredPlaneResamplingSpec EnsureValid()
    {
        ArgumentNullException.ThrowIfNull(Mapping);
        ArgumentNullException.ThrowIfNull(ReferenceGrid);
        Mapping.EnsureValid();
        ReferenceGrid.EnsureValid();
        if (!Enum.IsDefined(Interpolation) || !Enum.IsDefined(BorderPolicy) ||
            !Enum.IsDefined(Semantic) ||
            TargetPixelSize.Width < 1 || TargetPixelSize.Height < 1 ||
            Mapping.SourceAssetId != ReferenceGrid.PlaneRef.AssetId ||
            Mapping.SourceRevision != ReferenceGrid.PlaneRef.SourceRevision)
        {
            throw new InvalidOperationException(
                "配准重采样必须引用与 SpatialMapping 源素材/修订一致的参考网格及有效策略。");
        }

        if (Semantic == RegisteredPlaneSemantic.LabelOrMask &&
            Interpolation != RegisteredInterpolation.Nearest)
        {
            throw new InvalidOperationException("标签或掩膜平面禁止 Bilinear 插值，必须使用 Nearest。");
        }

        return this;
    }
}

/// <summary>
/// Display-only registered samples. SourcePlane is retained as the immutable raw
/// target plane so analysis callers cannot accidentally treat this buffer as raw data.
/// </summary>
public sealed record RegisteredPlaneResamplingResult
{
    private readonly double[] _samples;
    private readonly bool[] _validity;

    internal RegisteredPlaneResamplingResult(
        ImagePlane sourcePlane,
        RegisteredPlaneResamplingSpec spec,
        double[] samples,
        bool[] validity)
    {
        SourcePlane = sourcePlane;
        Spec = spec;
        _samples = samples;
        _validity = validity;
    }

    public ImagePlane SourcePlane { get; }

    public RegisteredPlaneResamplingSpec Spec { get; }

    public int Width => Spec.ReferenceGrid.Width;

    public int Height => Spec.ReferenceGrid.Height;

    public IReadOnlyList<double> Samples => Array.AsReadOnly(_samples);

    public IReadOnlyList<bool> Validity => Array.AsReadOnly(_validity);

    public double GetValue(int index) => _samples[index];

    public bool IsValid(int index) => _validity[index];
}

public static class RegisteredPlaneResampler
{
    /// <summary>
    /// Returns the smallest target-source crop needed by the selected interpolation.
    /// A 1×1 in-bounds placeholder is returned when the mapped grid is wholly outside
    /// the target; border policy still controls every output sample in that case.
    /// </summary>
    public static PixelRect64 CalculateSourceReadRegion(RegisteredPlaneResamplingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        spec.EnsureValid();
        PixelRect64 reference = spec.ReferenceGrid.Region;
        SpatialPoint[] centers =
        [
            spec.Mapping.MapForward(new SpatialPoint(reference.X + 0.5, reference.Y + 0.5)),
            spec.Mapping.MapForward(new SpatialPoint(reference.Right - 0.5, reference.Y + 0.5)),
            spec.Mapping.MapForward(new SpatialPoint(reference.Right - 0.5, reference.Bottom - 0.5)),
            spec.Mapping.MapForward(new SpatialPoint(reference.X + 0.5, reference.Bottom - 0.5)),
        ];
        double minimumX = centers.Min(point => point.X);
        double minimumY = centers.Min(point => point.Y);
        double maximumX = centers.Max(point => point.X);
        double maximumY = centers.Max(point => point.Y);

        long left;
        long top;
        long right;
        long bottom;
        if (spec.Interpolation == RegisteredInterpolation.Nearest)
        {
            left = checked((long)Math.Floor(minimumX));
            top = checked((long)Math.Floor(minimumY));
            right = checked((long)Math.Floor(maximumX));
            bottom = checked((long)Math.Floor(maximumY));
        }
        else
        {
            left = checked((long)Math.Floor(minimumX - 0.5));
            top = checked((long)Math.Floor(minimumY - 0.5));
            right = checked((long)Math.Floor(maximumX - 0.5) + 1);
            bottom = checked((long)Math.Floor(maximumY - 0.5) + 1);
        }

        long clippedLeft = Math.Max(0, left);
        long clippedTop = Math.Max(0, top);
        long clippedRight = Math.Min(spec.TargetPixelSize.Width - 1, right);
        long clippedBottom = Math.Min(spec.TargetPixelSize.Height - 1, bottom);
        if (clippedLeft > clippedRight || clippedTop > clippedBottom)
        {
            return new PixelRect64(0, 0, 1, 1);
        }

        return new PixelRect64(
            clippedLeft,
            clippedTop,
            checked(clippedRight - clippedLeft + 1),
            checked(clippedBottom - clippedTop + 1));
    }

    public static RegisteredPlaneResamplingResult Resample(
        ImagePlane sourcePlane,
        RegisteredPlaneResamplingSpec spec,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePlane);
        ArgumentNullException.ThrowIfNull(spec);
        spec.EnsureValid();
        sourcePlane.PlaneRef.EnsureValid();
        if (sourcePlane.AssetId != spec.Mapping.TargetAssetId ||
            sourcePlane.SourceRevision != spec.Mapping.TargetRevision)
        {
            throw new InvalidOperationException(
                "待重采样原始平面与 SpatialMapping 的目标素材/修订不一致。");
        }

        PixelRect64 required = CalculateSourceReadRegion(spec);
        if (!Contains(sourcePlane.Region, required))
        {
            throw new InvalidOperationException(
                "加载的目标原始平面区域不足以完成所选配准插值。");
        }

        int width = spec.ReferenceGrid.Width;
        int height = spec.ReferenceGrid.Height;
        double[] samples = new double[checked(width * height)];
        bool[] validity = new bool[samples.Length];
        PixelRect64 reference = spec.ReferenceGrid.Region;
        SpatialMatrix3x3 matrix = spec.Mapping.Matrix;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = checked(y * width + x);
                if ((index & 0x3FFF) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                double referenceX = reference.X + x + 0.5;
                double referenceY = reference.Y + y + 0.5;
                var target = new SpatialPoint(
                    matrix.M11 * referenceX + matrix.M12 * referenceY + matrix.M13,
                    matrix.M21 * referenceX + matrix.M22 * referenceY + matrix.M23);
                SampleOutcome outcome = spec.Interpolation switch
                {
                    RegisteredInterpolation.Nearest => SampleNearest(sourcePlane, spec, target),
                    RegisteredInterpolation.Bilinear => SampleBilinear(sourcePlane, spec, target),
                    _ => throw new InvalidOperationException("未知的配准插值策略。"),
                };
                samples[index] = outcome.Value;
                validity[index] = outcome.IsValid;
            }
        }

        return new RegisteredPlaneResamplingResult(sourcePlane, spec, samples, validity);
    }

    private static SampleOutcome SampleNearest(
        ImagePlane plane,
        RegisteredPlaneResamplingSpec spec,
        SpatialPoint coordinate)
    {
        long x = checked((long)Math.Floor(coordinate.X));
        long y = checked((long)Math.Floor(coordinate.Y));
        return TryRead(plane, spec, x, y, 1);
    }

    private static SampleOutcome SampleBilinear(
        ImagePlane plane,
        RegisteredPlaneResamplingSpec spec,
        SpatialPoint coordinate)
    {
        double sampleX = coordinate.X - 0.5;
        double sampleY = coordinate.Y - 0.5;
        long x0 = checked((long)Math.Floor(sampleX));
        long y0 = checked((long)Math.Floor(sampleY));
        double fractionX = sampleX - x0;
        double fractionY = sampleY - y0;
        double value = 0;
        long x1 = checked(x0 + 1);
        long y1 = checked(y0 + 1);
        if (!TryAccumulate(
                plane,
                spec,
                x0,
                y0,
                (1 - fractionX) * (1 - fractionY),
                ref value,
                out SampleOutcome border) ||
            !TryAccumulate(
                plane,
                spec,
                x1,
                y0,
                fractionX * (1 - fractionY),
                ref value,
                out border) ||
            !TryAccumulate(
                plane,
                spec,
                x0,
                y1,
                (1 - fractionX) * fractionY,
                ref value,
                out border) ||
            !TryAccumulate(
                plane,
                spec,
                x1,
                y1,
                fractionX * fractionY,
                ref value,
                out border))
        {
            return border;
        }

        return new SampleOutcome(value, true);
    }

    private static bool TryAccumulate(
        ImagePlane plane,
        RegisteredPlaneResamplingSpec spec,
        long x,
        long y,
        double weight,
        ref double value,
        out SampleOutcome border)
    {
        if (weight <= 0)
        {
            border = default;
            return true;
        }

        SampleOutcome tap = TryRead(plane, spec, x, y, weight);
        if (!tap.IsValid)
        {
            border = tap;
            return false;
        }

        value += tap.Value;
        border = default;
        return true;
    }

    private static SampleOutcome TryRead(
        ImagePlane plane,
        RegisteredPlaneResamplingSpec spec,
        long x,
        long y,
        double weight)
    {
        bool insideTarget = x >= 0 && y >= 0 &&
                            x < spec.TargetPixelSize.Width &&
                            y < spec.TargetPixelSize.Height;
        if (!insideTarget)
        {
            return spec.BorderPolicy switch
            {
                RegisteredBorderPolicy.Zero => new SampleOutcome(0, true),
                RegisteredBorderPolicy.Transparent => new SampleOutcome(0, false),
                RegisteredBorderPolicy.NoData => new SampleOutcome(double.NaN, false),
                _ => throw new InvalidOperationException("未知的配准边界策略。"),
            };
        }

        if (x < plane.Region.X || y < plane.Region.Y ||
            x >= plane.Region.Right || y >= plane.Region.Bottom)
        {
            throw new InvalidOperationException(
                "配准插值访问了未加载但位于素材边界内的目标原始像素。");
        }

        int localX = checked((int)(x - plane.Region.X));
        int localY = checked((int)(y - plane.Region.Y));
        return new SampleOutcome(plane.GetRawValue(localX, localY) * weight, true);
    }

    private static bool Contains(PixelRect64 outer, PixelRect64 inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y &&
        inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;

    private readonly record struct SampleOutcome(double Value, bool IsValid);
}
