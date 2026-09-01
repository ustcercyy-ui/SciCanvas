using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Linking;

namespace SciCanvas.Core.Tests;

public sealed class RegisteredPlaneResamplerTests
{
    [Fact]
    public void Resample_NearestSupportsIdentityTranslationRigidAndAffine()
    {
        (SpatialMappingKind Kind, SpatialMatrix3x3 Matrix, PixelSize64 Size, byte[] Raw, byte[] Expected)[] cases =
        [
            (
                SpatialMappingKind.Identity,
                SpatialMatrix3x3.Identity,
                new PixelSize64(2, 2),
                [1, 2, 3, 4],
                [1, 2, 3, 4]),
            (
                SpatialMappingKind.Translation,
                SpatialMatrix3x3.CreateTranslation(1, 0),
                new PixelSize64(3, 2),
                [9, 1, 2, 9, 3, 4],
                [1, 2, 3, 4]),
            (
                SpatialMappingKind.Rigid,
                new SpatialMatrix3x3(
                    0, -1, 2,
                    1, 0, 0,
                    0, 0, 1),
                new PixelSize64(2, 2),
                [1, 2, 3, 4],
                [2, 4, 1, 3]),
            (
                SpatialMappingKind.Affine,
                new SpatialMatrix3x3(
                    2, 0, -0.5,
                    0, 1, 0,
                    0, 0, 1),
                new PixelSize64(3, 2),
                [1, 9, 2, 3, 9, 4],
                [1, 2, 3, 4]),
        ];

        foreach ((SpatialMappingKind kind, SpatialMatrix3x3 matrix, PixelSize64 size, byte[] raw, byte[] expected) in cases)
        {
            Fixture fixture = CreateFixture(
                kind,
                matrix,
                size,
                raw,
                new PixelRect64(0, 0, 2, 2),
                RegisteredInterpolation.Nearest);

            RegisteredPlaneResamplingResult result =
                RegisteredPlaneResampler.Resample(fixture.Plane, fixture.Spec);

            Assert.Equal(expected.Select(value => (double)value), result.Samples);
            Assert.All(result.Validity, Assert.True);
        }
    }

    [Fact]
    public void Resample_BilinearUsesReferencePixelCentersWithoutMutatingRawPlane()
    {
        Fixture fixture = CreateFixture(
            SpatialMappingKind.Translation,
            SpatialMatrix3x3.CreateTranslation(0.5, 0),
            new PixelSize64(2, 1),
            [0, 100],
            new PixelRect64(0, 0, 1, 1),
            RegisteredInterpolation.Bilinear);

        RegisteredPlaneResamplingResult result =
            RegisteredPlaneResampler.Resample(fixture.Plane, fixture.Spec);

        Assert.Equal(50, Assert.Single(result.Samples), 10);
        Assert.True(Assert.Single(result.Validity));
        Assert.Equal(0, fixture.Plane.GetRawValue(0, 0));
        Assert.Equal(100, fixture.Plane.GetRawValue(1, 0));
        Assert.Same(fixture.Plane, result.SourcePlane);
    }

    [Fact]
    public void Resample_AppliesTransparentZeroAndNoDataBordersExplicitly()
    {
        foreach ((RegisteredBorderPolicy policy, bool expectedValid, bool expectedNan) in new[]
                 {
                     (RegisteredBorderPolicy.Transparent, false, false),
                     (RegisteredBorderPolicy.Zero, true, false),
                     (RegisteredBorderPolicy.NoData, false, true),
                 })
        {
            Fixture fixture = CreateFixture(
                SpatialMappingKind.Identity,
                SpatialMatrix3x3.Identity,
                new PixelSize64(1, 1),
                [7],
                new PixelRect64(0, 0, 3, 1),
                RegisteredInterpolation.Nearest,
                policy);

            RegisteredPlaneResamplingResult result =
                RegisteredPlaneResampler.Resample(fixture.Plane, fixture.Spec);

            Assert.Equal(7, result.GetValue(0));
            Assert.True(result.IsValid(0));
            Assert.Equal(expectedValid, result.IsValid(1));
            Assert.Equal(expectedValid, result.IsValid(2));
            Assert.Equal(expectedNan, double.IsNaN(result.GetValue(1)));
            if (!expectedNan)
            {
                Assert.Equal(0, result.GetValue(1));
            }
        }
    }

