using System.Text.Encodings.Web;
using System.Text.Json;

namespace SciCanvas.Core.Export;

public enum JournalPresetCollisionPolicy
{
    RequireDecision,
    GenerateNewId,
    Replace,
}

public sealed class JournalPresetCollisionException(IReadOnlyList<string> collidingIds)
    : InvalidOperationException($"Journal preset ID collision requires an explicit decision: {string.Join(", ", collidingIds)}")
{
    public IReadOnlyList<string> CollidingIds { get; } = collidingIds;
}

public sealed record JournalPresetImportPreview(
    string Name,
    double WidthMm,
    int Dpi,
    IReadOnlyList<string> Formats,
    string ColorMode);

public sealed record JournalPresetImportResult(
    IReadOnlyList<JournalExportPreset> Presets,
    IReadOnlyList<JournalPresetImportPreview> Preview,
    IReadOnlyList<string> ReplacedIds,
    IReadOnlyDictionary<string, string> GeneratedIds);

/// <summary>Portable single-preset and team-preset-pack JSON format.</summary>
public static class JournalPresetPortability
{
    public const string CurrentFormatVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = false,
    };

    public static string ExportPreset(JournalExportPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        return JsonSerializer.Serialize(PresetFileDto.FromPreset(preset), JsonOptions);
    }

    public static string ExportPack(
        string packName,
        IEnumerable<JournalExportPreset> presets,
        string? description = null,
        string? organization = null)
    {
        if (string.IsNullOrWhiteSpace(packName) || packName.Trim().Length > 256)
        {
            throw new ArgumentException("Preset pack name 无效。", nameof(packName));
        }

        PresetFileDto[] items = (presets ?? throw new ArgumentNullException(nameof(presets)))
            .Select(PresetFileDto.FromPreset)
            .ToArray();
        if (items.Length == 0 || items.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != items.Length)
        {
            throw new InvalidOperationException("Preset pack 必须包含至少一个且 ID 唯一的 preset。");
        }

        return JsonSerializer.Serialize(new PresetPackDto
        {
            FormatVersion = CurrentFormatVersion,
            Name = packName.Trim(),
            Description = NormalizeOptional(description),
            Organization = NormalizeOptional(organization),
            Presets = items,
        }, JsonOptions);
    }

    public static IReadOnlyList<JournalPresetImportPreview> PreviewImport(string json) =>
        Parse(json)
            .Select(preset => new JournalPresetImportPreview(
                preset.Name,
                preset.FigureWidthMm,
                preset.MinimumDpi,
                preset.AllowedFormats,
                preset.ColorMode))
            .ToArray();

    public static JournalPresetImportResult Import(
        IEnumerable<JournalExportPreset> existingPresets,
        string json,
        JournalPresetCollisionPolicy collisionPolicy = JournalPresetCollisionPolicy.RequireDecision)
    {
        JournalExportPreset[] imported = Parse(json).ToArray();
        var result = (existingPresets ?? throw new ArgumentNullException(nameof(existingPresets))).ToList();
        string[] collisions = imported
            .Where(item => result.Any(existing => string.Equals(existing.Id, item.Id, StringComparison.OrdinalIgnoreCase)))
            .Select(item => item.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (collisions.Length > 0 && collisionPolicy == JournalPresetCollisionPolicy.RequireDecision)
        {
            throw new JournalPresetCollisionException(collisions);
        }

        var replacements = new List<string>();
        var generatedIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (JournalExportPreset preset in imported)
        {
            int existingIndex = result.FindIndex(item => string.Equals(item.Id, preset.Id, StringComparison.OrdinalIgnoreCase));
            if (existingIndex < 0)
            {
                result.Add(preset);
                continue;
            }

            if (collisionPolicy == JournalPresetCollisionPolicy.Replace)
            {
                result[existingIndex] = preset;
                replacements.Add(preset.Id);
                continue;
            }

            string generatedId = GenerateId(preset.Id, result.Select(item => item.Id));
            generatedIds[preset.Id] = generatedId;
            result.Add(CloneWithId(preset, generatedId));
        }

        JournalPresetImportPreview[] preview = imported.Select(preset => new JournalPresetImportPreview(
            preset.Name,
            preset.FigureWidthMm,
            preset.MinimumDpi,
            preset.AllowedFormats,
            preset.ColorMode)).ToArray();
        return new JournalPresetImportResult(
            Array.AsReadOnly(result.ToArray()),
            Array.AsReadOnly(preview),
            Array.AsReadOnly(replacements.ToArray()),
            generatedIds);
    }

    private static IReadOnlyList<JournalExportPreset> Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("Journal preset JSON 为空。");
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("Journal preset JSON root 必须是 object。");
            }

            if (root.TryGetProperty("presets", out _))
            {
                PresetPackDto pack = JsonSerializer.Deserialize<PresetPackDto>(root.GetRawText(), JsonOptions) ??
                                     throw new InvalidDataException("Preset pack 无法解析。");
                EnsureFormatVersion(pack.FormatVersion);
                if (string.IsNullOrWhiteSpace(pack.Name) || pack.Presets.Length == 0 ||
                    pack.Presets.Select(item => item.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pack.Presets.Length)
                {
                    throw new InvalidDataException("Preset pack 缺少名称、preset 或包含重复 ID。");
                }

                return pack.Presets.Select(item => item.ToPreset()).ToArray();
            }

            PresetFileDto single = JsonSerializer.Deserialize<PresetFileDto>(root.GetRawText(), JsonOptions) ??
                                   throw new InvalidDataException("Journal preset 无法解析。");
            EnsureFormatVersion(single.FormatVersion);
            return [single.ToPreset()];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Journal preset JSON syntax 或字段类型无效。", exception);
        }
    }

    private static void EnsureFormatVersion(string? version)
    {
        if (!string.Equals(version, CurrentFormatVersion, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"不支持的 journal preset formatVersion：{version ?? "<missing>"}。");
        }
    }

    private static string GenerateId(string requestedId, IEnumerable<string> occupiedIds)
    {
        HashSet<string> occupied = occupiedIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (int suffix = 2; suffix < int.MaxValue; suffix++)
        {
            string candidate = $"{requestedId}-{suffix}";
            if (!occupied.Contains(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("无法为冲突 preset 生成唯一 ID。");
    }

    private static JournalExportPreset CloneWithId(JournalExportPreset preset, string id) => new(
        id,
        preset.Name,
        preset.FigureWidthMm,
        preset.FigureHeightMm,
        preset.MinimumDpi,
        preset.PreferredFormat,
        preset.AllowedFormats,
        preset.ColorMode,
        preset.MaximumFileSizeMb,
        preset.Description,
        preset.FontRecommendations,
        preset.MinimumLineWidthPt,
        preset.Notes,
        preset.SourceMetadata);

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class PresetPackDto
    {
        public string FormatVersion { get; set; } = CurrentFormatVersion;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? Organization { get; set; }

        public PresetFileDto[] Presets { get; set; } = [];
    }

    private sealed class PresetFileDto
    {
        public string FormatVersion { get; set; } = CurrentFormatVersion;

        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public double FigureWidthMm { get; set; }

        public double? FigureHeightMm { get; set; }

        public int MinimumDpi { get; set; }

        public string PreferredFormat { get; set; } = string.Empty;

        public string[] AllowedFormats { get; set; } = [];

        public string ColorMode { get; set; } = string.Empty;

        public double? MaximumFileSizeMb { get; set; }

        public string[] FontRecommendations { get; set; } = [];

        public double? MinimumLineWidthPt { get; set; }

        public string? Notes { get; set; }

        public JournalPresetSourceMetadata? SourceMetadata { get; set; }

        public JournalExportPreset ToPreset() => new(
            Id,
            Name,
            FigureWidthMm,
            FigureHeightMm,
            MinimumDpi,
            PreferredFormat,
            AllowedFormats,
            ColorMode,
            MaximumFileSizeMb,
            Description,
            FontRecommendations,
            MinimumLineWidthPt,
            Notes,
            SourceMetadata);

        public static PresetFileDto FromPreset(JournalExportPreset preset) => new()
        {
            FormatVersion = CurrentFormatVersion,
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
            SourceMetadata = preset.SourceMetadata,
        };
    }
}
