using SciCanvas.Core.Geometry;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class CropEditorViewModelTests
{
    [Fact]
    public void SetBounds_UpdatesAllCoordinatesWithOneSemanticChangeEvent()
    {
        var crop = new CropEditorViewModel();
        crop.ConfigureForSource(new PixelSize64(2400, 1600));
        int boundsChangedCount = 0;
        crop.BoundsChanged += (_, _) => boundsChangedCount++;

        bool changed = crop.SetBounds(120, 80, 900, 600);

        Assert.True(changed);
        Assert.Equal(1, boundsChangedCount);
        Assert.Equal(120, crop.X);
        Assert.Equal(80, crop.Y);
        Assert.Equal(900, crop.Width);
        Assert.Equal(600, crop.Height);
        Assert.True(crop.IsValid);
    }

    [Fact]
    public void SetBounds_WithIdenticalCoordinates_DoesNotRaiseChangeEvent()
    {
        var crop = new CropEditorViewModel();
        crop.ConfigureForSource(new PixelSize64(1200, 800));
        int boundsChangedCount = 0;
        crop.BoundsChanged += (_, _) => boundsChangedCount++;

        bool changed = crop.SetBounds(crop.X, crop.Y, crop.Width, crop.Height);

        Assert.False(changed);
        Assert.Equal(0, boundsChangedCount);
    }
}
