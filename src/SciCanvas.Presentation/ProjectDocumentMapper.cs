using System.IO;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Persistence;

namespace SciCanvas.Presentation;

internal static class ProjectDocumentMapper
{
    public static SciCanvasProjectDocument Create(
        Guid projectId,
        DateTimeOffset createdAt,
        string title,
        IReadOnlyCollection<SourceAssetItemViewModel> sources,
        SourceAssetItemViewModel? selectedSource,
        CropEditorViewModel crop,
        FigureCanvasViewModel figure,
        WorkspaceMode workspaceMode,
        bool lockCropSizeAcrossSources,
        bool cropOverlayVisible,
        IReadOnlyList<ProjectAuditEntrySnapshot>? auditTrail = null)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ProjectPixelRectSnapshot? activeCrop = crop.TryGetCrop(out PixelRect64 cropRect)
            ? ToSnapshot(cropRect)
            : null;

        Dictionary<Guid, string> layerSlots = figure.Panels.ToDictionary(
            panel => panel.Id,
            panel => panel.SlotId);
        Dictionary<Guid, ProjectScaleBarSnapshot> scaleBars = figure.Panels.ToDictionary(
            panel => panel.Id,
            panel => new ProjectScaleBarSnapshot
            {
                Enabled = panel.ShowScaleBar,
                PhysicalUnitsPerSourcePixel = panel.PhysicalUnitsPerSourcePixel,
                PhysicalLength = panel.ScaleBarPhysicalLength,
                Unit = panel.ScaleBarUnit,
                ShowLabel = panel.ScaleBarShowLabel,
            });

