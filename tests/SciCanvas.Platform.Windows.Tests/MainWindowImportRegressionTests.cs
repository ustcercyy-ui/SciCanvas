using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.App;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class MainWindowImportRegressionTests
{
    [Fact]
    public void AddingFirstImportedSource_DoesNotThrowDuringTemplateLayout()
    {
        Exception? failure = null;
        using var completed = new ManualResetEventSlim();

        var thread = new Thread(() =>
        {
            System.Windows.Application? application = null;
            MainWindow? window = null;

            try
            {
                application = new System.Windows.Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };

                MainWindowViewModel viewModel = CreateViewModel();
                window = new MainWindow
                {
                    DataContext = viewModel,
                };

                viewModel.Sources.Add(CreateSourceItem());
                viewModel.SelectedSource = viewModel.Sources[0];
                FigurePanelViewModel? figurePanel = viewModel.Figure.AddPanel(
                    viewModel.Sources[0],
                    new PixelRect64(0, 0, 2, 2));
                Assert.NotNull(figurePanel);
                figurePanel.PhysicalUnitsPerSourcePixel = 0.5;
                figurePanel.ScaleBarPhysicalLength = 0.5;
                figurePanel.ScaleBarUnit = "µm";
                figurePanel.ShowScaleBar = true;
                viewModel.Figure.AddTextAnnotationCommand.Execute(null);
                viewModel.Figure.AddArrowAnnotationCommand.Execute(null);
                viewModel.WorkspaceMode = WorkspaceMode.Figure;

                window.Show();
                window.UpdateLayout();
                window.DataContext = null;
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            finally
            {
                application?.Shutdown();
                completed.Set();
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.True(completed.Wait(TimeSpan.FromSeconds(10)), "WPF 回归测试未在10秒内完成。");
        thread.Join();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static MainWindowViewModel CreateViewModel() => new(
        new EmptyImageFilePicker(),
        new NoOpSourceAssetReader(),
        new NoOpPreviewLoader(),
        new EmptyExportFilePicker(),
        new AllowPathSafetyPolicy(),
        new NoOpCropExporter(),
        new NoOpFigureExporter(),
        new BuiltInTemplateCatalog().LoadAll(),
        new EmptyProjectFilePicker(),
        new NoOpProjectStore());

    private static SourceAssetItemViewModel CreateSourceItem()
    {
        byte[] pixels =
        [
            0, 0, 0, 255,
            255, 255, 255, 255,
            255, 0, 0, 255,
            0, 255, 0, 255,
        ];
        BitmapSource preview = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: 8);
        preview.Freeze();

        var source = new SourceAsset(
            Guid.NewGuid(),
            "regression.png",
            @"C:\images\regression.png",
            new SourceFingerprint(16, DateTimeOffset.UtcNow, new string('A', 64), "TEST:1"),
            new CoreImageMetadata(new PixelSize64(2, 2), 4, 8, "Bgra32"),
            SourceLinkState.Verified);

        return new SourceAssetItemViewModel(source, preview);
    }

    private sealed class EmptyImageFilePicker : IImageFilePicker
    {
        public IReadOnlyList<string> PickImageFiles() => [];
    }

    private sealed class EmptyExportFilePicker : IExportFilePicker
    {
        public string? PickNewExportPath(string suggestedFileName) => null;
    }

    private sealed class NoOpSourceAssetReader : ISourceAssetReader
    {
        public Task<SourceAsset> ImportAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SourceVerification> VerifyAsync(
            SourceAsset asset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SourceVerification(SourceLinkState.Verified, asset.Fingerprint, null));
    }

    private sealed class NoOpPreviewLoader : IImagePreviewLoader
    {
        public Task<BitmapSource> LoadAsync(
            string path,
            int maximumPixelWidth,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AllowPathSafetyPolicy : IPathSafetyPolicy
    {
        public Task<ExportPathDecision> ValidateExportTargetAsync(
            string targetPath,
            IReadOnlyCollection<SourceAsset> sources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExportPathDecision.Allow(targetPath));
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

    private sealed class EmptyProjectFilePicker : IProjectFilePicker
    {
        public string? PickProjectToOpen() => null;

        public string? PickProjectToSave(string suggestedFileName, string? currentPath) => null;
    }

    private sealed class NoOpProjectStore : IProjectStore
    {
        public Task<SciCanvasProjectDocument> LoadAsync(
            string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsync(
            string path,
            SciCanvasProjectDocument document,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
