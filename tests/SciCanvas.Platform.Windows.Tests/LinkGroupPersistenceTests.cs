using System.Security.Cryptography;
using SciCanvas.Core.Linking;
using SciCanvas.Persistence;
using SciCanvas.Presentation;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class LinkGroupPersistenceTests
{
    [Fact]
    public async Task JsonProjectStore_RoundTripsTranslationMappingProvenance()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("reference.tif", [1, 2, 3, 4]);
        string targetPath = workspace.CreateFile("target.tif", [5, 6, 7, 8]);
        string projectPath = Path.Combine(workspace.Root, "linked.scicanvas");
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        var mapping = SpatialMapping.CreateTranslation(
            referenceId,
            targetId,
            sourceRevision: 1,
            targetRevision: 1,
            offsetX: 12.5,
            offsetY: -4,
            createdAt: DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
        var group = new LinkGroup(
            groupId,
            "SEM / EDS linked view",
            referenceId,
            [referenceId, targetId],
            LinkSyncOptions.Crop | LinkSyncOptions.Roi | LinkSyncOptions.ColorScale,
            [mapping]);
        SciCanvasProjectDocument document = CreateDocument(
            sourcePath,
            targetPath,
            referenceId,
            targetId,
            groupId,
            ProjectDocumentMapper.ToSnapshot(group));

        var store = new JsonProjectStore();
        await store.SaveAsync(projectPath, document);
        SciCanvasProjectDocument loaded = await store.LoadAsync(projectPath);

        ProjectLinkGroupSnapshot snapshot = Assert.Single(loaded.LinkGroups);
        LinkGroup restored = ProjectDocumentMapper.ToLinkGroup(snapshot);
        SpatialMapping restoredMapping = Assert.Single(restored.Mappings);
        Assert.Equal(SpatialMappingKind.Translation, restoredMapping.Kind);
        Assert.Equal(SpatialMappingOrigin.UserDeclaredTranslation, restoredMapping.Origin);
        Assert.Equal(mapping.Matrix, restoredMapping.Matrix);
        Assert.Equal(mapping.CreatedAt, restoredMapping.CreatedAt);
        Assert.Null(restoredMapping.ResidualPixels);
    }

    [Fact]
    public async Task JsonProjectStore_RoundTripsManualAffineRegistrationProvenance()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("reference.tif", [1, 2, 3]);
        string targetPath = workspace.CreateFile("target.tif", [4, 5, 6]);
        string projectPath = Path.Combine(workspace.Root, "registration.scicanvas");
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        RegistrationLandmarkPair[] landmarks =
        [
            new(Guid.NewGuid(), new SpatialPoint(0, 0), new SpatialPoint(4, -2)),
            new(Guid.NewGuid(), new SpatialPoint(10, 0), new SpatialPoint(24, 3)),
            new(Guid.NewGuid(), new SpatialPoint(0, 10), new SpatialPoint(-6, 28)),
        ];
        SpatialMapping mapping = SpatialRegistrationSolver.Solve(
            referenceId,
            targetId,
            1,
            1,
            SpatialMappingKind.Affine,
            landmarks,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z")).Mapping;
        Guid groupId = Guid.NewGuid();
        var group = new LinkGroup(
            groupId,
            "SEM / EDS registration",
            referenceId,
            [referenceId, targetId],
            LinkSyncOptions.Crop | LinkSyncOptions.Roi,
            [mapping]);
        SciCanvasProjectDocument document = CreateDocument(
            sourcePath,
            targetPath,
            referenceId,
            targetId,
            groupId,
            ProjectDocumentMapper.ToSnapshot(group));

        var store = new JsonProjectStore();
        await store.SaveAsync(projectPath, document);
        SciCanvasProjectDocument loaded = await store.LoadAsync(projectPath);

        SpatialMapping restored = Assert.Single(
            ProjectDocumentMapper.ToLinkGroup(Assert.Single(loaded.LinkGroups)).Mappings);
        Assert.Equal(SpatialMappingKind.Affine, restored.Kind);
        Assert.Equal(SpatialMappingOrigin.ManualLandmarks, restored.Origin);
        Assert.Equal(mapping.Matrix, restored.Matrix);
        Assert.Equal(mapping.ResidualPixels, restored.ResidualPixels);
        Assert.Equal(landmarks, restored.EffectiveLandmarks);
    }
    [Fact]
    public async Task JsonProjectStore_RoundTripsCanonicalPolygonAndPropagationProvenance()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("reference.tif", [1]);
        string targetPath = workspace.CreateFile("target.tif", [2]);
        string projectPath = Path.Combine(workspace.Root, "roi-propagation.scicanvas");
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        SpatialMapping mapping = SpatialMapping.CreateTranslation(
            referenceId,
            targetId,
            1,
            1,
            -4,
            0,
            DateTimeOffset.Parse("2026-08-28T00:00:00Z"));
        var group = new LinkGroup(
            groupId,
            "HAADF / Ti",
            referenceId,
            [referenceId, targetId],
            LinkSyncOptions.Roi,
            [mapping]).EnsureValid();
        var reference = new SciCanvas.Core.Workspace.RoiObject
        {
            Id = Guid.NewGuid(),
            AssetId = referenceId,
            SourceRevision = 1,
            GeometryKind = SciCanvas.Core.Workspace.RoiGeometryKind.Polygon,
            FrameIndex = 0,
            SourceGeometry =
            [
                new SciCanvas.Core.Science.MeasurementPoint(1, 1),
                new SciCanvas.Core.Science.MeasurementPoint(8, 1),
                new SciCanvas.Core.Science.MeasurementPoint(1, 8),
            ],
            Style = SciCanvas.Core.Workspace.RoiStyle.Default with { Label = "Grain" },
        }.EnsureValid();
        SciCanvas.Core.Workspace.RoiObject target = Assert.Single(
            SciCanvas.Core.Workspace.RoiPropagationService.PropagatePolygon(
                reference,
                group,
                new Dictionary<Guid, SciCanvas.Core.Workspace.RoiSourceGeometryContext>
                {
                    [referenceId] = new(1, new SciCanvas.Core.Geometry.PixelSize64(20, 20)),
                    [targetId] = new(1, new SciCanvas.Core.Geometry.PixelSize64(20, 20)),
                }));
        SciCanvasProjectDocument document = CreateDocument(
            sourcePath,
            targetPath,
            referenceId,
            targetId,
            groupId,
            ProjectDocumentMapper.ToSnapshot(group),
            [ProjectDocumentMapper.ToSnapshot(reference), ProjectDocumentMapper.ToSnapshot(target)]);

        var store = new JsonProjectStore();
        await store.SaveAsync(projectPath, document);
        SciCanvasProjectDocument loaded = await store.LoadAsync(projectPath);

        Assert.Equal(2, loaded.Rois.Count);
        SciCanvas.Core.Workspace.RoiObject restoredReference = ProjectDocumentMapper.ToRoiObject(
            loaded.Rois.Single(roi => roi.Id == reference.Id));
        SciCanvas.Core.Workspace.RoiObject restoredTarget = ProjectDocumentMapper.ToRoiObject(
            loaded.Rois.Single(roi => roi.Id == target.Id));
        Assert.Equal(reference.SourceGeometry, restoredReference.SourceGeometry);
        Assert.Equal("Grain", restoredReference.Style.Label);
        Assert.Equal(reference.Id, restoredTarget.Propagation!.ReferenceRoiId);
        Assert.Equal(groupId, restoredTarget.Propagation.LinkGroupId);
        Assert.Equal(mapping.Id, restoredTarget.Propagation.MappingId);
        Assert.Equal(
            target.Propagation!.TargetCoverageFraction,
            restoredTarget.Propagation.TargetCoverageFraction,
            12);
        Assert.InRange(restoredTarget.Propagation.TargetCoverageFraction, double.Epsilon, 1 - double.Epsilon);
        Assert.Equal(
            SciCanvas.Core.Workspace.ScientificValidityState.ReviewRequired,
            restoredTarget.Validity.State);
    }
    [Fact]
    public async Task JsonProjectStore_RejectsMappingThatReferencesFutureRevision()
    {
        using var workspace = new TestWorkspace();
        string sourcePath = workspace.CreateFile("reference.tif", [1]);
        string targetPath = workspace.CreateFile("target.tif", [2]);
        string projectPath = Path.Combine(workspace.Root, "invalid-linked.scicanvas");
        Guid referenceId = Guid.NewGuid();
        Guid targetId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        ProjectLinkGroupSnapshot snapshot = ProjectDocumentMapper.ToSnapshot(new LinkGroup(
            groupId,
            "Invalid future mapping",
            referenceId,
            [referenceId, targetId],
            LinkSyncOptions.Crop,
            [SpatialMapping.CreateIdentity(
                referenceId,
                targetId,
                sourceRevision: 2,
                targetRevision: 1,
                createdAt: DateTimeOffset.UtcNow)]));
        SciCanvasProjectDocument document = CreateDocument(
            sourcePath,
            targetPath,
            referenceId,
            targetId,
            groupId,
            snapshot);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new JsonProjectStore().SaveAsync(projectPath, document));
    }

    private static SciCanvasProjectDocument CreateDocument(
        string referencePath,
        string targetPath,
        Guid referenceId,
        Guid targetId,
        Guid groupId,
        ProjectLinkGroupSnapshot group,
        IReadOnlyList<ProjectRoiSnapshot>? rois = null) => new()
    {
        ProjectId = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
        UpdatedAt = DateTimeOffset.UtcNow,
        Title = "Linked view persistence",
        Canvas = new ProjectCanvasSnapshot { Width = 1000, Height = 800, Background = "white" },
        Sources =
        [
            CreateSource(referenceId, referencePath),
            CreateSource(targetId, targetPath),
        ],
        Layers =
        [
            CreateLayer(Guid.NewGuid(), referenceId, groupId, 0),
            CreateLayer(Guid.NewGuid(), targetId, groupId, 1),
        ],
        LinkGroups = [group],
        Rois = rois ?? [],
        TemplateSnapshot = new ProjectTemplateSnapshot
        {
            TemplateId = "materials.multiscale-morphology.nature-double",
        },
    };

    private static ProjectSourceSnapshot CreateSource(Guid id, string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return new ProjectSourceSnapshot
        {
            Id = id,
            DisplayName = Path.GetFileName(path),
            OriginalPath = path,
            SourceRevision = 1,
            Fingerprint = new ProjectFingerprintSnapshot
            {
                ByteLength = bytes.Length,
                LastWriteTimeUtc = File.GetLastWriteTimeUtc(path),
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)),
            },
            Metadata = new ProjectImageMetadataSnapshot
            {
                Width = 20,
                Height = 20,
                Channels = 1,
                BitsPerChannel = 8,
                PixelFormat = "Gray8",
            },
            LinkState = "verified",
        };
    }

    private static ProjectImageLayerSnapshot CreateLayer(
        Guid id,
        Guid sourceId,
        Guid groupId,
        int zIndex) => new()
    {
        Id = id,
        Name = $"Layer {zIndex + 1}",
        SourceAssetId = sourceId,
        CropLinkGroupId = groupId,
        ZIndex = zIndex,
        SourceRect = new ProjectPixelRectSnapshot { Width = 10, Height = 10 },
    };
}
