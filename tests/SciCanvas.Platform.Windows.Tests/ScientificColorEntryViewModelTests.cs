using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ScientificColorEntryViewModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("not-a-color")]
    public void ColorBrush_InvalidDraftInputFallsBackToTransparent(string color)
    {
        var viewModel = new ScientificColorEntryViewModel(
            new ScientificColorDefinition(Guid.NewGuid(), "Draft", color));

        Assert.Same(Brushes.Transparent, viewModel.ColorBrush);
    }
}
