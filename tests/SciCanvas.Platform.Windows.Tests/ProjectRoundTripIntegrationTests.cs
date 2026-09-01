using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Platform.Windows;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using LinkingLinkGroup = SciCanvas.Core.Linking.LinkGroup;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ProjectRoundTripIntegrationTests
{
    [Fact]
    public async Task SaveThenOpen_RestoresSourcesCropFigureAndLayerState()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string projectPath = Path.Combine(workspace.Root, "research.scicanvas");
        CreatePng(sourcePath, 20, 16);
        byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        sourceItem.RestoreSourceRevision(3);
        sourceItem.Calibration.Restore(
            new SpatialCalibration(
                asset.Id,
                0.25,
                0.5,
                "µm",
                CalibrationOrigin.Manual,
                ReferencePixelLength: 10,
                ReferencePhysicalLength: 2.5),
            referenceStartX: 2,
            referenceStartY: 2,
            referenceEndX: 12,
            referenceEndY: 2);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        Assert.True(original.Crop.RestoreForSource(
            asset.Metadata.PixelSize,
            new PixelRect64(3, 4, 12, 8)));
        sourceItem.AddAnalysisResult(new RoiStatisticsResult
        {
            SourceAssetId = asset.Id,
            SourceRevision = sourceItem.SourceRevision,
            AnalyzerId = "test.roi.v1",
            SourceBitDepth = 8,
            Region = new PixelRect64(3, 4, 12, 8),
            PixelCount = 96,
            Minimum = 0,
            Maximum = 255,
            Mean = 128,
            StandardDeviation = 1,
            IntegratedIntensity = 12288,
            Histogram = new IntensityHistogram(
                [new IntensityHistogramBin(0, 255, 96)],
                96,
                0,
                255),
        });
        ScientificMeasurementViewModel styledMeasurement = sourceItem.AddMeasurement(
            ScientificMeasurementKind.RectangleRoi,
            new MeasurementPoint(4, 5),
            new MeasurementPoint(12, 10),
            visualStyle: new ScientificMeasurementVisualStyle
            {
                StrokeColor = "#FFFF0000",
                StrokeWidthPixels = 4,
                LineStyle = "dash",
                FillColor = "#FF0000FF",
                FillOpacityPercent = 20,
                MarkerStrokeColor = "#FF00FF00",
                MarkerFillColor = "#FF111111",
                MarkerSizePixels = 14,
                ShowMarkers = true,
                ShowLabel = true,
                LabelColor = "#FF00FF00",
                LabelFontFamily = "Consolas",
                LabelFontSizePt = 11,
                LabelIsBold = true,
                IsVisible = true,
                IsLocked = false,
            });
        original.SelectedFigureTemplate = original.AvailableTemplates.Single(
            template => template.Id == "materials.synthesis-structure-performance.nature-double");
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(sourceItem, new PixelRect64(3, 4, 12, 8)));
        FigureMeasurementOverlayViewModel pinnedOverlay = original.Figure.PinMeasurement(
            styledMeasurement,
            panel);
        panel.X = 111;
        panel.Y = 222;
        panel.IsLocked = true;
        panel.IsAspectRatioLocked = false;
        panel.IsVisible = false;
        panel.PhysicalUnitsPerSourcePixel = 0.25;
        panel.ScaleBarPhysicalLength = 2;
        panel.ScaleBarUnit = "µm";
        panel.CalibrationUnit = "µm";
        panel.ScaleBarShowLabel = true;
        panel.ShowScaleBar = true;
        FigureAdditionalScaleBarViewModel secondaryScaleBar = panel.AddAdditionalScaleBar();
        secondaryScaleBar.PhysicalLength = 500;
        secondaryScaleBar.Unit = "nm";
        secondaryScaleBar.Anchor = ScaleBarAnchor.TopLeft;
        secondaryScaleBar.ShowLabel = false;
        original.Figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel textAnnotation = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        textAnnotation.Text = "界面区域";
        textAnnotation.X = 300;
        textAnnotation.Y = 400;
        textAnnotation.TextColor = "#FF2255AA";
        textAnnotation.FontFamily = "Times New Roman";
        textAnnotation.FontSizePt = 12;
        textAnnotation.IsBold = true;
        original.Figure.AddArrowAnnotationCommand.Execute(null);
        FigureAnnotationViewModel arrowAnnotation = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        arrowAnnotation.X = 250;
        arrowAnnotation.Y = 500;
        arrowAnnotation.EndX = 450;
        arrowAnnotation.EndY = 560;
        arrowAnnotation.StrokeColor = "#FFE53935";
        arrowAnnotation.StrokeWidthPt = 1.5;
        arrowAnnotation.IsLocked = true;
        original.Figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel draftAnnotation = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        draftAnnotation.Text = string.Empty;
        draftAnnotation.TextColor = "#FF111111";
        original.Figure.AddRectangleAnnotationCommand.Execute(null);
        FigureAnnotationViewModel rectangle = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        rectangle.X = 520;
        rectangle.Y = 260;
        rectangle.EndX = 820;
        rectangle.EndY = 560;
        rectangle.StrokeColor = "#FFFFFF00";
        rectangle.FillColor = "#FF00FFFF";
        rectangle.FillOpacityPercent = 24;
        rectangle.StrokeWidthPt = 1.75;
        original.Figure.AddEllipseAnnotationCommand.Execute(null);
        FigureAnnotationViewModel ellipse = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        ellipse.X = 900;
        ellipse.Y = 300;
        ellipse.EndX = 1200;
        ellipse.EndY = 620;
        ellipse.Color = "#FF1E88E5";
        original.WorkspaceMode = WorkspaceMode.Figure;

        await original.SaveProjectToPathAsync(projectPath);

        Assert.False(original.IsDirty);
        Assert.True(File.Exists(projectPath));

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.Null(restored.LastError);
        Assert.False(restored.IsDirty);
        Assert.Equal(projectPath, restored.ProjectPath);
        Assert.Equal(WorkspaceMode.Figure, restored.WorkspaceMode);
        Assert.Equal(
            "materials.synthesis-structure-performance.nature-double",
            restored.Figure.Template.Id);
        Assert.Single(restored.Sources);
        Assert.Equal(asset.Id, restored.Sources[0].Asset.Id);
        RoiStatisticsResult restoredAnalysis = Assert.IsType<RoiStatisticsResult>(
            Assert.Single(restored.Sources[0].AnalysisResults));
        Assert.Equal(new PixelRect64(3, 4, 12, 8), restoredAnalysis.Region);
        Assert.Equal(12288, restoredAnalysis.IntegratedIntensity);
        ScientificMeasurementViewModel restoredMeasurement = Assert.Single(restored.Sources[0].Measurements);
        Assert.Equal(styledMeasurement.Id, restoredMeasurement.Id);
        Assert.Equal(3, restoredMeasurement.SourceRevision);
        Assert.Equal("#FFFF0000", restoredMeasurement.StrokeColor);
        Assert.Equal("#FF0000FF", restoredMeasurement.FillColor);
        Assert.Equal("#FF00FF00", restoredMeasurement.MarkerStrokeColor);
        Assert.Equal("#FF00FF00", restoredMeasurement.LabelColor);
        Assert.Equal("Consolas", restoredMeasurement.LabelFontFamily);
        Assert.Equal(11, restoredMeasurement.LabelFontSizePt);
        Assert.True(restoredMeasurement.LabelIsBold);
        Assert.True(restored.Crop.TryGetCrop(out PixelRect64 restoredCrop));
        Assert.Equal(new PixelRect64(3, 4, 12, 8), restoredCrop);
        FigurePanelViewModel restoredPanel = Assert.Single(restored.Figure.Panels);
        Assert.Equal(panel.Id, restoredPanel.Id);
        Assert.Equal(new PixelRect64(3, 4, 12, 8), restoredPanel.SourceRect);
        Assert.Equal(111, restoredPanel.X);
        Assert.Equal(222, restoredPanel.Y);
        Assert.True(restoredPanel.IsLocked);
        Assert.False(restoredPanel.IsAspectRatioLocked);
        Assert.False(restoredPanel.IsVisible);
        Assert.True(restoredPanel.ShowScaleBar);
        Assert.Equal(0.25, restoredPanel.PhysicalUnitsPerSourcePixel);
        Assert.Equal(2, restoredPanel.ScaleBarPhysicalLength);
        Assert.Equal("µm", restoredPanel.ScaleBarUnit);
        Assert.Equal("µm", restoredPanel.CalibrationUnit);
        FigureAdditionalScaleBarViewModel restoredSecondaryScaleBar = Assert.Single(restoredPanel.AdditionalScaleBars);
        Assert.Equal(secondaryScaleBar.Id, restoredSecondaryScaleBar.Id);
        Assert.Equal(500, restoredSecondaryScaleBar.PhysicalLength);
        Assert.Equal("nm", restoredSecondaryScaleBar.Unit);
        Assert.Equal(ScaleBarAnchor.TopLeft, restoredSecondaryScaleBar.Anchor);
        Assert.False(restoredSecondaryScaleBar.ShowLabel);
        FigureMeasurementOverlayViewModel restoredOverlay = Assert.Single(restored.Figure.MeasurementOverlays);
        Assert.Equal(pinnedOverlay.Id, restoredOverlay.Id);
        Assert.Equal(styledMeasurement.Id, restoredOverlay.MeasurementId);
        Assert.Equal(panel.Id, restoredOverlay.PanelId);
        Assert.Equal(3, restoredOverlay.SourceRevision);
        Assert.Equal(ScientificMeasurementKind.RectangleRoi, restoredOverlay.Kind);
        Assert.Equal("#FFFF0000", restoredOverlay.ScientificObject.Style.StrokeColor);
        Assert.Equal("#FF0000FF", restoredOverlay.ScientificObject.Style.FillColor);
        Assert.Equal(20, restoredOverlay.ScientificObject.Style.FillOpacityPercent);
        Assert.Equal("µm", restoredOverlay.ScientificObject.CalibrationRelationship!.Unit);
        Assert.Equal(5, restored.Figure.Annotations.Count);
        FigureAnnotationViewModel restoredText = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Text &&
                          annotation.Text == "界面区域");
        Assert.Equal("界面区域", restoredText.Text);
        Assert.Equal(300, restoredText.X);
        Assert.True(restoredText.IsBold);
        Assert.Equal("Times New Roman", restoredText.FontFamily);
        Assert.Equal(12, restoredText.FontSizePt);
        Assert.Equal("#FF2255AA", restoredText.TextColor);
        FigureAnnotationViewModel restoredArrow = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Arrow);
        Assert.Equal(450, restoredArrow.EndX);
        Assert.Equal(1.5, restoredArrow.StrokeWidthPt);
        Assert.True(restoredArrow.IsLocked);
        FigureAnnotationViewModel restoredDraft = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Text && annotation.Text.Length == 0);
        Assert.Equal("#FF111111", restoredDraft.TextColor);
        Assert.False(restoredDraft.IsValid);
        FigureAnnotationViewModel restoredRectangle = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Rectangle);
        Assert.Equal(820, restoredRectangle.EndX);
        Assert.Equal(1.75, restoredRectangle.StrokeWidthPt);
        Assert.Equal("#FFFFFF00", restoredRectangle.StrokeColor);
        Assert.Equal("#FF00FFFF", restoredRectangle.FillColor);
        Assert.Equal(24, restoredRectangle.FillOpacityPercent);
        FigureAnnotationViewModel restoredEllipse = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Ellipse);
        Assert.Equal(320, restoredEllipse.ShapeHeight);
        Assert.Equal("#FF1E88E5", restoredEllipse.Color);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task SaveThenOpen_RestoresScientificObjectsAndUndoRedo()
    {
        using var workspace = new TestWorkspace();
        string projectPath = Path.Combine(workspace.Root, "scientific-objects.scicanvas");
        MainWindowViewModel original = CreateViewModel();

        original.Figure.AddPolygonScientificObjectCommand.Execute(null);
        FigureScientificObjectViewModel polygon = Assert.IsType<FigureScientificObjectViewModel>(
            original.Figure.SelectedScientificObject);
        Guid polygonId = polygon.Id;
        original.UndoCommand.Execute(null);
        Assert.Empty(original.Figure.ScientificObjects);
        original.RedoCommand.Execute(null);
        polygon = Assert.Single(original.Figure.ScientificObjects);
        Assert.Equal(polygonId, polygon.Id);
        Assert.Equal(polygonId, original.Figure.SelectedScientificObject?.Id);
        polygon.Label = "Membrane boundary";
        polygon.PointsText = "100,100; 300,100; 300,240; 100,240";
        original.CompleteHistoryGesture();

        original.Figure.AddDirectionMarkerCommand.Execute(null);
        FigureScientificObjectViewModel direction = Assert.IsType<FigureScientificObjectViewModel>(
            original.Figure.SelectedScientificObject);
        direction.Label = "North";
        original.Figure.AddColorbarCommand.Execute(null);
        FigureScientificObjectViewModel colorbar = Assert.IsType<FigureScientificObjectViewModel>(
            original.Figure.SelectedScientificObject);
        colorbar.Label = "Intensity";
        colorbar.Minimum = 0;
        colorbar.Maximum = 4095;
        colorbar.Unit = "a.u.";
        colorbar.Colormap = "magma";
        Guid colorbarChannelId = Guid.NewGuid();
        colorbar.ChannelId = colorbarChannelId;
        colorbar.ColorbarBindingState = ColorbarBindingState.Detached;
        colorbar.ColorbarOrientation = FigureObjectOrientation.Horizontal;
        colorbar.ColorbarTicksText = "0|zero;2048|mid;4095|max";
        original.Figure.AddChannelLegendCommand.Execute(null);
        FigureScientificObjectViewModel legend = Assert.IsType<FigureScientificObjectViewModel>(
            original.Figure.SelectedScientificObject);
        legend.Label = "Channels";
        legend.ChannelEntriesText = "DAPI|#FF4FC3F7; GFP|#FF66BB6A";
        legend.ChannelLegendPadding = 13;
        legend.FontSizePt = 9;
        legend.TextColor = "#FF102030";
        legend.FillColor = "#CC203040";
        legend.StrokeColor = "#FF506070";
        original.CompleteHistoryGesture();

        await original.SaveProjectToPathAsync(projectPath);

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.Null(restored.LastError);
        Assert.False(restored.IsDirty);
        Assert.Equal(4, restored.Figure.ScientificObjects.Count);
        FigureScientificObjectViewModel restoredPolygon = restored.Figure.ScientificObjects.Single(
            item => item.Kind == FigureScientificObjectKind.PolygonAnnotation);
        Assert.Equal(polygonId, restoredPolygon.Id);
        Assert.Equal("Membrane boundary", restoredPolygon.Label);
        Assert.Equal("100,100; 300,100; 300,240; 100,240", restoredPolygon.PointsText);
        Assert.Equal("North", restored.Figure.ScientificObjects.Single(
            item => item.Kind == FigureScientificObjectKind.DirectionMarker).Label);
        FigureScientificObjectViewModel restoredColorbar = restored.Figure.ScientificObjects.Single(
            item => item.Kind == FigureScientificObjectKind.Colorbar);
        Assert.Equal(4095, restoredColorbar.Maximum);
        Assert.Equal("magma", restoredColorbar.Colormap);
        Assert.Equal(colorbarChannelId, restoredColorbar.ChannelId);
        Assert.Equal(ColorbarBindingState.Detached, restoredColorbar.ColorbarBindingState);
        Assert.Equal(FigureObjectOrientation.Horizontal, restoredColorbar.ColorbarOrientation);
        Assert.Equal("0|zero;2048|mid;4095|max", restoredColorbar.ColorbarTicksText);
        FigureScientificObjectViewModel restoredLegend = restored.Figure.ScientificObjects.Single(
            item => item.Kind == FigureScientificObjectKind.ChannelLegend);
        Assert.Equal(2, restoredLegend.ChannelEntries.Count);
        Assert.Equal("DAPI", restoredLegend.ChannelEntries[0].Label);
        Assert.Equal(13, restoredLegend.ChannelLegendPadding);
        Assert.Equal(9, restoredLegend.FontSizePt);
        Assert.Equal("#FF102030", restoredLegend.TextColor);
        Assert.Equal("#CC203040", restoredLegend.FillColor);
        Assert.Equal("#FF506070", restoredLegend.StrokeColor);
    }
    [Fact]
    public async Task SaveThenOpen_RestoresCompositePanelAndChannelSourceRevisions()
    {
        using var workspace = new TestWorkspace();
        string referencePath = Path.Combine(workspace.Root, "HAADF.png");
        string titaniumPath = Path.Combine(workspace.Root, "Ti.png");
        string projectPath = Path.Combine(workspace.Root, "composite.scicanvas");
        CreatePng(referencePath, 20, 16);
        CreatePng(titaniumPath, 20, 15);
        MainWindowViewModel original = CreateViewModel();
        SourceAsset referenceAsset = await CreateReader().ImportAsync(referencePath);
        SourceAsset titaniumAsset = await CreateReader().ImportAsync(titaniumPath);
        var reference = new SourceAssetItemViewModel(
            referenceAsset,
            await new WpfImagePreviewLoader().LoadAsync(referencePath, 1400));
        var titanium = new SourceAssetItemViewModel(
            titaniumAsset,
            await new WpfImagePreviewLoader().LoadAsync(titaniumPath, 1400));
        reference.RestoreSourceRevision(3);
        titanium.RestoreSourceRevision(4);
        original.Sources.Add(reference);
        original.Sources.Add(titanium);
        Guid groupId = Guid.NewGuid();

        Guid referenceChannelId = Guid.NewGuid();
        Guid titaniumChannelId = Guid.NewGuid();
        ChannelGroupMember referenceMember = new(
            referenceChannelId, reference.Asset.Id, ChannelPlaneSelector.InterleavedComponent(0, 0), "HAADF", "reference", "#FFFFFFFF",
            ChannelNameOrigin.User, true,
            new ChannelDisplaySettings(referenceChannelId, true, "#FFFFFFFF", 1, 0, 255, 1, false))
        { SourceRevision = 3 };
        ChannelGroupMember titaniumMember = new(
            titaniumChannelId, titanium.Asset.Id, ChannelPlaneSelector.InterleavedComponent(0, 2), "Ti", null, "#FFFF0000",
            ChannelNameOrigin.User, true,
            new ChannelDisplaySettings(titaniumChannelId, true, "#FFFF0000", 1, 0, 255, 1, false))
        { SourceRevision = 4 };
        var group = new MultiChannelAssetGroup(
            groupId,
            "EDS",
            reference.Asset.Id,
            [referenceMember, titaniumMember],
            SameFieldOfViewConfirmed: true).EnsureValid();
        original.MultiChannelWorkspace.Restore([group]);
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(reference, new PixelRect64(0, 0, 20, 16)));
        panel.CompositeGroupId = groupId;

        await original.SaveProjectToPathAsync(projectPath);
        Assert.True(original.LastError is null, original.LastError);
        Assert.True(File.Exists(projectPath));
        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.True(restored.LastError is null, restored.LastError);
        Assert.Equal(groupId, Assert.Single(restored.Figure.Panels).CompositeGroupId);
        MultiChannelAssetGroup restoredGroup = Assert.Single(restored.MultiChannelWorkspace.CreateModels());
        Assert.Equal([3L, 4L], restoredGroup.Members.Select(member => member.SourceRevision!.Value).Order().ToArray());
        string json = await File.ReadAllTextAsync(projectPath);
        Assert.Contains("\"schemaVersion\": \"3.0\"", json, StringComparison.Ordinal);
        Assert.Contains("\"compositeGroupId\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenProject_WhenSourceChanged_RefusesWithoutReplacingCurrentState()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string projectPath = Path.Combine(workspace.Root, "modified.scicanvas");
        CreatePng(sourcePath, 10, 10);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        await original.SaveProjectToPathAsync(projectPath);

        await File.AppendAllTextAsync(sourcePath, "external-change");

        MainWindowViewModel target = CreateViewModel();
        await target.OpenProjectFromPathAsync(projectPath);

        Assert.Empty(target.Sources);
        Assert.NotNull(target.LastError);
        Assert.Contains("未通过验证", target.LastError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenProject_WhenSourceMoved_RelinksOnlyExactHashAndRequiresProjectSave()
    {
        using var workspace = new TestWorkspace();
        string originalPath = Path.Combine(workspace.Root, "original.png");
        string relocatedPath = Path.Combine(workspace.Root, "archive", "relocated.png");
        string projectPath = Path.Combine(workspace.Root, "relink.scicanvas");
        Directory.CreateDirectory(Path.GetDirectoryName(relocatedPath)!);
        CreatePng(originalPath, 18, 14);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(originalPath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(originalPath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        await original.SaveProjectToPathAsync(projectPath);
        byte[] expectedHash = SHA256.HashData(await File.ReadAllBytesAsync(originalPath));
        File.Move(originalPath, relocatedPath);

        MainWindowViewModel restored = CreateViewModel(
            sourceRelinkFilePicker: new FixedSourceRelinkPicker(relocatedPath));
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.Null(restored.LastError);
        Assert.True(restored.IsDirty);
        SourceAsset relinked = Assert.Single(restored.Sources).Asset;
        Assert.Equal(relocatedPath, relinked.OriginalPath);
        Assert.Equal(SourceLinkState.Relocated, relinked.LinkState);
        Assert.Equal(expectedHash, SHA256.HashData(await File.ReadAllBytesAsync(relocatedPath)));

        await restored.SaveProjectToPathAsync(projectPath);
        SciCanvasProjectDocument saved = await new JsonProjectStore().LoadAsync(projectPath);
        Assert.Equal(relocatedPath, Assert.Single(saved.Sources).OriginalPath);
        Assert.False(restored.IsDirty);
        Assert.Equal(expectedHash, SHA256.HashData(await File.ReadAllBytesAsync(relocatedPath)));
    }

    [Fact]
    public async Task OpenProject_WhenRelinkHashDiffers_RefusesReplacement()
    {
        using var workspace = new TestWorkspace();
        string originalPath = Path.Combine(workspace.Root, "missing.png");
        string wrongPath = Path.Combine(workspace.Root, "wrong.png");
        string projectPath = Path.Combine(workspace.Root, "wrong-relink.scicanvas");
        CreatePng(originalPath, 18, 14);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(originalPath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(originalPath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        await original.SaveProjectToPathAsync(projectPath);
        File.Delete(originalPath);
        CreatePng(wrongPath, 17, 13);

        MainWindowViewModel target = CreateViewModel(
            sourceRelinkFilePicker: new FixedSourceRelinkPicker(wrongPath));
        await target.OpenProjectFromPathAsync(projectPath);

        Assert.Empty(target.Sources);
        Assert.NotNull(target.LastError);
        Assert.Contains("SHA-256 不匹配", target.LastError, StringComparison.Ordinal);
        Assert.True(File.Exists(wrongPath));
    }

    [Fact]
    public async Task UndoRedo_AfterOpeningProject_RestoresCropPanelAndAnnotationWithoutChangingSource()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "history-source.png");
        string projectPath = Path.Combine(workspace.Root, "history.scicanvas");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        Assert.True(original.Crop.RestoreForSource(
            asset.Metadata.PixelSize,
            new PixelRect64(3, 4, 12, 8)));
        await original.SaveProjectToPathAsync(projectPath);

        MainWindowViewModel editor = CreateViewModel();
        await editor.OpenProjectFromPathAsync(projectPath);
        byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));
        Assert.False(editor.IsDirty);

        editor.Crop.X = 5;
        editor.CompleteHistoryGesture();
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            editor.Figure.AddPanel(editor.SelectedSource!, new PixelRect64(5, 4, 12, 8)));
        editor.CompleteHistoryGesture();
        editor.Figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel annotation = Assert.IsType<FigureAnnotationViewModel>(
            editor.Figure.SelectedAnnotation);
        annotation.Text = "撤销测试";
        editor.CompleteHistoryGesture();

        Assert.True(editor.IsDirty);
        Assert.Single(editor.Figure.Panels);
        Assert.Single(editor.Figure.Annotations);

        editor.UndoCommand.Execute(null);
        Assert.Empty(editor.Figure.Annotations);
        Assert.Single(editor.Figure.Panels);

        editor.UndoCommand.Execute(null);
        Assert.Empty(editor.Figure.Panels);
        Assert.Equal(5, editor.Crop.X);

        editor.UndoCommand.Execute(null);
        Assert.Equal(3, editor.Crop.X);
        Assert.False(editor.IsDirty);

        editor.RedoCommand.Execute(null);
        editor.RedoCommand.Execute(null);
        editor.RedoCommand.Execute(null);
        Assert.Equal(5, editor.Crop.X);
        Assert.Single(editor.Figure.Panels);
        Assert.Single(editor.Figure.Annotations);
        Assert.True(editor.IsDirty);
        Assert.Equal(panel.Id, editor.Figure.Panels[0].Id);
        Assert.Equal(new PixelRect64(5, 4, 12, 8), editor.Figure.Panels[0].SourceRect);
        Assert.Equal(annotation.Id, editor.Figure.Annotations[0].Id);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task AutosaveThenReopen_RestoresDirtyEditsAndManualSaveRemovesRecovery()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "recovery-source.png");
        string projectPath = Path.Combine(workspace.Root, "recovery.scicanvas");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        Assert.True(original.Crop.RestoreForSource(
            asset.Metadata.PixelSize,
            new PixelRect64(3, 4, 12, 8)));
        await original.SaveProjectToPathAsync(projectPath);
        byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));

        var recoveryStore = new JsonProjectRecoveryStore(Path.Combine(workspace.Root, "unsaved-recovery"));
        MainWindowViewModel editor = CreateViewModel(recoveryStore, new AlwaysRestorePrompt());
        await editor.OpenProjectFromPathAsync(projectPath);
        editor.Crop.X = 5;
        editor.CompleteHistoryGesture();
        editor.Figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel annotation = Assert.IsType<FigureAnnotationViewModel>(
            editor.Figure.SelectedAnnotation);
        annotation.Text = "自动恢复标注";
        editor.CompleteHistoryGesture();

        await editor.FlushAutosaveAsync();
        string recoveryPath = projectPath + ".autosave.scicanvas";
        Assert.True(File.Exists(recoveryPath));
        File.SetLastWriteTimeUtc(recoveryPath, DateTime.UtcNow.AddMinutes(1));

        MainWindowViewModel recovered = CreateViewModel(recoveryStore, new AlwaysRestorePrompt());
        await recovered.OpenProjectFromPathAsync(projectPath);

        Assert.Null(recovered.LastError);
        Assert.True(recovered.IsDirty);
        Assert.Equal(projectPath, recovered.ProjectPath);
        Assert.Equal(5, recovered.Crop.X);
        Assert.Equal("自动恢复标注", Assert.Single(recovered.Figure.Annotations).Text);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));

        await recovered.SaveProjectToPathAsync(projectPath);

        Assert.False(recovered.IsDirty);
        Assert.False(File.Exists(recoveryPath));
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task MultiPanelAlignment_UndoRedoRestoresAllPositionsAndSelectionWithoutChangingSource()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "multi-source.png");
        string projectPath = Path.Combine(workspace.Root, "multi-align.scicanvas");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        FigurePanelViewModel first = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15)));
        FigurePanelViewModel second = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15)));
        FigurePanelViewModel third = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15)));
        ConfigurePanel(first, 100, 100);
        ConfigurePanel(second, 500, 300);
        ConfigurePanel(third, 900, 500);
        await original.SaveProjectToPathAsync(projectPath);
        byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));

        MainWindowViewModel editor = CreateViewModel();
        await editor.OpenProjectFromPathAsync(projectPath);
        FigurePanelViewModel[] panels = editor.Figure.Panels.OrderBy(panel => panel.ZIndex).ToArray();
        editor.Figure.SelectPanel(panels[0], toggle: false);
        editor.Figure.SelectPanel(panels[1], toggle: true);
        editor.Figure.SelectPanel(panels[2], toggle: true);
        editor.Figure.AlignSelectionLeftCommand.Execute(null);

        Assert.True(editor.IsDirty);
        Assert.All(panels, panel => Assert.Equal(100, panel.X));

        editor.UndoCommand.Execute(null);
        Assert.Equal(new long[] { 100, 500, 900 },
            editor.Figure.Panels.OrderBy(panel => panel.ZIndex).Select(panel => panel.X));
        Assert.False(editor.IsDirty);

        editor.RedoCommand.Execute(null);
        Assert.All(editor.Figure.Panels, panel => Assert.Equal(100, panel.X));
        Assert.Equal(3, editor.Figure.SelectedPanelCount);
        Assert.True(editor.IsDirty);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task GuidesAndSnapSettings_SaveOpenAndUndoWithoutEnteringFigureExport()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "guide-source.png");
        string projectPath = Path.Combine(workspace.Root, "guides.scicanvas");
        CreatePng(sourcePath, 24, 18);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        original.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15));
        original.Figure.AddVerticalGuideCommand.Execute(null);
        FigureGuideViewModel vertical = Assert.IsType<FigureGuideViewModel>(original.Figure.SelectedGuide);
        vertical.Position = 333;
        vertical.IsLocked = true;
        original.Figure.AddHorizontalGuideCommand.Execute(null);
        FigureGuideViewModel horizontal = Assert.IsType<FigureGuideViewModel>(original.Figure.SelectedGuide);
        horizontal.Position = 444;
        original.Figure.IsSnappingEnabled = false;
        original.Figure.SnapTolerancePixels = 20;
        original.Figure.ExactSpacingPixels = 32;
        await original.SaveProjectToPathAsync(projectPath);
        byte[] sourceHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.False(restored.IsDirty);
        Assert.Equal(2, restored.Figure.Guides.Count);
        FigureGuideViewModel restoredVertical = restored.Figure.Guides.Single(
            guide => guide.Orientation == FigureGuideOrientation.Vertical);
        FigureGuideViewModel restoredHorizontal = restored.Figure.Guides.Single(
            guide => guide.Orientation == FigureGuideOrientation.Horizontal);
        Assert.Equal(333, restoredVertical.Position);
        Assert.True(restoredVertical.IsLocked);
        Assert.Equal(444, restoredHorizontal.Position);
        Assert.False(restored.Figure.IsSnappingEnabled);
        Assert.Equal(20, restored.Figure.SnapTolerancePixels);
        Assert.Equal(32, restored.Figure.ExactSpacingPixels);
        Assert.Empty(restored.Figure.CreateExportDocument().Annotations);

        restoredHorizontal.Position = 555;
        restored.CompleteHistoryGesture();
        Assert.True(restored.IsDirty);
        restored.UndoCommand.Execute(null);

        Assert.Equal(444, restored.Figure.Guides.Single(
            guide => guide.Orientation == FigureGuideOrientation.Horizontal).Position);
        Assert.False(restored.IsDirty);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
    }

    [Fact]
    public async Task CanvasBackgroundAndPanelLabels_RoundTripAndUndo()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "labels.png");
        string projectPath = Path.Combine(workspace.Root, "labels.scicanvas");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15)));
        original.Figure.BackgroundColor = "#FFECEFF1";
        original.Figure.AutoPanelLabelsEnabled = false;
        original.Figure.PanelLabelSequence = "uppercase";
        original.Figure.ShowPanelLabels = true;
        original.Figure.GlobalFontFamily = "Segoe UI";
        original.Figure.GlobalFontSizePt = 8;
        original.Figure.GlobalStrokeWidthPt = 1.5;
        original.Figure.GlobalTextColor = "#FF223344";
        original.Figure.GlobalShapeColor = "#FF00AA88";
        original.Figure.GlobalScaleBarColor = "#FFFFFFFF";
        panel.RestoreStyleOverride(new StyleOverride(
            PanelLabel: new TextStyle("Consolas", 10, false, "#FF123456"),
            ScaleBarText: new TextStyle("Times New Roman", 9, true, "#FFABCDEF"),
            ScaleBar: new ScaleBarStyle(ScaleBarAnchor.BottomRight, 2.5, "#FF00FF00")));
        panel.Label = "SEM-1";
        await original.SaveProjectToPathAsync(projectPath);

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.False(restored.IsDirty);
        Assert.Equal("#FFECEFF1", restored.Figure.NormalizedBackgroundColor);
        Assert.False(restored.Figure.AutoPanelLabelsEnabled);
        Assert.True(restored.Figure.ShowPanelLabels);
        Assert.Equal("uppercase", restored.Figure.PanelLabelSequence);
        FigurePanelViewModel restoredPanel = Assert.Single(restored.Figure.Panels);
        Assert.Equal("SEM-1", restoredPanel.Label);
        Assert.Equal("Consolas", restoredPanel.StyleOverride?.PanelLabel?.FontFamily);
        Assert.Equal("#FF123456", restoredPanel.StyleOverride?.PanelLabel?.Color);
        Assert.Equal("Times New Roman", restoredPanel.StyleOverride?.ScaleBarText?.FontFamily);
        Assert.Equal("#FF00FF00", restoredPanel.StyleOverride?.ScaleBar?.Color);
        FigureGlobalStyle resolvedPanelStyle = restored.Figure.GlobalStyle.ResolvePanelOverride(
            restoredPanel.StyleOverride);
        Assert.Equal("Consolas", resolvedPanelStyle.EffectivePanelLabelFontFamily);
        Assert.Equal(2.5, resolvedPanelStyle.EffectiveScaleBarThicknessPt);
        Assert.Equal("#FFECEFF1", restored.Figure.CreateExportDocument().BackgroundColor);
        Assert.Equal("Segoe UI", restored.Figure.GlobalFontFamily);
        Assert.Equal(8, restored.Figure.GlobalFontSizePt);
        Assert.Equal(1.5, restored.Figure.GlobalStrokeWidthPt);
        Assert.Equal("#FF00AA88", restored.Figure.GlobalShapeColor);

        restored.Figure.BackgroundColor = "#FF000000";
        restored.Figure.GlobalFontSizePt = 12;
        restored.CompleteHistoryGesture();
        Assert.True(restored.IsDirty);
        restored.UndoCommand.Execute(null);
        Assert.Equal("#FFECEFF1", restored.Figure.NormalizedBackgroundColor);
        Assert.Equal(8, restored.Figure.GlobalFontSizePt);
        Assert.False(restored.IsDirty);
    }

    [Fact]
    public async Task FigureQc_ReportsLowResolutionAndNavigatesToTargetPanel()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "qc.png");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel viewModel = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        viewModel.Sources.Add(sourceItem);
        viewModel.SelectedSource = sourceItem;
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            viewModel.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15)));

        viewModel.RunFigureQcCommand.Execute(null);

        FigureQcIssueViewModel issue = Assert.Single(
            viewModel.FigureQcIssues,
            item => item.Code == "LOW_EFFECTIVE_DPI");
        Assert.Equal(panel.Label, issue.PanelLabel);
        Assert.Contains("提醒", viewModel.FigureQcCountText, StringComparison.Ordinal);
        viewModel.SelectedFigureQcIssue = issue;
        viewModel.NavigateToSelectedQcIssueCommand.Execute(null);
        Assert.Equal(WorkspaceMode.Figure, viewModel.WorkspaceMode);
        Assert.Same(panel, viewModel.Figure.SelectedPanel);
    }

    [Fact]
    public async Task FigureQc_ReportsAndNavigatesToStaleMeasurementRevision()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "stale-measurement.png");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel viewModel = CreateViewModel();
        SourceAsset asset = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(asset, preview);
        viewModel.Sources.Add(sourceItem);
        viewModel.SelectedSource = sourceItem;
        ScientificMeasurementViewModel measurement = sourceItem.AddMeasurement(
            ScientificMeasurementKind.Length,
            new MeasurementPoint(1, 1),
            new MeasurementPoint(10, 10));
        sourceItem.RestoreSourceRevision(2);

        viewModel.RunFigureQcCommand.Execute(null);

        FigureQcIssueViewModel issue = Assert.Single(
            viewModel.FigureQcIssues,
            item => item.Code == "STALE_MEASUREMENT_REVISION");
        Assert.Equal(measurement.Id, issue.ObjectId);
        Assert.True(issue.CanNavigate);
        viewModel.SelectedFigureQcIssue = issue;
        viewModel.NavigateToSelectedQcIssueCommand.Execute(null);
        Assert.Equal(WorkspaceMode.Crop, viewModel.WorkspaceMode);
        Assert.Same(sourceItem, viewModel.SelectedSource);
        Assert.Same(measurement, sourceItem.SelectedMeasurement);
    }

    [Fact]
    public async Task AcceptSourceRevision_RequiresApprovalUpdatesFingerprintAndWritesAuditTrail()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "revision.png");
        string projectPath = Path.Combine(workspace.Root, "revision.scicanvas");
        CreatePng(sourcePath, 30, 20);

        MainWindowViewModel viewModel = CreateViewModel(
            sourceRevisionAcceptancePrompt: new AcceptAllSourceRevisionPrompt());
        SourceAsset original = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(original, preview);
        viewModel.Sources.Add(sourceItem);
        viewModel.SelectedSource = sourceItem;
        viewModel.Figure.AddPanel(sourceItem, new PixelRect64(0, 0, 20, 15));
        await viewModel.SaveProjectToPathAsync(projectPath);
        string previousHash = sourceItem.Asset.Fingerprint.Sha256;

        File.Delete(sourcePath);
        CreatePng(sourcePath, 31, 20);
        byte[] acceptedFileHash = SHA256.HashData(await File.ReadAllBytesAsync(sourcePath));

        await viewModel.AcceptSelectedSourceRevisionAsync();

        Assert.Null(viewModel.LastError);
        Assert.True(viewModel.IsDirty);
        Assert.NotEqual(previousHash, sourceItem.Asset.Fingerprint.Sha256);
        Assert.Equal(Convert.ToHexString(acceptedFileHash), sourceItem.Asset.Fingerprint.Sha256);
        Assert.Equal(31, sourceItem.Width);
        Assert.Equal(acceptedFileHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));

        await viewModel.SaveProjectToPathAsync(projectPath);
        SciCanvasProjectDocument saved = await new JsonProjectStore().LoadAsync(projectPath);
        Assert.Contains(saved.AuditTrail, entry => entry.Command == "AcceptSourceRevision");
        Assert.Equal(sourceItem.Asset.Fingerprint.Sha256, Assert.Single(saved.Sources).Fingerprint.Sha256);
        Assert.False(viewModel.IsDirty);
    }

    [Fact]
    public async Task AcceptSourceRevision_WhenApprovalDeclined_LeavesProjectFingerprintUnchanged()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "declined.png");
        string projectPath = Path.Combine(workspace.Root, "declined.scicanvas");
        CreatePng(sourcePath, 24, 18);

        MainWindowViewModel viewModel = CreateViewModel();
        SourceAsset original = await CreateReader().ImportAsync(sourcePath);
        BitmapSource preview = await new WpfImagePreviewLoader().LoadAsync(sourcePath, 1400);
        var sourceItem = new SourceAssetItemViewModel(original, preview);
        viewModel.Sources.Add(sourceItem);
        viewModel.SelectedSource = sourceItem;
        await viewModel.SaveProjectToPathAsync(projectPath);
        string savedHash = sourceItem.Asset.Fingerprint.Sha256;

        File.Delete(sourcePath);
        CreatePng(sourcePath, 25, 18);
        await viewModel.AcceptSelectedSourceRevisionAsync();

        Assert.Equal(savedHash, sourceItem.Asset.Fingerprint.Sha256);
        Assert.False(viewModel.IsDirty);
        Assert.Contains("取消", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenProjectCommand_WhenCurrentProjectIsDirty_CanDiscardAndOpenAnotherProject()
    {
        using var workspace = new TestWorkspace();
        string currentPath = Path.Combine(workspace.Root, "current.scicanvas");
        string otherPath = Path.Combine(workspace.Root, "other.scicanvas");
        MainWindowViewModel seed = CreateViewModel();
        await seed.SaveProjectToPathAsync(otherPath);

        var prompt = new AlwaysDiscardUnsavedChangesPrompt();
        MainWindowViewModel editor = CreateViewModel(
            projectFilePicker: new FixedProjectPicker(otherPath),
            unsavedChangesPrompt: prompt);
        await editor.SaveProjectToPathAsync(currentPath);
        editor.Figure.BackgroundColor = "#FF101820";
        Assert.True(editor.IsDirty);

        editor.OpenProjectCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(
            () => string.Equals(editor.ProjectPath, otherPath, StringComparison.OrdinalIgnoreCase) &&
                  !editor.IsBusy,
            TimeSpan.FromSeconds(3)));
        Assert.Null(editor.LastError);
        Assert.False(editor.IsDirty);
        Assert.Equal(1, prompt.CallCount);
        Assert.Equal("#FFFFFFFF", editor.Figure.NormalizedBackgroundColor);
    }

    [Fact]
    public async Task CustomCanvasSize_SaveAndOpen_PreservesPixelDimensions()
    {
        using var workspace = new TestWorkspace();
        string projectPath = Path.Combine(workspace.Root, "custom-canvas.scicanvas");
        MainWindowViewModel original = CreateViewModel();
        original.CustomCanvasWidth = 1375;
        original.CustomCanvasHeight = 945;
        original.ApplyCustomCanvasSizeCommand.Execute(null);
        await original.SaveProjectToPathAsync(projectPath);

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.Null(restored.LastError);
        Assert.Equal(1375, restored.Figure.CanvasWidth);
        Assert.Equal(945, restored.Figure.CanvasHeight);
        Assert.Equal(1375, restored.CustomCanvasWidth);
        Assert.Equal(945, restored.CustomCanvasHeight);
        Assert.Equal(original.Figure.Template.Id, restored.Figure.Template.Id);
    }

    [Fact]
    public async Task MultiChannelGroup_SaveOpenAndUndoRedo_PreservesScientificChannelIdentity()
    {
        using var workspace = new TestWorkspace();
        string referencePath = Path.Combine(workspace.Root, "HAADF.png");
        string titaniumPath = Path.Combine(workspace.Root, "Ti.png");
        string projectPath = Path.Combine(workspace.Root, "eds.scicanvas");
        CreatePng(referencePath, 12, 10);
        CreatePng(titaniumPath, 12, 10);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset referenceAsset = await CreateReader().ImportAsync(referencePath);
        SourceAsset titaniumAsset = await CreateReader().ImportAsync(titaniumPath);
        var reference = new SourceAssetItemViewModel(
            referenceAsset,
            await new WpfImagePreviewLoader().LoadAsync(referencePath, 1400));
        var titanium = new SourceAssetItemViewModel(
            titaniumAsset,
            await new WpfImagePreviewLoader().LoadAsync(titaniumPath, 1400));
        original.Sources.Add(reference);
        original.Sources.Add(titanium);
        MultiChannelAssetGroup group = CreateMultiChannelGroup(referenceAsset.Id, titaniumAsset.Id);
        original.MultiChannelWorkspace.Restore([group]);
        await original.SaveProjectToPathAsync(projectPath);
        Assert.False(original.IsDirty);

        ChannelGroupMemberViewModel titaniumChannel =
            Assert.Single(original.MultiChannelWorkspace.Groups).Members[1];
        titaniumChannel.Color = "#FF00FFFF";
        titaniumChannel.Opacity = 0.65;
        Assert.True(original.IsDirty);
        original.UndoCommand.Execute(null);
        Assert.Equal("#FFFF3B30", Assert.Single(original.MultiChannelWorkspace.Groups).Members[1].Color);
        original.RedoCommand.Execute(null);
        Assert.Equal("#FF00FFFF", Assert.Single(original.MultiChannelWorkspace.Groups).Members[1].Color);
        await original.SaveProjectToPathAsync(projectPath);

        SciCanvasProjectDocument saved = await new JsonProjectStore().LoadAsync(projectPath);
        ProjectMultiChannelAssetGroupSnapshot savedGroup = Assert.Single(saved.MultiChannelGroups);
        Assert.Equal(group.Id, savedGroup.Id);
        Assert.Equal(referenceAsset.Id, savedGroup.ReferenceAssetId);
        ProjectChannelGroupMemberSnapshot savedTitanium = savedGroup.Members[1];
        Assert.Equal(group.Members[1].ChannelId, savedTitanium.ChannelId);
        Assert.Equal(titaniumAsset.Id, savedTitanium.AssetId);
        Assert.Equal("Ti", savedTitanium.Name);
        Assert.Equal("filenameSuggestion", savedTitanium.NameOrigin);
        Assert.Equal("interleavedComponent", savedTitanium.PlaneSelector?.SourceKind);
        Assert.Equal(2, savedTitanium.PlaneSelector?.ComponentIndex);
        Assert.Equal("#FF00FFFF", savedTitanium.Color);
        Assert.Equal(0.65, savedTitanium.Opacity);

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.Null(restored.LastError);
        MultiChannelAssetGroup restoredGroup = Assert.Single(
            restored.MultiChannelWorkspace.CreateModels());
        Assert.Equal(group.Id, restoredGroup.Id);
        Assert.Equal(referenceAsset.Id, restoredGroup.ReferenceAssetId);
        Assert.True(restoredGroup.SameFieldOfViewConfirmed);
        ChannelGroupMember restoredTitanium = restoredGroup.Members[1];
        Assert.Equal(group.Members[1].ChannelId, restoredTitanium.ChannelId);
        Assert.Equal(titaniumAsset.Id, restoredTitanium.AssetId);
        Assert.Equal(ChannelNameOrigin.FilenameSuggestion, restoredTitanium.NameOrigin);
        Assert.Equal(ChannelPlaneSelector.InterleavedComponent(0, 2), restoredTitanium.PlaneSelector);
        Assert.True(restoredTitanium.IsNameConfirmed);
        Assert.Equal("#FF00FFFF", restoredTitanium.Color);
        Assert.Equal(0.65, restoredTitanium.DisplaySettings.Opacity);
    }

    [Fact]
    public async Task LinkGroup_SaveOpenAndUndoRedo_PreservesCrossAssetIdentityMappings()
    {
        using var workspace = new TestWorkspace();
        string referencePath = Path.Combine(workspace.Root, "reference.png");
        string targetPath = Path.Combine(workspace.Root, "target.png");
        string projectPath = Path.Combine(workspace.Root, "linked-views.scicanvas");
        CreatePng(referencePath, 40, 30);
        CreatePng(targetPath, 40, 30);

        MainWindowViewModel original = CreateViewModel();
        SourceAsset referenceAsset = await CreateReader().ImportAsync(referencePath);
        SourceAsset targetAsset = await CreateReader().ImportAsync(targetPath);
        var reference = new SourceAssetItemViewModel(
            referenceAsset,
            await new WpfImagePreviewLoader().LoadAsync(referencePath, 1400));
        var target = new SourceAssetItemViewModel(
            targetAsset,
            await new WpfImagePreviewLoader().LoadAsync(targetPath, 1400));
        original.Sources.Add(reference);
        original.Sources.Add(target);
        FigurePanelViewModel referencePanel = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(reference, new PixelRect64(2, 3, 20, 15)));
        FigurePanelViewModel targetPanel = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(target, new PixelRect64(2, 3, 20, 15)));
        await original.SaveProjectToPathAsync(projectPath);
        Assert.False(original.IsDirty);

        original.Figure.SelectPanel(targetPanel, toggle: false);
        original.Figure.SelectPanel(referencePanel, toggle: true);
        original.Figure.LinkSelectedPanelCropsCommand.Execute(null);

        LinkingLinkGroup created = Assert.Single(original.Figure.LinkGroups);
        Assert.Equal(referenceAsset.Id, created.ReferenceAssetId);
        Assert.True(original.IsDirty);
        original.UndoCommand.Execute(null);
        Assert.Empty(original.Figure.LinkGroups);
        Assert.All(original.Figure.Panels, panel => Assert.Null(panel.CropLinkGroupId));

        original.RedoCommand.Execute(null);
        LinkingLinkGroup redone = Assert.Single(original.Figure.LinkGroups);
        Assert.Equal(created.Id, redone.Id);
        Assert.All(original.Figure.Panels, panel => Assert.Equal(created.Id, panel.CropLinkGroupId));
        await original.SaveProjectToPathAsync(projectPath);

        SciCanvasProjectDocument saved = await new JsonProjectStore().LoadAsync(projectPath);
        Assert.Equal(created.Id, Assert.Single(saved.LinkGroups).Id);
        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.Null(restored.LastError);
        LinkingLinkGroup restoredGroup = Assert.Single(restored.Figure.CreateLinkGroupModels());
        Assert.Equal(created.Id, restoredGroup.Id);
        Assert.Equal(referenceAsset.Id, restoredGroup.ReferenceAssetId);
        Assert.All(restored.Figure.Panels, panel => Assert.Equal(created.Id, panel.CropLinkGroupId));
        Assert.Contains(restored.Figure.Panels, panel => panel.Source.Asset.Id == referenceAsset.Id);
        Assert.Contains(restored.Figure.Panels, panel => panel.Source.Asset.Id == targetAsset.Id);
    }
    private static MultiChannelAssetGroup CreateMultiChannelGroup(
        Guid referenceAssetId,
        Guid titaniumAssetId)
    {
        Guid referenceChannelId = Guid.NewGuid();
        Guid titaniumChannelId = Guid.NewGuid();
        return new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS Map Group",
            referenceAssetId,
            [
                new ChannelGroupMember(
                    referenceChannelId,
                    referenceAssetId,
                    ChannelPlaneSelector.InterleavedComponent(0, 0),
                    "HAADF",
                    "Reference",
                    "#FFFFFFFF",
                    ChannelNameOrigin.User,
                    true,
                    new ChannelDisplaySettings(
                        referenceChannelId, true, "#FFFFFFFF", 1, 0, 255, 1, false)),
                new ChannelGroupMember(
                    titaniumChannelId,
                    titaniumAssetId,
                    ChannelPlaneSelector.InterleavedComponent(0, 2),
                    "Ti",
                    "ElementalMap",
                    "#FFFF3B30",
                    ChannelNameOrigin.FilenameSuggestion,
                    true,
                    new ChannelDisplaySettings(
                        titaniumChannelId, true, "#FFFF3B30", 1, 0, 255, 1, false)),
            ],
            SameFieldOfViewConfirmed: true).EnsureValid();
    }
    private static MainWindowViewModel CreateViewModel(
        IProjectRecoveryStore? recoveryStore = null,
        IProjectRecoveryPrompt? recoveryPrompt = null,
        ISourceRelinkFilePicker? sourceRelinkFilePicker = null,
        ISourceRevisionAcceptancePrompt? sourceRevisionAcceptancePrompt = null,
        IProjectFilePicker? projectFilePicker = null,
        IUnsavedChangesPrompt? unsavedChangesPrompt = null)
    {
        var identity = new WindowsFileIdentityProvider();
        return new MainWindowViewModel(
            new EmptyImagePicker(),
            new ReadOnlySourceAssetReader(new WpfImageMetadataProbe(), identity),
            new WpfImagePreviewLoader(),
            new EmptyExportPicker(),
            new WindowsPathSafetyPolicy(identity),
            new NoOpCropExporter(),
            new NoOpFigureExporter(),
            new BuiltInTemplateCatalog().LoadAll(),
            projectFilePicker ?? new EmptyProjectPicker(),
            new JsonProjectStore(),
            recoveryStore,
            recoveryPrompt,
            sourceRelinkFilePicker,
            sourceRevisionAcceptancePrompt,
            unsavedChangesPrompt: unsavedChangesPrompt);
    }

    private static ReadOnlySourceAssetReader CreateReader() => new(
        new WpfImageMetadataProbe(),
        new WindowsFileIdentityProvider());

    private static void CreatePng(string path, int width, int height)
    {
        int stride = width * 4;
        byte[] pixels = new byte[stride * height];
        for (int index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 40;
            pixels[index + 1] = 90;
            pixels[index + 2] = 180;
            pixels[index + 3] = 255;
        }

        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(stream);
    }

    private static void ConfigurePanel(FigurePanelViewModel panel, long x, long y)
    {
        panel.X = x;
        panel.Y = y;
        panel.Width = 200;
        panel.Height = 160;
    }

    private sealed class EmptyImagePicker : IImageFilePicker
    {
        public IReadOnlyList<string> PickImageFiles() => [];
    }

    private sealed class EmptyExportPicker : IExportFilePicker
    {
        public string? PickNewExportPath(string suggestedFileName) => null;
    }

    private sealed class FixedProjectPicker(string path) : IProjectFilePicker
    {
        public string? PickProjectToOpen() => path;

        public string? PickProjectToSave(string suggestedFileName, string? currentPath) => null;
    }

    private sealed class AlwaysDiscardUnsavedChangesPrompt : IUnsavedChangesPrompt
    {
        public int CallCount { get; private set; }

        public UnsavedChangesDecision ConfirmProjectReplacement(
            string actionLabel,
            string currentProjectDisplayName)
        {
            CallCount++;
            return UnsavedChangesDecision.Discard;
        }
    }

    private sealed class EmptyProjectPicker : IProjectFilePicker
    {
        public string? PickProjectToOpen() => null;

        public string? PickProjectToSave(string suggestedFileName, string? currentPath) => null;
    }

    private sealed class NoOpCropExporter : IImageCropExporter
    {
        public Task ExportAsync(
            string sourcePath,
            string targetPath,
            PixelRect64 crop,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpFigureExporter : IFigureExporter
    {
        public Task ExportAsync(
            FigureExportDocument document,
            string targetPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AlwaysRestorePrompt : IProjectRecoveryPrompt
    {
        public bool ShouldRestore(ProjectRecoveryCandidate candidate) => true;
    }

    private sealed class FixedSourceRelinkPicker(string path) : ISourceRelinkFilePicker
    {
        public string? PickReplacement(
            string displayName,
            string originalPath,
            string expectedSha256) => path;
    }

    private sealed class AcceptAllSourceRevisionPrompt : ISourceRevisionAcceptancePrompt
    {
        public bool ConfirmAcceptance(SourceRevisionAcceptanceRequest request) => true;
    }
}
