using SciCanvas.Core.Images;

namespace SciCanvas.Core.Tests;

public sealed class ImageAdjustmentParametersTests
{
    [Fact]
    public void Identity_IsSafeAndValid()
    {
        ImageAdjustmentParameters parameters = new ImageAdjustmentParameters();

        Assert.True(parameters.IsIdentity);
        Assert.True(parameters.IsValid);
        Assert.Contains("源像素", parameters.ValidationMessage);
    }

    [Fact]
    public void Normalize_ClampsValuesAndNormalizesChannel()
    {
        ImageAdjustmentParameters parameters = new ImageAdjustmentParameters()
        {
            Brightness = 4,
            Contrast = -4,
            Gamma = 100,
            BlackPoint = 0.9,
            WhitePoint = 0,
            Channel = " RED ",
        }.Normalize();

        Assert.Equal(1, parameters.Brightness);
        Assert.Equal(-1, parameters.Contrast);
        Assert.Equal(10, parameters.Gamma);
        Assert.Equal("red", parameters.Channel);
        Assert.True(parameters.BlackPoint < parameters.WhitePoint);
        Assert.True(parameters.IsValid);
    }

    [Fact]
    public void InvalidChannel_IsRejected()
    {
        ImageAdjustmentParameters parameters = new ImageAdjustmentParameters() { Channel = "auto" };

        Assert.False(parameters.IsValid);
        Assert.Contains("无效", parameters.ValidationMessage);
    }
}
