using SciCanvas.Core.Linking;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Tests;

public sealed class SpatialRegistrationTests
{
    private static readonly DateTimeOffset CreatedAt =
        DateTimeOffset.Parse("2026-08-28T00:00:00Z");

    [Fact]
    public void Translation_UsesLeastSquaresMeanOffsetAndComputesRms()
    {
        Guid sourceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        SpatialRegistrationResult result = SpatialRegistrationSolver.Solve(
            sourceId,
            targetId,
            2,
            3,
            SpatialMappingKind.Translation,
            [
                Pair(0, 0, 5, 7),
                Pair(10, 20, 16, 27),
            ],
            CreatedAt);

        Assert.Equal(5.5, result.Mapping.Matrix.M13, 10);
        Assert.Equal(7, result.Mapping.Matrix.M23, 10);
        Assert.Equal(0.5, result.RmsPixels, 10);
        Assert.Equal(SpatialMappingOrigin.ManualLandmarks, result.Mapping.Origin);
        Assert.Equal(2, result.Mapping.EffectiveLandmarks.Count);
    }

    [Fact]
    public void Rigid_RecoversKnownRotationAndTranslation()
    {
        SpatialRegistrationResult result = SpatialRegistrationSolver.Solve(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            1,
            SpatialMappingKind.Rigid,
            [
                Pair(0, 0, 5, -3),
                Pair(10, 0, 5, 7),
                Pair(0, 4, 1, -3),
            ],
            CreatedAt);

        Assert.Equal(0, result.Mapping.Matrix.M11, 10);
        Assert.Equal(-1, result.Mapping.Matrix.M12, 10);
        Assert.Equal(5, result.Mapping.Matrix.M13, 10);
        Assert.Equal(1, result.Mapping.Matrix.M21, 10);
        Assert.Equal(0, result.Mapping.Matrix.M22, 10);
        Assert.Equal(-3, result.Mapping.Matrix.M23, 10);
        Assert.Equal(0, result.RmsPixels, 10);
        Assert.Equal(new SpatialPoint(2, 5), result.Mapping.MapReverse(new SpatialPoint(0, -1)));
    }

    [Fact]
    public void Affine_RecoversKnownMatrixAndRejectsCollinearSourcePoints()
    {
        Guid sourceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        RegistrationLandmarkPair[] valid =
        [
            Pair(0, 0, 4, -2),
            Pair(10, 0, 24, 3),
            Pair(0, 10, -6, 28),
            Pair(4, 7, 5, 21),
        ];

        SpatialRegistrationResult result = SpatialRegistrationSolver.Solve(
            sourceId, targetId, 4, 5, SpatialMappingKind.Affine, valid, CreatedAt);

        Assert.Equal(2, result.Mapping.Matrix.M11, 10);
        Assert.Equal(-1, result.Mapping.Matrix.M12, 10);
        Assert.Equal(4, result.Mapping.Matrix.M13, 10);
        Assert.Equal(0.5, result.Mapping.Matrix.M21, 10);
        Assert.Equal(3, result.Mapping.Matrix.M22, 10);
        Assert.Equal(-2, result.Mapping.Matrix.M23, 10);
        Assert.Equal(0, result.RmsPixels, 10);

        Assert.Throws<InvalidOperationException>(() => SpatialRegistrationSolver.Solve(
            sourceId,
            targetId,
            4,
            5,
            SpatialMappingKind.Affine,
            [Pair(0, 0, 0, 0), Pair(1, 1, 2, 2), Pair(2, 2, 4, 4)],
            CreatedAt));
    }

    [Fact]
    public void RmsPhysical_UsesTargetCalibrationAndRevisionStateIsExplicit()
    {
        Guid sourceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        var calibration = new SpatialCalibration(
            targetId,
            2,
            3,
            "nm",
            CalibrationOrigin.Manual);

        SpatialRegistrationResult result = SpatialRegistrationSolver.Solve(
            sourceId,
            targetId,
            8,
            9,
            SpatialMappingKind.Translation,
            [Pair(0, 0, 1, 0), Pair(10, 0, 13, 0)],
            CreatedAt,
            calibration);

        Assert.Equal(1, result.RmsPixels, 10);
        Assert.Equal(2, result.RmsPhysical!.Value, 10);
        Assert.Equal("nm", result.PhysicalUnit);
        Assert.Equal(SpatialMappingRevisionState.Current, result.Mapping.GetRevisionState(8, 9));
        Assert.Equal(SpatialMappingRevisionState.ReviewRequired, result.Mapping.GetRevisionState(8, 10));
        Assert.Contains(SpatialMappingQcCodes.MappingRevisionStale, result.Mapping.AssessRevisions(8, 10).QcCodes);
    }

    [Fact]
    public void RigidMappingValidation_RejectsScaleOrReflection()
    {
        SpatialMapping Create(SpatialMatrix3x3 matrix) => new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            1,
            SpatialMappingKind.Rigid,
            matrix,
            SpatialMappingOrigin.ImportedMetadata,
            CreatedAt);

        Assert.Throws<InvalidOperationException>(() => Create(new SpatialMatrix3x3(
            2, 0, 0,
            0, 2, 0,
            0, 0, 1)).EnsureValid());
        Assert.Throws<InvalidOperationException>(() => Create(new SpatialMatrix3x3(
            -1, 0, 0,
            0, 1, 0,
            0, 0, 1)).EnsureValid());
    }

    private static RegistrationLandmarkPair Pair(
        double sourceX,
        double sourceY,
        double targetX,
        double targetY) => new(
            Guid.NewGuid(),
            new SpatialPoint(sourceX, sourceY),
            new SpatialPoint(targetX, targetY));
}
