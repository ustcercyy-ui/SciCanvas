using SciCanvas.Core.Channels;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Persistence;

/// <summary>
/// Explicit, idempotent migration boundary for project files. V2 adds workspace,
/// normalized crop, millimeter frames, source revisions and scientific validity;
/// V2.1 adds source-revision-bound scientific image analysis results; V2.2 adds
/// persisted threshold/particle analysis and reproducible automation parameters; V2.3
/// separates scientific stroke/fill/marker/label and annotation text/shape styles; V2.4
/// persists scientific objects, multichannel/link/mapping state, composite panels, publishing
/// portability and export policy. V2.5 separates canonical ROI data from Figure ROI projections
/// and canonicalizes ROI shape/label styles. V2.6 persists typed colorbar/legend adapters and
/// per-channel colormaps. V2.7 adds typed, source-fingerprinted tabular data assets. V2.8 adds
/// data-bound 2D PlotObjects with axes, typography and series style. V2.9 adds auditable Plot
/// filters, excluded-row counts and ordered non-destructive transforms. V3.0 persists PlotObjects
/// as native Figure panels with geometry and style inheritance. All new fields have
/// deterministic defaults for legacy documents.
/// </summary>
public static class ProjectMigrationPipeline
{
    public const string CurrentVersion = "3.0";

    public static IReadOnlySet<string> SupportedVersions { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "0.1",
            "0.9",
            "1.1",
            "1.2",
            "2.0",
            "2.1",
            "2.2",
            "2.3",
            "2.4",
            "2.5",
            "2.6",
            "2.7",
            "2.8",
            "2.9",
            CurrentVersion,
        };

    public static SciCanvasProjectDocument MigrateToCurrent(SciCanvasProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!SupportedVersions.Contains(document.SchemaVersion))
        {
            throw new NotSupportedException($"暂不支持工程版本 {document.SchemaVersion}。");
        }

        if (document.SchemaVersion == CurrentVersion &&
            document.MultiChannelGroups
                .SelectMany(group => group.Members)
                .All(member => member.PlaneSelector is not null))
        {
            return document;
        }

        bool requiresCanonicalStyleMigration =
            document.SchemaVersion is not ("2.3" or "2.4" or "2.5" or "2.9" or CurrentVersion);
        Guid figureId = CreateStableFigureId(document.ProjectId);
        ProjectWorkspaceSnapshot workspace = document.Workspace.Figures.Count > 0
            ? document.Workspace
            : new ProjectWorkspaceSnapshot
            {
                ActiveFigureId = figureId,
                Figures =
                [
                    new ProjectFigureSnapshot
                    {
                        Id = figureId,
                        Name = string.IsNullOrWhiteSpace(document.Title) ? "Figure 1" : document.Title,
                        WidthMm = document.Canvas.Width / 300.0 * 25.4,
                        HeightMm = document.Canvas.Height / 300.0 * 25.4,
                        Dpi = 300,
                        TemplateId = document.TemplateSnapshot?.TemplateId ?? string.Empty,
                        LayerIds = document.Layers.Select(layer => layer.Id).ToArray(),
                    },
                ],
            };
        string globalFontFamily = string.IsNullOrWhiteSpace(document.TemplateSnapshot?.GlobalStyle?.FontFamily)
            ? "Arial"
            : document.TemplateSnapshot.GlobalStyle.FontFamily.Trim();
        string globalTextColor = string.IsNullOrWhiteSpace(document.TemplateSnapshot?.GlobalStyle?.TextColor)
            ? "#FF111111"
            : document.TemplateSnapshot.GlobalStyle.TextColor;
        string globalShapeColor = string.IsNullOrWhiteSpace(document.TemplateSnapshot?.GlobalStyle?.ShapeColor)
            ? "#FFE53935"
            : document.TemplateSnapshot.GlobalStyle.ShapeColor;
        IReadOnlyDictionary<Guid, ProjectSourceSnapshot> sourcesById =
            document.Sources.ToDictionary(source => source.Id);