    [Fact]
    public void EnsureValid_ForbidsBilinearForLabelsAndMasks()
    {
        Fixture fixture = CreateFixture(
            SpatialMappingKind.Identity,
            SpatialMatrix3x3.Identity,
            new PixelSize64(1, 1),
            [1],
            new PixelRect64(0, 0, 1, 1),
            RegisteredInterpolation.Nearest);
        RegisteredPlaneResamplingSpec invalid = fixture.Spec with
        {
            Interpolation = RegisteredInterpolation.Bilinear,
            Semantic = RegisteredPlaneSemantic.LabelOrMask,
        };

        InvalidOperationException exception =
            Assert.Throws<InvalidOperationException>(() => invalid.EnsureValid());

        Assert.Contains("Nearest", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CalculateSourceReadRegion_IncludesBilinearNeighbourhoodAndClipsToTarget()
    {
        Fixture fixture = CreateFixture(
            SpatialMappingKind.Translation,
            SpatialMatrix3x3.CreateTranslation(1.25, 0),
            new PixelSize64(4, 1),
            [1, 2, 3, 4],
            new PixelRect64(0, 0, 2, 1),
            RegisteredInterpolation.Bilinear);

        PixelRect64 region = RegisteredPlaneResampler.CalculateSourceReadRegion(fixture.Spec);

        Assert.Equal(new PixelRect64(1, 0, 3, 1), region);
    }

    [Fact]
    public void Composite_UsesRegisteredValidityForTransparentAndZeroBorders()
    {
        foreach ((RegisteredBorderPolicy policy, double expectedAlpha) in new[]
                 {
                     (RegisteredBorderPolicy.Transparent, 0d),
                     (RegisteredBorderPolicy.Zero, 1d),
                 })
        {
            Fixture fixture = CreateFixture(
                SpatialMappingKind.Identity,
                SpatialMatrix3x3.Identity,
                new PixelSize64(1, 1),
                [7],
                new PixelRect64(0, 0, 2, 1),
                RegisteredInterpolation.Nearest,
                policy);
            RegisteredPlaneResamplingResult registered =
                RegisteredPlaneResampler.Resample(fixture.Plane, fixture.Spec);
            var settings = new ChannelDisplaySettings(
                fixture.Plane.Channel.Id,
                true,
                "#FFFFFFFF",
                1,
                0,
                byte.MaxValue,
                1,
                false);

            ScientificChannelCompositeResult composite =
                ScientificChannelComposite.ComposeHighPrecision(
                    [new ScientificChannelCompositeInput(fixture.Plane, settings, registered)]);

            Assert.Equal(1, composite[0, 0].Alpha);
            Assert.Equal(expectedAlpha, composite[1, 0].Alpha);
        }
    }

    private static Fixture CreateFixture(
        SpatialMappingKind kind,
        SpatialMatrix3x3 matrix,
        PixelSize64 targetSize,
        byte[] raw,
        PixelRect64 referenceRegion,
        RegisteredInterpolation interpolation,
        RegisteredBorderPolicy borderPolicy = RegisteredBorderPolicy.Transparent)
    {
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        var mapping = new SpatialMapping(
            Guid.NewGuid(),
            referenceId,
            targetId,
            3,
            7,
            kind,
            matrix,
            SpatialMappingOrigin.ImportedMetadata,
            DateTimeOffset.UnixEpoch).EnsureValid();
        var selector = ChannelPlaneSelector.ExternalAsset(0);
        var grid = new RegisteredReferenceGrid(
            new ScientificPlaneRef(referenceId, 3, selector),
            referenceRegion).EnsureValid();
        var spec = new RegisteredPlaneResamplingSpec(
            mapping,
            grid,
            targetSize,
            interpolation,
            borderPolicy).EnsureValid();
        var channel = selector.CreateChannelDescriptor(
            Guid.NewGuid(),
            "signal",
            ScientificSampleType.UInt8,
            8,
            role: null,
            defaultColor: "#FFFFFFFF");
        var plane = new ImagePlane(
            targetId,
            7,
            0,
            new PixelRect64(0, 0, targetSize.Width, targetSize.Height),
            channel,
            new UInt8ImagePlaneSamples(raw));
        return new Fixture(plane, spec);
    }

    private sealed record Fixture(
        ImagePlane Plane,
        RegisteredPlaneResamplingSpec Spec);
}
