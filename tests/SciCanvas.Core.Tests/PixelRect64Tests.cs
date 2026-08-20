using SciCanvas.Core.Cropping;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Tests;

public sealed class PixelRect64Tests
{
    [Fact]
    public void Constructor_RejectsNegativeCoordinates()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect64(-1, 0, 10, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect64(0, -1, 10, 10));
    }

    [Fact]
    public void Constructor_RejectsZeroDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect64(0, 0, 0, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new PixelRect64(0, 0, 10, 0));
    }

    [Fact]
    public void Constructor_RejectsOverflowingEdges()
    {
        Assert.Throws<OverflowException>(() => new PixelRect64(long.MaxValue, 0, 1, 1));
        Assert.Throws<OverflowException>(() => new PixelRect64(0, long.MaxValue, 1, 1));
    }

    [Fact]
    public void MoveBy_ChangesOnlyPosition()
    {
        PixelRect64 original = new(100, 200, 1200, 800);

        PixelRect64 moved = original.MoveBy(10, 20);

        Assert.Equal(new PixelRect64(110, 220, 1200, 800), moved);
    }

    [Fact]
    public void Validator_AcceptsCropExactlyOnSourceBoundary()
    {
        PixelRect64 crop = new(848, 736, 1200, 800);
        PixelSize64 source = new(2048, 1536);

        CropValidationResult result = CropBoundsValidator.Validate(crop, source);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validator_RejectsCropBeyondRightBoundary()
    {
        PixelRect64 crop = new(849, 736, 1200, 800);
        PixelSize64 source = new(2048, 1536);

        CropValidationResult result = CropBoundsValidator.Validate(crop, source);

        Assert.False(result.IsValid);
        Assert.Contains("右边界", result.Message);
    }

    [Fact]
    public void Validator_RejectsCropBeyondBottomBoundary()
    {
        PixelRect64 crop = new(848, 737, 1200, 800);
        PixelSize64 source = new(2048, 1536);

        CropValidationResult result = CropBoundsValidator.Validate(crop, source);

        Assert.False(result.IsValid);
        Assert.Contains("下边界", result.Message);
    }
}