        return new SciCanvasProjectDocument
        {
            SchemaVersion = CurrentVersion,
            ProjectId = document.ProjectId,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Title = document.Title,
            Canvas = document.Canvas,
            Sources = document.Sources,
            DataAssets = document.DataAssets,
            Plots = document.Plots,
            Layers = document.Layers,
            CropPresets = document.CropPresets,
            Guides = document.Guides,
            ExportProfiles = document.ExportProfiles,
            JournalPresetSnapshots = document.JournalPresetSnapshots,
            FontSubstitutions = document.FontSubstitutions,
            Calibrations = document.Calibrations,
            Measurements = requiresCanonicalStyleMigration
                ? document.Measurements
                    .Select(measurement => MigrateMeasurement(measurement, globalFontFamily))
                    .ToArray()
                : document.Measurements,
            Analyses = document.Analyses
                .Select(analysis => MigrateAnalysis(analysis, sourcesById))
                .ToArray(),
            MultiChannelGroups = MigrateMultiChannelGroups(document.MultiChannelGroups, document.Sources),
            LinkGroups = document.LinkGroups,
            Rois = document.Rois
                .Select(roi => MigrateRoi(roi, sourcesById))
                .ToArray(),
            TemplateSnapshot = MigrateTemplateSnapshot(
                document.TemplateSnapshot,
                globalFontFamily,
                globalTextColor,
                globalShapeColor,
                requiresCanonicalStyleMigration,
                document.SchemaVersion != CurrentVersion),
            AuditTrail = document.SchemaVersion == CurrentVersion
                ? document.AuditTrail
                : document.AuditTrail.Concat(
                [
                    new ProjectAuditEntrySnapshot
                    {
                        Timestamp = document.UpdatedAt,
                        Command = "MigrateProject",
                        Parameters = new Dictionary<string, object?>
                        {
                            ["from"] = document.SchemaVersion,
                            ["to"] = CurrentVersion,
                        },
                    },
                ])
                .ToArray(),
            Workspace = workspace,
        };
    }

    private static IReadOnlyList<ProjectMultiChannelAssetGroupSnapshot> MigrateMultiChannelGroups(
        IReadOnlyList<ProjectMultiChannelAssetGroupSnapshot> groups,
        IReadOnlyList<ProjectSourceSnapshot> sources)
    {
        IReadOnlyDictionary<Guid, long> revisions = sources.ToDictionary(
            source => source.Id,
            source => Math.Max(1, source.SourceRevision));
        return groups.Select(group => new ProjectMultiChannelAssetGroupSnapshot
        {
            Id = group.Id,
            Name = group.Name,
            ReferenceAssetId = group.ReferenceAssetId,
            SameFieldOfViewConfirmed = group.SameFieldOfViewConfirmed,
            Members = group.Members.Select(member => new ProjectChannelGroupMemberSnapshot
            {
                ChannelId = member.ChannelId,
                AssetId = member.AssetId,
                SourceRevision = member.SourceRevision ?? revisions.GetValueOrDefault(member.AssetId, 1),
                FrameIndex = member.FrameIndex,
                PlaneSelector = MigratePlaneSelector(member, group, sources),
                Name = member.Name,
                Role = member.Role,
                Color = member.Color,
                NameOrigin = member.NameOrigin,
                IsNameConfirmed = member.IsNameConfirmed,
                Visible = member.Visible,
                Opacity = member.Opacity,
                DisplayMinimum = member.DisplayMinimum,
                DisplayMaximum = member.DisplayMaximum,
                Gamma = member.Gamma,
                Invert = member.Invert,
                Colormap = string.IsNullOrWhiteSpace(member.Colormap)
                    ? "viridis"
                    : ScientificColormap.Normalize(member.Colormap),
            }).ToArray(),
        }).ToArray();
    }

    private static ProjectChannelPlaneSelectorSnapshot MigratePlaneSelector(
        ProjectChannelGroupMemberSnapshot member,
        ProjectMultiChannelAssetGroupSnapshot group,
        IReadOnlyList<ProjectSourceSnapshot> sources)
    {
        if (member.PlaneSelector is { } selector)
        {
            return selector;
        }

        ProjectSourceSnapshot? source = sources.FirstOrDefault(item => item.Id == member.AssetId);
        string sourceKind = source?.Metadata.Channels > 1
            ? "interleavedComponent"
            : group.Members.Count(item => item.AssetId == member.AssetId) > 1 || member.FrameIndex > 0
                ? "framePlane"
                : "externalAsset";
        return new ProjectChannelPlaneSelectorSnapshot
        {
            SourceKind = sourceKind,
            FrameIndex = member.FrameIndex,
            ComponentIndex = sourceKind == "interleavedComponent" ? 0 : null,
        };
    }

    private static ProjectMeasurementSnapshot MigrateMeasurement(
        ProjectMeasurementSnapshot measurement,
        string globalFontFamily) => new()
    {
        Id = measurement.Id,
        SourceAssetId = measurement.SourceAssetId,
        SourceRevision = Math.Max(1, measurement.SourceRevision),
        Kind = measurement.Kind,
        X1 = measurement.X1,
        Y1 = measurement.Y1,
        X2 = measurement.X2,
        Y2 = measurement.Y2,
        X3 = measurement.X3,
        Y3 = measurement.Y3,
        StrokeColor = measurement.StrokeColor,
        StrokeWidthPixels = measurement.StrokeWidthPixels,
        LineStyle = measurement.LineStyle,
        FillColor = measurement.StrokeColor,
        MarkerStrokeColor = measurement.StrokeColor,
        MarkerFillColor = "#FF11171F",
        MarkerSizePixels = measurement.MarkerSizePixels,
        ShowMarkers = measurement.ShowMarkers,
        ShowLabel = measurement.ShowLabel,
        LabelColor = measurement.StrokeColor,
        LabelFontFamily = globalFontFamily,
        LabelFontSizePt = 16.5,
        LabelIsBold = true,
        FillOpacityPercent = measurement.FillOpacityPercent,
        IsVisible = measurement.IsVisible,
        IsLocked = measurement.IsLocked,
        Points = measurement.Points,
    };

    private static ProjectTemplateSnapshot? MigrateTemplateSnapshot(
        ProjectTemplateSnapshot? template,
        string globalFontFamily,
        string globalTextColor,
        string globalShapeColor,
        bool migrateCanonicalStyles,
        bool migrateColorbarAdapters)
    {
        if (template is null)
        {
            return null;
        }

        return new ProjectTemplateSnapshot
        {
            TemplateId = template.TemplateId,
            WorkspaceMode = template.WorkspaceMode,
            SelectedSourceId = template.SelectedSourceId,
            ActiveCrop = template.ActiveCrop,
            LockCropSizeAcrossSources = template.LockCropSizeAcrossSources,
            CropOverlayVisible = template.CropOverlayVisible,
            SnappingEnabled = template.SnappingEnabled,
            SnapTolerancePixels = template.SnapTolerancePixels,
            ExactSpacingPixels = template.ExactSpacingPixels,
            AutoPanelLabelsEnabled = template.AutoPanelLabelsEnabled,
            ShowPanelLabels = template.ShowPanelLabels,
            PanelLabelSequence = template.PanelLabelSequence,
            LayerSlots = template.LayerSlots,
            ScaleBars = template.ScaleBars,
            MeasurementOverlays = template.MeasurementOverlays,
            PlotPanels = template.PlotPanels,
            ScientificObjects = template.ScientificObjects
                .Select(item => MigrateFigureScientificObject(item, migrateColorbarAdapters))
                .ToArray(),
            RoiProjections = template.RoiProjections,
            Annotations = migrateCanonicalStyles
                ? template.Annotations
                    .Select(annotation => MigrateAnnotation(
                        annotation,
                        globalFontFamily,
                        globalTextColor,
                        globalShapeColor))
                    .ToArray()
                : template.Annotations,
            GlobalStyle = migrateCanonicalStyles
                ? MigrateGlobalStyle(template.GlobalStyle)
                : template.GlobalStyle,
            ScientificColors = template.ScientificColors,
        };
    }

    private static ProjectRoiSnapshot MigrateRoi(
        ProjectRoiSnapshot roi,
        IReadOnlyDictionary<Guid, ProjectSourceSnapshot> sourcesById)
    {
        ProjectRoiStyleSnapshot style = roi.Style ?? new ProjectRoiStyleSnapshot();
        RoiGeometryValidationResult? validation = TryValidateGeometry(
            roi.AssetId,
            roi.SourceRevision,
            roi.FrameIndex,
            roi.GeometryKind,
            roi.SourceGeometry,
            sourcesById);
        ProjectScientificValiditySnapshot validity = roi.Validity ?? new ProjectScientificValiditySnapshot();
        if (validation is not null)
        {
            RoiBoundaryPolicyResult policy = RoiOutOfBoundsPolicy.Evaluate(
                validation,
                roi.Propagation is null ? RoiBoundaryRole.Reference : RoiBoundaryRole.Propagated,
                partialReferenceConfirmed:
                    roi.Propagation is null &&
                    validation.State == RoiGeometryValidationState.PartiallyOutside);
            validity = new ProjectScientificValiditySnapshot
            {
                State = policy.Validity.State.ToString().ToLowerInvariant(),
                Reasons = policy.Validity.Reasons,
            };
        }

        return new ProjectRoiSnapshot
        {
            Id = roi.Id,
            AssetId = roi.AssetId,
            SourceRevision = roi.SourceRevision,
            GeometryKind = roi.GeometryKind,
            FrameIndex = roi.FrameIndex,
            SourceGeometry = roi.SourceGeometry,
            Validity = validity,
            Style = new ProjectRoiStyleSnapshot
            {
                StrokeColor = style.StrokeColor,
                StrokeWidth = style.StrokeWidth,
                FillColor = style.FillColor,
                FillOpacity = style.FillOpacity,
                Label = style.Label,
                LabelFont = style.LabelFont,
                LabelFontSizePt = style.LabelFontSizePt,
                LabelIsBold = style.LabelIsBold,
                LabelColor = style.LabelColor,
            },
            Propagation = roi.Propagation is null
                ? null
                : new ProjectRoiPropagationSnapshot
                {
                    ReferenceRoiId = roi.Propagation.ReferenceRoiId,
                    TargetRoiId = roi.Propagation.TargetRoiId,
                    LinkGroupId = roi.Propagation.LinkGroupId,
                    MappingId = roi.Propagation.MappingId,
                    TargetCoverageFraction =
                        validation?.CoverageFraction ??
                        roi.Propagation.TargetCoverageFraction,
                },
        };
    }

    private static ProjectScientificAnalysisSnapshot MigrateAnalysis(
        ProjectScientificAnalysisSnapshot analysis,
        IReadOnlyDictionary<Guid, ProjectSourceSnapshot> sourcesById)
    {
        if (!string.Equals(analysis.Kind, "roiStatistics", StringComparison.OrdinalIgnoreCase) ||
            analysis.PolygonMask.Count < 3)
        {
            return analysis;
        }

        RoiGeometryValidationResult? validation = TryValidateGeometry(
            analysis.SourceAssetId,
            analysis.SourceRevision,
            analysis.FrameIndex,
            "polygon",
            analysis.PolygonMask,
            sourcesById);
        if (validation is null)
        {
            return CloneAnalysis(
                analysis,
                clippedToImage: false,
                coverageFraction: 0,
                new ProjectScientificValiditySnapshot
                {
                    State = "invalid",
                    Reasons = [.. analysis.Validity.Reasons, "ROI analysis source dimensions are unavailable after migration."],
                });
        }

        return validation.State switch
        {
            RoiGeometryValidationState.Inside => CloneAnalysis(
                analysis,
                clippedToImage: false,
                coverageFraction: 1,
                analysis.Validity),
            RoiGeometryValidationState.PartiallyOutside => CloneAnalysis(
                analysis,
                clippedToImage: true,
                coverageFraction: validation.CoverageFraction,
                new ProjectScientificValiditySnapshot
                {
                    State = "reviewrequired",
                    Reasons =
                    [
                        .. analysis.Validity.Reasons,
                        $"Migrated ROI statistics used an image-clipped polygon (coverage fraction {validation.CoverageFraction:0.######}).",
                    ],
                }),
            _ => CloneAnalysis(
                analysis,
                clippedToImage: false,
                coverageFraction: 0,
                new ProjectScientificValiditySnapshot
                {
                    State = "invalid",
                    Reasons =
                    [
                        .. analysis.Validity.Reasons,
                        "Migrated ROI statistics reference geometry that is outside or invalid; do not use this result.",
                    ],
                }),
        };
    }

    private static ProjectScientificAnalysisSnapshot CloneAnalysis(
        ProjectScientificAnalysisSnapshot analysis,
        bool clippedToImage,
        double coverageFraction,
        ProjectScientificValiditySnapshot validity) => new()
    {
        Id = analysis.Id,
        SourceAssetId = analysis.SourceAssetId,
        SourceRevision = analysis.SourceRevision,
        Kind = analysis.Kind,
        FrameIndex = analysis.FrameIndex,
        Channel = analysis.Channel,
        AnalyzerId = analysis.AnalyzerId,
        AnalyzedAt = analysis.AnalyzedAt,
        Validity = validity,
        SourceBitDepth = analysis.SourceBitDepth,
        Region = analysis.Region,
        RoiId = analysis.RoiId,
        ScientificChannelId = analysis.ScientificChannelId,
        LinkGroupId = analysis.LinkGroupId,
        MappingId = analysis.MappingId,
        PolygonMask = analysis.PolygonMask,
        ClippedToImage = clippedToImage,
        CoverageFraction = coverageFraction,
        PixelCount = analysis.PixelCount,
        Minimum = analysis.Minimum,
        Maximum = analysis.Maximum,
        Mean = analysis.Mean,
        StandardDeviation = analysis.StandardDeviation,
        IntegratedIntensity = analysis.IntegratedIntensity,
        Histogram = analysis.Histogram,
        DistanceUnit = analysis.DistanceUnit,
        Samples = analysis.Samples,
        AnalysisMode = analysis.AnalysisMode,
        UseAutomaticThreshold = analysis.UseAutomaticThreshold,
        ThresholdNormalized = analysis.ThresholdNormalized,
        AppliedThresholdNormalized = analysis.AppliedThresholdNormalized,
        MinimumAreaPixels = analysis.MinimumAreaPixels,
        MaximumCandidates = analysis.MaximumCandidates,
        AnalysisMaxPixels = analysis.AnalysisMaxPixels,
        AnalysisMaxComponentsSafety = analysis.AnalysisMaxComponentsSafety,
        AnalysisMaxBoundaryPoints = analysis.AnalysisMaxBoundaryPoints,
        AnalysisMemoryBudgetBytes = analysis.AnalysisMemoryBudgetBytes,
        ForegroundPixelCount = analysis.ForegroundPixelCount,
        TotalPixelCount = analysis.TotalPixelCount,
        Particles = analysis.Particles,
    };

    private static RoiGeometryValidationResult? TryValidateGeometry(
        Guid assetId,
        long sourceRevision,
        int frameIndex,
        string? geometryKind,
        IReadOnlyList<ProjectMeasurementPointSnapshot> points,
        IReadOnlyDictionary<Guid, ProjectSourceSnapshot> sourcesById)
    {
        if (!sourcesById.TryGetValue(assetId, out ProjectSourceSnapshot? source) ||
            source.Metadata.Width <= 0 || source.Metadata.Height <= 0)
        {
            return null;
        }

        RoiGeometryKind kind;
        switch (geometryKind?.ToLowerInvariant())
        {
            case "rectangle":
                kind = RoiGeometryKind.Rectangle;
                break;
            case "ellipse":
                kind = RoiGeometryKind.Ellipse;
                break;
            case "polygon":
                kind = RoiGeometryKind.Polygon;
                break;
            case "polyline":
                kind = RoiGeometryKind.Polyline;
                break;
            default:
                return new RoiGeometryValidationResult(
                    RoiGeometryValidationState.Invalid,
                    0,
                    null,
                    ["ROI geometry kind is unknown."]);
        }

        var roi = new RoiObject
        {
            Id = Guid.Empty,
            AssetId = assetId,
            SourceRevision = Math.Max(1, sourceRevision),
            FrameIndex = Math.Max(0, frameIndex),
            GeometryKind = kind,
            SourceGeometry = points
                .Select(point => new MeasurementPoint(point.X, point.Y))
                .ToArray(),
        };
        return RoiGeometryValidator.Validate(roi, source.Metadata.Width, source.Metadata.Height);
    }

    private static ProjectFigureScientificObjectSnapshot MigrateFigureScientificObject(
        ProjectFigureScientificObjectSnapshot scientificObject,
        bool migrateColorbarAdapters)
    {
        // Schema 2.4 Figure ROI records contain canvas geometry only and no canonical
        // RoiId/PanelId/AssetId/revision. Preserve the visual exactly as a polygon annotation;
        // never fabricate a scientific ROI relationship from ambiguous data.
        string kind = string.Equals(scientificObject.Kind, "roi", StringComparison.OrdinalIgnoreCase)
            ? "PolygonAnnotation"
            : scientificObject.Kind;
        string bindingState = migrateColorbarAdapters
            ? scientificObject.ChannelId.HasValue ? "Linked" : "Detached"
            : string.IsNullOrWhiteSpace(scientificObject.ColorbarBindingState)
                ? scientificObject.ChannelId.HasValue ? "Linked" : "Detached"
                : scientificObject.ColorbarBindingState;
        IReadOnlyList<ProjectColorbarTickSnapshot> ticks = scientificObject.Ticks.Count > 0
            ? scientificObject.Ticks
            : CreateDefaultColorbarTicks(scientificObject.Minimum, scientificObject.Maximum);
        return new ProjectFigureScientificObjectSnapshot
        {
            Id = scientificObject.Id,
            Kind = kind,
            Points = scientificObject.Points,
            Label = scientificObject.Label,
            StrokeColor = scientificObject.StrokeColor,
            FillColor = scientificObject.FillColor,
            FillOpacityPercent = scientificObject.FillOpacityPercent,
            TextColor = scientificObject.TextColor,
            FontFamily = scientificObject.FontFamily,
            FontSizePt = scientificObject.FontSizePt,
            StrokeWidthPt = scientificObject.StrokeWidthPt,
            IsBold = scientificObject.IsBold,
            Visible = scientificObject.Visible,
            Locked = scientificObject.Locked,
            ZIndex = scientificObject.ZIndex,
            Minimum = scientificObject.Minimum,
            Maximum = scientificObject.Maximum,
            Unit = scientificObject.Unit,
            Colormap = scientificObject.Colormap,
            ChannelEntries = scientificObject.ChannelEntries,
            ChannelId = scientificObject.ChannelId,
            ColorbarBindingState = bindingState,
            Orientation = scientificObject.Orientation?.ToLowerInvariant() switch
            {
                null or "" or "vertical" => "Vertical",
                "horizontal" => "Horizontal",
                _ => throw new InvalidDataException(
                    $"工程包含未知 Colorbar orientation：{scientificObject.Orientation}。"),
            },
            Ticks = ticks,
            ChannelLegendPadding = scientificObject.ChannelLegendPadding > 0
                ? scientificObject.ChannelLegendPadding
                : 5,
        };
    }

    private static IReadOnlyList<ProjectColorbarTickSnapshot> CreateDefaultColorbarTicks(
        double minimum,
        double maximum)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum)
        {
            return [];
        }

        return ColorbarObject.CreateDefaultTicks(minimum, maximum)
            .Select(tick => new ProjectColorbarTickSnapshot
            {
                Value = tick.Value,
                Label = tick.Label,
            }).ToArray();
    }

    private static ProjectGlobalStyleSnapshot? MigrateGlobalStyle(ProjectGlobalStyleSnapshot? style)
    {
        if (style is null)
        {
            return null;
        }

        return new ProjectGlobalStyleSnapshot
        {
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            StrokeWidthPt = style.StrokeWidthPt,
            TextColor = style.TextColor,
            ShapeColor = style.ShapeColor,
            ScaleBarColor = style.ScaleBarColor,
            PanelLabelFontFamily = style.FontFamily,
            PanelLabelFontSizePt = style.FontSizePt,
            PanelLabelTextColor = style.TextColor,
            PanelLabelIsBold = true,
            ScaleBarLabelColor = style.ScaleBarColor,
            ScaleBarFontFamily = style.FontFamily,
            ScaleBarFontSizePt = style.FontSizePt,
            ScaleBarLabelIsBold = true,
            ScaleBarThicknessPt = style.StrokeWidthPt,
        };
    }

    private static ProjectAnnotationSnapshot MigrateAnnotation(
        ProjectAnnotationSnapshot annotation,
        string globalFontFamily,
        string globalTextColor,
        string globalShapeColor)
    {
        bool isText = string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase);
        return new ProjectAnnotationSnapshot
        {
            Id = annotation.Id,
            Kind = annotation.Kind,
            X = annotation.X,
            Y = annotation.Y,
            EndX = annotation.EndX,
            EndY = annotation.EndY,
            Text = annotation.Text,
            Color = annotation.Color,
            StrokeColor = isText ? globalShapeColor : annotation.Color,
            FillColor = annotation.Color,
            FillOpacityPercent = 0,
            TextColor = isText ? annotation.Color : globalTextColor,
            FontFamily = globalFontFamily,
            FontSizePt = annotation.FontSizePt,
            StrokeWidthPt = annotation.StrokeWidthPt,
            IsBold = annotation.IsBold,
            Visible = annotation.Visible,
            Locked = annotation.Locked,
            ZIndex = annotation.ZIndex,
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
}
