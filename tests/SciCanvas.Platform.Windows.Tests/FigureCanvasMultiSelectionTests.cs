using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using LinkingLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkingLinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using LinkingSpatialMapping = SciCanvas.Core.Linking.SpatialMapping;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class FigureCanvasMultiSelectionTests
{
    [Fact]
    public void ArchitectureCollections_ExposeOwnedAggregatesThroughStableCanvasAliases()
    {
        FigureCanvasViewModel figure = CreateFigure();

        Assert.Same(figure.PanelCollection.Panels, figure.Panels);
        Assert.Same(figure.PanelCollection.MeasurementOverlays, figure.MeasurementOverlays);
        Assert.Same(figure.PanelCollection.RoiProjections, figure.RoiProjections);
        Assert.Same(figure.ObjectCollection.Annotations, figure.Annotations);
        Assert.Same(figure.ObjectCollection.ScientificObjects, figure.ScientificObjects);
        Assert.Same(figure.ObjectCollection.Guides, figure.Guides);
        Assert.Same(figure.ObjectCollection.ScientificColors, figure.ScientificColors);
        Assert.Same(figure.LinkCoordinator.LinkGroups, figure.LinkGroups);

        figure.LinkCoordinator.StatusText = "同步边界测试";

        Assert.Equal("同步边界测试", figure.LinkSynchronizationStatusText);
    }

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
    public void AnnotationMultiSelection_AlignsToPrimaryAndSharesDirection()
    {
        FigureCanvasViewModel figure = CreateFigure();
        figure.AddLineAnnotationCommand.Execute(null);
        FigureAnnotationViewModel first = Assert.IsType<FigureAnnotationViewModel>(figure.SelectedAnnotation);
        first.X = 100;
        first.Y = 120;
        first.EndX = 300;
        first.EndY = 120;
        figure.AddLineAnnotationCommand.Execute(null);
        FigureAnnotationViewModel reference = Assert.IsType<FigureAnnotationViewModel>(figure.SelectedAnnotation);
        reference.X = 500;
        reference.Y = 300;
        reference.EndX = 700;
        reference.EndY = 360;

        figure.SelectAnnotation(first, toggle: false);
        figure.SelectAnnotation(reference, toggle: true);

        Assert.Equal(2, figure.SelectedAnnotationCount);
        Assert.Same(reference, figure.SelectedAnnotation);
        figure.AlignAnnotationLeftCommand.Execute(null);
        Assert.Equal(reference.Bounds.Left, first.Bounds.Left, 8);

        figure.SetAnnotationDirectionVerticalCommand.Execute(null);
        Assert.All(
            figure.SelectedAnnotations,
            annotation => Assert.Equal(90, annotation.DirectionAngleDegrees, 8));
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
        figure.SelectPanel(panel, toggle: false);

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
    public void MatchFrame_UsesPrimarySelectionAndSkipsLockedPanels()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource(400, 200);
        FigurePanelViewModel target = AddPanel(figure, source);
        FigurePanelViewModel locked = AddPanel(figure, source);
        FigurePanelViewModel reference = AddPanel(figure, source);
        target.IsAspectRatioLocked = false;
        locked.IsAspectRatioLocked = false;
        reference.IsAspectRatioLocked = false;
        Configure(target, 100, 100, 120, 80);
        Configure(locked, 400, 100, 90, 90);
        Configure(reference, 700, 100, 240, 160);
        locked.IsLocked = true;
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(locked, toggle: true);
        figure.SelectPanel(reference, toggle: true);

        figure.MatchSelectionFrameCommand.Execute(null);

        Assert.Equal((240L, 160L), (target.Width, target.Height));
        Assert.Equal((90L, 90L), (locked.Width, locked.Height));
        Assert.Equal((240L, 160L), (reference.Width, reference.Height));
        Assert.False(target.IsAspectRatioLocked);
    }

    [Fact]
    public void MatchAspectRatio_PreservesTargetWidthAndClampsInsideCanvas()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource(400, 200);
        FigurePanelViewModel target = AddPanel(figure, source);
        FigurePanelViewModel reference = AddPanel(figure, source);
        target.IsAspectRatioLocked = false;
        reference.IsAspectRatioLocked = false;
        Configure(target, figure.CanvasWidth - 150, figure.CanvasHeight - 150, 300, 300);
        Configure(reference, 100, 100, 400, 200);
        figure.SelectPanel(target, toggle: false);
        figure.SelectPanel(reference, toggle: true);

        figure.MatchSelectionAspectRatioCommand.Execute(null);

        Assert.Equal(2d, target.Width / (double)target.Height, 8);
        Assert.True(target.X + target.Width <= figure.CanvasWidth);
        Assert.True(target.Y + target.Height <= figure.CanvasHeight);
        Assert.Equal((400L, 200L), (reference.Width, reference.Height));
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

    [Fact]
    public void GlobalStyle_AppliesTypedDefaultsAndFlowsIntoExportDocument()
    {
        FigureCanvasViewModel figure = CreateFigure();
        figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel text = Assert.IsType<FigureAnnotationViewModel>(figure.SelectedAnnotation);
        figure.AddLineAnnotationCommand.Execute(null);
        FigureAnnotationViewModel line = Assert.IsType<FigureAnnotationViewModel>(figure.SelectedAnnotation);
        figure.GlobalFontFamily = "Segoe UI";
        figure.GlobalFontSizePt = 8;
        figure.GlobalStrokeWidthPt = 1.75;
        figure.GlobalTextColor = "#FF223344";
        figure.GlobalShapeColor = "#FF22AA88";
        figure.GlobalScaleBarColor = "#FFFFFFFF";

        figure.ApplyGlobalStyleCommand.Execute(null);

        Assert.Equal(8, text.FontSizePt);
        Assert.Equal("#FF223344", text.Color);
        Assert.Equal(1.75, line.StrokeWidthPt);
        Assert.Equal("#FF22AA88", line.Color);
        FigureGlobalStyle exported = figure.CreateExportDocument().GlobalStyle;
        Assert.Equal("Segoe UI", exported.FontFamily);
        Assert.Equal(8, exported.FontSizePt);
        Assert.Equal("#FFFFFFFF", exported.ScaleBarColor);
    }

    [Fact]
    public void ScientificColorDictionary_AppliesSelectedPhysicalObjectColorToAnnotation()
    {
        FigureCanvasViewModel figure = CreateFigure();
        figure.AddRectangleAnnotationCommand.Execute(null);
        FigureAnnotationViewModel annotation = Assert.IsType<FigureAnnotationViewModel>(
            figure.SelectedAnnotation);
        ScientificColorEntryViewModel color = figure.ScientificColors[1];
        figure.SelectedScientificColor = color;

        figure.ApplySelectedScientificColorCommand.Execute(null);

        Assert.Equal(color.Color, annotation.Color);
        Assert.Contains("项目颜色", figure.ScientificColorStatusText, StringComparison.Ordinal);
    }

    [Fact]
    public void PanelResize_PreservesSourceAspectRatioUntilUnlocked()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource(400, 200);
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(0, 0, 400, 200)));

        Assert.True(panel.IsAspectRatioLocked);
        panel.Width = 300;
        Assert.Equal(150, panel.Height);

        panel.Height = 100;
        Assert.Equal(200, panel.Width);

        panel.ScalePercent = 50;
        Assert.Equal((200L, 100L), (panel.Width, panel.Height));

        panel.IsAspectRatioLocked = false;
        panel.Width = 320;
        Assert.Equal(100, panel.Height);
    }

    [Fact]
    public void ManualCrop_RemainsPixelExactAcrossFitModeChangesAndExportSnapshot()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource(101, 103);
        var expected = new PixelRect64(0, 0, 7, 11);
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, expected));

        panel.FitMode = PanelFitMode.Fill;
        panel.FitMode = PanelFitMode.Manual;

        Assert.Equal(expected, panel.SourceRect);
        Assert.Equal(expected, Assert.Single(figure.CreateExportDocument().Panels).SourceRect);
    }

    [Fact]
    public void CreateExportDocument_UsesReferenceGridAndBilinearRegistrationForLinkedComposite()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel reference = CreateSource(2, 1);
        SourceAssetItemViewModel target = CreateSource(3, 1);
        Guid groupId = Guid.NewGuid();
        Guid referenceChannelId = Guid.NewGuid();
        Guid targetChannelId = Guid.NewGuid();
        ChannelGroupMember referenceMember = CreateChannelMember(
            referenceChannelId,
            reference.Asset.Id,
            "reference",
            "#FFFFFFFF");
        ChannelGroupMember targetMember = CreateChannelMember(
            targetChannelId,
            target.Asset.Id,
            "target",
            "#FFFF0000");
        var group = new MultiChannelAssetGroup(
            groupId,
            "registered",
            reference.Asset.Id,
            [referenceMember, targetMember],
            SameFieldOfViewConfirmed: false).EnsureValid();
        LinkingSpatialMapping mapping = LinkingSpatialMapping.CreateTranslation(
            reference.Asset.Id,
            target.Asset.Id,
            reference.SourceRevision,
            target.SourceRevision,
            1,
            0,
            DateTimeOffset.UnixEpoch);
        figure.LinkGroups.Add(new LinkingLinkGroup(
            Guid.NewGuid(),
            "registered",
            reference.Asset.Id,
            [reference.Asset.Id, target.Asset.Id],
            LinkingLinkSyncOptions.Crop | LinkingLinkSyncOptions.Roi,
            [mapping]).EnsureValid());
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(reference, new PixelRect64(0, 0, 2, 1)));
        panel.CompositeGroupId = groupId;

        FigurePanelExportItem exported = Assert.Single(
            figure.CreateExportDocument([group], [reference, target]).Panels);

        FigureChannelLayerExportItem referenceLayer = Assert.Single(
            exported.EffectiveChannelLayers,
            layer => layer.Source.Id == reference.Asset.Id);
        FigureChannelLayerExportItem targetLayer = Assert.Single(
            exported.EffectiveChannelLayers,
            layer => layer.Source.Id == target.Asset.Id);
        Assert.Null(referenceLayer.RegistrationResampling);
        RegisteredPlaneResamplingSpec resampling = Assert.IsType<RegisteredPlaneResamplingSpec>(
            targetLayer.RegistrationResampling);
        Assert.Equal(mapping.Id, resampling.Mapping.Id);
        Assert.Equal(RegisteredInterpolation.Bilinear, resampling.Interpolation);
        Assert.Equal(RegisteredBorderPolicy.Transparent, resampling.BorderPolicy);
        Assert.Equal(panel.SourceRect, resampling.ReferenceGrid.Region);
        Assert.Equal(referenceLayer.OutputWidth, targetLayer.OutputWidth);
        Assert.NotEqual(referenceLayer.SourceRect, targetLayer.SourceRect);
    }

    [Fact]
    public void CreateInset_CreatesCenteredDetailCropAndMarksExportPanel()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource(400, 200);
        FigurePanelViewModel reference = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(40, 20, 320, 160)));

        figure.CreateInsetCommand.Execute(null);

        FigurePanelViewModel inset = Assert.Single(figure.Panels, panel => panel.IsInset);
        Assert.Equal(new PixelRect64(120, 60, 160, 80), inset.SourceRect);
        Assert.Same(source, inset.Source);
        Assert.Equal(reference.Adjustments, inset.Adjustments);
        Assert.True(inset.DestinationRect.Right <= figure.CanvasWidth);
        Assert.True(inset.DestinationRect.Bottom <= figure.CanvasHeight);
        FigurePanelExportItem exportedInset = Assert.Single(
            figure.CreateExportDocument().Panels,
            panel => panel.IsInset);
        Assert.Equal(inset.SourceRect, exportedInset.SourceRect);
        Assert.StartsWith("inset:", inset.SlotId, StringComparison.Ordinal);
    }

    [Fact]
    public void LinkedCrops_PropagateNewSourceRectAndCanBeUnlinked()
    {
        FigureCanvasViewModel figure = CreateFigure();
        SourceAssetItemViewModel source = CreateSource(400, 200);
        FigurePanelViewModel first = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(0, 0, 100, 100)));
        FigurePanelViewModel second = Assert.IsType<FigurePanelViewModel>(
            figure.AddPanel(source, new PixelRect64(100, 0, 100, 100)));
        figure.SelectPanel(first, toggle: false);
        figure.SelectPanel(second, toggle: true);

        figure.LinkSelectedPanelCropsCommand.Execute(null);
        Guid groupId = Assert.IsType<Guid>(first.CropLinkGroupId);
        Assert.Equal(groupId, second.CropLinkGroupId);

        var synchronizedCrop = new PixelRect64(20, 30, 160, 80);
        first.ReplaceSource(source, synchronizedCrop);

        Assert.Equal(synchronizedCrop, second.SourceRect);
        figure.UnlinkSelectedPanelCropsCommand.Execute(null);
        Assert.Null(first.CropLinkGroupId);
        Assert.Null(second.CropLinkGroupId);
        first.ReplaceSource(source, new PixelRect64(0, 0, 80, 80));
        Assert.Equal(synchronizedCrop, second.SourceRect);
    }

    [Fact]
    public void LayerSelection_KeepsOnlyOneFigureLayerTypeActive()
    {
        FigureCanvasViewModel figure = CreateFigure();
        FigurePanelViewModel panel = AddPanel(figure, CreateSource());
        figure.AddTextAnnotationCommand.Execute(null);
        Assert.NotNull(figure.SelectedAnnotation);
        Assert.Null(figure.SelectedPanel);

        figure.AddVerticalGuideCommand.Execute(null);
        Assert.NotNull(figure.SelectedGuide);
        Assert.Null(figure.SelectedAnnotation);

        panel.IsSelected = true;

        Assert.Same(panel, figure.SelectedPanel);
        Assert.Null(figure.SelectedGuide);
        Assert.Null(figure.SelectedAnnotation);
    }

    [Fact]
    public void LockedLayers_CannotBeDeletedOrReorderedUntilUnlocked()
    {
        FigureCanvasViewModel figure = CreateFigure();
        FigurePanelViewModel panel = AddPanel(figure, CreateSource());
        panel.IsLocked = true;

        Assert.False(figure.RemoveSelectedCommand.CanExecute(null));
        Assert.False(figure.MoveLayerUpCommand.CanExecute(null));

        figure.AddLineAnnotationCommand.Execute(null);
        FigureAnnotationViewModel annotation = Assert.IsType<FigureAnnotationViewModel>(figure.SelectedAnnotation);
        annotation.IsLocked = true;

        Assert.False(figure.RemoveSelectedAnnotationCommand.CanExecute(null));
        Assert.False(figure.MoveAnnotationUpCommand.CanExecute(null));
        annotation.IsLocked = false;
        Assert.True(figure.RemoveSelectedAnnotationCommand.CanExecute(null));
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

    private static SourceAssetItemViewModel CreateSource(int width = 100, int height = 100)
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
            "selection.png",
            "selection.png",
            new SourceFingerprint(0, DateTimeOffset.UtcNow, new string('0', 64), null),
            new SciCanvas.Core.Images.ImageMetadata(
                new PixelSize64(width, height),
                4,
                8,
                "Bgra32"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, preview);
    }

    private static ChannelGroupMember CreateChannelMember(
        Guid channelId,
        Guid assetId,
        string name,
        string color) => new(
        channelId,
        assetId,
        ChannelPlaneSelector.InterleavedComponent(0, 0),
        name,
        null,
        color,
        ChannelNameOrigin.User,
        true,
        new ChannelDisplaySettings(channelId, true, color, 1, 0, byte.MaxValue, 1, false))
    {
        SourceRevision = 1,
    };
}
