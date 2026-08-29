using SciCanvas.Core.Science;

namespace SciCanvas.Core.Linking;

public sealed record RegistrationLandmarkPair(
    Guid Id,
    SpatialPoint SourcePoint,
    SpatialPoint TargetPoint)
{
    public RegistrationLandmarkPair EnsureValid()
    {
        if (Id == Guid.Empty || !SourcePoint.IsFinite || !TargetPoint.IsFinite)
        {
            throw new InvalidOperationException("配准 landmark 必须包含有效 ID 与有限的 source/target 像素坐标。");
        }

        return this;
    }
}

public sealed record RegistrationLandmarkResidual(
    Guid LandmarkId,
    double DeltaX,
    double DeltaY,
    double DistancePixels)
{
    public RegistrationLandmarkResidual EnsureValid()
    {
        if (LandmarkId == Guid.Empty || !double.IsFinite(DeltaX) || !double.IsFinite(DeltaY) ||
            !double.IsFinite(DistancePixels) || DistancePixels < 0)
        {
            throw new InvalidOperationException("配准逐点残差无效。");
        }

        return this;
    }
}

public sealed record SpatialRegistrationResult(
    SpatialMapping Mapping,
    IReadOnlyList<RegistrationLandmarkResidual> PointResiduals,
    double RmsPixels,
    double? RmsPhysical,
    string? PhysicalUnit)
{
    public SpatialRegistrationResult EnsureValid()
    {
        Mapping.EnsureValid();
        if (!double.IsFinite(RmsPixels) || RmsPixels < 0 ||
            PointResiduals.Count != Mapping.EffectiveLandmarks.Count ||
            PointResiduals.Any(residual => !double.IsFinite(residual.DistancePixels) || residual.DistancePixels < 0) ||
            (RmsPhysical.HasValue != !string.IsNullOrWhiteSpace(PhysicalUnit)) ||
            RmsPhysical is double physical && (!double.IsFinite(physical) || physical < 0))
        {
            throw new InvalidOperationException("配准求解结果或 RMS 无效。");
        }

        return this;
    }
}

/// <summary>
/// Least-squares manual-landmark registration. Matrices use
/// TargetPoint = M × SourcePoint and are stored row-major.
/// </summary>
public static class SpatialRegistrationSolver
{
    private const double DegenerateTolerance = 1e-12;

    public static SpatialRegistrationResult Solve(
        Guid sourceAssetId,
        Guid targetAssetId,
        long sourceRevision,
        long targetRevision,
        SpatialMappingKind kind,
        IEnumerable<RegistrationLandmarkPair> landmarkPairs,
        DateTimeOffset createdAt,
        SpatialCalibration? targetCalibration = null,
        Guid? mappingId = null)
    {
        if (sourceAssetId == Guid.Empty || targetAssetId == Guid.Empty ||
            sourceAssetId == targetAssetId || sourceRevision < 1 || targetRevision < 1 ||
            createdAt == default)
        {
            throw new InvalidOperationException("配准必须绑定两个不同素材及其有效 source revision。");
        }

        RegistrationLandmarkPair[] pairs = (landmarkPairs ?? throw new ArgumentNullException(nameof(landmarkPairs)))
            .Select(pair => pair.EnsureValid())
            .ToArray();
        if (pairs.Select(pair => pair.Id).Distinct().Count() != pairs.Length)
        {
            throw new InvalidOperationException("配准 landmark ID 不能重复。");
        }

        int minimumCount = kind switch
        {
            SpatialMappingKind.Translation => 1,
            SpatialMappingKind.Rigid => 2,
            SpatialMappingKind.Affine => 3,
            _ => throw new NotSupportedException("manual landmark registration 仅支持 Translation、Rigid 与 Affine。"),
        };
        if (pairs.Length < minimumCount)
        {
            throw new InvalidOperationException($"{kind} 配准至少需要 {minimumCount} 对有效 landmark。");
        }

        SpatialMatrix3x3 matrix = kind switch
        {
            SpatialMappingKind.Translation => SolveTranslation(pairs),
            SpatialMappingKind.Rigid => SolveRigid(pairs),
            SpatialMappingKind.Affine => SolveAffine(pairs),
            _ => throw new NotSupportedException(),
        };

        RegistrationLandmarkResidual[] residuals = pairs.Select(pair =>
        {
            SpatialPoint predicted = matrix.Transform(pair.SourcePoint);
            double deltaX = predicted.X - pair.TargetPoint.X;
            double deltaY = predicted.Y - pair.TargetPoint.Y;
            return new RegistrationLandmarkResidual(
                pair.Id,
                deltaX,
                deltaY,
                Math.Sqrt(deltaX * deltaX + deltaY * deltaY)).EnsureValid();
        }).ToArray();
        double rmsPixels = Math.Sqrt(residuals.Average(residual =>
            residual.DeltaX * residual.DeltaX + residual.DeltaY * residual.DeltaY));

        double? rmsPhysical = null;
        string? physicalUnit = null;
        if (targetCalibration is { IsValid: true } calibration &&
            calibration.SourceAssetId == targetAssetId)
        {
            rmsPhysical = Math.Sqrt(residuals.Average(residual =>
                Math.Pow(residual.DeltaX * calibration.UnitsPerPixelX, 2) +
                Math.Pow(residual.DeltaY * calibration.UnitsPerPixelY, 2)));
            physicalUnit = ScientificLengthUnits.Normalize(calibration.Unit);
        }

        var mapping = new SpatialMapping(
            mappingId ?? Guid.NewGuid(),
            sourceAssetId,
            targetAssetId,
            sourceRevision,
            targetRevision,
            kind,
            matrix,
            SpatialMappingOrigin.ManualLandmarks,
            createdAt,
            rmsPixels,
            Array.AsReadOnly(pairs),
            rmsPhysical,
            physicalUnit).EnsureValid();

        return new SpatialRegistrationResult(
            mapping,
            Array.AsReadOnly(residuals),
            rmsPixels,
            rmsPhysical,
            physicalUnit).EnsureValid();
    }

