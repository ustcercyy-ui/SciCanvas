using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Linking;

public enum SpatialMappingKind
{
    Identity,
    Translation,
    Rigid,
    Affine,
}

public enum SpatialMappingOrigin
{
    UserDeclaredIdentity,
    UserDeclaredTranslation,
    ManualLandmarks,
    ImportedMetadata,
}

public enum SpatialMappingRevisionState
{
    Current,
    ReviewRequired,
}

public static class SpatialMappingQcCodes
{
    public const string MappingRevisionStale = "mapping-revision-stale";
}

public sealed record SpatialMappingRevisionAssessment(
    SpatialMappingRevisionState State,
    IReadOnlyList<string> QcCodes);

public readonly record struct SpatialPoint(double X, double Y)
{
    public bool IsFinite => double.IsFinite(X) && double.IsFinite(Y);
}

/// <summary>
/// Row-major homogeneous matrix using the convention TargetPoint = M × SourcePoint.
/// The last row must remain [0, 0, 1] for all mappings supported by SciCanvas.
/// </summary>
public sealed record SpatialMatrix3x3(
    double M11,
    double M12,
    double M13,
    double M21,
    double M22,
    double M23,
    double M31,
    double M32,
    double M33)
{
    private const double Tolerance = 1e-10;

    public static SpatialMatrix3x3 Identity { get; } = new(
        1, 0, 0,
        0, 1, 0,
        0, 0, 1);

    public static SpatialMatrix3x3 CreateTranslation(double offsetX, double offsetY)
    {
        if (!double.IsFinite(offsetX) || !double.IsFinite(offsetY))
        {
            throw new ArgumentOutOfRangeException(nameof(offsetX), "平移量必须为有限像素值。");
        }

        return new SpatialMatrix3x3(
            1, 0, offsetX,
            0, 1, offsetY,
            0, 0, 1);
    }

    public double LinearDeterminant => M11 * M22 - M12 * M21;

    public SpatialMatrix3x3 EnsureValid()
    {
        double[] values = [M11, M12, M13, M21, M22, M23, M31, M32, M33];
        if (values.Any(value => !double.IsFinite(value)) ||
            Math.Abs(M31) > Tolerance || Math.Abs(M32) > Tolerance ||
            Math.Abs(M33 - 1) > Tolerance || Math.Abs(LinearDeterminant) <= Tolerance)
        {
            throw new InvalidOperationException("SpatialMapping 必须是有限且可逆的仿射 3×3 矩阵，末行为 [0, 0, 1]。");
        }

        return this;
    }

    public SpatialPoint Transform(SpatialPoint point)
    {
        EnsureValid();
        if (!point.IsFinite)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "待映射坐标必须为有限值。");
        }

        return new SpatialPoint(
            M11 * point.X + M12 * point.Y + M13,
            M21 * point.X + M22 * point.Y + M23);
    }

    public SpatialMatrix3x3 Inverse()
    {
        EnsureValid();
        double inverseDeterminant = 1 / LinearDeterminant;
        double a = M22 * inverseDeterminant;
        double b = -M12 * inverseDeterminant;
        double d = -M21 * inverseDeterminant;
        double e = M11 * inverseDeterminant;
        return new SpatialMatrix3x3(
            a, b, -(a * M13 + b * M23),
            d, e, -(d * M13 + e * M23),
            0, 0, 1);
    }

    public static SpatialMatrix3x3 Multiply(
        SpatialMatrix3x3 left,
        SpatialMatrix3x3 right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        left.EnsureValid();
        right.EnsureValid();
        return new SpatialMatrix3x3(
            left.M11 * right.M11 + left.M12 * right.M21 + left.M13 * right.M31,
            left.M11 * right.M12 + left.M12 * right.M22 + left.M13 * right.M32,
            left.M11 * right.M13 + left.M12 * right.M23 + left.M13 * right.M33,
            left.M21 * right.M11 + left.M22 * right.M21 + left.M23 * right.M31,
            left.M21 * right.M12 + left.M22 * right.M22 + left.M23 * right.M32,
            left.M21 * right.M13 + left.M22 * right.M23 + left.M23 * right.M33,
            left.M31 * right.M11 + left.M32 * right.M21 + left.M33 * right.M31,
            left.M31 * right.M12 + left.M32 * right.M22 + left.M33 * right.M32,
            left.M31 * right.M13 + left.M32 * right.M23 + left.M33 * right.M33).EnsureValid();
    }
}

