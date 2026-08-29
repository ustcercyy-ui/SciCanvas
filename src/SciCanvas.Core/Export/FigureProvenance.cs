using System.Text.Encodings.Web;
using System.Text.Json;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

public sealed record FigureProvenanceSource(
    Guid Id,
    string DisplayName,
    string OriginalPath,
    string Sha256,
    long ByteLength,
    long Width,
    long Height,
    int BitsPerChannel,
    int Channels,
    string PixelFormat,
    double? DpiX,
    double? DpiY,
    string? PhysicalUnit,
    int FrameCount,
    OmeImageMetadata? Ome = null,
    long SourceRevision = 1);

public sealed record FigureProvenancePanel(
    string Label,
    Guid SourceId,
    int FrameIndex,
    PixelRect64 SourceRect,
    PixelRect64 DestinationRect,
    bool Visible,
    ImageAdjustmentParameters Adjustments,
    Guid PanelId = default,
    Guid? CompositeGroupId = null,
    IReadOnlyList<Guid>? ChannelIds = null);

public sealed record FigureProvenanceMeasurementOverlay(
    Guid OverlayId,
    Guid MeasurementId,
    Guid SourceAssetId,
    long SourceRevision,
    Guid PanelId,
    string MeasurementKind,
    ScientificMeasurement SourceGeometry,
    FigureMeasurementCalibrationRelationship? CalibrationRelationship,
    FigureMeasurementOverlayStyle Style,
    bool Visible,
    int ZIndex);
public sealed record FigureProvenanceFontResolution(
    string RequestedFont,
    string EffectiveFont,
    string ResolutionKind,
    string? SubstitutionRule);

public sealed record FigureProvenanceScientificObject(
    Guid ObjectId,
    string ObjectKind,
    Guid? SourceAssetId,
    long? SourceRevision,
    Guid? ChannelId,
    IReadOnlyList<FigureScientificPoint> Geometry,
    string Label,
    string StrokeColor,
    string FillColor,
    double FillOpacityPercent,
    string TextColor,
    string RequestedFont,
    string EffectiveFont,
    double FontSizePt,
    double StrokeWidthPt,
    bool Visible,
    int ZIndex,
    FigureProvenanceFontResolution? StyleResolution);

public sealed record FigureProvenanceChannel(
    Guid GroupId,
    Guid ChannelId,
    string ChannelName,
    Guid SourceAssetId,
    long SourceRevision,
    int Frame,
    int BitDepth,
    double DisplayMinimum,
    double DisplayMaximum,
    double Gamma,
    string Color,
    double Opacity,
    string RenderMode,
    string BlendMode);

public sealed record FigureProvenanceRegistration(
    Guid LinkGroupId,
    Guid MappingId,
    Guid SourceAssetId,
    Guid TargetAssetId,
    long SourceRevision,
    long TargetRevision,
    string Kind,
    SciCanvas.Core.Linking.SpatialMatrix3x3 Matrix,
    IReadOnlyList<SciCanvas.Core.Linking.RegistrationLandmarkPair> Landmarks,
    double? RmsPixels,
    double? RmsPhysical,
    string? RmsPhysicalUnit,
    string Origin);

public sealed record FigureProvenanceRoiPropagation(
    Guid ReferenceRoiId,
    Guid TargetRoiId,
    Guid LinkGroupId,
    Guid MappingId);

public sealed record FigureProvenanceColorbar(
    Guid ObjectId,
    double Minimum,
    double Maximum,
    string Unit,
    string Colormap,
    Guid? ChannelId);

public sealed record FigureProvenanceChannelLegendEntry(
    Guid? ChannelId,
    string Label,
    string Color);

public sealed record FigureProvenanceChannelLegend(
    Guid ObjectId,
    IReadOnlyList<FigureProvenanceChannelLegendEntry> Entries);

public sealed record FigureProvenancePdfFont(
    string RequestedFont,
    string EffectiveFont,
    string? Substitution,
    string PdfFontStrategy,
    bool Embedded,
    bool Outlined,
    string? FallbackReason);

public sealed record FigureProvenanceAnalysis(
    Guid AnalysisId,
    string Kind,
    Guid SourceAssetId,
    long SourceRevision,
    int FrameIndex,
    string Channel,
    string AlgorithmId,
    string AlgorithmVersion,
    DateTimeOffset AnalyzedAt,
    string State,
    IReadOnlyDictionary<string, object?> Parameters);