        return new SciCanvasProjectDocument
        {
            ProjectId = projectId,
            CreatedAt = createdAt,
            UpdatedAt = now,
            Title = title,
            Canvas = new ProjectCanvasSnapshot
            {
                Width = figure.CanvasWidth,
                Height = figure.CanvasHeight,
                Background = "custom",
                BackgroundColor = figure.NormalizedBackgroundColor,
            },
            Sources = sources.Select(ToSnapshot).ToArray(),
            Layers = figure.Panels
                .OrderBy(panel => panel.ZIndex)
                .Select(ToSnapshot)
                .ToArray(),
            CropPresets = activeCrop is null
                ? []
                :
                [
                    new ProjectCropPresetSnapshot
                    {
                        Id = Guid.Parse("51E93E83-5B29-4E82-A432-A1507D7AA14C"),
                        Name = $"当前裁剪 {activeCrop.Width}×{activeCrop.Height}",
                        Width = activeCrop.Width,
                        Height = activeCrop.Height,
                        Unit = "px",
                    },
                ],
            Guides = figure.Guides.Select(guide => new ProjectGuideSnapshot
            {
                Orientation = guide.OrientationKey,
                Position = guide.Position,
                Locked = guide.IsLocked,
            }).ToArray(),
            ExportProfiles = FigureExportProfile.BuiltIns
                .Select(profile => new ProjectExportProfileSnapshot
                {
                    Id = GetStableExportProfileId(profile.Id),
                    Name = profile.Name,
                    Format = profile.Format,
                    Dpi = profile.Dpi,
                    Scale = profile.Scale,
                    WidthPixels = profile.WidthPixels,
                    HeightPixels = profile.HeightPixels,
                    WriteProvenance = profile.WriteProvenance,
                    BitDepth = 8,
                    ColorMode = "rgb",
                    Resampling = null,
                    JournalPresetId = figure.Template.PublisherProfileId,
                    WriteAuditReport = true,
                })
                .ToArray(),
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                TemplateId = figure.Template.Id,
                WorkspaceMode = workspaceMode == WorkspaceMode.Figure ? "figure" : "crop",
                SelectedSourceId = selectedSource?.Asset.Id,
                ActiveCrop = activeCrop,
                LockCropSizeAcrossSources = lockCropSizeAcrossSources,
                CropOverlayVisible = cropOverlayVisible,
                SnappingEnabled = figure.IsSnappingEnabled,
                SnapTolerancePixels = figure.SnapTolerancePixels,
                ExactSpacingPixels = figure.ExactSpacingPixels,
                AutoPanelLabelsEnabled = figure.AutoPanelLabelsEnabled,
                ShowPanelLabels = figure.ShowPanelLabels,
                PanelLabelSequence = figure.PanelLabelSequence,
                LayerSlots = layerSlots,
                ScaleBars = scaleBars,
                Annotations = figure.Annotations
                    .OrderBy(annotation => annotation.ZIndex)
                    .Select(annotation => new ProjectAnnotationSnapshot
                    {
                        Id = annotation.Id,
                        Kind = annotation.KindKey,
                        X = annotation.X,
                        Y = annotation.Y,
                        EndX = annotation.EndX,
                        EndY = annotation.EndY,
                        Text = annotation.Text,
                        Color = annotation.Color,
                        FontSizePt = annotation.FontSizePt,
                        StrokeWidthPt = annotation.StrokeWidthPt,
                        IsBold = annotation.IsBold,
                        Visible = annotation.IsVisible,
                        Locked = annotation.IsLocked,
                        ZIndex = annotation.ZIndex,
                    })
                    .ToArray(),
            },
            AuditTrail = (auditTrail ?? [])
                .Concat(
                [
                    new ProjectAuditEntrySnapshot
                    {
                        Timestamp = now,
                        Command = "SaveProject",
                        Parameters = new Dictionary<string, object?>
                        {
                            ["sourceCount"] = sources.Count,
                            ["layerCount"] = figure.Panels.Count,
                            ["annotationCount"] = figure.Annotations.Count,
                            ["templateId"] = figure.Template.Id,
                        },
                    },
                ])
                .ToArray(),
        };
    }

    public static SourceAsset ToSourceAsset(ProjectSourceSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.DisplayName,
        snapshot.OriginalPath,
        new SourceFingerprint(
            snapshot.Fingerprint.ByteLength,
            snapshot.Fingerprint.LastWriteTimeUtc,
            snapshot.Fingerprint.Sha256,
            snapshot.Fingerprint.WindowsFileId),
        new ImageMetadata(
            new PixelSize64(snapshot.Metadata.Width, snapshot.Metadata.Height),
            snapshot.Metadata.Channels,
            snapshot.Metadata.BitsPerChannel,
            snapshot.Metadata.PixelFormat,
            snapshot.Metadata.DpiX,
            snapshot.Metadata.DpiY,
            snapshot.Metadata.PhysicalSizeX,
            snapshot.Metadata.PhysicalSizeY,
            snapshot.Metadata.PhysicalUnit,
            snapshot.Metadata.IccProfileName,
            snapshot.Metadata.FrameCount),
        ParseLinkState(snapshot.LinkState));

    public static PixelRect64 ToPixelRect(ProjectPixelRectSnapshot snapshot) => new(
        snapshot.X,
        snapshot.Y,
        snapshot.Width,
        snapshot.Height);

    public static PixelRect64 ToDestinationRect(ProjectImageLayerSnapshot layer)
    {
        long width = Math.Max(1, (long)Math.Round(layer.SourceRect.Width * layer.Transform.ScaleX));
        long height = Math.Max(1, (long)Math.Round(layer.SourceRect.Height * layer.Transform.ScaleY));
        return new PixelRect64(
            Math.Max(0, (long)Math.Round(layer.Transform.X)),
            Math.Max(0, (long)Math.Round(layer.Transform.Y)),
            width,
            height);
    }

    private static Guid GetStableExportProfileId(string profileId) => profileId switch
    {
        "main-tiff" => Guid.Parse("4757F9DE-FE43-47F6-9675-690BE0A431E0"),
        "supplement-png" => Guid.Parse("B7D1C6D5-4B43-4C36-9A6F-7F6F2F4D5E22"),
        "thumbnail-png" => Guid.Parse("F6A3B8E8-9B8D-4BA0-A9D9-5AF1BA58C44F"),
        _ => throw new InvalidDataException($"未知的内置导出预设：{profileId}"),
    };
    private static ProjectSourceSnapshot ToSnapshot(SourceAssetItemViewModel item)
    {
        SourceAsset source = item.Asset;
        ImageMetadata metadata = source.Metadata;
        return new ProjectSourceSnapshot
        {
            Id = source.Id,
            DisplayName = source.DisplayName,
            OriginalPath = source.OriginalPath,
            ProjectRelativePath = null,
            Fingerprint = new ProjectFingerprintSnapshot
            {
                ByteLength = source.Fingerprint.ByteLength,
                LastWriteTimeUtc = source.Fingerprint.LastWriteTimeUtc,
                Sha256 = source.Fingerprint.Sha256,
                WindowsFileId = source.Fingerprint.WindowsFileId,
            },
            Metadata = new ProjectImageMetadataSnapshot
            {
                Width = metadata.PixelSize.Width,
                Height = metadata.PixelSize.Height,
                Channels = metadata.Channels,
                BitsPerChannel = metadata.BitsPerChannel,
                PixelFormat = metadata.PixelFormat,
                DpiX = metadata.DpiX,
                DpiY = metadata.DpiY,
                PhysicalSizeX = metadata.PhysicalSizeX,
                PhysicalSizeY = metadata.PhysicalSizeY,
                PhysicalUnit = metadata.PhysicalUnit,
                IccProfileName = metadata.IccProfileName,
                FrameCount = metadata.FrameCount,
            },
            LinkState = source.LinkState.ToString().ToLowerInvariant(),
        };
    }

    private static ProjectImageLayerSnapshot ToSnapshot(FigurePanelViewModel panel) => new()
    {
        Type = "image",
        Id = panel.Id,
        Name = $"面板 {panel.Label} · {panel.RoleDisplayName}",
        PanelLabel = panel.Label,
        Visible = panel.IsVisible,
        Locked = panel.IsLocked,
        ZIndex = panel.ZIndex,
        Opacity = 1,
        SourceAssetId = panel.Source.Asset.Id,
        SourceRect = ToSnapshot(panel.SourceRect),
        FrameIndex = panel.FrameIndex,
        LockAspectRatio = panel.IsAspectRatioLocked,
        Transform = new ProjectTransformSnapshot
        {
            X = panel.X,
            Y = panel.Y,
            ScaleX = panel.Width / (double)panel.SourceRect.Width,
            ScaleY = panel.Height / (double)panel.SourceRect.Height,
            RotationQuarterTurns = 0,
        },
        Adjustments = [new ProjectImageAdjustmentSnapshot
        {
            Brightness = panel.Adjustments.Brightness,
            Contrast = panel.Adjustments.Contrast,
            Gamma = panel.Adjustments.Gamma,
            BlackPoint = panel.Adjustments.BlackPoint,
            WhitePoint = panel.Adjustments.WhitePoint,
            Invert = panel.Adjustments.Invert,
            Grayscale = panel.Adjustments.Grayscale,
            Channel = panel.Adjustments.Channel,
        }],
    };

    private static ProjectPixelRectSnapshot ToSnapshot(PixelRect64 rect) => new()
    {
        X = rect.X,
        Y = rect.Y,
        Width = rect.Width,
        Height = rect.Height,
    };

    private static SourceLinkState ParseLinkState(string state) => state.ToLowerInvariant() switch
    {
        "verified" => SourceLinkState.Verified,
        "relocated" => SourceLinkState.Relocated,
        "modified" => SourceLinkState.Modified,
        "missing" => SourceLinkState.Missing,
        _ => SourceLinkState.Unverified,
    };
}
