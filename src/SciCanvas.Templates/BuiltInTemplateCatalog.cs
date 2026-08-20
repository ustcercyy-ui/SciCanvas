using System.Reflection;
using System.Text.Json;

namespace SciCanvas.Templates;

public sealed class BuiltInTemplateCatalog
{
    private static readonly string[] TemplateResources =
    [
        "SciCanvas.Templates.Builtin.multiscale-morphology.nature-double.json",
        "SciCanvas.Templates.Builtin.comparison-2x2.nature-double.json",
        "SciCanvas.Templates.Builtin.synthesis-structure-performance.nature-double.json",
        "SciCanvas.Templates.Builtin.energy-storage-electrochemistry.nature-double.json",
        "SciCanvas.Templates.Builtin.phase-structure-mechanism.nature-double.json",
        "SciCanvas.Templates.Builtin.mechanics-fracture.nature-double.json",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public IReadOnlyList<FigureTemplateDefinition> LoadAll()
    {
        Assembly assembly = typeof(BuiltInTemplateCatalog).Assembly;
        List<FigureTemplateDefinition> templates = [];
        foreach (string resourceName in TemplateResources)
        {
            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"找不到内置材料组图模板资源：{resourceName}");
            FigureTemplateDefinition template = JsonSerializer.Deserialize<FigureTemplateDefinition>(
                stream,
                JsonOptions) ?? throw new InvalidOperationException("无法读取内置材料组图模板。");
            Validate(template);
            templates.Add(template);
        }

        if (templates.Select(template => template.Id).Distinct(StringComparer.Ordinal).Count() != templates.Count)
        {
            throw new InvalidOperationException("内置材料组图模板 ID 重复。");
        }

        return templates;
    }

    private static void Validate(FigureTemplateDefinition template)
    {
        if (string.IsNullOrWhiteSpace(template.Id) ||
            string.IsNullOrWhiteSpace(template.Name) ||
            template.Canvas.Dpi <= 0 ||
            template.Grid.Columns <= 0 ||
            template.Grid.Rows <= 0 ||
            template.Slots.Count == 0)
        {
            throw new InvalidOperationException("内置材料组图模板缺少必要字段。");
        }

        if (template.LabelStyle.Sequence is not ("lowercase" or "uppercase" or "numeric") ||
            template.LabelStyle.FontSizePt is < 5 or > 7 ||
            template.LabelStyle.FontWeight is < 100 or > 900)
        {
            throw new InvalidOperationException($"模板 {template.Id} 的面板编号样式无效。");
        }

        foreach (TemplateSlotDefinition slot in template.Slots)
        {
            if (slot.Rect.Column <= 0 || slot.Rect.Row <= 0 ||
                slot.Rect.ColumnSpan <= 0 || slot.Rect.RowSpan <= 0 ||
                slot.Rect.Column + slot.Rect.ColumnSpan - 1 > template.Grid.Columns ||
                slot.Rect.Row + slot.Rect.RowSpan - 1 > template.Grid.Rows)
            {
                throw new InvalidOperationException($"模板插槽 {slot.Id} 超出网格边界。");
            }
        }

        if (template.Slots.Select(slot => slot.Id).Distinct(StringComparer.Ordinal).Count() != template.Slots.Count ||
            template.Slots.Select(slot => slot.Label).Distinct(StringComparer.Ordinal).Count() != template.Slots.Count)
        {
            throw new InvalidOperationException($"模板 {template.Id} 包含重复的插槽 ID 或面板标签。");
        }
    }
}
