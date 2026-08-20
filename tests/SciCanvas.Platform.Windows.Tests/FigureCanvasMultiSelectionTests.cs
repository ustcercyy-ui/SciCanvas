using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class FigureCanvasMultiSelectionTests
{
    [Fact]
    public void MultiSelection_AlignsEdgesAndDistributesEqualHorizontalGaps()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource();
        FigurePanelViewModel first = AddPanel(figure, source);
        FigurePanelViewModel second = AddPanel(figure, source);
        FigurePanelViewModel third = AddPanel(figure, source);
        Configure(first, 0, 100, 100, 100);
        Configure(second, 260, 240, 100, 100);
        Configure(third, 1000, 420, 100, 100);
        figure.SelectPanel(first, toggle: false);
        figure.SelectPanel(second, toggle: true);
        figure.SelectPanel(third, toggle: true);

        Assert.Equal(3, figure.SelectedPanelCount);
        figure.AlignSelectionTopCommand.Execute(null);
        Assert.All(figure.SelectedPanels, panel => Assert.Equal(100, panel.Y));

        figure.DistributeSelectionHorizontallyCommand.Execute(null);
        Assert.Equal(0, first.X);
        Assert.Equal(500, second.X);
        Assert.Equal(1000, third.X);
        Assert.Equal(400, second.X - (first.X + first.Width));
        Assert.Equal(400, third.X - (second.X + second.Width));
    }

    [Fact]
    public void MultiSelection_GroupMoveClampsTogetherAndDoesNotMoveLockedPanel()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource();
        FigurePanelViewModel first = AddPanel(figure, source);
        FigurePanelViewModel locked = AddPanel(figure, source);
        FigurePanelViewModel third = AddPanel(figure, source);
        Configure(first, 100, 100, 100, 100);
        Configure(locked, 400, 100, 100, 100);
        Configure(third, 800, 100, 100, 100);
        locked.IsLocked = true;
        figure.SelectPanel(first, toggle: false);
        figure.SelectPanel(locked, toggle: true);
        figure.SelectPanel(third, toggle: true);

        (long movedX, long movedY) = figure.MoveSelectedPanelsBy(-500, -500);

        Assert.Equal(-100, movedX);
        Assert.Equal(-100, movedY);
        Assert.Equal((0L, 0L), (first.X, first.Y));
        Assert.Equal((400L, 100L), (locked.X, locked.Y));
        Assert.Equal((700L, 0L), (third.X, third.Y));
        Assert.False(figure.DistributeSelectionHorizontallyCommand.CanExecute(null));
    }

    [Fact]
    public void GroupMove_SnapsSelectedBoundsToGuideWithinTolerance()
    {
        FigureCanvasViewModel figure = CreateFigure();
        FigurePanelViewModel panel = AddPanel(figure, CreateSource());
        Configure(panel, 100, 100, 100, 100);
        figure.AddVerticalGuideCommand.Execute(null);
        FigureGuideViewModel guide = Assert.IsType<FigureGuideViewModel>(figure.SelectedGuide);
        guide.Position = 400;
        figure.SnapTolerancePixels = 12;

        (long movedX, long movedY) = figure.MoveSelectedPanelsBy(291, 0);

        Assert.Equal(300, movedX);
        Assert.Equal(0, movedY);
        Assert.Equal(400, panel.X);
    }

    [Fact]
    public void ExactSpacing_PreservesFirstPanelAndSetsRequestedBoundaryGap()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource();
        FigurePanelViewModel first = AddPanel(figure, source);
        FigurePanelViewModel second = AddPanel(figure, source);
        FigurePanelViewModel third = AddPanel(figure, source);
        Configure(first, 100, 100, 100, 100);
        Configure(second, 500, 100, 100, 100);
        Configure(third, 900, 100, 100, 100);
        figure.SelectPanel(first, toggle: false);
        figure.SelectPanel(second, toggle: true);
        figure.SelectPanel(third, toggle: true);
        figure.ExactSpacingPixels = 24;

        figure.SetHorizontalSpacingCommand.Execute(null);

        Assert.Equal(100, first.X);
        Assert.Equal(224, second.X);
        Assert.Equal(348, third.X);
        Assert.Equal(24, second.X - (first.X + first.Width));
        Assert.Equal(24, third.X - (second.X + second.Width));
    }

    [Fact]
    public void CanvasBackgroundAndPanelNumbering_AreAppliedToExportDocument()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource();
        FigurePanelViewModel first = AddPanel(figure, source);
        FigurePanelViewModel second = AddPanel(figure, source);
        FigurePanelViewModel third = AddPanel(figure, source);
        Configure(first, 600, 400, 100, 100);
        Configure(second, 100, 400, 100, 100);
        Configure(third, 100, 50, 100, 100);
        figure.BackgroundColor = "#123456";
        figure.PanelLabelSequence = "uppercase";

        figure.RenumberPanelLabels(force: true);

        Assert.Equal("A", third.Label);
        Assert.Equal("B", second.Label);
        Assert.Equal("C", first.Label);
        FigureExportDocument exported = figure.CreateExportDocument();
        Assert.Equal("#FF123456", exported.BackgroundColor);
        Assert.Equal(["C", "B", "A"], exported.Panels.Select(panel => panel.Label).ToArray());

        figure.ShowPanelLabels = false;
        Assert.All(figure.CreateExportDocument().Panels, panel => Assert.Equal(string.Empty, panel.Label));
    }

    private static FigureCanvasViewModel CreateFigure() => new(
        new BuiltInTemplateCatalog().LoadAll().Single(
            template => template.Id == "materials.multiscale-morphology.nature-double"));

    private static FigurePanelViewModel AddPanel(
        FigureCanvasViewModel figure,
        SourceAssetItemViewModel source) =>
        Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(0, 0, 100, 100)));

    private static void Configure(
        FigurePanelViewModel panel,
        long x,
        long y,
        long width,
        long height)
    {
        panel.X = x;
        panel.Y = y;
        panel.Width = width;
        panel.Height = height;
    }

    private static SourceAssetItemViewModel CreateSource()
    {
        int stride = 100 * 4;
        byte[] pixels = new byte[stride * 100];
        BitmapSource preview = BitmapSource.Create(
            100,
            100,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        preview.Freeze();
        var asset = new SourceAsset(
            Guid.NewGuid(),
            "selection.png",
            "selection.png",
            new SourceFingerprint(0, DateTimeOffset.UtcNow, new string('0', 64), null),
            new SciCanvas.Core.Images.ImageMetadata(
                new PixelSize64(100, 100),
                4,
                8,
                "Bgra32"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }
}
