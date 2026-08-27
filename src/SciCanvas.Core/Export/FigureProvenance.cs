using System.Text.Encodings.Web;
using System.Text.Json;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;

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
    ImageAdjustmentParameters Adjustments);

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
    IReadOnlyList<FigureProvenanceAnalysis>? Analyses = null);

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
        IEnumerable<ScientificImageAnalysisResult>? analyses = null)
    {
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
                (panel.Adjustments ?? new()).Normalize())).ToArray(),
            preflight.Issues,
            exportProfileId,
            exportProfileName,
            (analyses ?? []).Select(CreateAnalysisProvenance).ToArray());
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
                ["maximumCandidates"] = particles.Options.MaximumCandidates,
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
