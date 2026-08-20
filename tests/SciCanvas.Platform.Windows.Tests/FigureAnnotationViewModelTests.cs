using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class FigureAnnotationViewModelTests
{
    [Fact]
    public void TextAnnotation_ValidatesPublicationPointSizeAndColor()
    {
        var annotation = new FigureAnnotationViewModel(
            FigureAnnotationKind.Text,
            1000,
            800,
            300,
            0)
        {
            Text = "晶界",
            X = 120,
            Y = 240,
            FontSizePt = 7,
            Color = "#FF112233",
        };

        Assert.True(annotation.IsValid);
        Assert.Equal(7.0 / 72.0 * 300, annotation.FontSizePixels, 8);
        Assert.Equal("text", annotation.CreateExportItem().Kind);

        annotation.Color = "red";
        Assert.False(annotation.IsValid);
        Assert.Contains("#RRGGBB", annotation.ValidationMessage, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => annotation.CreateExportItem());
    }

    [Fact]
    public void ArrowAnnotation_MoveByClampsBothEndpointsInsideCanvas()
    {
        var annotation = new FigureAnnotationViewModel(
            FigureAnnotationKind.Arrow,
            500,
            400,
            300,
            0)
        {
            X = 100,
            Y = 100,
            EndX = 300,
            EndY = 200,
        };

        annotation.MoveBy(500, 500);

        Assert.Equal(300, annotation.X);
        Assert.Equal(300, annotation.Y);
        Assert.Equal(500, annotation.EndX);
        Assert.Equal(400, annotation.EndY);
        Assert.True(annotation.IsValid);
    }

    [Theory]
    [InlineData(FigureAnnotationKind.Rectangle, "rectangle")]
    [InlineData(FigureAnnotationKind.Ellipse, "ellipse")]
    public void ShapeAnnotation_ValidatesBoundsAndMovesAsOneObject(
        FigureAnnotationKind kind,
        string expectedKind)
    {
        var annotation = new FigureAnnotationViewModel(kind, 500, 400, 300, 0)
        {
            X = 100,
            Y = 80,
            EndX = 260,
            EndY = 180,
            StrokeWidthPt = 1.5,
        };

        Assert.True(annotation.IsValid);
        Assert.Equal(160, annotation.ShapeWidth);
        Assert.Equal(100, annotation.ShapeHeight);
        Assert.Equal(expectedKind, annotation.CreateExportItem().Kind);

        annotation.MoveBy(500, 500);
        Assert.Equal(340, annotation.X);
        Assert.Equal(300, annotation.Y);
        Assert.Equal(500, annotation.EndX);
        Assert.Equal(400, annotation.EndY);

        annotation.EndX = annotation.X + 4;
        Assert.False(annotation.IsValid);
        Assert.Contains("至少为 5 px", annotation.ValidationMessage, StringComparison.Ordinal);
    }
}