public sealed record SpatialMapping(
    Guid Id,
    Guid SourceAssetId,
    Guid TargetAssetId,
    long SourceRevision,
    long TargetRevision,
    SpatialMappingKind Kind,
    SpatialMatrix3x3 Matrix,
    SpatialMappingOrigin Origin,
    DateTimeOffset CreatedAt,
    double? ResidualPixels = null,
    IReadOnlyList<RegistrationLandmarkPair>? Landmarks = null,
    double? ResidualPhysical = null,
    string? ResidualPhysicalUnit = null)
{
    private const double Tolerance = 1e-10;

    public SpatialMapping EnsureValid()
    {
        Matrix.EnsureValid();
        IReadOnlyList<RegistrationLandmarkPair> landmarks = EffectiveLandmarks;
        foreach (RegistrationLandmarkPair landmark in landmarks)
        {
            landmark.EnsureValid();
        }

        if (Id == Guid.Empty || SourceAssetId == Guid.Empty || TargetAssetId == Guid.Empty ||
            SourceAssetId == TargetAssetId || SourceRevision < 1 || TargetRevision < 1 ||
            !Enum.IsDefined(Kind) || !Enum.IsDefined(Origin) || CreatedAt == default ||
            ResidualPixels is double residual && (!double.IsFinite(residual) || residual < 0) ||
            landmarks.Select(landmark => landmark.Id).Distinct().Count() != landmarks.Count ||
            ResidualPhysical is double physical && (!double.IsFinite(physical) || physical < 0) ||
            ResidualPhysical.HasValue != !string.IsNullOrWhiteSpace(ResidualPhysicalUnit))
        {
            throw new InvalidOperationException("SpatialMapping 缺少有效的素材、修订、类型或溯源信息。");
        }

        if (Kind == SpatialMappingKind.Identity && !ApproximatelyEquals(Matrix, SpatialMatrix3x3.Identity))
        {
            throw new InvalidOperationException("Identity mapping 必须使用单位矩阵。");
        }

        if (Kind == SpatialMappingKind.Translation &&
            (Math.Abs(Matrix.M11 - 1) > Tolerance || Math.Abs(Matrix.M12) > Tolerance ||
             Math.Abs(Matrix.M21) > Tolerance || Math.Abs(Matrix.M22 - 1) > Tolerance))
        {
            throw new InvalidOperationException("Translation mapping 的线性部分必须是单位矩阵。");
        }

        if (Kind == SpatialMappingKind.Rigid)
        {
            double firstLength = Matrix.M11 * Matrix.M11 + Matrix.M21 * Matrix.M21;
            double secondLength = Matrix.M12 * Matrix.M12 + Matrix.M22 * Matrix.M22;
            double dot = Matrix.M11 * Matrix.M12 + Matrix.M21 * Matrix.M22;
            if (Math.Abs(firstLength - 1) > 1e-8 || Math.Abs(secondLength - 1) > 1e-8 ||
                Math.Abs(dot) > 1e-8 || Math.Abs(Matrix.LinearDeterminant - 1) > 1e-8)
            {
                throw new InvalidOperationException("Rigid mapping 的线性部分必须是 det=+1 的正交旋转矩阵。");
            }
        }

        if (Origin == SpatialMappingOrigin.ManualLandmarks)
        {
            int minimum = Kind switch
            {
                SpatialMappingKind.Translation => 1,
                SpatialMappingKind.Rigid => 2,
                SpatialMappingKind.Affine => 3,
                _ => int.MaxValue,
            };
            if (landmarks.Count < minimum || ResidualPixels is null)
            {
                throw new InvalidOperationException("ManualLandmarks mapping 必须保存足量 landmark 与 RMS residual。");
            }
        }

        return this;
    }

    public IReadOnlyList<RegistrationLandmarkPair> EffectiveLandmarks => Landmarks ?? [];

    public bool MatchesRevisions(long sourceRevision, long targetRevision) =>
        SourceRevision == sourceRevision && TargetRevision == targetRevision;

    public SpatialMappingRevisionState GetRevisionState(long sourceRevision, long targetRevision) =>
        MatchesRevisions(sourceRevision, targetRevision)
            ? SpatialMappingRevisionState.Current
            : SpatialMappingRevisionState.ReviewRequired;

    public SpatialMappingRevisionAssessment AssessRevisions(long sourceRevision, long targetRevision)
    {
        SpatialMappingRevisionState state = GetRevisionState(sourceRevision, targetRevision);
        return state == SpatialMappingRevisionState.Current
            ? new SpatialMappingRevisionAssessment(state, [])
            : new SpatialMappingRevisionAssessment(
                state,
                [SpatialMappingQcCodes.MappingRevisionStale]);
    }

    public SpatialPoint MapForward(SpatialPoint point) => Matrix.Transform(point);

    public SpatialPoint MapReverse(SpatialPoint point) => Matrix.Inverse().Transform(point);

    public static SpatialMapping CreateIdentity(
        Guid sourceAssetId,
        Guid targetAssetId,
        long sourceRevision,
        long targetRevision,
        DateTimeOffset createdAt,
        Guid? id = null) => new SpatialMapping(
            id ?? Guid.NewGuid(),
            sourceAssetId,
            targetAssetId,
            sourceRevision,
            targetRevision,
            SpatialMappingKind.Identity,
            SpatialMatrix3x3.Identity,
            SpatialMappingOrigin.UserDeclaredIdentity,
            createdAt).EnsureValid();

    public static SpatialMapping CreateTranslation(
        Guid sourceAssetId,
        Guid targetAssetId,
        long sourceRevision,
        long targetRevision,
        double offsetX,
        double offsetY,
        DateTimeOffset createdAt,
        Guid? id = null) => new SpatialMapping(
            id ?? Guid.NewGuid(),
            sourceAssetId,
            targetAssetId,
            sourceRevision,
            targetRevision,
            SpatialMappingKind.Translation,
            SpatialMatrix3x3.CreateTranslation(offsetX, offsetY),
            SpatialMappingOrigin.UserDeclaredTranslation,
            createdAt).EnsureValid();

    private static bool ApproximatelyEquals(SpatialMatrix3x3 left, SpatialMatrix3x3 right) =>
        Math.Abs(left.M11 - right.M11) <= Tolerance &&
        Math.Abs(left.M12 - right.M12) <= Tolerance &&
        Math.Abs(left.M13 - right.M13) <= Tolerance &&
        Math.Abs(left.M21 - right.M21) <= Tolerance &&
        Math.Abs(left.M22 - right.M22) <= Tolerance &&
        Math.Abs(left.M23 - right.M23) <= Tolerance &&
        Math.Abs(left.M31 - right.M31) <= Tolerance &&
        Math.Abs(left.M32 - right.M32) <= Tolerance &&
        Math.Abs(left.M33 - right.M33) <= Tolerance;
}

