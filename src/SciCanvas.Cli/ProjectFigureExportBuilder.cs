using System.Globalization;
using System.IO;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Linking;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;
using SciCanvas.Persistence;
using LinkingLinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkingSpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using LinkingSpatialMappingKind = SciCanvas.Core.Linking.SpatialMappingKind;
using LinkingSpatialMappingOrigin = SciCanvas.Core.Linking.SpatialMappingOrigin;

namespace SciCanvas.Cli;

internal sealed record ProjectFigureExportContext(
    FigureExportDocument Document,
    IReadOnlyList<LinkingLinkGroup> LinkGroups,
    IReadOnlyList<RoiObject> Rois,
    IReadOnlyList<MultiChannelAssetGroup> MultiChannelGroups);

/// <summary>
/// Converts the persisted project contract into the immutable export contract.
/// This mapper deliberately has no ViewModel dependency so GUI and CLI exporters
/// consume the same Core scientific types.
/// </summary>
internal static class ProjectFigureExportBuilder
{
    public static ProjectFigureExportContext Create(
        SciCanvasProjectDocument project,
        IReadOnlyList<SourceAsset> sources)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(sources);
        Dictionary<Guid, SourceAsset> sourceMap = sources.ToDictionary(source => source.Id);
        Dictionary<Guid, long> revisions = project.Sources.ToDictionary(
            source => source.Id,
            source => source.SourceRevision);
        MultiChannelAssetGroup[] groups = project.MultiChannelGroups
            .Select(group => ToMultiChannelAssetGroup(group, sourceMap))
            .ToArray();
        LinkingLinkGroup[] linkGroups = project.LinkGroups.Select(ToLinkGroup).ToArray();
        RoiObject[] rois = project.Rois.Select(ToRoiObject).ToArray();
        IReadOnlyDictionary<Guid, RoiObject> roisById = rois.ToDictionary(roi => roi.Id);
        ProjectTemplateSnapshot? template = project.TemplateSnapshot;
        IReadOnlyDictionary<Guid, ProjectScaleBarSnapshot> scaleBars =
            template?.ScaleBars ?? new Dictionary<Guid, ProjectScaleBarSnapshot>();
        Dictionary<Guid, TabularDataAsset> dataAssets = project.DataAssets
            .Select(TabularDataSnapshotMapper.ToModel)
            .ToDictionary(asset => asset.Id);
        Dictionary<Guid, PlotObject> plots = project.Plots
            .Select(snapshot =>
            {
                TabularDataAsset asset = dataAssets.GetValueOrDefault(snapshot.Data.DataAssetId)
                    ?? throw new InvalidDataException($"Plot {snapshot.Name} 引用了不存在的 DataAsset。");
                return PlotSnapshotMapper.ToModel(snapshot, asset);
            })
            .ToDictionary(plot => plot.Id);

