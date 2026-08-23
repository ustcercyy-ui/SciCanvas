using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Export;

public enum FigurePreflightSeverity
{
    Info,
    Warning,
    Error,
}

public sealed record FigurePreflightIssue(
    FigurePreflightSeverity Severity,
    string Code,
    string Message,
    string? PanelLabel = null);

public sealed record FigurePreflightResult(IReadOnlyList<FigurePreflightIssue> Issues)
{
    public bool HasErrors => Issues.Any(issue => issue.Severity == FigurePreflightSeverity.Error);

    public bool HasWarnings => Issues.Any(issue => issue.Severity == FigurePreflightSeverity.Warning);

    public string Summary => HasErrors
        ? $"发现 {Issues.Count(issue => issue.Severity == FigurePreflightSeverity.Error)} 个错误，导出已阻止。"
        : HasWarnings
            ? $"检查通过，但有 {Issues.Count(issue => issue.Severity == FigurePreflightSeverity.Warning)} 个提醒。"
            : "投稿预检通过。";
}

/// <summary>Deterministic, side-effect-free checks run before a figure export.</summary>
public static class FigurePreflight
{
    public static FigurePreflightResult Check(
        FigureExportDocument document,
        IReadOnlyCollection<SourceAsset> projectSources,
        bool hasUnsavedChanges = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(projectSources);
        List<FigurePreflightIssue> issues = [];

        if (document.Panels.Count == 0)
        {
            issues.Add(new(FigurePreflightSeverity.Error, "NO_PANELS", "拼版中没有可导出的图像面板。"));
        }

        HashSet<Guid> sourceIds = projectSources.Select(source => source.Id).ToHashSet();
        foreach (FigurePanelExportItem panel in document.Panels)
        {
            if (!panel.IsVisible)
            {
                issues.Add(new(FigurePreflightSeverity.Warning, "HIDDEN_PANEL", $"面板 {panel.Label} 已隐藏，不会进入导出。", panel.Label));
            }

            if (!sourceIds.Contains(panel.Source.Id))
            {
                issues.Add(new(FigurePreflightSeverity.Error, "SOURCE_NOT_IN_PROJECT", $"面板 {panel.Label} 引用了不在当前工程中的源图。", panel.Label));
            }

            if (panel.Source.LinkState != SourceLinkState.Verified)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "SOURCE_UNVERIFIED", $"面板 {panel.Label} 的源图未通过完整性验证。", panel.Label));
            }

            if (panel.FrameIndex < 0 || panel.FrameIndex >= Math.Max(1, panel.Source.Metadata.FrameCount))
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_FRAME", $"面板 {panel.Label} 选择了不存在的图像帧 {panel.FrameIndex + 1}。", panel.Label));
            }

            if (panel.DestinationRect.X < 0 || panel.DestinationRect.Y < 0 ||
                panel.DestinationRect.Right > document.WidthPixels ||
                panel.DestinationRect.Bottom > document.HeightPixels)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "PANEL_OUT_OF_BOUNDS", $"面板 {panel.Label} 超出画布边界。", panel.Label));
            }

            if (panel.SourceRect.Width <= 0 || panel.SourceRect.Height <= 0)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_CROP", $"面板 {panel.Label} 的源裁剪区域无效。", panel.Label));
            }

            double effectiveDpi = Math.Min(
                panel.SourceRect.Width / Math.Max(1, panel.DestinationRect.Width) * document.Dpi,
                panel.SourceRect.Height / Math.Max(1, panel.DestinationRect.Height) * document.Dpi);
            if (effectiveDpi < 300)
            {
                issues.Add(new(FigurePreflightSeverity.Warning, "LOW_EFFECTIVE_DPI", $"面板 {panel.Label} 的有效分辨率约 {effectiveDpi:0} dpi。", panel.Label));
            }

            if (panel.ScaleBar is not null &&
                panel.ScaleBar.PhysicalLength / panel.ScaleBar.PhysicalUnitsPerSourcePixel > panel.SourceRect.Width * 0.8)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "SCALE_BAR_TOO_LONG", $"面板 {panel.Label} 的比例尺超过图像宽度 80%。", panel.Label));
            }

            if (panel.Adjustments is { IsValid: false })
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_ADJUSTMENT", $"面板 {panel.Label} 的图像处理参数无效。", panel.Label));
            }
        }

        foreach (FigureAnnotationExportItem annotation in document.Annotations.Where(item => item.IsVisible))
        {
            if (string.IsNullOrWhiteSpace(annotation.Text) && annotation.Kind == "text")
            {
                issues.Add(new(FigurePreflightSeverity.Error, "EMPTY_ANNOTATION", "存在空的文字标注。"));
            }
        }

        if (document.Panels.Where(panel => panel.IsVisible).Select(panel => panel.Label).Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != document.Panels.Count(panel => panel.IsVisible && !string.IsNullOrWhiteSpace(panel.Label)))
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "DUPLICATE_LABEL", "可见面板存在重复编号。"));
        }

        if (hasUnsavedChanges)
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "UNSAVED_CHANGES", "工程仍有未保存修改；建议先保存工程副本。"));
        }

        return new FigurePreflightResult(issues);
    }
}