public static class SpatialMappingGeometry
{
    public static PixelRect64 MapBoundingRect(
        PixelRect64 sourceRect,
        Func<SpatialPoint, SpatialPoint> mapPoint)
    {
        ArgumentNullException.ThrowIfNull(mapPoint);
        SpatialPoint[] corners =
        [
            mapPoint(new SpatialPoint(sourceRect.X, sourceRect.Y)),
            mapPoint(new SpatialPoint(sourceRect.Right, sourceRect.Y)),
            mapPoint(new SpatialPoint(sourceRect.Right, sourceRect.Bottom)),
            mapPoint(new SpatialPoint(sourceRect.X, sourceRect.Bottom)),
        ];
        if (corners.Any(point => !point.IsFinite))
        {
            throw new InvalidOperationException("映射后的裁剪坐标不是有限值。");
        }

        double minimumX = corners.Min(point => point.X);
        double minimumY = corners.Min(point => point.Y);
        double maximumX = corners.Max(point => point.X);
        double maximumY = corners.Max(point => point.Y);
        if (minimumX < long.MinValue || minimumY < long.MinValue ||
            maximumX > long.MaxValue || maximumY > long.MaxValue)
        {
            throw new OverflowException("映射后的裁剪坐标超出 PixelRect64 范围。");
        }

        long left = checked((long)Math.Floor(SnapNearInteger(minimumX)));
        long top = checked((long)Math.Floor(SnapNearInteger(minimumY)));
        long right = checked((long)Math.Ceiling(SnapNearInteger(maximumX)));
        long bottom = checked((long)Math.Ceiling(SnapNearInteger(maximumY)));
        return new PixelRect64(left, top, checked(right - left), checked(bottom - top));
    }

    private static double SnapNearInteger(double value)
    {
        double rounded = Math.Round(value);
        double tolerance = 1e-9 * Math.Max(1, Math.Abs(value));
        return Math.Abs(value - rounded) <= tolerance ? rounded : value;
    }
}
