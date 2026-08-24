using SciCanvas.Core.Export;

namespace SciCanvas.Core.Tests;

public sealed class ScientificColorPaletteTests
{
    [Fact]
    public void DefaultPalette_IsValidAndUsesUniqueObjectNames()
    {
        ScientificColorPaletteReview review = ScientificColorPalette.Review(
            ScientificColorPalette.Default);

        Assert.True(review.IsValid, string.Join(Environment.NewLine, review.Warnings));
        Assert.Equal(
            ScientificColorPalette.Default.Count,
            ScientificColorPalette.Default.Select(color => color.Name).Distinct().Count());
    }

    [Fact]
    public void Review_FlagsDuplicateNamesAndIndistinguishableColors()
    {
        ScientificColorPaletteReview review = ScientificColorPalette.Review(
        [
            new ScientificColorDefinition(Guid.NewGuid(), "phase", "#FFFF0000"),
            new ScientificColorDefinition(Guid.NewGuid(), "Phase", "#FFFF0000"),
        ]);

        Assert.False(review.IsValid);
        Assert.Contains(review.Warnings, warning => warning.Contains("名称", StringComparison.Ordinal));
        Assert.Contains(review.Warnings, warning => warning.Contains("难以区分", StringComparison.Ordinal));
    }
}
