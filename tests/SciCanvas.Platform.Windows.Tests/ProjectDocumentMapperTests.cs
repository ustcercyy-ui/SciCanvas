using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ProjectDocumentMapperTests
{
    [Fact]
    public void CreateAndRestore_PreservesCropPanelAndEditorState()
    {
        SourceAssetItemViewModel source = CreateSourceItem();
        var crop = new CropEditorViewModel();
        Assert.True(crop.RestoreForSource(
            source.Asset.Metadata.PixelSize,
            new PixelRect64(10, 12, 60, 40)));
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(10, 12, 60, 40)));
        panel.X = 123;
        panel.Y = 234;
        panel.IsLocked = true;
        panel.IsVisible = false;
        panel.PhysicalUnitsPerSourcePixel = 0.5;
        panel.ScaleBarPhysicalLength = 10;
        panel.ScaleBarUnit = "nm";
        panel.ShowScaleBar = true;
        figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel annotation = Assert.IsType<FigureAnnotationViewModel>(
            figure.SelectedAnnotation);
        annotation.Text = "活性位点";
        annotation.X = 240;
        annotation.Y = 360;
        annotation.Color = "#FF1A1A1A";
        annotation.IsBold = true;

        SciCanvasProjectDocument document = ProjectDocumentMapper.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow.AddHours(-1),
            "往返工程",
            [source],
            source,
            crop,
            figure,
            WorkspaceMode.Figure,
            lockCropSizeAcrossSources: true,
            cropOverlayVisible: false);

        Assert.Single(document.Sources);
        ProjectImageLayerSnapshot layer = Assert.Single(document.Layers);
        Assert.Equal(panel.Id, layer.Id);
        Assert.Equal(10, layer.SourceRect.X);
        Assert.Equal(123, layer.Transform.X);
        Assert.False(layer.Visible);
        Assert.True(layer.Locked);
        Assert.Equal("figure", document.TemplateSnapshot!.WorkspaceMode);
        Assert.Equal(source.Asset.Id, document.TemplateSnapshot.SelectedSourceId);
        Assert.Equal(60, document.TemplateSnapshot.ActiveCrop!.Width);
        ProjectScaleBarSnapshot scaleBar = document.TemplateSnapshot.ScaleBars[panel.Id];
        Assert.True(scaleBar.Enabled);
        Assert.Equal(0.5, scaleBar.PhysicalUnitsPerSourcePixel);
        Assert.Equal(10, scaleBar.PhysicalLength);
        Assert.Equal("nm", scaleBar.Unit);
        ProjectAnnotationSnapshot savedAnnotation = Assert.Single(
            document.TemplateSnapshot.Annotations);
        Assert.Equal(annotation.Id, savedAnnotation.Id);
        Assert.Equal("text", savedAnnotation.Kind);
        Assert.Equal("活性位点", savedAnnotation.Text);
        Assert.Equal(240, savedAnnotation.X);
        Assert.True(savedAnnotation.IsBold);

        SourceAsset restoredAsset = ProjectDocumentMapper.ToSourceAsset(document.Sources[0]);
        Assert.Equal(source.Asset.Id, restoredAsset.Id);
        Assert.Equal(source.Asset.Fingerprint.Sha256, restoredAsset.Fingerprint.Sha256);
        PixelRect64 restoredDestination = ProjectDocumentMapper.ToDestinationRect(layer);
        Assert.Equal(panel.DestinationRect, restoredDestination);

        panel.IsLocked = false;
        figure.AlignPanelRightCommand.Execute(null);
        Assert.Equal(figure.CanvasWidth - panel.Width, panel.X);
        figure.AlignPanelVerticalCenterCommand.Execute(null);
        Assert.Equal((figure.CanvasHeight - panel.Height) / 2, panel.Y);
        panel.IsLocked = true;
        Assert.False(figure.AlignPanelLeftCommand.CanExecute(null));
    }

    private static SourceAssetItemViewModel CreateSourceItem()
    {
        byte[] pixels = new byte[100 * 80 * 4];
        for (int index = 3; index < pixels.Length; index += 4)
        {
            pixels[index] = 255;
        }

        BitmapSource preview = BitmapSource.Create(
            100,
            80,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: 400);
        preview.Freeze();

        var asset = new SourceAsset(
            Guid.NewGuid(),
            "source.png",
            @"C:\research\source.png",
            new SourceFingerprint(32000, DateTimeOffset.UtcNow, new string('B', 64), "TEST:2"),
            new CoreImageMetadata(new PixelSize64(100, 80), 4, 8, "Bgra32"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }
}