        FigurePanelExportItem[] panels = project.Layers
            .OrderBy(layer => layer.ZIndex)
            .Select(layer => CreatePanel(
                layer,
                template?.ShowPanelLabels ?? true,
                scaleBars.GetValueOrDefault(layer.Id),
                sourceMap,
                revisions,
                groups,
                linkGroups))
            .ToArray();
        FigureAnnotationExportItem[] annotations = (template?.Annotations ?? [])
            .OrderBy(annotation => annotation.ZIndex)
            .Select(ToAnnotation)
            .ToArray();
        FigureScientificObjectExportItem[] scientificObjects = (template?.ScientificObjects ?? [])
            .OrderBy(item => item.ZIndex)
            .Select(item => ToScientificObject(item, project.Canvas.Width, project.Canvas.Height))
            .ToArray();
        FigureMeasurementOverlayExportItem[] measurements = (template?.MeasurementOverlays ?? [])
            .OrderBy(item => item.ZIndex)
            .Select(item => new FigureMeasurementOverlayExportItem(ToMeasurementOverlay(item)))
            .ToArray();
        FigureRoiProjectionExportItem[] roiProjections = (template?.RoiProjections ?? [])
            .OrderBy(item => item.ZIndex)
            .Select(item => new FigureRoiProjectionExportItem(
                ToRoiFigureProjection(item),
                roisById.TryGetValue(item.RoiId, out RoiObject? roi)
                    ? roi
                    : throw new InvalidDataException(
                        "ROI Figure Projection 引用了不存在的 canonical ROI。")))
            .ToArray();
        FigurePlotPanelExportItem[] plotPanels = (template?.PlotPanels ?? [])
            .OrderBy(item => item.ZIndex)
            .Select(item => CreatePlotPanel(
                item,
                template?.ShowPanelLabels ?? true,
                plots,
                dataAssets))
            .ToArray();
        string background = project.Canvas.BackgroundColor ?? project.Canvas.Background switch
        {
            "black" => "#FF000000",
            "transparent" => "#00FFFFFF",
            _ => "#FFFFFFFF",
        };
        var document = new FigureExportDocument(
            project.Canvas.Width,
            project.Canvas.Height,
            ResolveCanvasDpi(project),
            panels,
            annotations,
            background,
            globalStyle: ToGlobalStyle(template?.GlobalStyle),
            measurementOverlays: measurements,
            scientificObjects: scientificObjects,
            roiProjections: roiProjections,
            plotPanels: plotPanels);
        return new ProjectFigureExportContext(document, linkGroups, rois, groups);
    }

    private static FigurePanelExportItem CreatePanel(
        ProjectImageLayerSnapshot layer,
        bool showPanelLabels,
        ProjectScaleBarSnapshot? scaleBar,
        IReadOnlyDictionary<Guid, SourceAsset> sourceMap,
        IReadOnlyDictionary<Guid, long> revisions,
        IReadOnlyList<MultiChannelAssetGroup> groups,
        IReadOnlyList<LinkingLinkGroup> linkGroups)
    {
        if (!sourceMap.TryGetValue(layer.SourceAssetId, out SourceAsset? source))
        {
            throw new InvalidDataException($"图层 {layer.Name} 引用了不存在的源图。");
        }

        PixelRect64 sourceRect = ToRect(layer.SourceRect);
        var destination = new PixelRect64(
            Math.Max(0, (long)Math.Round(layer.Transform.X)),
            Math.Max(0, (long)Math.Round(layer.Transform.Y)),
            Math.Max(1, (long)Math.Round(sourceRect.Width * layer.Transform.ScaleX)),
            Math.Max(1, (long)Math.Round(sourceRect.Height * layer.Transform.ScaleY)));
        FigureScaleBarExportSpec[] bars = CreateScaleBars(scaleBar).ToArray();
        ProjectImageAdjustmentSnapshot? adjustment = layer.Adjustments.FirstOrDefault();
        FigureChannelLayerExportItem[] channelLayers = CreateChannelLayers(
            layer,
            sourceRect,
            sourceMap,
            revisions,
            groups,
            linkGroups);
        return new FigurePanelExportItem(
            source,
            sourceRect,
            destination,
            showPanelLabels ? layer.PanelLabel ?? string.Empty : string.Empty,
            layer.Visible,
            bars.FirstOrDefault(),
            adjustment is null ? null : ToAdjustment(adjustment),
            layer.FrameIndex,
            IsInset: false,
            StyleOverride: ToStyleOverride(layer.StyleOverride),
            PanelId: layer.Id,
            ScaleBars: bars,
            ChannelLayers: channelLayers,
            SourceRevision: revisions.GetValueOrDefault(layer.SourceAssetId, 1));
    }

    private static FigurePlotPanelExportItem CreatePlotPanel(
        ProjectFigurePlotPanelSnapshot snapshot,
        bool showPanelLabels,
        IReadOnlyDictionary<Guid, PlotObject> plots,
        IReadOnlyDictionary<Guid, TabularDataAsset> dataAssets)
    {
        PlotObject plot = plots.GetValueOrDefault(snapshot.PlotId)
            ?? throw new InvalidDataException("Figure Plot panel 引用了不存在的 Plot。");
        TabularDataAsset asset = dataAssets.GetValueOrDefault(plot.Data.DataAssetId)
            ?? throw new InvalidDataException($"Plot {plot.Name} 引用了不存在的 DataAsset。");
        return FigurePlotPanelExportItem.Create(
            plot,
            asset,
            ToRect(snapshot.DestinationRect),
            showPanelLabels ? snapshot.Label : string.Empty,
            snapshot.Visible,
            ToStyleOverride(snapshot.StyleOverride),
            ToPlotTypographyOverride(snapshot.TypographyOverride),
            snapshot.ZIndex,
            snapshot.Id);
    }

    private static IEnumerable<FigureScaleBarExportSpec> CreateScaleBars(ProjectScaleBarSnapshot? saved)
    {
        if (saved is not { Enabled: true })
        {
            yield break;
        }

        yield return new FigureScaleBarExportSpec(
            saved.PhysicalUnitsPerSourcePixel,
            saved.PhysicalLength,
            saved.Unit,
            saved.ShowLabel,
            saved.CalibrationUnit,
            ParseScaleBarAnchor(saved.Anchor));
        foreach (ProjectAdditionalScaleBarSnapshot additional in saved.AdditionalBars.Where(item => item.IsVisible))
        {
            yield return new FigureScaleBarExportSpec(
                saved.PhysicalUnitsPerSourcePixel,
                additional.PhysicalLength,
                additional.Unit,
                additional.ShowLabel,
                saved.CalibrationUnit,
                ParseScaleBarAnchor(additional.Anchor),
                additional.Id);
        }
    }

    private static FigureChannelLayerExportItem[] CreateChannelLayers(
        ProjectImageLayerSnapshot panel,
        PixelRect64 panelSourceRect,
        IReadOnlyDictionary<Guid, SourceAsset> sourceMap,
        IReadOnlyDictionary<Guid, long> revisions,
        IReadOnlyList<MultiChannelAssetGroup> groups,
        IReadOnlyList<LinkingLinkGroup> linkGroups)
    {
        if (panel.CompositeGroupId is not Guid groupId)
        {
            return [];
        }

        MultiChannelAssetGroup group = groups.SingleOrDefault(item => item.Id == groupId)
            ?? throw new InvalidDataException($"Composite panel 引用了不存在的多通道组 {groupId}。");
        group.EnsureValid(sourceMap.Keys.ToHashSet());
        if (!group.Members.Any(member => member.AssetId == panel.SourceAssetId))
        {
            throw new InvalidDataException("Composite panel 的源素材不属于对应多通道组。");
        }
        if (panel.SourceAssetId != group.ReferenceAssetId)
        {
            throw new InvalidDataException("Composite panel 必须以多通道组参考通道定义输出网格。");
        }

        LinkingLinkGroup? linkGroup = linkGroups.FirstOrDefault(link =>
            link.ContainsAsset(panel.SourceAssetId) &&
            group.Members.All(member => link.ContainsAsset(member.AssetId)));
        if (!group.SameFieldOfViewConfirmed && linkGroup is null)
        {
            throw new InvalidDataException("Composite group 在导出前需要当前有效的 LinkGroup/SpatialMapping。");
        }
        if (linkGroup is not null && !linkGroup.AreMappingsCurrent(revisions))
        {
            throw new InvalidDataException("Composite group 的 SpatialMapping 已因 source revision 变化而失效。");
        }
        if (linkGroup is not null && linkGroup.ReferenceAssetId != group.ReferenceAssetId)
        {
            throw new InvalidDataException("Composite group 与 LinkGroup 必须使用相同参考素材。");
        }

        ChannelGroupMember referenceMember = group.Members.Single(
            member => member.AssetId == group.ReferenceAssetId);
        var referenceGrid = new RegisteredReferenceGrid(
            new ScientificPlaneRef(
                group.ReferenceAssetId,
                revisions.GetValueOrDefault(group.ReferenceAssetId, 1),
                referenceMember.PlaneSelector),
            panelSourceRect).EnsureValid();
        return group.Members.Select(member =>
        {
            SourceAsset memberSource = sourceMap.GetValueOrDefault(member.AssetId)
                ?? throw new InvalidDataException("多通道成员引用了不存在的源素材。");
            long currentRevision = revisions.GetValueOrDefault(member.AssetId, 1);
            if (member.SourceRevision is long capturedRevision && capturedRevision != currentRevision)
            {
                throw new InvalidDataException(
                    $"多通道成员 {member.Name} 的 source revision 已失效，需要重新确认后再导出。");
            }

            RegisteredPlaneResamplingSpec? resampling = null;
            PixelRect64 memberRect = panelSourceRect;
            if (member.AssetId != group.ReferenceAssetId && linkGroup is not null)
            {
                LinkingSpatialMapping mapping = linkGroup.Mappings.Single(
                    candidate => candidate.TargetAssetId == member.AssetId);
                resampling = new RegisteredPlaneResamplingSpec(
                    mapping,
                    referenceGrid,
                    memberSource.Metadata.PixelSize,
                    RegisteredInterpolation.Bilinear,
                    RegisteredBorderPolicy.Transparent,
                    RegisteredPlaneSemantic.ContinuousDisplay).EnsureValid();
                memberRect = RegisteredPlaneResampler.CalculateSourceReadRegion(resampling);
            }

            ScientificSampleType sampleType = memberSource.Metadata.BitsPerChannel <= 8
                ? ScientificSampleType.UInt8
                : ScientificSampleType.UInt16;
            ScientificChannelDescriptor selector = member.PlaneSelector.CreateChannelDescriptor(
                member.ChannelId,
                member.Name,
                sampleType,
                memberSource.Metadata.BitsPerChannel,
                member.Role,
                member.Color);
            return new FigureChannelLayerExportItem(
                group.Id,
                memberSource,
                currentRevision,
                memberRect,
                member.FrameIndex,
                selector,
                member.DisplaySettings,
                RegistrationResampling: resampling).EnsureValid();
        }).ToArray();
    }

    private static MultiChannelAssetGroup ToMultiChannelAssetGroup(
        ProjectMultiChannelAssetGroupSnapshot snapshot,
        IReadOnlyDictionary<Guid, SourceAsset> sources) =>
        new MultiChannelAssetGroup(
            snapshot.Id,
            snapshot.Name,
            snapshot.ReferenceAssetId,
            snapshot.Members.Select(member => new ChannelGroupMember(
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
                new ChannelDisplaySettings(
                    member.ChannelId,
                    member.Visible,
                    member.Color,
                    member.Opacity,
                    member.DisplayMinimum,
                    member.DisplayMaximum,
                    member.Gamma,
                    member.Invert,
                    member.Colormap))
            {
                SourceRevision = member.SourceRevision,
            }).ToArray(),
            snapshot.SameFieldOfViewConfirmed).EnsureValid();

    private static ChannelPlaneSelector ToChannelPlaneSelector(
        ProjectChannelGroupMemberSnapshot member,
        ProjectMultiChannelAssetGroupSnapshot group,
        IReadOnlyDictionary<Guid, SourceAsset> sources)
    {
        if (member.PlaneSelector is { } selector)
        {
            return new ChannelPlaneSelector(
                selector.SourceKind?.ToLowerInvariant() switch
                {
                    "externalasset" => ScientificChannelSourceKind.ExternalAsset,
                    "interleavedcomponent" => ScientificChannelSourceKind.InterleavedComponent,
                    "frameplane" => ScientificChannelSourceKind.FramePlane,
                    _ => throw new InvalidDataException("工程包含未知 scientific plane source kind。"),
                },
                selector.FrameIndex,
                selector.ComponentIndex,
                selector.ZIndex,
                selector.CIndex,
                selector.TIndex).EnsureValid();
        }

        int channels = sources.GetValueOrDefault(member.AssetId)?.Metadata.Channels ?? 1;
        return channels > 1
            ? ChannelPlaneSelector.InterleavedComponent(member.FrameIndex, 0)
            : group.Members.Count(item => item.AssetId == member.AssetId) > 1 || member.FrameIndex > 0
                ? ChannelPlaneSelector.FramePlane(member.FrameIndex)
                : ChannelPlaneSelector.ExternalAsset(member.FrameIndex);
    }

    private static LinkingLinkGroup ToLinkGroup(ProjectLinkGroupSnapshot snapshot)
    {
        SciCanvas.Core.Linking.LinkSyncOptions sync = SciCanvas.Core.Linking.LinkSyncOptions.None;
        if (snapshot.SyncOptions.Pan) sync |= SciCanvas.Core.Linking.LinkSyncOptions.Pan;
        if (snapshot.SyncOptions.Zoom) sync |= SciCanvas.Core.Linking.LinkSyncOptions.Zoom;
        if (snapshot.SyncOptions.Crop) sync |= SciCanvas.Core.Linking.LinkSyncOptions.Crop;
        if (snapshot.SyncOptions.Roi) sync |= SciCanvas.Core.Linking.LinkSyncOptions.Roi;
        if (snapshot.SyncOptions.ColorScale) sync |= SciCanvas.Core.Linking.LinkSyncOptions.ColorScale;
        LinkingSpatialMapping[] mappings = snapshot.Mappings.Select(mapping =>
        {
            if (mapping.Matrix.Count != 9)
            {
                throw new InvalidDataException("SpatialMapping matrix 必须包含 9 个 row-major 数值。");
            }
            double[] matrix = mapping.Matrix.ToArray();
            return new LinkingSpatialMapping(
                mapping.Id,
                mapping.SourceAssetId,
                mapping.TargetAssetId,
                mapping.SourceRevision,
                mapping.TargetRevision,
                ParseMappingKind(mapping.Kind),
                new SpatialMatrix3x3(
                    matrix[0], matrix[1], matrix[2],
                    matrix[3], matrix[4], matrix[5],
                    matrix[6], matrix[7], matrix[8]),
                ParseMappingOrigin(mapping.Origin),
                mapping.CreatedAt,
                mapping.ResidualPixels,
                mapping.Landmarks.Select(landmark => new RegistrationLandmarkPair(
                    landmark.Id,
                    new SpatialPoint(landmark.SourceX, landmark.SourceY),
                    new SpatialPoint(landmark.TargetX, landmark.TargetY))).ToArray(),
                mapping.ResidualPhysical,
                mapping.ResidualPhysicalUnit).EnsureValid();
        }).ToArray();
        return new LinkingLinkGroup(
            snapshot.Id,
            snapshot.Name,
            snapshot.ReferenceAssetId,
            snapshot.AssetIds.ToArray(),
            sync,
            mappings).EnsureValid();
    }

    private static RoiObject ToRoiObject(ProjectRoiSnapshot snapshot) => new RoiObject
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
        SourceGeometry = snapshot.SourceGeometry.Select(point => new MeasurementPoint(point.X, point.Y)).ToArray(),
        Validity = snapshot.Validity.State?.ToLowerInvariant() switch
        {
            "warning" => ScientificValidity.Warning(snapshot.Validity.Reasons.ToArray()),
            "invalid" => ScientificValidity.Invalid(snapshot.Validity.Reasons.ToArray()),
            "reviewrequired" => ScientificValidity.ReviewRequired(snapshot.Validity.Reasons.ToArray()),
            _ => ScientificValidity.Valid,
        },
        Style = new RoiStyle(
            snapshot.Style.StrokeColor,
            snapshot.Style.StrokeWidth,
            snapshot.Style.FillColor,
            snapshot.Style.FillOpacity,
            snapshot.Style.Label,
            snapshot.Style.LabelFont,
            snapshot.Style.LabelColor,
            snapshot.Style.LabelFontSizePt,
            snapshot.Style.LabelIsBold),
        Propagation = snapshot.Propagation is null
            ? null
            : new RoiPropagationProvenance(
                snapshot.Propagation.ReferenceRoiId,
                snapshot.Propagation.TargetRoiId,
                snapshot.Propagation.LinkGroupId,
                snapshot.Propagation.MappingId,
                snapshot.Propagation.TargetCoverageFraction),
    }.EnsureValid();

    private static RoiFigureProjectionObject ToRoiFigureProjection(
        ProjectRoiFigureProjectionSnapshot snapshot) => new()
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

    private static MeasurementOverlayObject ToMeasurementOverlay(ProjectMeasurementOverlaySnapshot snapshot)
    {
        ProjectMeasurementSnapshot source = snapshot.SourceGeometry;
        var geometry = new ScientificMeasurement(
            source.Id,
            source.SourceAssetId,
            ParseMeasurementKind(source.Kind),
            new MeasurementPoint(source.X1, source.Y1),
            new MeasurementPoint(source.X2, source.Y2),
            source.X3.HasValue && source.Y3.HasValue
                ? new MeasurementPoint(source.X3.Value, source.Y3.Value)
                : null,
            PathPoints: source.Points.Select(point => new MeasurementPoint(point.X, point.Y)).ToArray(),
            SourceRevision: source.SourceRevision);
        ProjectMeasurementOverlayStyleSnapshot style = snapshot.Style;
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
        return new MeasurementOverlayObject
        {
            Id = snapshot.Id,
            AssetId = geometry.SourceAssetId,
            PanelId = snapshot.PanelId,
            SourceRevision = geometry.SourceRevision,
            MeasurementId = snapshot.MeasurementId,
            SourceGeometry = geometry,
            CalibrationRelationship = snapshot.CalibrationRelationship is { } calibration
                ? new FigureMeasurementCalibrationRelationship(
                    calibration.SourceAssetId,
                    calibration.SourceRevision,
                    calibration.UnitsPerPixelX,
                    calibration.UnitsPerPixelY,
                    calibration.Unit)
                : null,
            Style = visualStyle,
            LabelOverride = snapshot.LabelOverride,
            IsVisible = snapshot.IsVisible,
            ZIndex = snapshot.ZIndex,
        };
    }

    private static FigureScientificObjectExportItem ToScientificObject(
        ProjectFigureScientificObjectSnapshot snapshot,
        int canvasWidth,
        int canvasHeight)
    {
        FigureScientificPoint[] points = snapshot.Points
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(token =>
            {
                string[] pair = token.Split(',', StringSplitOptions.TrimEntries);
                if (pair.Length != 2 ||
                    !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                    !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                {
                    throw new InvalidDataException("科研对象几何点必须使用 invariant x,y; x,y 格式。");
                }
                return new FigureScientificPoint(x, y);
            }).ToArray();
        FigureChannelLegendEntry[] entries = snapshot.ChannelEntries
            .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Split('|', 3, StringSplitOptions.TrimEntries))
            .Where(pair => pair.Length is 2 or 3)
            .Select(pair => pair.Length == 3 && Guid.TryParse(pair[0], out Guid channelId)
                ? new FigureChannelLegendEntry(pair[1], pair[2], channelId)
                : new FigureChannelLegendEntry(pair[0], pair[1]))
            .ToArray();
        FigureScientificObjectKind kind = ParseScientificObjectKind(snapshot.Kind);
        FigureColorbarExportSpec? colorbar = kind == FigureScientificObjectKind.Colorbar
            ? new FigureColorbarExportSpec(
                snapshot.Minimum,
                snapshot.Maximum,
                snapshot.Unit,
                snapshot.Colormap,
                snapshot.ChannelId,
                ParseColorbarBindingState(snapshot.ColorbarBindingState),
                ParseScientificObjectOrientation(snapshot.Orientation),
                snapshot.Ticks.Select(tick => new ColorbarTick(tick.Value, tick.Label)).ToArray())
            : null;
        FigureChannelLegendExportSpec? channelLegend = kind == FigureScientificObjectKind.ChannelLegend
            ? new FigureChannelLegendExportSpec(
                entries,
                snapshot.FontFamily,
                snapshot.FontSizePt,
                snapshot.IsBold,
                snapshot.TextColor,
                snapshot.FillColor,
                snapshot.FillOpacityPercent,
                snapshot.StrokeColor,
                snapshot.StrokeWidthPt,
                snapshot.ChannelLegendPadding)
            : null;
        var item = new FigureScientificObjectExportItem(
            snapshot.Id,
            kind,
            points,
            snapshot.Label,
            snapshot.StrokeColor,
            snapshot.FillColor,
            snapshot.FillOpacityPercent,
            snapshot.TextColor,
            snapshot.FontFamily,
            snapshot.FontSizePt,
            snapshot.StrokeWidthPt,
            snapshot.IsBold,
            snapshot.Visible,
            snapshot.ZIndex,
            snapshot.Minimum,
            snapshot.Maximum,
            snapshot.Unit,
            snapshot.Colormap,
            entries,
            ChannelId: snapshot.ChannelId,
            Colorbar: colorbar,
            ChannelLegend: channelLegend);
        item.EnsureValid(canvasWidth, canvasHeight);
        return item;
    }

    private static FigureAnnotationExportItem ToAnnotation(ProjectAnnotationSnapshot annotation) => new(
        annotation.Kind,
        annotation.X,
        annotation.Y,
        annotation.EndX,
        annotation.EndY,
        annotation.Text,
        string.IsNullOrWhiteSpace(annotation.StrokeColor) ? annotation.Color : annotation.StrokeColor,
        string.IsNullOrWhiteSpace(annotation.FillColor) ? annotation.Color : annotation.FillColor,
        annotation.FillOpacityPercent,
        string.IsNullOrWhiteSpace(annotation.TextColor) ? annotation.Color : annotation.TextColor,
        string.IsNullOrWhiteSpace(annotation.FontFamily) ? "Arial" : annotation.FontFamily,
        annotation.FontSizePt,
        annotation.StrokeWidthPt,
        annotation.IsBold,
        annotation.Visible,
        annotation.ZIndex);

    private static FigureGlobalStyle ToGlobalStyle(ProjectGlobalStyleSnapshot? saved) => saved is null
        ? FigureGlobalStyle.Default
        : new FigureGlobalStyle(
            saved.FontFamily,
            saved.FontSizePt,
            saved.StrokeWidthPt,
            saved.TextColor,
            saved.ShapeColor,
            saved.ScaleBarColor,
            saved.PanelLabelFontFamily,
            saved.PanelLabelFontSizePt,
            saved.PanelLabelTextColor,
            saved.PanelLabelIsBold,
            saved.ScaleBarLabelColor,
            saved.ScaleBarFontFamily,
            saved.ScaleBarFontSizePt,
            saved.ScaleBarLabelIsBold,
            saved.ScaleBarThicknessPt);

    private static StyleOverride? ToStyleOverride(ProjectPanelStyleOverrideSnapshot? snapshot)
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
                    ParseScaleBarAnchor(snapshot.ScaleBar.DefaultPosition),
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

    private static FigurePlotTypographyOverride? ToPlotTypographyOverride(
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

    private static int ResolveCanvasDpi(SciCanvasProjectDocument project) =>
        project.ExportProfiles.FirstOrDefault()?.Dpi is > 0 and var dpi ? dpi : 300;

    private static PixelRect64 ToRect(ProjectPixelRectSnapshot snapshot) =>
        new(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height);

    private static SciCanvas.Core.Images.ImageAdjustmentParameters ToAdjustment(
        ProjectImageAdjustmentSnapshot snapshot) => new()
    {
        Brightness = snapshot.Brightness,
        Contrast = snapshot.Contrast,
        Gamma = snapshot.Gamma,
        BlackPoint = snapshot.BlackPoint,
        WhitePoint = snapshot.WhitePoint,
        Invert = snapshot.Invert,
        Grayscale = snapshot.Grayscale,
        Channel = snapshot.Channel,
    };

    private static ScaleBarAnchor ParseScaleBarAnchor(string? value) => value?.ToLowerInvariant() switch
    {
        "bottomleft" => ScaleBarAnchor.BottomLeft,
        "topleft" => ScaleBarAnchor.TopLeft,
        "topright" => ScaleBarAnchor.TopRight,
        "custom" => ScaleBarAnchor.Custom,
        _ => ScaleBarAnchor.BottomRight,
    };

    private static FigureScientificObjectKind ParseScientificObjectKind(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "polygonannotation" => FigureScientificObjectKind.PolygonAnnotation,
            "directionmarker" => FigureScientificObjectKind.DirectionMarker,
            "colorbar" => FigureScientificObjectKind.Colorbar,
            "channellegend" => FigureScientificObjectKind.ChannelLegend,
            _ => throw new InvalidDataException($"工程包含未知科研对象类型：{value}"),
        };

    private static ColorbarBindingState ParseColorbarBindingState(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "linked" => ColorbarBindingState.Linked,
            "detached" => ColorbarBindingState.Detached,
            _ => throw new InvalidDataException($"工程包含未知 Colorbar 绑定状态：{value}"),
        };

    private static FigureObjectOrientation ParseScientificObjectOrientation(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "vertical" => FigureObjectOrientation.Vertical,
            "horizontal" => FigureObjectOrientation.Horizontal,
            _ => throw new InvalidDataException($"工程包含未知科研对象方向：{value}"),
        };

    private static ScientificMeasurementKind ParseMeasurementKind(string? value) =>
        value?.ToLowerInvariant() switch
        {
            "length" => ScientificMeasurementKind.Length,
            "angle" => ScientificMeasurementKind.Angle,
            "rectangleroi" => ScientificMeasurementKind.RectangleRoi,
            "circleroi" => ScientificMeasurementKind.CircleRoi,
            "polyline" => ScientificMeasurementKind.Polyline,
            _ => throw new InvalidDataException($"工程包含未知测量类型：{value}"),
        };

    private static LinkingSpatialMappingKind ParseMappingKind(string? value) => value?.ToLowerInvariant() switch
    {
        "identity" => LinkingSpatialMappingKind.Identity,
        "translation" => LinkingSpatialMappingKind.Translation,
        "rigid" => LinkingSpatialMappingKind.Rigid,
        "affine" => LinkingSpatialMappingKind.Affine,
        _ => throw new InvalidDataException("工程包含未知 SpatialMapping 类型。"),
    };

    private static LinkingSpatialMappingOrigin ParseMappingOrigin(string? value) => value?.ToLowerInvariant() switch
    {
        "userdeclaredidentity" => LinkingSpatialMappingOrigin.UserDeclaredIdentity,
        "userdeclaredtranslation" => LinkingSpatialMappingOrigin.UserDeclaredTranslation,
        "manuallandmarks" => LinkingSpatialMappingOrigin.ManualLandmarks,
        "importedmetadata" => LinkingSpatialMappingOrigin.ImportedMetadata,
        _ => throw new InvalidDataException("工程包含未知 SpatialMapping 来源。"),
    };
}