public sealed record FigureProvenanceDocument(
    string Software,
    string SoftwareVersion,
    DateTimeOffset ExportedAt,
    string ExportPath,
    string Format,
    int WidthPixels,
    int HeightPixels,
    int Dpi,
    int BitDepth,
    string BackgroundColor,
    IReadOnlyList<FigureProvenanceSource> Sources,
    IReadOnlyList<FigureProvenancePanel> Panels,
    IReadOnlyList<FigurePreflightIssue> PreflightIssues,
    string? ExportProfileId = null,
    string? ExportProfileName = null,
    IReadOnlyList<FigureProvenanceAnalysis>? Analyses = null,
    IReadOnlyList<FigureProvenanceMeasurementOverlay>? MeasurementOverlays = null,
    IReadOnlyList<FigureProvenanceScientificObject>? ScientificObjects = null,
    IReadOnlyList<FigureProvenanceChannel>? Channels = null,
    IReadOnlyList<FigureProvenanceRegistration>? Registrations = null,
    IReadOnlyList<FigureProvenanceRoiPropagation>? RoiPropagations = null,
    IReadOnlyList<FigureProvenanceColorbar>? Colorbars = null,
    IReadOnlyList<FigureProvenanceChannelLegend>? ChannelLegends = null,
    IReadOnlyList<FigureProvenanceFontResolution>? FontResolutions = null,
    IReadOnlyList<FigureProvenancePdfFont>? PdfFonts = null);

