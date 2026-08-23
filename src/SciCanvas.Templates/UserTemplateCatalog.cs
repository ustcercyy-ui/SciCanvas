using System.Text.Json;
using System.Text.RegularExpressions;

namespace SciCanvas.Templates;

public sealed partial class UserTemplateCatalog : IUserTemplateCatalog
{
    private const int MaximumTemplateBytes = 1024 * 1024;
    private static readonly HashSet<string> AllowedCategories = new(StringComparer.Ordinal)
    {
        "general", "morphology", "structure-performance", "energy-storage",
        "electrocatalysis", "optoelectronics", "mechanics", "thermal-transport",
        "time-series", "comparison-matrix", "extended-data",
    };
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 64,
    };

    private readonly string _storageDirectory;
    private readonly HashSet<string> _reservedTemplateIds;

    public UserTemplateCatalog(
        string? storageDirectory = null,
        IEnumerable<string>? reservedTemplateIds = null)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SciCanvas",
            "Templates");
        _reservedTemplateIds = reservedTemplateIds?.ToHashSet(StringComparer.Ordinal) ?? [];
    }

    public IReadOnlyList<FigureTemplateDefinition> LoadInstalled()
    {
        if (!Directory.Exists(_storageDirectory))
        {
            return [];
        }

        List<FigureTemplateDefinition> templates = [];
        foreach (string path in Directory.EnumerateFiles(_storageDirectory, "*.json")
                     .OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                templates.Add(ReadAndValidate(path).Template);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or JsonException)
            {
                // One damaged user file must not prevent SciCanvas from starting.
            }
        }

        return templates
            .GroupBy(template => template.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }

    public FigureTemplateDefinition ImportFromFile(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        (FigureTemplateDefinition template, byte[] json) = ReadAndValidate(sourcePath);

        Directory.CreateDirectory(_storageDirectory);
        string targetPath = Path.Combine(_storageDirectory, $"{template.Id}.json");
        if (File.Exists(targetPath))
        {
            throw new IOException($"用户模板 {template.Id} 已安装；请修改模板 ID 后再导入。");
        }

        if (_reservedTemplateIds.Contains(template.Id))
        {
            throw new IOException($"模板 ID {template.Id} 与内置模板冲突，请修改后再导入。");
        }

        string temporaryPath = Path.Combine(
            _storageDirectory,
            $".{template.Id}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var output = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                output.Write(json);
                output.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, targetPath);
        }
        finally
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return template;
    }

    private static (FigureTemplateDefinition Template, byte[] Json) ReadAndValidate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("找不到要导入的模板文件。", path);
        }

        if (info.Length is <= 0 or > MaximumTemplateBytes)
        {
            throw new InvalidDataException("模板文件必须大于 0 字节且不超过 1 MiB。");
        }

        byte[] json = File.ReadAllBytes(info.FullName);
        using JsonDocument document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 64,
        });
        ValidateEnvelope(document.RootElement);

        FigureTemplateDefinition template = JsonSerializer.Deserialize<FigureTemplateDefinition>(json, JsonOptions)
            ?? throw new InvalidDataException("模板 JSON 不能反序列化为 SciCanvas 模板。");
        BuiltInTemplateCatalog.Validate(template);
        ValidateLayoutLimits(template);
        return (template, json);
    }

    private static void ValidateEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetRequiredString(root, "schemaVersion", out string? schemaVersion) ||
            schemaVersion != "0.1" ||
            !TryGetRequiredString(root, "version", out string? version) ||
            version is null ||
            !SemanticVersionRegex().IsMatch(version) ||
            !TryGetRequiredString(root, "id", out string? id) ||
            id is null ||
            !TemplateIdRegex().IsMatch(id) ||
            !TryGetRequiredString(root, "category", out string? category) ||
            category is null ||
            !AllowedCategories.Contains(category) ||
            !root.TryGetProperty("validators", out JsonElement validators) ||
            validators.ValueKind != JsonValueKind.Array ||
            !root.TryGetProperty("provenance", out JsonElement provenance) ||
            provenance.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException(
                "模板缺少有效的 schemaVersion、version、id、category、validators 或 provenance 字段。");
        }
    }

    private static void ValidateLayoutLimits(FigureTemplateDefinition template)
    {
        bool validCanvas = template.Canvas.Dpi is >= 1 and <= 2400 &&
                           template.Canvas.WidthPx is null or (>= 1 and <= 200000) &&
                           template.Canvas.HeightPx is null or (>= 1 and <= 200000) &&
                           template.Canvas.WidthMm is null or (> 0 and <= 2000) &&
                           template.Canvas.HeightMm is null or (> 0 and <= 2000);
        bool validGrid = template.Grid.Columns is >= 1 and <= 64 &&
                         template.Grid.Rows is >= 1 and <= 64 &&
                         template.Grid.GutterX is >= 0 and <= 1000 &&
                         template.Grid.GutterY is >= 0 and <= 1000 &&
                         template.Grid.Margin.Top is >= 0 and <= 1000 &&
                         template.Grid.Margin.Right is >= 0 and <= 1000 &&
                         template.Grid.Margin.Bottom is >= 0 and <= 1000 &&
                         template.Grid.Margin.Left is >= 0 and <= 1000 &&
                         template.Slots.Count <= 256;
        if (!validCanvas || !validGrid)
        {
            throw new InvalidDataException("模板画布或网格参数超出 SciCanvas 的安全范围。");
        }
    }

    private static bool TryGetRequiredString(JsonElement root, string name, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    [GeneratedRegex("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TemplateIdRegex();

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionRegex();
}
