using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class RoiGeometryValidatorTests
{
    [Fact]
    public void Validate_ClassifiesSupportedGeometryAndComputesCoverage()
    {
        RoiGeometryValidationResult inside = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Rectangle, [new(1, 2), new(8, 9)]),
            10,
            10);
        RoiGeometryValidationResult partial = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Polygon, [new(-5, 0), new(5, 0), new(5, 10), new(-5, 10)]),
            10,
            10);
        RoiGeometryValidationResult outside = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Ellipse, [new(20, 20), new(30, 30)]),
            10,
            10);

        Assert.Equal(RoiGeometryValidationState.Inside, inside.State);
        Assert.Equal(1, inside.CoverageFraction);
        Assert.Equal(RoiGeometryValidationState.PartiallyOutside, partial.State);
        Assert.Equal(0.5, partial.CoverageFraction, 10);
        Assert.True(partial.ClippedToImage);
        Assert.Equal(RoiGeometryValidationState.Outside, outside.State);
        Assert.Equal(0, outside.CoverageFraction);
    }

    [Fact]
    public void Validate_DistinguishesDegenerateSelfIntersectingAndInvalid()
    {
        RoiGeometryValidationResult degenerate = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Polyline, [new(1, 1), new(1, 1)]),
            10,
            10);
        RoiGeometryValidationResult selfIntersecting = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Polygon, [new(1, 1), new(9, 9), new(1, 9), new(9, 1)]),
            10,
            10);
        RoiGeometryValidationResult invalid = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Rectangle, [new(double.NaN, 1), new(2, 2)]),
            10,
            10);

        Assert.Equal(RoiGeometryValidationState.Degenerate, degenerate.State);
        Assert.Equal(RoiGeometryValidationState.SelfIntersecting, selfIntersecting.State);
        Assert.Equal(RoiGeometryValidationState.Invalid, invalid.State);
    }

    [Fact]
    public void OutOfBoundsPolicy_RequiresReferenceConfirmationButAllowsPropagatedReview()
    {
        RoiGeometryValidationResult partial = RoiGeometryValidator.Validate(
            Roi(RoiGeometryKind.Polygon, [new(-2, 2), new(2, 2), new(2, 6), new(-2, 6)]),
            10,
            10);

        RoiBoundaryPolicyResult rejectedReference = RoiOutOfBoundsPolicy.Evaluate(
            partial,
            RoiBoundaryRole.Reference);
        RoiBoundaryPolicyResult confirmedReference = RoiOutOfBoundsPolicy.Evaluate(
            partial,
            RoiBoundaryRole.Reference,
            partialReferenceConfirmed: true);
        RoiBoundaryPolicyResult propagated = RoiOutOfBoundsPolicy.Evaluate(
            partial,
            RoiBoundaryRole.Propagated);

        Assert.False(rejectedReference.CanPersist);
        Assert.False(rejectedReference.CanAnalyze);
        Assert.True(confirmedReference.CanPersist);
        Assert.True(confirmedReference.CanAnalyze);
        Assert.Equal(ScientificValidityState.Warning, confirmedReference.Validity.State);
        Assert.True(propagated.CanPersist);
        Assert.True(propagated.CanAnalyze);
        Assert.Equal(ScientificValidityState.ReviewRequired, propagated.Validity.State);
    }

    private static RoiObject Roi(
        RoiGeometryKind kind,
        IReadOnlyList<MeasurementPoint> points) => new()
        {
            Id = Guid.NewGuid(),
            AssetId = Guid.NewGuid(),
            SourceRevision = 1,
            GeometryKind = kind,
            FrameIndex = 0,
            SourceGeometry = points,
        };
}