public static class FigureProvenanceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteJson(FigureProvenanceDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        WriteNewFile(path, JsonSerializer.Serialize(document, JsonOptions));
    }

    public static void WriteHtml(FigureProvenanceDocument document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        string json = JsonSerializer.Serialize(document, JsonOptions);
        string encoded = System.Net.WebUtility.HtmlEncode(json);
        string html = $"<!doctype html><meta charset=\"utf-8\"><title>SciCanvas export report</title>" +
                      "<style>body{font:14px system-ui;max-width:1100px;margin:32px auto;padding:0 18px}" +
                      "code,pre{background:#f4f5f7;padding:12px;border-radius:6px;white-space:pre-wrap}" +
                      "h1{font-size:22px}</style>" +
                      "<h1>SciCanvas 投稿导出报告</h1><p>此报告由本地 SciCanvas 自动生成，记录源图指纹、裁剪、布局和处理参数。</p>" +
                      $"<pre>{encoded}</pre>";
        WriteNewFile(path, html);
    }

    public static FigureProvenanceDocument Create(
        FigureExportDocument document,
        string exportPath,
        string softwareVersion,
        IReadOnlyCollection<SourceAsset> sources,
        FigurePreflightResult preflight,
        string? exportProfileId = null,
        string? exportProfileName = null,
        IReadOnlyDictionary<Guid, long>? sourceRevisions = null,
        IEnumerable<ScientificImageAnalysisResult>? analyses = null,
        IEnumerable<ResolvedFont>? fontResolutions = null,
        IEnumerable<SciCanvas.Core.Linking.LinkGroup>? linkGroups = null,
        IEnumerable<RoiObject>? rois = null)
    {
        ResolvedFont[] resolvedFonts = (fontResolutions ?? CreateExactFontResolutions(document))
            .DistinctBy(item => item.RequestedFamily, StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item.RequestedFamily, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        FigureProvenanceFontResolution[] fontRecords = resolvedFonts
            .Select(CreateFontResolutionProvenance)
            .ToArray();
        return new FigureProvenanceDocument(
            "SciCanvas",
            softwareVersion,
            DateTimeOffset.UtcNow,
            exportPath,
            Path.GetExtension(exportPath).TrimStart('.').ToLowerInvariant(),
            document.WidthPixels,
            document.HeightPixels,
            document.Dpi,
            document.BitDepth,
            document.BackgroundColor,
            sources.Select(source => new FigureProvenanceSource(
                source.Id,
                source.DisplayName,
                source.OriginalPath,
                source.Fingerprint.Sha256,
                source.Fingerprint.ByteLength,
                source.Metadata.PixelSize.Width,
                source.Metadata.PixelSize.Height,
                source.Metadata.BitsPerChannel,
                source.Metadata.Channels,
                source.Metadata.PixelFormat,
                source.Metadata.DpiX,
                source.Metadata.DpiY,
                source.Metadata.PhysicalUnit,
                source.Metadata.FrameCount,
                source.Metadata.Ome,
                sourceRevisions?.GetValueOrDefault(source.Id) ?? 1)).ToArray(),
            document.Panels.Select(panel => new FigureProvenancePanel(
                panel.Label,
                panel.Source.Id,
                panel.FrameIndex,
                panel.SourceRect,
                panel.DestinationRect,
                panel.IsVisible,
                (panel.Adjustments ?? new()).Normalize(),
                panel.PanelId,
                panel.IsComposite ? panel.EffectiveChannelLayers[0].GroupId : null,
                panel.EffectiveChannelLayers.Select(layer => layer.ChannelSelector.Id).ToArray())).ToArray(),
            preflight.Issues,
            exportProfileId,
            exportProfileName,
            (analyses ?? []).Select(CreateAnalysisProvenance).ToArray(),
            document.MeasurementOverlays.Select(overlay => new FigureProvenanceMeasurementOverlay(
                overlay.Id,
                overlay.MeasurementId,
                overlay.SourceAssetId,
                overlay.SourceRevision,
                overlay.PanelId,
                overlay.MeasurementKind.ToString(),
                overlay.SourceGeometry,
                overlay.CalibrationRelationship,
                overlay.Style,
                overlay.IsVisible,
                overlay.ZIndex)).ToArray(),
            document.ScientificObjects.Select(item => CreateScientificObjectProvenance(item, resolvedFonts)).ToArray(),
            document.Panels.SelectMany(panel => panel.EffectiveChannelLayers)
                .Select(layer => new FigureProvenanceChannel(
                    layer.GroupId,
                    layer.ChannelSelector.Id,
                    layer.ChannelSelector.Name,
                    layer.Source.Id,
                    layer.SourceRevision,
                    layer.FrameIndex,
                    layer.ChannelSelector.BitDepth,
                    layer.DisplaySettings.DisplayMinimum,
                    layer.DisplaySettings.DisplayMaximum,
                    layer.DisplaySettings.Gamma,
                    layer.DisplaySettings.Color,
                    layer.DisplaySettings.Opacity,
                    layer.RenderMode,
                    layer.BlendMode))
                .DistinctBy(item => (item.GroupId, item.ChannelId, item.SourceAssetId, item.Frame))
                .ToArray(),
            (linkGroups ?? []).SelectMany(group => group.Mappings.Select(mapping =>
                new FigureProvenanceRegistration(
                    group.Id,
                    mapping.Id,
                    mapping.SourceAssetId,
                    mapping.TargetAssetId,
                    mapping.SourceRevision,
                    mapping.TargetRevision,
                    mapping.Kind.ToString(),
                    mapping.Matrix,
                    mapping.EffectiveLandmarks,
                    mapping.ResidualPixels,
                    mapping.ResidualPhysical,
                    mapping.ResidualPhysicalUnit,
                    mapping.Origin.ToString()))).ToArray(),
            (rois ?? []).Where(roi => roi.Propagation is not null)
                .Select(roi => new FigureProvenanceRoiPropagation(
                    roi.Propagation!.ReferenceRoiId,
                    roi.Propagation.TargetRoiId,
                    roi.Propagation.LinkGroupId,
                    roi.Propagation.MappingId)).ToArray(),
            document.ScientificObjects.Where(item => item.Kind == FigureScientificObjectKind.Colorbar)
                .Select(item => new FigureProvenanceColorbar(
                    item.Id, item.Minimum, item.Maximum, item.Unit, item.Colormap, item.ChannelId)).ToArray(),
            document.ScientificObjects.Where(item => item.Kind == FigureScientificObjectKind.ChannelLegend)
                .Select(item => new FigureProvenanceChannelLegend(
                    item.Id,
                    item.EffectiveChannelLegendEntries.Select(entry =>
                        new FigureProvenanceChannelLegendEntry(null, entry.Label, entry.Color)).ToArray())).ToArray(),
            fontRecords,
            resolvedFonts.Select(font => CreatePdfFontProvenance(font, document.PdfFontStrategy)).ToArray());
    }

    private static FigureProvenanceScientificObject CreateScientificObjectProvenance(
        FigureScientificObjectExportItem item,
        IReadOnlyList<ResolvedFont> resolutions)
    {
        ResolvedFont? resolution = resolutions.FirstOrDefault(candidate =>
            string.Equals(candidate.EffectiveFamily, item.FontFamily, StringComparison.OrdinalIgnoreCase));
        return new FigureProvenanceScientificObject(
            item.Id,
            item.Kind.ToString(),
            item.SourceAssetId,
            item.SourceRevision,
            item.ChannelId,
            item.Points,
            item.Label,
            item.StrokeColor,
            item.FillColor,
            item.FillOpacityPercent,
            item.TextColor,
            resolution?.RequestedFamily ?? item.FontFamily,
            item.FontFamily,
            item.FontSizePt,
            item.StrokeWidthPt,
            item.IsVisible,
            item.ZIndex,
            resolution is null ? null : CreateFontResolutionProvenance(resolution));
    }

    private static FigureProvenanceFontResolution CreateFontResolutionProvenance(ResolvedFont font) => new(
        font.RequestedFamily,
        font.EffectiveFamily,
        font.ResolutionKind.ToString(),
        font.SubstitutionRule is null
            ? null
            : $"{font.SubstitutionRule.RequestedFontFamily} -> {font.SubstitutionRule.SubstituteFontFamily}");

    private static FigureProvenancePdfFont CreatePdfFontProvenance(
        ResolvedFont font,
        PdfFontStrategy strategy)
    {
        bool outlined = strategy != PdfFontStrategy.EmbedSubsetWhenPermitted;
        string? fallback = strategy == PdfFontStrategy.PreferEmbeddedWithOutlineFallback
            ? "the current PDF writer has no reliable subset/ToUnicode implementation"
            : strategy == PdfFontStrategy.EmbedSubsetWhenPermitted
                ? "strict embedding must pass preflight before export"
                : null;
        return new FigureProvenancePdfFont(
            font.RequestedFamily,
            font.EffectiveFamily,
            font.SubstitutionRule?.SubstituteFontFamily,
            strategy.ToString(),
            Embedded: false,
            Outlined: outlined,
            FallbackReason: fallback);
    }

    private static IEnumerable<ResolvedFont> CreateExactFontResolutions(FigureExportDocument document)
    {
        IEnumerable<string> fonts =
        [
            document.GlobalStyle.FontFamily,
            document.GlobalStyle.EffectivePanelLabelFontFamily,
            document.GlobalStyle.EffectiveScaleBarFontFamily,
            .. document.Annotations.Select(item => item.FontFamily),
            .. document.MeasurementOverlays.Select(item => item.Style.LabelFontFamily),
            .. document.ScientificObjects.Select(item => item.FontFamily),
        ];
        return fonts.Where(font => !string.IsNullOrWhiteSpace(font))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(font => new ResolvedFont(font, font, FontResolutionKind.Exact));
    }

    private static FigureProvenanceAnalysis CreateAnalysisProvenance(
        ScientificImageAnalysisResult result)
    {
        string analyzerId = result.AnalyzerId.Trim();
        int versionSeparator = analyzerId.LastIndexOf(".v", StringComparison.OrdinalIgnoreCase);
        string version = versionSeparator >= 0 && versionSeparator + 2 < analyzerId.Length
            ? analyzerId[(versionSeparator + 2)..]
            : "unspecified";
        IReadOnlyDictionary<string, object?> parameters = result switch
        {
            RoiStatisticsResult roi => new Dictionary<string, object?>
            {
                ["region"] = roi.Region,
                ["sourceBitDepth"] = roi.SourceBitDepth,
                ["histogramBinCount"] = roi.Histogram.Bins.Count,
            },
            IntensityProfileResult profile => new Dictionary<string, object?>
            {
                ["sampleCount"] = profile.Samples.Count,
                ["distanceUnit"] = profile.DistanceUnit,
                ["sourceBitDepth"] = profile.SourceBitDepth,
            },
            AssistedRegionAnalysisResult particles => new Dictionary<string, object?>
            {
                ["mode"] = particles.Options.Mode.ToString(),
                ["regionOfInterest"] = particles.Options.RegionOfInterest,
                ["useAutomaticThreshold"] = particles.Options.UseAutomaticThreshold,
                ["requestedThreshold"] = particles.Options.ThresholdNormalized,
                ["appliedThreshold"] = particles.AppliedThresholdNormalized,
                ["minimumAreaPixels"] = particles.Options.MinimumAreaPixels,
                ["resultLimit"] = null,
                ["particleCount"] = particles.Candidates.Count,
                ["sourceBitDepth"] = particles.SourceBitDepth,
            },
            _ => new Dictionary<string, object?>(),
        };
        return new FigureProvenanceAnalysis(
            result.Id,
            result.Kind.ToString(),
            result.SourceAssetId,
            result.SourceRevision,
            result.FrameIndex,
            result.Channel.ToString(),
            analyzerId,
            version,
            result.AnalyzedAt,
            result.Validity.State.ToString(),
            parameters);
    }

    private static void WriteNewFile(string path, string content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
