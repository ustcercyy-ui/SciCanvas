using SciCanvas.Core.Export;

namespace SciCanvas.Core.Tests;

public sealed class FigureGlobalStyleTests
{
    [Fact]
    public void ValidStyle_IsAcceptedByExportDocument()
    {
        var style = new FigureGlobalStyle(
            "Segoe UI",
            8,
            1.5,
            "#FF223344",
            "#FF00AA88",
            "#FFFFFFFF");

        var document = new FigureExportDocument(
            100,
            80,
            300,
            [],
            globalStyle: style);

        Assert.True(style.IsValid);
        Assert.Same(style, document.GlobalStyle);
    }

    [Theory]
    [InlineData("", 7, 1, "#FF111111")]
    [InlineData("Arial", 3, 1, "#FF111111")]
    [InlineData("Arial", 7, 0.1, "#FF111111")]
    [InlineData("Arial", 7, 1, "red")]
    public void InvalidStyle_IsRejected(
        string fontFamily,
        double fontSize,
        double strokeWidth,
        string textColor)
    {
        var style = new FigureGlobalStyle(
            fontFamily,
            fontSize,
            strokeWidth,
            textColor,
            "#FFE53935",
            "#FFFFFFFF");

        Assert.False(style.IsValid);
        Assert.Throws<InvalidOperationException>(() => new FigureExportDocument(
            100,
            80,
            300,
            [],
            globalStyle: style));
    }
}
