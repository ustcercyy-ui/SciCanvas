using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
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
        figure.GlobalFontFamily = "Segoe UI";
        figure.GlobalFontSizePt = 8;
        figure.GlobalStrokeWidthPt = 1.5;
        figure.GlobalTextColor = "#FF223344";
        figure.GlobalShapeColor = "#FF00AA88";
        figure.GlobalScaleBarColor = "#FFFFFFFF";
        figure.ScientificColors[0].Name = "α phase";
        figure.ScientificColors[0].Color = "#FF2255AA";

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
        ProjectGlobalStyleSnapshot savedStyle = Assert.IsType<ProjectGlobalStyleSnapshot>(
            document.TemplateSnapshot.GlobalStyle);
        Assert.Equal("Segoe UI", savedStyle.FontFamily);
        Assert.Equal(8, savedStyle.FontSizePt);
        Assert.Equal("#FF00AA88", savedStyle.ShapeColor);
        ProjectScientificColorSnapshot savedColor = Assert.Single(
            document.TemplateSnapshot.ScientificColors,
            color => color.Name == "α phase");
        Assert.Equal("#FF2255AA", savedColor.Color);

        SourceAsset restoredAsset = ProjectDocumentMapper.ToSourceAsset(document.Sources[0]);
        Assert.Equal(source.Asset.Id, restoredAsset.Id);
        Assert.Equal(source.Asset.Fingerprint.Sha256, restoredAsset.Fingerprint.Sha256);
        PixelRect64 restoredDestination = ProjectDocumentMapper.ToDestinationRect(layer);
        Assert.Equal(panel.DestinationRect, restoredDestination);

        panel.IsLocked = false;
        figure.SelectPanel(panel, toggle: false);
        figure.AlignPanelRightCommand.Execute(null);
        Assert.Equal(figure.CanvasWidth - panel.Width, panel.X);
        figure.AlignPanelVerticalCenterCommand.Execute(null);
        Assert.Equal((figure.CanvasHeight - panel.Height) / 2, panel.Y);
        panel.IsLocked = true;
        Assert.False(figure.AlignPanelLeftCommand.CanExecute(null));
    }

    [Fact]
    public void Create_PersistsSourceCalibrationReferenceAndMeasurements()
    {
        SourceAssetItemViewModel source = CreateSourceItem();
        source.Calibration.Restore(
            new SpatialCalibration(
                source.Asset.Id,
                0.02,
                0.025,
                "µm",
                CalibrationOrigin.Manual,
                ReferencePixelLength: 50,
                ReferencePhysicalLength: 1),
            referenceStartX: 5,
            referenceStartY: 10,
            referenceEndX: 55,
            referenceEndY: 10);
        ScientificMeasurementViewModel length = source.AddMeasurement(
            ScientificMeasurementKind.Length,
            new MeasurementPoint(10, 10),
            new MeasurementPoint(40, 50));
        length.StrokeColor = "#FFFFD740";
        length.StrokeWidthPixels = 4;
        length.LineStyle = "dash-dot";
        length.MarkerSizePixels = 26;
        length.ShowMarkers = false;
        length.ShowLabel = false;
        length.FillOpacityPercent = 22;
        length.IsVisible = false;
        length.IsLocked = true;
        MeasurementPoint[] polylinePoints =
        [
            new(5, 5),
            new(25, 15),
            new(45, 10),
        ];
        ScientificMeasurementViewModel polyline = source.AddMeasurement(
            ScientificMeasurementKind.Polyline,
            polylinePoints[0],
            polylinePoints[^1],
            pathPoints: polylinePoints);
        var crop = new CropEditorViewModel();
        Assert.True(crop.RestoreForSource(
            source.Asset.Metadata.PixelSize,
            new PixelRect64(0, 0, 80, 60)));
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);

        SciCanvasProjectDocument document = ProjectDocumentMapper.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "科学测量工程",
            [source],
            source,
            crop,
            figure,
            WorkspaceMode.Crop,
            lockCropSizeAcrossSources: true,
            cropOverlayVisible: true);

        Assert.Equal("1.2", document.SchemaVersion);
        ProjectCalibrationSnapshot savedCalibration = Assert.Single(document.Calibrations);
        Assert.Equal(source.Asset.Id, savedCalibration.SourceAssetId);
        Assert.Equal(0.02, savedCalibration.UnitsPerPixelX);
        Assert.Equal(0.025, savedCalibration.UnitsPerPixelY);
        Assert.Equal("manual", savedCalibration.Origin);
        Assert.Equal(5, savedCalibration.ReferenceStartX);
        Assert.Equal(2, document.Measurements.Count);
        ProjectMeasurementSnapshot savedMeasurement = Assert.Single(
            document.Measurements,
            measurement => measurement.Kind == "length");
        Assert.Equal(length.Id, savedMeasurement.Id);
        Assert.Equal("length", savedMeasurement.Kind);
        Assert.Equal("#FFFFD740", savedMeasurement.StrokeColor);
        Assert.Equal(4, savedMeasurement.StrokeWidthPixels);
        Assert.Equal("dash-dot", savedMeasurement.LineStyle);
        Assert.Equal(26, savedMeasurement.MarkerSizePixels);
        Assert.False(savedMeasurement.ShowMarkers);
        Assert.False(savedMeasurement.ShowLabel);
        Assert.Equal(22, savedMeasurement.FillOpacityPercent);
        Assert.False(savedMeasurement.IsVisible);
        Assert.True(savedMeasurement.IsLocked);
        ProjectMeasurementSnapshot savedPolyline = Assert.Single(
            document.Measurements,
            measurement => measurement.Kind == "polyline");
        Assert.Equal(polyline.Id, savedPolyline.Id);
        Assert.Equal(3, savedPolyline.Points.Count);
        Assert.Equal(25, savedPolyline.Points[1].X);

        SpatialCalibration restoredCalibration = ProjectDocumentMapper.ToCalibration(savedCalibration);
        ScientificMeasurement restoredMeasurement = ProjectDocumentMapper.ToMeasurement(savedMeasurement);
        Assert.True(restoredCalibration.IsAnisotropic);
        Assert.Equal(length.Measurement.PointB, restoredMeasurement.PointB);
        Assert.Equal(length.NumericValue, restoredMeasurement.PhysicalValue(restoredCalibration));
        ScientificMeasurement restoredPolyline = ProjectDocumentMapper.ToMeasurement(savedPolyline);
        Assert.Equal(polylinePoints, restoredPolyline.EffectivePathPoints);
        Assert.Equal(polyline.NumericValue, restoredPolyline.PhysicalValue(restoredCalibration));
    }

    [Fact]
    public void Create_PersistsInsetSlotAndLinkedCropGroup()
    {
        SourceAssetItemViewModel source = CreateSourceItem();
        var crop = new CropEditorViewModel();
        Assert.True(crop.RestoreForSource(
            source.Asset.Metadata.PixelSize,
            new PixelRect64(0, 0, 80, 60)));
        var figure = new FigureCanvasViewModel(new BuiltInTemplateCatalog().LoadAll()[0]);
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(0, 0, 80, 60)));
        figure.CreateInsetCommand.Execute(null);
        FigurePanelViewModel inset = Assert.Single(figure.Panels, panel => panel.IsInset);
        figure.SelectPanel(reference, toggle: false);
        figure.SelectPanel(inset, toggle: true);
        figure.LinkSelectedPanelCropsCommand.Execute(null);

        SciCanvasProjectDocument document = ProjectDocumentMapper.Create(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            "Inset 工程",
            [source],
            source,
            crop,
            figure,
            WorkspaceMode.Figure,
            lockCropSizeAcrossSources: false,
            cropOverlayVisible: true);

        Assert.Equal(2, document.Layers.Count);
        string insetSlot = document.TemplateSnapshot!.LayerSlots[inset.Id];
        Assert.StartsWith("inset:", insetSlot, StringComparison.Ordinal);
        Guid groupId = Assert.IsType<Guid>(reference.CropLinkGroupId);
        Assert.All(document.Layers, layer => Assert.Equal(groupId, layer.CropLinkGroupId));
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
