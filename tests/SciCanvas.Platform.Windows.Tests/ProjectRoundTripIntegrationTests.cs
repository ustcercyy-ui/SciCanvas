using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Platform.Windows;
using SciCanvas.Presentation;
using SciCanvas.Templates;

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
        original.Sources.Add(sourceItem);
        original.SelectedSource = sourceItem;
        Assert.True(original.Crop.RestoreForSource(
            asset.Metadata.PixelSize,
            new PixelRect64(3, 4, 12, 8)));
        original.SelectedFigureTemplate = original.AvailableTemplates.Single(
            template => template.Id == "materials.synthesis-structure-performance.nature-double");
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            original.Figure.AddPanel(sourceItem, new PixelRect64(3, 4, 12, 8)));
        panel.X = 111;
        panel.Y = 222;
        panel.IsLocked = true;
        panel.IsAspectRatioLocked = false;
        panel.IsVisible = false;
        panel.PhysicalUnitsPerSourcePixel = 0.25;
        panel.ScaleBarPhysicalLength = 2;
        panel.ScaleBarUnit = "µm";
        panel.ScaleBarShowLabel = true;
        panel.ShowScaleBar = true;
        original.Figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel textAnnotation = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        textAnnotation.Text = "界面区域";
        textAnnotation.X = 300;
        textAnnotation.Y = 400;
        textAnnotation.Color = "#FF2255AA";
        textAnnotation.IsBold = true;
        original.Figure.AddArrowAnnotationCommand.Execute(null);
        FigureAnnotationViewModel arrowAnnotation = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        arrowAnnotation.X = 250;
        arrowAnnotation.Y = 500;
        arrowAnnotation.EndX = 450;
        arrowAnnotation.EndY = 560;
        arrowAnnotation.Color = "#FFE53935";
        arrowAnnotation.StrokeWidthPt = 1.5;
        arrowAnnotation.IsLocked = true;
        original.Figure.AddTextAnnotationCommand.Execute(null);
        FigureAnnotationViewModel draftAnnotation = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        draftAnnotation.Text = string.Empty;
        draftAnnotation.Color = "待填写";
        original.Figure.AddRectangleAnnotationCommand.Execute(null);
        FigureAnnotationViewModel rectangle = Assert.IsType<FigureAnnotationViewModel>(
            original.Figure.SelectedAnnotation);
        rectangle.X = 520;
        rectangle.Y = 260;
        rectangle.EndX = 820;
        rectangle.EndY = 560;
        rectangle.Color = "#FFFFB300";
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
        Assert.True(restored.Crop.TryGetCrop(out PixelRect64 restoredCrop));
        Assert.Equal(new PixelRect64(3, 4, 12, 8), restoredCrop);
        FigurePanelViewModel restoredPanel = Assert.Single(restored.Figure.Panels);
        Assert.Equal(panel.Id, restoredPanel.Id);
        Assert.Equal(111, restoredPanel.X);
        Assert.Equal(222, restoredPanel.Y);
        Assert.True(restoredPanel.IsLocked);
        Assert.False(restoredPanel.IsAspectRatioLocked);
        Assert.False(restoredPanel.IsVisible);
        Assert.True(restoredPanel.ShowScaleBar);
        Assert.Equal(0.25, restoredPanel.PhysicalUnitsPerSourcePixel);
        Assert.Equal(2, restoredPanel.ScaleBarPhysicalLength);
        Assert.Equal("µm", restoredPanel.ScaleBarUnit);
        Assert.Equal(5, restored.Figure.Annotations.Count);
        FigureAnnotationViewModel restoredText = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Text &&
                          annotation.Text == "界面区域");
        Assert.Equal("界面区域", restoredText.Text);
        Assert.Equal(300, restoredText.X);
        Assert.True(restoredText.IsBold);
        FigureAnnotationViewModel restoredArrow = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Arrow);
        Assert.Equal(450, restoredArrow.EndX);
        Assert.Equal(1.5, restoredArrow.StrokeWidthPt);
        Assert.True(restoredArrow.IsLocked);
        FigureAnnotationViewModel restoredDraft = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Text && annotation.Text.Length == 0);
        Assert.Equal("待填写", restoredDraft.Color);
        Assert.False(restoredDraft.IsValid);
        FigureAnnotationViewModel restoredRectangle = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Rectangle);
        Assert.Equal(820, restoredRectangle.EndX);
        Assert.Equal(1.75, restoredRectangle.StrokeWidthPt);
        FigureAnnotationViewModel restoredEllipse = restored.Figure.Annotations.Single(
            annotation => annotation.Kind == FigureAnnotationKind.Ellipse);
        Assert.Equal(320, restoredEllipse.ShapeHeight);
        Assert.Equal("#FF1E88E5", restoredEllipse.Color);
        Assert.Equal(sourceHash, SHA256.HashData(await File.ReadAllBytesAsync(sourcePath)));
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
        panel.Label = "SEM-1";
        await original.SaveProjectToPathAsync(projectPath);

        MainWindowViewModel restored = CreateViewModel();
        await restored.OpenProjectFromPathAsync(projectPath);

        Assert.False(restored.IsDirty);
        Assert.Equal("#FFECEFF1", restored.Figure.NormalizedBackgroundColor);
        Assert.False(restored.Figure.AutoPanelLabelsEnabled);
        Assert.True(restored.Figure.ShowPanelLabels);
        Assert.Equal("uppercase", restored.Figure.PanelLabelSequence);
        Assert.Equal("SEM-1", Assert.Single(restored.Figure.Panels).Label);
        Assert.Equal("#FFECEFF1", restored.Figure.CreateExportDocument().BackgroundColor);

        restored.Figure.BackgroundColor = "#FF000000";
        restored.CompleteHistoryGesture();
        Assert.True(restored.IsDirty);
        restored.UndoCommand.Execute(null);
        Assert.Equal("#FFECEFF1", restored.Figure.NormalizedBackgroundColor);
        Assert.False(restored.IsDirty);
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

    private static MainWindowViewModel CreateViewModel(
        IProjectRecoveryStore? recoveryStore = null,
        IProjectRecoveryPrompt? recoveryPrompt = null,
        ISourceRelinkFilePicker? sourceRelinkFilePicker = null,
        ISourceRevisionAcceptancePrompt? sourceRevisionAcceptancePrompt = null)
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
            new EmptyProjectPicker(),
            new JsonProjectStore(),
            recoveryStore,
            recoveryPrompt,
            sourceRelinkFilePicker,
            sourceRevisionAcceptancePrompt);
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