    private static SpatialMatrix3x3 SolveTranslation(IReadOnlyList<RegistrationLandmarkPair> pairs)
    {
        double offsetX = pairs.Average(pair => pair.TargetPoint.X - pair.SourcePoint.X);
        double offsetY = pairs.Average(pair => pair.TargetPoint.Y - pair.SourcePoint.Y);
        return SpatialMatrix3x3.CreateTranslation(offsetX, offsetY);
    }

    private static SpatialMatrix3x3 SolveRigid(IReadOnlyList<RegistrationLandmarkPair> pairs)
    {
        double sourceX = pairs.Average(pair => pair.SourcePoint.X);
        double sourceY = pairs.Average(pair => pair.SourcePoint.Y);
        double targetX = pairs.Average(pair => pair.TargetPoint.X);
        double targetY = pairs.Average(pair => pair.TargetPoint.Y);
        double dot = 0;
        double cross = 0;
        double sourceSpread = 0;
        double targetSpread = 0;
        foreach (RegistrationLandmarkPair pair in pairs)
        {
            double sx = pair.SourcePoint.X - sourceX;
            double sy = pair.SourcePoint.Y - sourceY;
            double tx = pair.TargetPoint.X - targetX;
            double ty = pair.TargetPoint.Y - targetY;
            dot += sx * tx + sy * ty;
            cross += sx * ty - sy * tx;
            sourceSpread += sx * sx + sy * sy;
            targetSpread += tx * tx + ty * ty;
        }

        double norm = Math.Sqrt(dot * dot + cross * cross);
        if (sourceSpread <= DegenerateTolerance || targetSpread <= DegenerateTolerance ||
            norm <= DegenerateTolerance)
        {
            throw new InvalidOperationException("Rigid 配准 landmark 退化；source 与 target 均至少需要两个不同点。");
        }

        double cosine = dot / norm;
        double sine = cross / norm;
        double translateX = targetX - (cosine * sourceX - sine * sourceY);
        double translateY = targetY - (sine * sourceX + cosine * sourceY);
        return new SpatialMatrix3x3(
            cosine, -sine, translateX,
            sine, cosine, translateY,
            0, 0, 1).EnsureValid();
    }

    private static SpatialMatrix3x3 SolveAffine(IReadOnlyList<RegistrationLandmarkPair> pairs)
    {
        // Solve the normal equations for [x y 1] independently for target X and Y.
        double xx = 0, xy = 0, x = 0, yy = 0, y = 0;
        double bxX = 0, byX = 0, bX = 0;
        double bxY = 0, byY = 0, bY = 0;
        foreach (RegistrationLandmarkPair pair in pairs)
        {
            double sx = pair.SourcePoint.X;
            double sy = pair.SourcePoint.Y;
            xx += sx * sx;
            xy += sx * sy;
            x += sx;
            yy += sy * sy;
            y += sy;
            bxX += sx * pair.TargetPoint.X;
            byX += sy * pair.TargetPoint.X;
            bX += pair.TargetPoint.X;
            bxY += sx * pair.TargetPoint.Y;
            byY += sy * pair.TargetPoint.Y;
            bY += pair.TargetPoint.Y;
        }

        double[,] normal =
        {
            { xx, xy, x },
            { xy, yy, y },
            { x, y, pairs.Count },
        };
        double[] targetX = SolveThreeByThree(normal, [bxX, byX, bX]);
        double[] targetY = SolveThreeByThree(normal, [bxY, byY, bY]);
        try
        {
            return new SpatialMatrix3x3(
                targetX[0], targetX[1], targetX[2],
                targetY[0], targetY[1], targetY[2],
                0, 0, 1).EnsureValid();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException("Affine 配准结果不可逆；target landmark 几何可能退化。", exception);
        }
    }

    private static double[] SolveThreeByThree(double[,] coefficients, double[] values)
    {
        var augmented = new double[3, 4];
        double scale = 0;
        for (int row = 0; row < 3; row++)
        {
            for (int column = 0; column < 3; column++)
            {
                augmented[row, column] = coefficients[row, column];
                scale = Math.Max(scale, Math.Abs(coefficients[row, column]));
            }

            augmented[row, 3] = values[row];
        }

        for (int pivot = 0; pivot < 3; pivot++)
        {
            int bestRow = pivot;
            for (int row = pivot + 1; row < 3; row++)
            {
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[bestRow, pivot]))
                {
                    bestRow = row;
                }
            }

            if (Math.Abs(augmented[bestRow, pivot]) <= DegenerateTolerance * Math.Max(1, scale))
            {
                throw new InvalidOperationException("Affine 配准 landmark 退化；至少需要 3 个不共线 source points。");
            }

            if (bestRow != pivot)
            {
                for (int column = pivot; column < 4; column++)
                {
                    (augmented[pivot, column], augmented[bestRow, column]) =
                        (augmented[bestRow, column], augmented[pivot, column]);
                }
            }

            double pivotValue = augmented[pivot, pivot];
            for (int column = pivot; column < 4; column++)
            {
                augmented[pivot, column] /= pivotValue;
            }

            for (int row = 0; row < 3; row++)
            {
                if (row == pivot)
                {
                    continue;
                }

                double factor = augmented[row, pivot];
                for (int column = pivot; column < 4; column++)
                {
                    augmented[row, column] -= factor * augmented[pivot, column];
                }
            }
        }

        return [augmented[0, 3], augmented[1, 3], augmented[2, 3]];
    }
}
