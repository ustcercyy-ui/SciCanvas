using System.IO;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Plotting;
using LinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using SpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using SpatialMappingKind = SciCanvas.Core.Linking.SpatialMappingKind;
using SpatialMappingOrigin = SciCanvas.Core.Linking.SpatialMappingOrigin;
using SpatialMatrix3x3 = SciCanvas.Core.Linking.SpatialMatrix3x3;
using RegistrationLandmarkPair = SciCanvas.Core.Linking.RegistrationLandmarkPair;
using SpatialPoint = SciCanvas.Core.Linking.SpatialPoint;using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
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
        IReadOnlyList<ProjectAuditEntrySnapshot>? auditTrail = null,
        IReadOnlyList<FigureExportProfile>? exportProfiles = null,
        int minimumEffectiveDpi = 300,
        IReadOnlyList<MultiChannelAssetGroup>? multiChannelGroups = null,
        IReadOnlyList<LinkGroup>? linkGroups = null,
        IReadOnlyList<RoiObject>? rois = null,
        IReadOnlyList<JournalExportPreset>? journalPresetSnapshots = null,
        IReadOnlyList<FontSubstitutionRule>? fontSubstitutions = null,
        IReadOnlyList<TabularDataAsset>? dataAssets = null,
        IReadOnlyList<PlotObject>? plots = null)
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
                CalibrationUnit = panel.CalibrationUnit,
                PhysicalLength = panel.ScaleBarPhysicalLength,
                Unit = panel.ScaleBarUnit,
                Anchor = ToScaleBarAnchorKey(panel.PrimaryScaleBarAnchor),
                ShowLabel = panel.ScaleBarShowLabel,
                AdditionalBars = panel.AdditionalScaleBars.Select(scaleBar => new ProjectAdditionalScaleBarSnapshot
                {
                    Id = scaleBar.Id,
                    PhysicalLength = scaleBar.PhysicalLength,
                    Unit = scaleBar.Unit,
                    Anchor = ToScaleBarAnchorKey(scaleBar.Anchor),
                    ShowLabel = scaleBar.ShowLabel,
                    IsVisible = scaleBar.IsVisible,
                }).ToArray(),
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
            DataAssets = (dataAssets ?? [])
                .Select(TabularDataSnapshotMapper.ToSnapshot)
                .ToArray(),
            Plots = (plots ?? [])
                .Select(PlotSnapshotMapper.ToSnapshot)
                .ToArray(),
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
            ExportProfiles = (exportProfiles ?? FigureExportProfile.BuiltIns)
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
                    BitDepth = profile.BitDepth,
                    ColorMode = "rgb",
                    Resampling = null,
                    JournalPresetId = figure.Template.PublisherProfileId,
                    WriteAuditReport = true,
                    PdfFontStrategy = ToPdfFontStrategyKey(profile.PdfFontStrategy),
                })
                .ToArray(),
            JournalPresetSnapshots = (journalPresetSnapshots ?? [])
                .Select(ToSnapshot)
                .ToArray(),
            FontSubstitutions = (fontSubstitutions ?? [])
                .Select(rule =>
                {
                    rule.EnsureValid();
                    return new ProjectFontSubstitutionSnapshot
                    {
                        Requested = rule.RequestedFontFamily.Trim(),
                        Substitute = rule.SubstituteFontFamily.Trim(),
                    };
                })
                .ToArray(),
            Calibrations = sources.Select(source =>
            {
                SpatialCalibration calibration = source.Calibration.Calibration;
                return new ProjectCalibrationSnapshot
                {
                    SourceAssetId = source.Asset.Id,
                    UnitsPerPixelX = calibration.UnitsPerPixelX,
                    UnitsPerPixelY = calibration.UnitsPerPixelY,
                    Unit = string.IsNullOrWhiteSpace(calibration.Unit) ? "µm" : calibration.Unit,
                    Origin = ToCalibrationOriginKey(calibration.Origin),
                    ReferencePixelLength = calibration.ReferencePixelLength,
                    ReferencePhysicalLength = calibration.ReferencePhysicalLength,
                    ReferenceStartX = source.Calibration.ReferenceStartX,
                    ReferenceStartY = source.Calibration.ReferenceStartY,
                    ReferenceEndX = source.Calibration.ReferenceEndX,
                    ReferenceEndY = source.Calibration.ReferenceEndY,
                };
            }).ToArray(),
            Measurements = sources
                .SelectMany(source => source.Measurements)
                .Select(measurement =>
                {
                    ScientificMeasurement model = measurement.Measurement;
                    return new ProjectMeasurementSnapshot
                    {
                        Id = model.Id,
                        SourceAssetId = model.SourceAssetId,
                        SourceRevision = model.SourceRevision,
                        Kind = ToMeasurementKindKey(model.Kind),
                        X1 = model.PointA.X,
                        Y1 = model.PointA.Y,
                        X2 = model.PointB.X,
                        Y2 = model.PointB.Y,
                        X3 = model.PointC?.X,
                        Y3 = model.PointC?.Y,
                        StrokeColor = measurement.StrokeColor,
                        StrokeWidthPixels = measurement.StrokeWidthPixels,
                        LineStyle = measurement.LineStyle,
                        FillColor = measurement.FillColor,
                        MarkerStrokeColor = measurement.MarkerStrokeColor,
                        MarkerFillColor = measurement.MarkerFillColor,
                        MarkerSizePixels = measurement.MarkerSizePixels,
                        ShowMarkers = measurement.ShowMarkers,
                        ShowLabel = measurement.ShowLabel,
                        LabelColor = measurement.LabelColor,
                        LabelFontFamily = measurement.LabelFontFamily,
                        LabelFontSizePt = measurement.LabelFontSizePt,
                        LabelIsBold = measurement.LabelIsBold,
                        FillOpacityPercent = measurement.FillOpacityPercent,
                        IsVisible = measurement.IsVisible,
                        IsLocked = measurement.IsLocked,
                        Points = model.Kind == ScientificMeasurementKind.Polyline
                            ? model.EffectivePathPoints.Select(point => new ProjectMeasurementPointSnapshot
                            {
                                X = point.X,
                                Y = point.Y,
                            }).ToArray()
                            : [],
                    };
                })
                .ToArray(),
            Analyses = sources
                .SelectMany(source => source.AnalysisResults)
                .Select(ToSnapshot)
                .ToArray(),
            MultiChannelGroups = (multiChannelGroups ?? [])
                .Select(ToSnapshot)
                .ToArray(),
            LinkGroups = (linkGroups ?? [])
                .Select(ToSnapshot)
                .ToArray(),
            Rois = (rois ?? [])
                .Select(ToSnapshot)
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
                        StrokeColor = annotation.StrokeColor,
                        FillColor = annotation.FillColor,
                        FillOpacityPercent = annotation.FillOpacityPercent,
                        TextColor = annotation.TextColor,
                        FontFamily = annotation.FontFamily,
                        FontSizePt = annotation.FontSizePt,
                        StrokeWidthPt = annotation.StrokeWidthPt,
                        IsBold = annotation.IsBold,
                        Visible = annotation.IsVisible,
                        Locked = annotation.IsLocked,
                        ZIndex = annotation.ZIndex,
                    })
                    .ToArray(),
                ScientificObjects = figure.ScientificObjects
                    .OrderBy(scientificObject => scientificObject.ZIndex)
                    .Select(scientificObject => new ProjectFigureScientificObjectSnapshot
                    {
                        Id = scientificObject.Id,
                        Kind = scientificObject.Kind.ToString(),
                        Points = scientificObject.PointsText,
                        Label = scientificObject.Label,
                        StrokeColor = scientificObject.StrokeColor,
                        FillColor = scientificObject.FillColor,
                        FillOpacityPercent = scientificObject.FillOpacityPercent,
                        TextColor = scientificObject.TextColor,
                        FontFamily = scientificObject.FontFamily,
                        FontSizePt = scientificObject.FontSizePt,
                        StrokeWidthPt = scientificObject.StrokeWidthPt,
                        IsBold = scientificObject.IsBold,
                        Visible = scientificObject.IsVisible,
                        Locked = scientificObject.IsLocked,
                        ZIndex = scientificObject.ZIndex,
                        Minimum = scientificObject.Minimum,
                        Maximum = scientificObject.Maximum,
                        Unit = scientificObject.Unit,
                        Colormap = scientificObject.Colormap,
                        ChannelEntries = scientificObject.ChannelEntriesText,
                        ChannelId = scientificObject.ChannelId,
                        ColorbarBindingState = scientificObject.ColorbarBindingState.ToString(),
                        Orientation = scientificObject.ColorbarOrientation.ToString(),
                        Ticks = scientificObject.Colorbar?.Ticks
                            .Select(tick => new ProjectColorbarTickSnapshot
                            {
                                Value = tick.Value,
                                Label = tick.Label,
                            }).ToArray() ?? [],
                        ChannelLegendPadding = scientificObject.ChannelLegendPadding,
                    })
                    .ToArray(),
                RoiProjections = figure.RoiProjections
                    .OrderBy(projection => projection.ZIndex)
                    .Select(projection => new ProjectRoiFigureProjectionSnapshot
                    {
                        Id = projection.Id,
                        RoiId = projection.RoiId,
                        PanelId = projection.PanelId,
                        AssetId = projection.AssetId,
                        SourceRevision = projection.SourceRevision,
                        StyleOverride = ToSnapshot(projection.Projection.StyleOverride),
                        Visible = projection.IsVisible,
                        ZIndex = projection.ZIndex,
                    })
                    .ToArray(),
                MeasurementOverlays = figure.MeasurementOverlays
                    .OrderBy(overlay => overlay.ZIndex)
                    .Select(ToSnapshot)
                    .ToArray(),
                PlotPanels = figure.PlotPanels
                    .OrderBy(panel => panel.ZIndex)
                    .Select(panel => new ProjectFigurePlotPanelSnapshot
                    {
                        Id = panel.Id,
                        PlotId = panel.PlotId,
                        DestinationRect = new ProjectPixelRectSnapshot
                        {
                            X = panel.X,
                            Y = panel.Y,
                            Width = panel.Width,
                            Height = panel.Height,
                        },
                        Label = panel.Label,
                        Visible = panel.IsVisible,
                        Locked = panel.IsLocked,
                        ZIndex = panel.ZIndex,
                        StyleOverride = ToSnapshot(panel.StyleOverride),
                        TypographyOverride = ToSnapshot(panel.TypographyOverride),
                    })
                    .ToArray(),
                GlobalStyle = new ProjectGlobalStyleSnapshot
                {
                    FontFamily = figure.GlobalFontFamily,
                    FontSizePt = figure.GlobalFontSizePt,
                    StrokeWidthPt = figure.GlobalStrokeWidthPt,
                    TextColor = figure.GlobalTextColor,
                    ShapeColor = figure.GlobalShapeColor,
                    ScaleBarColor = figure.GlobalScaleBarColor,
                    PanelLabelFontFamily = figure.PanelLabelFontFamily,
                    PanelLabelFontSizePt = figure.PanelLabelFontSizePt,
                    PanelLabelTextColor = figure.PanelLabelTextColor,
                    PanelLabelIsBold = figure.PanelLabelIsBold,
                    ScaleBarLabelColor = figure.ScaleBarLabelColor,
                    ScaleBarFontFamily = figure.ScaleBarFontFamily,
                    ScaleBarFontSizePt = figure.ScaleBarFontSizePt,
                    ScaleBarLabelIsBold = figure.ScaleBarLabelIsBold,
                    ScaleBarThicknessPt = figure.ScaleBarThicknessPt,
                },
                ScientificColors = figure.ScientificColors
                    .Select(entry => new ProjectScientificColorSnapshot
                    {
                        Id = entry.Id,
                        Name = entry.Name,
                        Color = entry.Color,
                    })
                    .ToArray(),
            },
            Workspace = CreateWorkspaceSnapshot(projectId, title, figure, minimumEffectiveDpi),
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
                            ["plotPanelCount"] = figure.PlotPanels.Count,
                            ["annotationCount"] = figure.Annotations.Count,
                            ["scientificObjectCount"] = figure.ScientificObjects.Count,
                            ["multiChannelGroupCount"] = multiChannelGroups?.Count ?? 0,
                            ["calibrationCount"] = sources.Count(source => source.Calibration.IsCalibrated),
                            ["measurementCount"] = sources.Sum(source => source.Measurements.Count),
                            ["analysisCount"] = sources.Sum(source => source.AnalysisResults.Count),
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
            snapshot.Metadata.FrameCount,
            ToOmeMetadata(snapshot.Metadata.Ome)),
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

    public static SpatialCalibration ToCalibration(ProjectCalibrationSnapshot snapshot) => new(
        snapshot.SourceAssetId,
        snapshot.UnitsPerPixelX,
        snapshot.UnitsPerPixelY,
        snapshot.Unit,
        ParseCalibrationOrigin(snapshot.Origin),
        snapshot.ReferencePixelLength,
        snapshot.ReferencePhysicalLength);

    public static ScientificMeasurement ToMeasurement(ProjectMeasurementSnapshot snapshot) => new(
        snapshot.Id,
        snapshot.SourceAssetId,
        ParseMeasurementKind(snapshot.Kind),
        new MeasurementPoint(snapshot.X1, snapshot.Y1),
        new MeasurementPoint(snapshot.X2, snapshot.Y2),
        snapshot.X3.HasValue && snapshot.Y3.HasValue
            ? new MeasurementPoint(snapshot.X3.Value, snapshot.Y3.Value)
            : null,
        Name: null,
        PathPoints: snapshot.Points.Select(point => new MeasurementPoint(point.X, point.Y)).ToArray(),
        SourceRevision: snapshot.SourceRevision);

    public static MeasurementOverlayObject ToMeasurementOverlay(
        ProjectMeasurementOverlaySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(snapshot.SourceGeometry);
        ScientificMeasurement geometry = ToMeasurement(snapshot.SourceGeometry);
        if (snapshot.Id == Guid.Empty || snapshot.MeasurementId == Guid.Empty ||
            snapshot.PanelId == Guid.Empty || geometry.Id != snapshot.MeasurementId ||
            geometry.SourceAssetId == Guid.Empty || geometry.SourceRevision < 1)
        {
            throw new InvalidDataException("Measurement Overlay 快照缺少有效的对象、测量、面板或源修订标识。");
        }

        ProjectMeasurementOverlayStyleSnapshot style = snapshot.Style ??
            throw new InvalidDataException("Measurement Overlay 快照缺少样式。");
        var visualStyle = new FigureMeasurementOverlayStyle(
            style.StrokeColor,
            style.StrokeWidthPixels,
            style.LineStyle,
            style.FillColor,
            style.FillOpacityPercent,
            style.MarkerStrokeColor,
            style.MarkerFillColor,
            style.MarkerSizePixels,
            style.ShowMarkers,
            style.LabelColor,
            style.LabelFontFamily,
            style.LabelFontSizePt,
            style.LabelIsBold,
            style.ShowLabel);
        visualStyle.EnsureValid();
        FigureMeasurementCalibrationRelationship? calibration = snapshot.CalibrationRelationship is { } savedCalibration
            ? new FigureMeasurementCalibrationRelationship(
                savedCalibration.SourceAssetId,
                savedCalibration.SourceRevision,
                savedCalibration.UnitsPerPixelX,
                savedCalibration.UnitsPerPixelY,
                savedCalibration.Unit)
            : null;
        return new MeasurementOverlayObject
        {
            Id = snapshot.Id,
            AssetId = geometry.SourceAssetId,
            PanelId = snapshot.PanelId,
            SourceRevision = geometry.SourceRevision,
            MeasurementId = snapshot.MeasurementId,
            SourceGeometry = geometry,
            CalibrationRelationship = calibration,
            Style = visualStyle,
            StyleOverride = CreateMeasurementStyleOverride(visualStyle),
            LabelOverride = snapshot.LabelOverride,
            IsVisible = snapshot.IsVisible,
            ZIndex = snapshot.ZIndex,
        };
    }

    private static ProjectMeasurementOverlaySnapshot ToSnapshot(
        FigureMeasurementOverlayViewModel overlay)
    {
        MeasurementOverlayObject scientificObject = overlay.ScientificObject;
        ScientificMeasurement geometry = scientificObject.SourceGeometry;
        FigureMeasurementOverlayStyle style = scientificObject.Style;
        return new ProjectMeasurementOverlaySnapshot
        {
            Id = scientificObject.Id,
            MeasurementId = scientificObject.MeasurementId,
            PanelId = scientificObject.PanelId ?? Guid.Empty,
            SourceGeometry = new ProjectMeasurementSnapshot
            {
                Id = geometry.Id,
                SourceAssetId = geometry.SourceAssetId,
                SourceRevision = geometry.SourceRevision,
                Kind = ToMeasurementKindKey(geometry.Kind),
                X1 = geometry.PointA.X,
                Y1 = geometry.PointA.Y,
                X2 = geometry.PointB.X,
                Y2 = geometry.PointB.Y,
                X3 = geometry.PointC?.X,
                Y3 = geometry.PointC?.Y,
                Points = geometry.Kind == ScientificMeasurementKind.Polyline
                    ? geometry.EffectivePathPoints.Select(point => new ProjectMeasurementPointSnapshot
                    {
                        X = point.X,
                        Y = point.Y,
                    }).ToArray()
                    : [],
            },
            CalibrationRelationship = scientificObject.CalibrationRelationship is { } calibration
                ? new ProjectMeasurementOverlayCalibrationSnapshot
                {
                    SourceAssetId = calibration.SourceAssetId,
                    SourceRevision = calibration.SourceRevision,
                    UnitsPerPixelX = calibration.UnitsPerPixelX,
                    UnitsPerPixelY = calibration.UnitsPerPixelY,
                    Unit = calibration.Unit,
                }
                : null,
            Style = new ProjectMeasurementOverlayStyleSnapshot
            {
                StrokeColor = style.StrokeColor,
                StrokeWidthPixels = style.StrokeWidthPixels,
                LineStyle = style.LineStyle,
                FillColor = style.FillColor,
                FillOpacityPercent = style.FillOpacityPercent,
                MarkerStrokeColor = style.MarkerStrokeColor,
                MarkerFillColor = style.MarkerFillColor,
                MarkerSizePixels = style.MarkerSizePixels,
                ShowMarkers = style.ShowMarkers,
                LabelColor = style.LabelColor,
                LabelFontFamily = style.LabelFontFamily,
                LabelFontSizePt = style.LabelFontSizePt,
                LabelIsBold = style.LabelIsBold,
                ShowLabel = style.ShowLabel,
            },
            LabelOverride = scientificObject.LabelOverride,
            IsVisible = scientificObject.IsVisible,
            ZIndex = scientificObject.ZIndex,
        };
    }

    private static StyleOverride CreateMeasurementStyleOverride(
        FigureMeasurementOverlayStyle style) => new(
        Measurement: new MeasurementStyle(
            new ShapeStyle(
                style.StrokeColor,
                style.FillColor,
                style.FillOpacityPercent,
                Math.Clamp(style.StrokeWidthPixels * 72.0 / 96.0, 0.25, 10)),
            new MarkerStyle(style.MarkerStrokeColor, style.MarkerFillColor, style.MarkerSizePixels),
            new TextStyle(style.LabelFontFamily, style.LabelFontSizePt, style.LabelIsBold, style.LabelColor),
            style.LineStyle,
            style.ShowMarkers,
            style.ShowLabel));
    public static ScientificImageAnalysisResult ToAnalysis(ProjectScientificAnalysisSnapshot snapshot) =>
        snapshot.Kind.ToLowerInvariant() switch
        {
            "roistatistics" => ToRoiStatistics(snapshot),
            "lineprofile" => ToLineProfile(snapshot),
            "particleanalysis" => ToParticleAnalysis(snapshot),
            _ => throw new InvalidDataException($"未知图像分析类型：{snapshot.Kind}"),
        };

    public static JournalExportPreset ToJournalPreset(ProjectJournalPresetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!string.Equals(snapshot.FormatVersion, JournalPresetPortability.CurrentFormatVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"不支持的 journal preset snapshot formatVersion：{snapshot.FormatVersion}");
        }

        JournalPresetSourceMetadata? source = snapshot.SourceName is null && snapshot.SourceUrl is null &&
                                              snapshot.SourceUpdatedAt is null && snapshot.PresetCreatedAt is null &&
                                              snapshot.Author is null && snapshot.Organization is null
            ? null
            : new JournalPresetSourceMetadata(
                snapshot.SourceName,
                snapshot.SourceUrl,
                snapshot.SourceUpdatedAt,
                snapshot.PresetCreatedAt,
                snapshot.Author,
                snapshot.Organization);
        return new JournalExportPreset(
            snapshot.Id,
            snapshot.Name,
            snapshot.FigureWidthMm,
            snapshot.FigureHeightMm,
            snapshot.MinimumDpi,
            snapshot.PreferredFormat,
            snapshot.AllowedFormats,
            snapshot.ColorMode,
            snapshot.MaximumFileSizeMb,
            snapshot.Description,
            snapshot.FontRecommendations,
            snapshot.MinimumLineWidthPt,
            snapshot.Notes,
            source);
    }

    public static FontSubstitutionRule ToFontSubstitution(ProjectFontSubstitutionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new FontSubstitutionRule(snapshot.Requested, snapshot.Substitute).EnsureValid();
    }

    private static ProjectJournalPresetSnapshot ToSnapshot(JournalExportPreset preset) => new()
    {
        FormatVersion = JournalPresetPortability.CurrentFormatVersion,
        Id = preset.Id,
        Name = preset.Name,
        Description = preset.Description,
        FigureWidthMm = preset.FigureWidthMm,
        FigureHeightMm = preset.FigureHeightMm,
        MinimumDpi = preset.MinimumDpi,
        PreferredFormat = preset.PreferredFormat,
        AllowedFormats = preset.AllowedFormats.ToArray(),
        ColorMode = preset.ColorMode,
        MaximumFileSizeMb = preset.MaximumFileSizeMb,
        FontRecommendations = preset.FontRecommendations.ToArray(),
        MinimumLineWidthPt = preset.MinimumLineWidthPt,
        Notes = preset.Notes,
        SourceName = preset.SourceMetadata?.SourceName,
        SourceUrl = preset.SourceMetadata?.SourceUrl,
        SourceUpdatedAt = preset.SourceMetadata?.SourceUpdatedAt,
        PresetCreatedAt = preset.SourceMetadata?.CreatedAt,
        Author = preset.SourceMetadata?.Author,
        Organization = preset.SourceMetadata?.Organization,
    };

    private static string ToPdfFontStrategyKey(PdfFontStrategy strategy) => strategy switch
    {
        PdfFontStrategy.OutlineText => "outlineText",
        PdfFontStrategy.EmbedSubsetWhenPermitted => "embedSubsetWhenPermitted",
        PdfFontStrategy.PreferEmbeddedWithOutlineFallback => "preferEmbeddedWithOutlineFallback",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy)),
    };

    private static Guid GetStableExportProfileId(string profileId)
    {
        Guid? builtIn = profileId switch
        {
            "main-tiff" => Guid.Parse("4757F9DE-FE43-47F6-9675-690BE0A431E0"),
            "supplement-png" => Guid.Parse("B7D1C6D5-4B43-4C36-9A6F-7F6F2F4D5E22"),
            "thumbnail-png" => Guid.Parse("F6A3B8E8-9B8D-4BA0-A9D9-5AF1BA58C44F"),
            _ => null,
        };
        if (builtIn is Guid known || Guid.TryParse(profileId, out known))
        {
            return known;
        }

        throw new InvalidDataException($"导出预设 ID 不是有效 GUID：{profileId}");
    }
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
                Ome = ToSnapshot(metadata.Ome),
            },
            LinkState = source.LinkState.ToString().ToLowerInvariant(),
            AssetKind = InferAssetKind(source.DisplayName),
            Tags = [],
            SourceRevision = item.SourceRevision,
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
        CropLinkGroupId = panel.CropLinkGroupId,
        CompositeGroupId = panel.CompositeGroupId,
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
        NormalizedCrop = new ProjectNormalizedRectSnapshot
        {
            X = panel.NormalizedCrop.X,
            Y = panel.NormalizedCrop.Y,
            Width = panel.NormalizedCrop.Width,
            Height = panel.NormalizedCrop.Height,
        },
        FrameMm = new ProjectFigureRectMmSnapshot
        {
            X = panel.FrameMm.X,
            Y = panel.FrameMm.Y,
            Width = panel.FrameMm.Width,
            Height = panel.FrameMm.Height,
        },
        FitMode = panel.FitMode.ToString().ToLowerInvariant(),
        RotationDegrees = panel.RotationDegrees,
        ScientificValidity = new ProjectScientificValiditySnapshot
        {
            State = panel.ReplacementValidity.State.ToString().ToLowerInvariant(),
            Reasons = panel.ReplacementValidity.Reasons,
        },
        StyleOverride = ToSnapshot(panel.StyleOverride),
    };

    private static ProjectPanelStyleOverrideSnapshot? ToSnapshot(StyleOverride? style) =>
        style is null || style.IsEmpty
            ? null
            : new ProjectPanelStyleOverrideSnapshot
            {
                PanelLabel = ToSnapshot(style.PanelLabel),
                Annotation = ToSnapshot(style.Annotation),
                ScaleBarText = ToSnapshot(style.ScaleBarText),
                ScaleBar = style.ScaleBar is null
                    ? null
                    : new ProjectScaleBarStyleSnapshot
                    {
                        DefaultPosition = style.ScaleBar.DefaultPosition switch
                        {
                            ScaleBarAnchor.BottomLeft => "bottomLeft",
                            ScaleBarAnchor.TopLeft => "topLeft",
                            ScaleBarAnchor.TopRight => "topRight",
                            ScaleBarAnchor.Custom => "custom",
                            _ => "bottomRight",
                        },
                        BarThicknessPt = style.ScaleBar.BarThicknessPt,
                        Color = style.ScaleBar.Color,
                    },
                Shapes = style.Shapes is null
                    ? null
                    : new ProjectShapeStyleSnapshot
                    {
                        StrokeColor = style.Shapes.StrokeColor,
                        FillColor = style.Shapes.FillColor,
                        FillOpacityPercent = style.Shapes.FillOpacityPercent,
                        StrokeWidthPt = style.Shapes.StrokeWidthPt,
                    },
            };

    private static ProjectTextStyleSnapshot? ToSnapshot(TextStyle? style) => style is null
        ? null
        : new ProjectTextStyleSnapshot
        {
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            IsBold = style.IsBold,
            Color = style.Color,
        };

    private static ProjectPlotTypographyOverrideSnapshot? ToSnapshot(
        FigurePlotTypographyOverride? typography) => typography is null || typography.IsEmpty
        ? null
        : new ProjectPlotTypographyOverrideSnapshot
        {
            Axis = ToSnapshot(typography.Axis),
            Tick = ToSnapshot(typography.Tick),
            Legend = ToSnapshot(typography.Legend),
            Annotation = ToSnapshot(typography.Annotation),
        };

    internal static StyleOverride? ToStyleOverride(ProjectPanelStyleOverrideSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var result = new StyleOverride(
            PanelLabel: ToTextStyle(snapshot.PanelLabel),
            Annotation: ToTextStyle(snapshot.Annotation),
            ScaleBarText: ToTextStyle(snapshot.ScaleBarText),
            ScaleBar: snapshot.ScaleBar is null
                ? null
                : new ScaleBarStyle(
                    snapshot.ScaleBar.DefaultPosition.ToLowerInvariant() switch
                    {
                        "bottomleft" => ScaleBarAnchor.BottomLeft,
                        "topleft" => ScaleBarAnchor.TopLeft,
                        "topright" => ScaleBarAnchor.TopRight,
                        "custom" => ScaleBarAnchor.Custom,
                        _ => ScaleBarAnchor.BottomRight,
                    },
                    snapshot.ScaleBar.BarThicknessPt,
                    snapshot.ScaleBar.Color),
            Shapes: snapshot.Shapes is null
                ? null
                : new ShapeStyle(
                    snapshot.Shapes.StrokeColor,
                    snapshot.Shapes.FillColor,
                    snapshot.Shapes.FillOpacityPercent,
                    snapshot.Shapes.StrokeWidthPt));
        result.EnsureValid();
        return result.IsEmpty ? null : result;
    }

    private static TextStyle? ToTextStyle(ProjectTextStyleSnapshot? snapshot) => snapshot is null
        ? null
        : new TextStyle(snapshot.FontFamily, snapshot.FontSizePt, snapshot.IsBold, snapshot.Color);

    internal static FigurePlotTypographyOverride? ToPlotTypographyOverride(
        ProjectPlotTypographyOverrideSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        var result = new FigurePlotTypographyOverride(
            ToTextStyle(snapshot.Axis),
            ToTextStyle(snapshot.Tick),
            ToTextStyle(snapshot.Legend),
            ToTextStyle(snapshot.Annotation));
        result.EnsureValid();
        return result.IsEmpty ? null : result;
    }

    private static ProjectScientificAnalysisSnapshot ToSnapshot(
        ScientificImageAnalysisResult result) => result switch
    {
        RoiStatisticsResult roi => new ProjectScientificAnalysisSnapshot
        {
            Id = roi.Id,
            SourceAssetId = roi.SourceAssetId,
            SourceRevision = roi.SourceRevision,
            Kind = "roiStatistics",
            FrameIndex = roi.FrameIndex,
            Channel = ToAnalysisChannelKey(roi.Channel),
            AnalyzerId = roi.AnalyzerId,
            AnalyzedAt = roi.AnalyzedAt,
            Validity = ToSnapshot(roi.Validity),
            SourceBitDepth = roi.SourceBitDepth,
            Region = ToSnapshot(roi.Region),
            RoiId = roi.RoiId,
            ScientificChannelId = roi.ScientificChannelId,
            LinkGroupId = roi.LinkGroupId,
            MappingId = roi.MappingId,
            PolygonMask = roi.PolygonMask.Select(point => new ProjectMeasurementPointSnapshot
            {
                X = point.X,
                Y = point.Y,
            }).ToArray(),
            ClippedToImage = roi.ClippedToImage,
            CoverageFraction = roi.CoverageFraction,
            PixelCount = roi.PixelCount,
            Minimum = roi.Minimum,
            Maximum = roi.Maximum,
            Mean = roi.Mean,
            StandardDeviation = roi.StandardDeviation,
            IntegratedIntensity = roi.IntegratedIntensity,
            Histogram = roi.Histogram.Bins.Select(bin => new ProjectIntensityHistogramBinSnapshot
            {
                LowerBound = bin.LowerBound,
                UpperBound = bin.UpperBound,
                Count = bin.Count,
            }).ToArray(),
        },
        IntensityProfileResult profile => new ProjectScientificAnalysisSnapshot
        {
            Id = profile.Id,
            SourceAssetId = profile.SourceAssetId,
            SourceRevision = profile.SourceRevision,
            Kind = "lineProfile",
            FrameIndex = profile.FrameIndex,
            Channel = ToAnalysisChannelKey(profile.Channel),
            AnalyzerId = profile.AnalyzerId,
            AnalyzedAt = profile.AnalyzedAt,
            Validity = ToSnapshot(profile.Validity),
            SourceBitDepth = profile.SourceBitDepth,
            DistanceUnit = profile.DistanceUnit,
            Samples = profile.Samples.Select(sample => new ProjectIntensityProfileSampleSnapshot
            {
                Index = sample.Index,
                PixelX = sample.PixelX,
                PixelY = sample.PixelY,
                DistancePixels = sample.DistancePixels,
                PhysicalDistance = sample.PhysicalDistance,
                RawIntensity = sample.RawIntensity,
                NormalizedIntensity = sample.NormalizedIntensity,
            }).ToArray(),
        },
        AssistedRegionAnalysisResult particles => new ProjectScientificAnalysisSnapshot
        {
            Id = particles.Id,
            SourceAssetId = particles.SourceAssetId,
            SourceRevision = particles.SourceRevision,
            Kind = "particleAnalysis",
            FrameIndex = particles.FrameIndex,
            Channel = ToAnalysisChannelKey(particles.Channel),
            AnalyzerId = particles.AnalyzerId,
            AnalyzedAt = particles.AnalyzedAt,
            Validity = ToSnapshot(particles.Validity),
            SourceBitDepth = particles.SourceBitDepth,
            Region = ToSnapshot(particles.Options.RegionOfInterest),
            AnalysisMode = ToAssistedRegionModeKey(particles.Options.Mode),
            UseAutomaticThreshold = particles.Options.UseAutomaticThreshold,
            ThresholdNormalized = particles.Options.ThresholdNormalized,
            AppliedThresholdNormalized = particles.AppliedThresholdNormalized,
            MinimumAreaPixels = particles.Options.MinimumAreaPixels,
            MaximumCandidates = null,
            AnalysisMaxPixels = particles.ResourcePolicy.MaxPixels,
            AnalysisMaxComponentsSafety = particles.ResourcePolicy.MaxComponentsSafety,
            AnalysisMaxBoundaryPoints = particles.ResourcePolicy.MaxBoundaryPoints,
            AnalysisMemoryBudgetBytes = particles.ResourcePolicy.MemoryBudgetBytes,
            ForegroundPixelCount = particles.ForegroundPixelCount,
            TotalPixelCount = particles.TotalPixelCount,
            Particles = particles.Candidates.Select(candidate => new ProjectParticleSnapshot
            {
                Id = candidate.Id,
                Bounds = ToSnapshot(candidate.Bounds),
                CentroidX = candidate.CentroidX,
                CentroidY = candidate.CentroidY,
                AreaPixels = candidate.AreaPixels,
                PerimeterPixels = candidate.PerimeterPixels,
                MeanIntensity = candidate.MeanIntensity,
                RawMeanIntensity = candidate.RawMeanIntensity,
                AspectRatio = candidate.AspectRatio,
                FeretMaximumPixels = candidate.FeretMaximumPixels,
                FeretMinimumPixels = candidate.FeretMinimumPixels,
            }).ToArray(),
        },
        _ => throw new InvalidDataException($"未知图像分析类型：{result.GetType().Name}"),
    };

    private static ProjectScientificValiditySnapshot ToSnapshot(AnalysisResultValidity validity) => new()
    {
        State = validity.State.ToString().ToLowerInvariant(),
        Reasons = validity.Reasons,
    };

    private static ProjectScientificValiditySnapshot ToSnapshot(ScientificValidity validity) => new()
    {
        State = validity.State.ToString().ToLowerInvariant(),
        Reasons = validity.Reasons,
    };

    private static RoiStatisticsResult ToRoiStatistics(ProjectScientificAnalysisSnapshot snapshot)
    {
        ProjectPixelRectSnapshot region = snapshot.Region ??
            throw new InvalidDataException("ROI 统计分析缺少源像素区域。");
        IntensityHistogramBin[] bins = snapshot.Histogram
            .Select(bin => new IntensityHistogramBin(bin.LowerBound, bin.UpperBound, bin.Count))
            .ToArray();
        long pixelCount = snapshot.PixelCount ?? bins.Sum(bin => bin.Count);
        return new RoiStatisticsResult
        {
            Id = snapshot.Id,
            SourceAssetId = snapshot.SourceAssetId,
            SourceRevision = snapshot.SourceRevision,
            FrameIndex = snapshot.FrameIndex,
            Channel = ParseAnalysisChannel(snapshot.Channel),
            AnalyzerId = snapshot.AnalyzerId,
            AnalyzedAt = snapshot.AnalyzedAt,
            Validity = ToAnalysisValidity(snapshot.Validity),
            SourceBitDepth = snapshot.SourceBitDepth,
            Region = ToPixelRect(region),
            RoiId = snapshot.RoiId,
            ScientificChannelId = snapshot.ScientificChannelId,
            LinkGroupId = snapshot.LinkGroupId,
            MappingId = snapshot.MappingId,
            PolygonMask = (snapshot.PolygonMask ?? [])
                .Select(point => new MeasurementPoint(point.X, point.Y))
                .ToArray(),
            ClippedToImage = snapshot.ClippedToImage,
            CoverageFraction = snapshot.CoverageFraction,
            PixelCount = pixelCount,
            Minimum = snapshot.Minimum ?? 0,
            Maximum = snapshot.Maximum ?? 0,
            Mean = snapshot.Mean ?? 0,
            StandardDeviation = snapshot.StandardDeviation ?? 0,
            IntegratedIntensity = snapshot.IntegratedIntensity ?? 0,
            Histogram = new IntensityHistogram(
                bins,
                pixelCount,
                snapshot.Minimum ?? 0,
                snapshot.Maximum ?? 0),
        };
    }

    private static IntensityProfileResult ToLineProfile(ProjectScientificAnalysisSnapshot snapshot) =>
        new(
            snapshot.Samples.Select(sample => new IntensityProfileSample(
                sample.Index,
                sample.PixelX,
                sample.PixelY,
                sample.DistancePixels,
                sample.PhysicalDistance,
                sample.NormalizedIntensity)
            {
                RawIntensity = sample.RawIntensity,
            }).ToArray(),
            snapshot.DistanceUnit ?? "px",
            snapshot.SourceBitDepth)
        {
            Id = snapshot.Id,
            SourceAssetId = snapshot.SourceAssetId,
            SourceRevision = snapshot.SourceRevision,
            FrameIndex = snapshot.FrameIndex,
            Channel = ParseAnalysisChannel(snapshot.Channel),
            AnalyzerId = snapshot.AnalyzerId,
            AnalyzedAt = snapshot.AnalyzedAt,
            Validity = ToAnalysisValidity(snapshot.Validity),
        };

    private static AssistedRegionAnalysisResult ToParticleAnalysis(
        ProjectScientificAnalysisSnapshot snapshot)
    {
        ProjectPixelRectSnapshot region = snapshot.Region ??
            throw new InvalidDataException("颗粒分析缺少源像素 ROI。");
        var options = new AssistedRegionAnalysisOptions(
            ParseAssistedRegionMode(snapshot.AnalysisMode),
            ToPixelRect(region),
            snapshot.UseAutomaticThreshold ?? true,
            snapshot.ThresholdNormalized ?? 0.5,
            snapshot.MinimumAreaPixels ?? 16);
        AnalysisResourcePolicy defaults = AnalysisResourcePolicy.Default;
        var resourcePolicy = new AnalysisResourcePolicy(
            snapshot.AnalysisMaxPixels ?? defaults.MaxPixels,
            snapshot.AnalysisMaxComponentsSafety ?? defaults.MaxComponentsSafety,
            snapshot.AnalysisMaxBoundaryPoints ?? defaults.MaxBoundaryPoints,
            snapshot.AnalysisMemoryBudgetBytes ?? defaults.MemoryBudgetBytes);
        AssistedRegionCandidate[] candidates = snapshot.Particles.Select(particle =>
            new AssistedRegionCandidate(
                particle.Id,
                ToPixelRect(particle.Bounds),
                particle.CentroidX,
                particle.CentroidY,
                particle.AreaPixels,
                particle.PerimeterPixels,
                particle.MeanIntensity,
                particle.AspectRatio)
            {
                RawMeanIntensity = particle.RawMeanIntensity,
                FeretMaximumPixels = particle.FeretMaximumPixels,
                FeretMinimumPixels = particle.FeretMinimumPixels,
            }).ToArray();
        return new AssistedRegionAnalysisResult(
            options,
            candidates,
            snapshot.AppliedThresholdNormalized ?? 0,
            snapshot.ForegroundPixelCount ?? 0,
            snapshot.TotalPixelCount ?? region.Width * region.Height)
        {
            Id = snapshot.Id,
            SourceAssetId = snapshot.SourceAssetId,
            SourceRevision = snapshot.SourceRevision,
            FrameIndex = snapshot.FrameIndex,
            Channel = ParseAnalysisChannel(snapshot.Channel),
            AnalyzerId = snapshot.AnalyzerId,
            AnalyzedAt = snapshot.AnalyzedAt,
            Validity = ToAnalysisValidity(snapshot.Validity),
            SourceBitDepth = snapshot.SourceBitDepth,
            ResourcePolicy = resourcePolicy,
        };
    }

    private static AnalysisResultValidity ToAnalysisValidity(ProjectScientificValiditySnapshot validity)
    {
        string[] reasons = validity.Reasons.ToArray();
        return validity.State.ToLowerInvariant() switch
        {
            "reviewrequired" => AnalysisResultValidity.ReviewRequired(reasons),
            "invalid" => AnalysisResultValidity.Invalid(reasons),
            _ => AnalysisResultValidity.Valid,
        };
    }

    public static string ToScaleBarAnchorKey(ScaleBarAnchor anchor) => anchor switch
    {
        ScaleBarAnchor.BottomLeft => "bottomLeft",
        ScaleBarAnchor.TopLeft => "topLeft",
        ScaleBarAnchor.TopRight => "topRight",
        ScaleBarAnchor.Custom => "custom",
        _ => "bottomRight",
    };

    public static ScaleBarAnchor ParseScaleBarAnchor(string? anchor) => anchor?.ToLowerInvariant() switch
    {
        "bottomleft" => ScaleBarAnchor.BottomLeft,
        "topleft" => ScaleBarAnchor.TopLeft,
        "topright" => ScaleBarAnchor.TopRight,
        "custom" => ScaleBarAnchor.Custom,
        _ => ScaleBarAnchor.BottomRight,
    };
    public static PanelFitMode ParsePanelFitMode(string? fitMode) => fitMode?.ToLowerInvariant() switch
    {
        "fit" => PanelFitMode.Fit,
        "fill" => PanelFitMode.Fill,
        _ => PanelFitMode.Manual,
    };

    public static ScientificValidity ToScientificValidity(ProjectScientificValiditySnapshot snapshot)
    {
        string[] reasons = snapshot.Reasons.ToArray();
        return snapshot.State.ToLowerInvariant() switch
        {
            "warning" => ScientificValidity.Warning(reasons),
            "invalid" => ScientificValidity.Invalid(reasons),
            "reviewrequired" => ScientificValidity.ReviewRequired(reasons),
            _ => ScientificValidity.Valid,
        };
    }

    private static ProjectWorkspaceSnapshot CreateWorkspaceSnapshot(
        Guid projectId,
        string title,
        FigureCanvasViewModel figure,
        int minimumEffectiveDpi)
    {
        Guid figureId = CreateStableFigureId(projectId);
        return new ProjectWorkspaceSnapshot
        {
            ActiveFigureId = figureId,
            MinimumEffectiveDpi = Math.Clamp(minimumEffectiveDpi, 1, 2400),
            Figures =
            [
                new ProjectFigureSnapshot
                {
                    Id = figureId,
                    Name = string.IsNullOrWhiteSpace(title) ? "Figure 1" : title,
                    WidthMm = figure.CanvasWidth / (double)figure.Dpi * 25.4,
                    HeightMm = figure.CanvasHeight / (double)figure.Dpi * 25.4,
                    Dpi = figure.Dpi,
                    TemplateId = figure.Template.Id,
                    LayerIds = figure.Panels.OrderBy(panel => panel.ZIndex).Select(panel => panel.Id).ToArray(),
                },
            ],
        };
    }

    private static Guid CreateStableFigureId(Guid projectId)
    {
        byte[] bytes = projectId.ToByteArray();
        bytes[0] ^= 0x53;
        bytes[1] ^= 0x43;
        bytes[2] ^= 0x49;
        return new Guid(bytes);
    }

    private static string InferAssetKind(string displayName)
    {
        string value = displayName.ToLowerInvariant();
        if (value.Contains("sem")) return "sem";
        if (value.Contains("stem")) return "stem";
        if (value.Contains("tem")) return "tem";
        if (value.Contains("ebsd")) return "ebsd";
        if (value.Contains("eds") || value.Contains("edx")) return "eds";
        if (value.Contains("afm")) return "afm";
        if (value.Contains("xrd")) return "xrd";
        if (value.Contains("graph") || value.Contains("plot")) return "graph";
        if (value.Contains("schematic")) return "schematic";
        return "other";
    }

    private static ProjectPixelRectSnapshot ToSnapshot(PixelRect64 rect) => new()
    {
        X = rect.X,
        Y = rect.Y,
        Width = rect.Width,
        Height = rect.Height,
    };

    private static ProjectOmeMetadataSnapshot? ToSnapshot(OmeImageMetadata? ome) => ome is null
        ? null
        : new ProjectOmeMetadataSnapshot
        {
            DimensionOrder = ome.DimensionOrder,
            PixelType = ome.PixelType,
            SizeZ = ome.SizeZ,
            SizeC = ome.SizeC,
            SizeT = ome.SizeT,
            PhysicalSizeX = ome.PhysicalSizeX,
            PhysicalSizeY = ome.PhysicalSizeY,
            PhysicalSizeZ = ome.PhysicalSizeZ,
            PhysicalSizeXUnit = ome.PhysicalSizeXUnit,
            PhysicalSizeYUnit = ome.PhysicalSizeYUnit,
            PhysicalSizeZUnit = ome.PhysicalSizeZUnit,
            TimeIncrement = ome.TimeIncrement,
            TimeIncrementUnit = ome.TimeIncrementUnit,
            ChannelNames = ome.ChannelNames,
            XmlSha256 = ome.XmlSha256,
        };

    private static OmeImageMetadata? ToOmeMetadata(ProjectOmeMetadataSnapshot? ome) => ome is null
        ? null
        : new OmeImageMetadata(
            ome.DimensionOrder,
            ome.PixelType,
            ome.SizeZ,
            ome.SizeC,
            ome.SizeT,
            ome.PhysicalSizeX,
            ome.PhysicalSizeY,
            ome.PhysicalSizeZ,
            ome.PhysicalSizeXUnit,
            ome.PhysicalSizeYUnit,
            ome.PhysicalSizeZUnit,
            ome.TimeIncrement,
            ome.TimeIncrementUnit,
            ome.ChannelNames,
            ome.XmlSha256);

    private static SourceLinkState ParseLinkState(string state) => state.ToLowerInvariant() switch
    {
        "verified" => SourceLinkState.Verified,
        "relocated" => SourceLinkState.Relocated,
        "modified" => SourceLinkState.Modified,
        "missing" => SourceLinkState.Missing,
        _ => SourceLinkState.Unverified,
    };

    internal static ProjectMultiChannelAssetGroupSnapshot ToSnapshot(MultiChannelAssetGroup group)
    {
        group.EnsureValid();
        return new ProjectMultiChannelAssetGroupSnapshot
        {
            Id = group.Id,
            Name = group.Name,
            ReferenceAssetId = group.ReferenceAssetId,
            SameFieldOfViewConfirmed = group.SameFieldOfViewConfirmed,
            Members = group.Members.Select(member => new ProjectChannelGroupMemberSnapshot
            {
                ChannelId = member.ChannelId,
                AssetId = member.AssetId,
                SourceRevision = member.SourceRevision,
                FrameIndex = member.FrameIndex,
                PlaneSelector = ToSnapshot(member.PlaneSelector),
                Name = member.Name,
                Role = member.Role,
                Color = member.Color,
                NameOrigin = member.NameOrigin switch
                {
                    ChannelNameOrigin.User => "user",
                    ChannelNameOrigin.FilenameSuggestion => "filenameSuggestion",
                    ChannelNameOrigin.OmeMetadata => "omeMetadata",
                    _ => throw new InvalidDataException("未知通道名称来源。"),
                },
                IsNameConfirmed = member.IsNameConfirmed,
                Visible = member.DisplaySettings.Visible,
                Opacity = member.DisplaySettings.Opacity,
                DisplayMinimum = member.DisplaySettings.DisplayMinimum,
                DisplayMaximum = member.DisplaySettings.DisplayMaximum,
                Gamma = member.DisplaySettings.Gamma,
                Invert = member.DisplaySettings.Invert,
                Colormap = member.DisplaySettings.Colormap,
            }).ToArray(),
        };
    }

    internal static MultiChannelAssetGroup ToMultiChannelAssetGroup(
        ProjectMultiChannelAssetGroupSnapshot snapshot,
        IReadOnlyDictionary<Guid, ProjectSourceSnapshot>? sources = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new MultiChannelAssetGroup(
            snapshot.Id,
            snapshot.Name,
            snapshot.ReferenceAssetId,
            snapshot.Members.Select(member =>
            {
                var display = new ChannelDisplaySettings(
                    member.ChannelId,
                    member.Visible,
                    member.Color,
                    member.Opacity,
                    member.DisplayMinimum,
                    member.DisplayMaximum,
                    member.Gamma,
                    member.Invert,
                    member.Colormap);
                return new ChannelGroupMember(
                    member.ChannelId,
                    member.AssetId,
                    ToChannelPlaneSelector(member, snapshot, sources),
                    member.Name,
                    member.Role,
                    member.Color,
                    member.NameOrigin?.ToLowerInvariant() switch
                    {
                        "user" => ChannelNameOrigin.User,
                        "filenamesuggestion" => ChannelNameOrigin.FilenameSuggestion,
                        "omemetadata" => ChannelNameOrigin.OmeMetadata,
                        _ => throw new InvalidDataException("工程包含未知的通道名称来源。"),
                    },
                    member.IsNameConfirmed,
                    display)
                {
                    SourceRevision = member.SourceRevision,
                };
            }).ToArray(),
            snapshot.SameFieldOfViewConfirmed).EnsureValid();
    }

    private static ProjectChannelPlaneSelectorSnapshot ToSnapshot(ChannelPlaneSelector selector)
    {
        selector.EnsureValid();
        return new ProjectChannelPlaneSelectorSnapshot
        {
            SourceKind = selector.SourceKind switch
            {
                ScientificChannelSourceKind.ExternalAsset => "externalAsset",
                ScientificChannelSourceKind.InterleavedComponent => "interleavedComponent",
                ScientificChannelSourceKind.FramePlane => "framePlane",
                _ => throw new InvalidDataException("未知 scientific plane source kind。"),
            },
            FrameIndex = selector.FrameIndex,
            ComponentIndex = selector.ComponentIndex,
            ZIndex = selector.ZIndex,
            CIndex = selector.CIndex,
            TIndex = selector.TIndex,
        };
    }

    private static ChannelPlaneSelector ToChannelPlaneSelector(
        ProjectChannelGroupMemberSnapshot member,
        ProjectMultiChannelAssetGroupSnapshot group,
        IReadOnlyDictionary<Guid, ProjectSourceSnapshot>? sources)
    {
        if (member.PlaneSelector is { } selector)
        {
            return new ChannelPlaneSelector(
                ParseChannelSourceKind(selector.SourceKind),
                selector.FrameIndex,
                selector.ComponentIndex,
                selector.ZIndex,
                selector.CIndex,
                selector.TIndex).EnsureValid();
        }

        int channels = sources?.GetValueOrDefault(member.AssetId)?.Metadata.Channels ?? 1;
        return channels > 1
            ? ChannelPlaneSelector.InterleavedComponent(member.FrameIndex, 0)
            : group.Members.Count(item => item.AssetId == member.AssetId) > 1 || member.FrameIndex > 0
                ? ChannelPlaneSelector.FramePlane(member.FrameIndex)
                : ChannelPlaneSelector.ExternalAsset(member.FrameIndex);
    }

    private static ScientificChannelSourceKind ParseChannelSourceKind(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "externalasset" => ScientificChannelSourceKind.ExternalAsset,
            "interleavedcomponent" => ScientificChannelSourceKind.InterleavedComponent,
            "frameplane" => ScientificChannelSourceKind.FramePlane,
            _ => throw new InvalidDataException("工程包含未知 scientific plane source kind。"),
        };
    internal static ProjectLinkGroupSnapshot ToSnapshot(LinkGroup group)
    {
        group.EnsureValid();
        return new ProjectLinkGroupSnapshot
        {
            Id = group.Id,
            Name = group.Name,
            ReferenceAssetId = group.ReferenceAssetId,
            AssetIds = group.AssetIds.ToArray(),
            SyncOptions = new ProjectLinkSyncOptionsSnapshot
            {
                Pan = group.SyncOptions.HasFlag(LinkSyncOptions.Pan),
                Zoom = group.SyncOptions.HasFlag(LinkSyncOptions.Zoom),
                Crop = group.SyncOptions.HasFlag(LinkSyncOptions.Crop),
                Roi = group.SyncOptions.HasFlag(LinkSyncOptions.Roi),
                ColorScale = group.SyncOptions.HasFlag(LinkSyncOptions.ColorScale),
            },
            Mappings = group.Mappings.Select(mapping => new ProjectSpatialMappingSnapshot
            {
                Id = mapping.Id,
                SourceAssetId = mapping.SourceAssetId,
                TargetAssetId = mapping.TargetAssetId,
                SourceRevision = mapping.SourceRevision,
                TargetRevision = mapping.TargetRevision,
                Kind = mapping.Kind switch
                {
                    SpatialMappingKind.Identity => "identity",
                    SpatialMappingKind.Translation => "translation",
                    SpatialMappingKind.Rigid => "rigid",
                    SpatialMappingKind.Affine => "affine",
                    _ => throw new InvalidDataException("未知 SpatialMapping 类型。"),
                },
                Matrix =
                [
                    mapping.Matrix.M11, mapping.Matrix.M12, mapping.Matrix.M13,
                    mapping.Matrix.M21, mapping.Matrix.M22, mapping.Matrix.M23,
                    mapping.Matrix.M31, mapping.Matrix.M32, mapping.Matrix.M33,
                ],
                Origin = mapping.Origin switch
                {
                    SpatialMappingOrigin.UserDeclaredIdentity => "userDeclaredIdentity",
                    SpatialMappingOrigin.UserDeclaredTranslation => "userDeclaredTranslation",
                    SpatialMappingOrigin.ManualLandmarks => "manualLandmarks",
                    SpatialMappingOrigin.ImportedMetadata => "importedMetadata",
                    _ => throw new InvalidDataException("未知 SpatialMapping 来源。"),
                },
                CreatedAt = mapping.CreatedAt,
                ResidualPixels = mapping.ResidualPixels,
                Landmarks = mapping.EffectiveLandmarks.Select(landmark => new ProjectRegistrationLandmarkSnapshot
                {
                    Id = landmark.Id,
                    SourceX = landmark.SourcePoint.X,
                    SourceY = landmark.SourcePoint.Y,
                    TargetX = landmark.TargetPoint.X,
                    TargetY = landmark.TargetPoint.Y,
                }).ToArray(),
                ResidualPhysical = mapping.ResidualPhysical,
                ResidualPhysicalUnit = mapping.ResidualPhysicalUnit,
            }).ToArray(),
        };
    }

    internal static LinkGroup ToLinkGroup(ProjectLinkGroupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        LinkSyncOptions syncOptions = LinkSyncOptions.None;
        if (snapshot.SyncOptions.Pan) syncOptions |= LinkSyncOptions.Pan;
        if (snapshot.SyncOptions.Zoom) syncOptions |= LinkSyncOptions.Zoom;
        if (snapshot.SyncOptions.Crop) syncOptions |= LinkSyncOptions.Crop;
        if (snapshot.SyncOptions.Roi) syncOptions |= LinkSyncOptions.Roi;
        if (snapshot.SyncOptions.ColorScale) syncOptions |= LinkSyncOptions.ColorScale;

        SpatialMapping[] mappings = snapshot.Mappings.Select(mapping =>
        {
            if (mapping.Matrix.Count != 9)
            {
                throw new InvalidDataException("SpatialMapping matrix 必须包含 9 个 row-major 数值。");
            }

            double[] matrix = mapping.Matrix.ToArray();
            return new SpatialMapping(
                mapping.Id,
                mapping.SourceAssetId,
                mapping.TargetAssetId,
                mapping.SourceRevision,
                mapping.TargetRevision,
                mapping.Kind?.ToLowerInvariant() switch
                {
                    "identity" => SpatialMappingKind.Identity,
                    "translation" => SpatialMappingKind.Translation,
                    "rigid" => SpatialMappingKind.Rigid,
                    "affine" => SpatialMappingKind.Affine,
                    _ => throw new InvalidDataException("工程包含未知 SpatialMapping 类型。"),
                },
                new SpatialMatrix3x3(
                    matrix[0], matrix[1], matrix[2],
                    matrix[3], matrix[4], matrix[5],
                    matrix[6], matrix[7], matrix[8]),
                mapping.Origin?.ToLowerInvariant() switch
                {
                    "userdeclaredidentity" => SpatialMappingOrigin.UserDeclaredIdentity,
                    "userdeclaredtranslation" => SpatialMappingOrigin.UserDeclaredTranslation,
                    "manuallandmarks" => SpatialMappingOrigin.ManualLandmarks,
                    "importedmetadata" => SpatialMappingOrigin.ImportedMetadata,
                    _ => throw new InvalidDataException("工程包含未知 SpatialMapping 来源。"),
                },
                mapping.CreatedAt,
                mapping.ResidualPixels,
                (mapping.Landmarks ?? [])
                    .Select(landmark => new RegistrationLandmarkPair(
                        landmark.Id,
                        new SpatialPoint(landmark.SourceX, landmark.SourceY),
                        new SpatialPoint(landmark.TargetX, landmark.TargetY)))
                    .ToArray(),
                mapping.ResidualPhysical,
                mapping.ResidualPhysicalUnit).EnsureValid();
        }).ToArray();

        return new LinkGroup(
            snapshot.Id,
            snapshot.Name,
            snapshot.ReferenceAssetId,
            snapshot.AssetIds.ToArray(),
            syncOptions,
            mappings).EnsureValid();
    }

    internal static ProjectRoiSnapshot ToSnapshot(RoiObject roi)
    {
        roi.EnsureValid();
        return new ProjectRoiSnapshot
        {
            Id = roi.Id,
            AssetId = roi.AssetId!.Value,
            SourceRevision = roi.SourceRevision!.Value,
            GeometryKind = roi.GeometryKind.ToString().ToLowerInvariant(),
            FrameIndex = roi.FrameIndex,
            SourceGeometry = roi.SourceGeometry.Select(point => new ProjectMeasurementPointSnapshot
            {
                X = point.X,
                Y = point.Y,
            }).ToArray(),
            Validity = ToSnapshot(roi.Validity),
            Style = new ProjectRoiStyleSnapshot
            {
                StrokeColor = roi.Style.StrokeColor,
                StrokeWidth = roi.Style.StrokeWidth,
                FillColor = roi.Style.FillColor,
                FillOpacity = roi.Style.FillOpacity,
                Label = roi.Style.Label,
                LabelFont = roi.Style.LabelFont,
                LabelFontSizePt = roi.Style.LabelFontSizePt,
                LabelIsBold = roi.Style.LabelIsBold,
                LabelColor = roi.Style.LabelColor,
            },
            Propagation = roi.Propagation is null
                ? null
                : new ProjectRoiPropagationSnapshot
                {
                    ReferenceRoiId = roi.Propagation.ReferenceRoiId,
                    TargetRoiId = roi.Propagation.TargetRoiId,
                    LinkGroupId = roi.Propagation.LinkGroupId,
                    MappingId = roi.Propagation.MappingId,
                    TargetCoverageFraction = roi.Propagation.TargetCoverageFraction,
                },
        };
    }

    internal static RoiObject ToRoiObject(ProjectRoiSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ProjectRoiStyleSnapshot style = snapshot.Style ?? new ProjectRoiStyleSnapshot();
        var roi = new RoiObject
        {
            Id = snapshot.Id,
            AssetId = snapshot.AssetId,
            SourceRevision = snapshot.SourceRevision,
            GeometryKind = snapshot.GeometryKind?.ToLowerInvariant() switch
            {
                "rectangle" => RoiGeometryKind.Rectangle,
                "ellipse" => RoiGeometryKind.Ellipse,
                "polygon" => RoiGeometryKind.Polygon,
                "polyline" => RoiGeometryKind.Polyline,
                _ => throw new InvalidDataException("工程包含未知 canonical ROI geometry kind。"),
            },
            FrameIndex = snapshot.FrameIndex,
            SourceGeometry = (snapshot.SourceGeometry ?? [])
                .Select(point => new MeasurementPoint(point.X, point.Y))
                .ToArray(),
            Validity = ToScientificValidity(snapshot.Validity ?? new ProjectScientificValiditySnapshot()),
            Style = new RoiStyle(
                style.StrokeColor,
                style.StrokeWidth,
                style.FillColor,
                style.FillOpacity,
                style.Label,
                style.LabelFont,
                style.LabelColor,
                style.LabelFontSizePt,
                style.LabelIsBold),
            Propagation = snapshot.Propagation is null
                ? null
                : new RoiPropagationProvenance(
                    snapshot.Propagation.ReferenceRoiId,
                    snapshot.Propagation.TargetRoiId,
                    snapshot.Propagation.LinkGroupId,
                    snapshot.Propagation.MappingId,
                    snapshot.Propagation.TargetCoverageFraction),
        };
        return roi.EnsureValid();
    }

    internal static RoiFigureProjectionObject ToRoiFigureProjection(
        ProjectRoiFigureProjectionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new RoiFigureProjectionObject
        {
            Id = snapshot.Id,
            RoiId = snapshot.RoiId,
            PanelId = snapshot.PanelId,
            AssetId = snapshot.AssetId,
            SourceRevision = snapshot.SourceRevision,
            StyleOverride = ToStyleOverride(snapshot.StyleOverride),
            IsVisible = snapshot.Visible,
            ZIndex = snapshot.ZIndex,
        };
    }

    private static string ToCalibrationOriginKey(CalibrationOrigin origin) => origin switch
    {
        CalibrationOrigin.Metadata => "metadata",
        CalibrationOrigin.Manual => "manual",
        CalibrationOrigin.Linked => "linked",
        _ => "none",
    };

    private static CalibrationOrigin ParseCalibrationOrigin(string? origin) => origin?.ToLowerInvariant() switch
    {
        "metadata" => CalibrationOrigin.Metadata,
        "manual" => CalibrationOrigin.Manual,
        "linked" => CalibrationOrigin.Linked,
        _ => CalibrationOrigin.None,
    };

    private static string ToMeasurementKindKey(ScientificMeasurementKind kind) => kind switch
    {
        ScientificMeasurementKind.Length => "length",
        ScientificMeasurementKind.Angle => "angle",
        ScientificMeasurementKind.RectangleRoi => "rectangleRoi",
        ScientificMeasurementKind.CircleRoi => "circleRoi",
        ScientificMeasurementKind.Polyline => "polyline",
        _ => throw new InvalidDataException($"未知测量类型：{kind}"),
    };

    private static ScientificMeasurementKind ParseMeasurementKind(string? kind) => kind?.ToLowerInvariant() switch
    {
        "length" => ScientificMeasurementKind.Length,
        "angle" => ScientificMeasurementKind.Angle,
        "rectangleroi" => ScientificMeasurementKind.RectangleRoi,
        "circleroi" => ScientificMeasurementKind.CircleRoi,
        "polyline" => ScientificMeasurementKind.Polyline,
        _ => throw new InvalidDataException($"未知测量类型：{kind}"),
    };

    private static string ToAnalysisChannelKey(ImageAnalysisChannel channel) => channel switch
    {
        ImageAnalysisChannel.Luminance => "luminance",
        ImageAnalysisChannel.Red => "red",
        ImageAnalysisChannel.Green => "green",
        ImageAnalysisChannel.Blue => "blue",
        ImageAnalysisChannel.Alpha => "alpha",
        _ => throw new InvalidDataException($"未知图像分析通道：{channel}"),
    };

    private static ImageAnalysisChannel ParseAnalysisChannel(string? channel) => channel?.ToLowerInvariant() switch
    {
        "luminance" => ImageAnalysisChannel.Luminance,
        "red" => ImageAnalysisChannel.Red,
        "green" => ImageAnalysisChannel.Green,
        "blue" => ImageAnalysisChannel.Blue,
        "alpha" => ImageAnalysisChannel.Alpha,
        _ => throw new InvalidDataException($"未知图像分析通道：{channel}"),
    };

    private static string ToAssistedRegionModeKey(AssistedRegionMode mode) => mode switch
    {
        AssistedRegionMode.BrightParticles => "brightParticles",
        AssistedRegionMode.DarkParticles => "darkParticles",
        AssistedRegionMode.DarkPores => "darkPores",
        AssistedRegionMode.BrightPhase => "brightPhase",
        AssistedRegionMode.GrainRegions => "grainRegions",
        AssistedRegionMode.DarkCracks => "darkCracks",
        AssistedRegionMode.BrightLamellae => "brightLamellae",
        _ => throw new InvalidDataException($"未知颗粒分析模式：{mode}"),
    };

    private static AssistedRegionMode ParseAssistedRegionMode(string? mode) => mode?.ToLowerInvariant() switch
    {
        "brightparticles" => AssistedRegionMode.BrightParticles,
        "darkparticles" => AssistedRegionMode.DarkParticles,
        "darkpores" => AssistedRegionMode.DarkPores,
        "brightphase" => AssistedRegionMode.BrightPhase,
        "grainregions" => AssistedRegionMode.GrainRegions,
        "darkcracks" => AssistedRegionMode.DarkCracks,
        "brightlamellae" => AssistedRegionMode.BrightLamellae,
        _ => throw new InvalidDataException($"未知颗粒分析模式：{mode}"),
    };
}
