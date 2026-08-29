using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class LinkedViewsTests
{
    [Fact]
    public void CrossAssetTranslation_SynchronizesCropAndColorScaleWithoutReplacingSources()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel targetSource = CreateSource("target.png", 500, 400);
        SourceAssetItemViewModel referenceSource = CreateSource("reference.png", 500, 400);
        FigurePanelViewModel target = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(targetSource, new PixelRect64(0, 0, 100, 80)));
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(referenceSource, new PixelRect64(20, 30, 100, 80)));
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);

        figure.LinkSelectedPanelCropsCommand.Execute(null);

        LinkGroup group = Assert.Single(figure.LinkGroups);
        Assert.Equal(referenceSource.Asset.Id, group.ReferenceAssetId);
        Assert.Equal(SpatialMappingOrigin.UserDeclaredIdentity, Assert.Single(group.Mappings).Origin);
        Assert.Same(targetSource, target.Source);
        Assert.Same(referenceSource, reference.Source);

        figure.UpdateLinkTranslation(group.Id, targetSource.Asset.Id, 10, 15);
        var changedReferenceCrop = new PixelRect64(40, 50, 120, 90);
        reference.ReplaceSource(referenceSource, changedReferenceCrop);
        reference.BlackPoint = 0.2;
        reference.WhitePoint = 0.8;

        Assert.Equal(new PixelRect64(50, 65, 120, 90), target.SourceRect);
        Assert.Equal(0.2, target.BlackPoint, 6);
        Assert.Equal(0.8, target.WhitePoint, 6);
        Assert.Same(targetSource, target.Source);
        Assert.Same(referenceSource, reference.Source);
        Assert.Equal(SpatialMappingKind.Translation, Assert.Single(figure.LinkGroups).Mappings[0].Kind);
    }

    [Fact]
    public void StaleMappingRevision_StopsCrossAssetCropSynchronization()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel targetSource = CreateSource("target.png", 300, 300);
        SourceAssetItemViewModel referenceSource = CreateSource("reference.png", 300, 300);
        FigurePanelViewModel target = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(targetSource, new PixelRect64(0, 0, 100, 100)));
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(referenceSource, new PixelRect64(0, 0, 100, 100)));
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);
        PixelRect64 unchanged = target.SourceRect;
        targetSource.RestoreSourceRevision(2);

        reference.ReplaceSource(referenceSource, new PixelRect64(20, 30, 120, 110));

        Assert.Equal(unchanged, target.SourceRect);
        Assert.Contains("过期", figure.LinkSynchronizationStatusText, StringComparison.Ordinal);
        Assert.Same(targetSource, target.Source);
    }

    private static FigureCanvasViewModel CreateFigure() => new(
        new BuiltInTemplateCatalog().LoadAll().Single(
            template => template.Id == "materials.multiscale-morphology.nature-double"));

    private static SourceAssetItemViewModel CreateSource(string name, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        BitmapSource preview = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        preview.Freeze();
        var asset = new SourceAsset(
            Guid.NewGuid(),
            name,
            name,
            new SourceFingerprint(0, DateTimeOffset.UtcNow, new string('0', 64), null),
            new SciCanvas.Core.Images.ImageMetadata(
                new PixelSize64(width, height),
                4,
                8,
                "Bgra32"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }
}
