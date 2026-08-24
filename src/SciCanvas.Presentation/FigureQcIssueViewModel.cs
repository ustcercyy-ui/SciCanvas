using System.Windows.Media;
using SciCanvas.Core.Export;

namespace SciCanvas.Presentation;

public sealed class FigureQcIssueViewModel
{
    public FigureQcIssueViewModel(FigurePreflightIssue issue)
    {
        Issue = issue ?? throw new ArgumentNullException(nameof(issue));
    }

    public FigurePreflightIssue Issue { get; }

    public FigurePreflightSeverity Severity => Issue.Severity;

    public string Code => Issue.Code;

    public string Message => Issue.Message;

    public string? PanelLabel => Issue.PanelLabel;

    public string SeverityText => Severity switch
    {
        FigurePreflightSeverity.Error => "错误",
        FigurePreflightSeverity.Warning => "提醒",
        _ => "信息",
    };

    public Brush SeverityBrush => Severity switch
    {
        FigurePreflightSeverity.Error => CreateBrush("#FFF28A8A"),
        FigurePreflightSeverity.Warning => CreateBrush("#FFFFD166"),
        _ => CreateBrush("#FF80DDEA"),
    };

    public string TargetText => string.IsNullOrWhiteSpace(PanelLabel)
        ? "全局"
        : $"面板 {PanelLabel}";

    public bool CanNavigate => !string.IsNullOrWhiteSpace(PanelLabel);

    public string SuggestedAction => Code switch
    {
        "NO_PANELS" => "加入至少一个有效裁剪面板。",
        "SOURCE_UNVERIFIED" or "SOURCE_NOT_IN_PROJECT" => "重新链接源图并完成 SHA-256 完整性验证。",
        "INVALID_FRAME" => "切换到源图实际存在的帧。",
        "PANEL_OUT_OF_BOUNDS" => "定位面板并移回画布范围。",
        "LOW_EFFECTIVE_DPI" => "缩小输出尺寸，或换用更高分辨率源图。",
        "INVALID_SCALE_BAR" or "SCALE_BAR_TOO_LONG" => "检查源图校准；缩短比例尺或关闭该面板比例尺。",
        "INVALID_ADJUSTMENT" => "恢复有效的亮度、对比度、Gamma 与黑白场参数。",
        "EMPTY_ANNOTATION" or "INVALID_ANNOTATION_BOUNDS" or "INVALID_ANNOTATION_STYLE" => "在科研标注层中修正或删除对应标注。",
        "MISSING_LABEL" or "DUPLICATE_LABEL" => "使用“按画布位置重新编号”，再核对图注。",
        "PANEL_OVERLAP" => "定位面板并检查边界；若为 Inset 叠放可保留。",
        "UNSAVED_CHANGES" => "先保存工程副本，再进行最终投稿导出。",
        "TRANSPARENT_BACKGROUND" => "按期刊要求改为白色背景，或确认透明背景可被接收。",
        "STYLE_HARMONIZATION" => "预览后使用“协调全局样式”，或明确保留例外。",
        "LOW_COLOR_CONTRAST" => "提高文字/图形与背景的明度对比，并重新运行辅助审查。",
        "INTEGRITY_EXTREME_ADJUSTMENT" => "核对全局处理前后图像，在方法中披露参数；不要选择性处理局部对象。",
        "INTEGRITY_INCONSISTENT_ADJUSTMENT" => "对可比较面板使用同一组全局处理参数，或说明差异原因。",
        "INTEGRITY_NARROW_CROP" => "保留带上下文的原图或补充图，并在图注说明裁剪范围。",
        "INTEGRITY_NON_GENERATIVE_PIPELINE" => "信息项：继续保留源文件、工程记录与导出溯源报告。",
        "QC_ENGINE_ERROR" => "修正检查器提示的编辑状态后重新运行 QC。",
        _ => "核对该项并重新运行 Figure QC。",
    };

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
