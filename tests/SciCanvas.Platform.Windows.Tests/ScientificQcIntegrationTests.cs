using System.Security.Cryptography;
using System.Windows.Media.Imaging;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using SpatialLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using SpatialLinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ScientificQcIntegrationTests
{
    [Fact]
    public void MainWindowFigureQc_ReportsStaleSpatialMappingFromCoreEngine()
    {
        using var workspace = new TestWorkspace();
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel reference = CreateGray8Source(
            workspace,
            "reference.png",
            2,
            2,
            [1, 2, 3, 4]);
        SourceAssetItemViewModel target = CreateGray8Source(
            workspace,
            "target.png",
            2,
            2,
            [4, 3, 2, 1]);
        AddSourcesAndPanels(viewModel, reference, target);
        SpatialMapping mapping = SpatialMapping.CreateIdentity(
            reference.Asset.Id,
            target.Asset.Id,
            sourceRevision: 1,
            targetRevision: 1,
            DateTimeOffset.Parse("2026-08-30T00:00:00Z"));
        var links = new SpatialLinkGroup(
            Guid.NewGuid(),
            "registered",
            reference.Asset.Id,
            [reference.Asset.Id, target.Asset.Id],
            SpatialLinkSyncOptions.Crop,
            [mapping]);
        viewModel.Figure.RestoreLinkGroups([links]);
        target.RestoreSourceRevision(2);

        viewModel.RunFigureQcCommand.Execute(null);

        Assert.Contains(viewModel.FigureQcIssues, issue =>
            issue.Code == "linked-view.mapping-revision-stale" &&
            issue.SourceId == target.Asset.Id);
    }

    [Fact]
    public void MainWindowFigureQc_LinkedColorbarSynchronizesBeforeCoreEngineRuns()
    {
        using var workspace = new TestWorkspace();
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel source = CreateGray8Source(
            workspace,
            "eds-map.png",
            2,
            2,
            [10, 20, 30, 40]);
        SourceAssetItemViewModel secondary = CreateGray8Source(
            workspace,
            "eds-map-secondary.png",
            2,
            2,
            [40, 30, 20, 10]);
        AddSourcesAndPanels(viewModel, source, secondary);
        Guid channelId = Guid.NewGuid();
        Guid secondaryChannelId = Guid.NewGuid();
        var member = new ChannelGroupMember(
            channelId,
            source.Asset.Id,
            ChannelPlaneSelector.ExternalAsset(frameIndex: 0),
            "Ti",
            "quantitative",
            "#FFFFFFFF",
            ChannelNameOrigin.User,
            true,
            new ChannelDisplaySettings(channelId, true, "#FFFFFFFF", 1, 0, 255, 1, false))
        {
            SourceRevision = 1,
        };
        var secondaryMember = new ChannelGroupMember(
            secondaryChannelId,
            secondary.Asset.Id,
            ChannelPlaneSelector.ExternalAsset(frameIndex: 0),
            "Al",
            null,
            "#FF00FF00",
            ChannelNameOrigin.User,
            true,
            new ChannelDisplaySettings(
                secondaryChannelId,
                true,
                "#FF00FF00",
                1,
                0,
                255,
                1,
                false))
        {
            SourceRevision = 1,
        };
        var group = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            "EDS",
            source.Asset.Id,
            [member, secondaryMember],
            SameFieldOfViewConfirmed: true).EnsureValid(new HashSet<Guid>
            {
                source.Asset.Id,
                secondary.Asset.Id,
            });
        viewModel.MultiChannelWorkspace.Restore([group]);
        viewModel.Figure.SynchronizeScientificObjectChannels(
            viewModel.MultiChannelWorkspace.CreateModels());
        viewModel.Figure.RestoreScientificObject(
            Guid.NewGuid(),
            FigureScientificObjectKind.Colorbar,
            "20,20;40,140",
            "Intensity",
            "#FFFFFFFF",
            "#FF000000",
            100,
            "#FFFFFFFF",
            "Arial",
            7,
            1.25,
            true,
            true,
            false,
            0,
            minimum: 0,
            maximum: 100,
            "a.u.",
            "viridis",
            string.Empty,
            channelId);

        FigureScientificObjectViewModel colorbar = viewModel.Figure.ScientificObjects.Single(
            item => item.Kind == FigureScientificObjectKind.Colorbar);
        Assert.Equal(ColorbarBindingState.Linked, colorbar.ColorbarBindingState);
        Assert.Equal(255, colorbar.Maximum);

        viewModel.RunFigureQcCommand.Execute(null);

        Assert.DoesNotContain(viewModel.FigureQcIssues, issue =>
            issue.Code == "colorbar.range-mismatch");
    }

    [Fact]
    public void MainWindowFigureQc_ReportsRotate90ExactRawDuplicate()
    {
        using var workspace = new TestWorkspace();
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel first = CreateGray8Source(
            workspace,
            "first.png",
            3,
            2,
            [1, 2, 3, 4, 5, 6]);
        SourceAssetItemViewModel rotated = CreateGray8Source(
            workspace,
            "rotated.png",
            2,
            3,
            [4, 1, 5, 2, 6, 3]);
        AddSourcesAndPanels(viewModel, first, rotated);

        viewModel.RunFigureQcCommand.Execute(null);

        FigureQcIssueViewModel issue = Assert.Single(
            viewModel.FigureQcIssues,
            item => item.Code == "integrity.exact-transformed-duplicate");
        Assert.Contains("Rotate90", issue.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmissionPackage_UnifiedScientificErrorBlocksBeforeAnyWriteOrExport()
    {
        using var workspace = new TestWorkspace();
        SourceAssetItemViewModel reference = CreateGray8Source(
            workspace,
            "reference.png",
            2,
            2,
            [1, 2, 3, 4]);
        SourceAssetItemViewModel target = CreateGray8Source(
            workspace,
            "target.png",
            2,
            2,
            [4, 3, 2, 1]);
        target.RestoreSourceRevision(2);
        SourceAsset[] sources = [reference.Asset, target.Asset];
        Dictionary<Guid, long> revisions = new()
        {
            [reference.Asset.Id] = 1,
            [target.Asset.Id] = 2,
        };
        var figure = new FigureExportDocument(100, 80, 300, []);
        ScientificProject project = ScientificQcProjectFactory.Create(
            Guid.NewGuid(),
            "Submission QC",
            figure,
            sources,
            revisions);
        SpatialMapping mapping = SpatialMapping.CreateIdentity(
            reference.Asset.Id,
            target.Asset.Id,
            sourceRevision: 1,
            targetRevision: 1,
            DateTimeOffset.Parse("2026-08-30T00:00:00Z"));
        var links = new SpatialLinkGroup(
            Guid.NewGuid(),
            "registered",
            reference.Asset.Id,
            [reference.Asset.Id, target.Asset.Id],
            SpatialLinkSyncOptions.Crop,
            [mapping]);
        UnifiedQcReport report = new ScientificQcCoordinator().Run(new ScientificQcRequest(
            new FigurePreflightContext(figure),
            sources,
            new QcContext(project, new QcConfiguration(), LinkGroups: [links])));
        var exporter = new RecordingFigureExporter();
        var builder = new SubmissionPackageBuilder(exporter);
        string targetDirectory = Path.Combine(workspace.Root, "SubmissionPackage");

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            builder.BuildAsync(new SubmissionPackageRequest(
                targetDirectory,
                figure,
                [reference, target],
                report,
                [],
                "2.4.1-alpha")));

        Assert.Contains("Figure QC", exception.Message, StringComparison.Ordinal);
        Assert.Contains(report.Issues, issue => issue.Code == "linked-view.mapping-revision-stale");
        Assert.False(Directory.Exists(targetDirectory));
        Assert.Equal(0, exporter.CallCount);
    }

    private static void AddSourcesAndPanels(
        MainWindowViewModel viewModel,
        params SourceAssetItemViewModel[] sources)
    {
        foreach (SourceAssetItemViewModel source in sources)
        {
            viewModel.Sources.Add(source);
            Assert.NotNull(viewModel.Figure.AddPanel(
                source,
                new PixelRect64(0, 0, source.Width, source.Height)));
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

    private static SourceAssetItemViewModel CreateGray8Source(
        TestWorkspace workspace,
        string fileName,
        int width,
        int height,
        byte[] pixels)
    {
        string path = Path.Combine(workspace.Root, fileName);
        BitmapSource bitmap = BitmapSource.Create(
            width,
            height,
            96,
            96,
            System.Windows.Media.PixelFormats.Gray8,
            null,
            pixels,
            width);
        bitmap.Freeze();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            encoder.Save(output);
        }

        var info = new FileInfo(path);
        var asset = new SourceAsset(
            Guid.NewGuid(),
            fileName,
            path,
            new SourceFingerprint(
                info.Length,
                info.LastWriteTimeUtc,
                Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                null),
            new ImageMetadata(new PixelSize64(width, height), 1, 8, "Gray8"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(asset, bitmap);
    }

    private sealed class RecordingFigureExporter : IFigureExporter
    {
        public int CallCount { get; private set; }

        public Task ExportAsync(
            FigureExportDocument document,
            string targetPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
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
