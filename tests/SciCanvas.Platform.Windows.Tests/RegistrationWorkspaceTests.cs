using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Sources;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class RegistrationWorkspaceTests
{
    [Fact]
    public void AffineCommand_UpdatesMappingMatrixLandmarksRmsAndLinkedCrop()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel targetSource = CreateSource("eds.tif", 500, 500);
        SourceAssetItemViewModel referenceSource = CreateSource("sem.tif", 500, 500);
        FigurePanelViewModel target = Assert.IsType<FigurePanelViewModel>(figure.AddPanel(
            targetSource,
            new PixelRect64(0, 0, 100, 100)));
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(figure.AddPanel(
            referenceSource,
            new PixelRect64(10, 20, 100, 100)));
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);
        using var workspace = new RegistrationWorkspaceViewModel(figure);
        RegistrationMappingItemViewModel registration = Assert.Single(workspace.Registrations);
        registration.LandmarksText = "0,0 -> 5,7\n10,0 -> 25,7\n0,10 -> 5,37";

        registration.SolveAffineCommand.Execute(null);

        SpatialMapping mapping = Assert.Single(figure.LinkGroups).Mappings[0];
        Assert.Equal(SpatialMappingKind.Affine, mapping.Kind);
        Assert.Equal(SpatialMappingOrigin.ManualLandmarks, mapping.Origin);
        Assert.Equal(3, mapping.EffectiveLandmarks.Count);
        Assert.Equal(2, mapping.Matrix.M11, 10);
        Assert.Equal(3, mapping.Matrix.M22, 10);
        Assert.Equal(5, mapping.Matrix.M13, 10);
        Assert.Equal(7, mapping.Matrix.M23, 10);
        Assert.Equal(0, mapping.ResidualPixels!.Value, 10);
        Assert.Equal(new PixelRect64(25, 67, 200, 300), target.SourceRect);
        Assert.Contains("RMS = 0 px", registration.RmsText, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceRevisionChange_MarksRegistrationReviewRequiredAndReSolveBindsCurrentRevision()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel targetSource = CreateSource("target.tif", 300, 300);
        SourceAssetItemViewModel referenceSource = CreateSource("reference.tif", 300, 300);
        FigurePanelViewModel target = Assert.IsType<FigurePanelViewModel>(figure.AddPanel(targetSource, new PixelRect64(0, 0, 50, 50)));
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(figure.AddPanel(referenceSource, new PixelRect64(0, 0, 50, 50)));
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);
        using var workspace = new RegistrationWorkspaceViewModel(figure);
        RegistrationMappingItemViewModel registration = Assert.Single(workspace.Registrations);
        targetSource.RestoreSourceRevision(2);

        Assert.Contains("ReviewRequired", registration.RevisionStatusText, StringComparison.Ordinal);

        registration.LandmarksText = "0,0 -> 2,3";
        registration.SolveTranslationCommand.Execute(null);

        SpatialMapping mapping = Assert.Single(figure.LinkGroups).Mappings[0];
        Assert.Equal(2, mapping.TargetRevision);
        Assert.Equal(SpatialMappingRevisionState.Current,
            figure.GetLinkMappingRevisionState(mapping.SourceAssetId == referenceSource.Asset.Id
                ? figure.LinkGroups[0].Id
                : Guid.Empty, targetSource.Asset.Id));
        Assert.Contains("Current", registration.RevisionStatusText, StringComparison.Ordinal);
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
