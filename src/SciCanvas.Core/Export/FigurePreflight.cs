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

        if (document.BackgroundColor.StartsWith("#00", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new(
                FigurePreflightSeverity.Info,
                "TRANSPARENT_BACKGROUND",
                "画布背景为透明；投稿系统转码时可能显示为黑色，请核对期刊要求。"));
        }

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

            if (panel.ScaleBar is { } scaleBar &&
                (!double.IsFinite(scaleBar.PhysicalUnitsPerSourcePixel) ||
                 scaleBar.PhysicalUnitsPerSourcePixel <= 0 ||
                 !double.IsFinite(scaleBar.PhysicalLength) ||
                 scaleBar.PhysicalLength <= 0 ||
                 string.IsNullOrWhiteSpace(scaleBar.Unit)))
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_SCALE_BAR", $"面板 {panel.Label} 的比例尺校准参数无效。", panel.Label));
            }
            else if (panel.ScaleBar is { } validScaleBar &&
                     validScaleBar.PhysicalLength / validScaleBar.PhysicalUnitsPerSourcePixel > panel.SourceRect.Width * 0.8)
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

            bool knownKind = annotation.Kind is "text" or "arrow" or "line" or "rectangle" or "ellipse";
            bool startInside = double.IsFinite(annotation.X) && double.IsFinite(annotation.Y) &&
                               annotation.X >= 0 && annotation.X <= document.WidthPixels &&
                               annotation.Y >= 0 && annotation.Y <= document.HeightPixels;
            bool needsEnd = annotation.Kind != "text";
            bool endInside = double.IsFinite(annotation.EndX) && double.IsFinite(annotation.EndY) &&
                             annotation.EndX >= 0 && annotation.EndX <= document.WidthPixels &&
                             annotation.EndY >= 0 && annotation.EndY <= document.HeightPixels;
            if (!knownKind || !startInside || (needsEnd && !endInside))
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_ANNOTATION_BOUNDS", "存在类型未知、坐标无效或超出画布的标注。"));
            }

            if (!IsHexColor(annotation.Color) ||
                !double.IsFinite(annotation.FontSizePt) || annotation.FontSizePt is < 4 or > 72 ||
                !double.IsFinite(annotation.StrokeWidthPt) || annotation.StrokeWidthPt is < 0.25 or > 10)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_ANNOTATION_STYLE", "存在颜色、字号或线宽无效的标注。"));
            }
        }

        FigurePanelExportItem[] visiblePanels = document.Panels.Where(panel => panel.IsVisible).ToArray();
        string[] nonEmptyLabels = visiblePanels
            .Select(panel => panel.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
        if (nonEmptyLabels.Length > 0 && nonEmptyLabels.Length < visiblePanels.Length)
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "MISSING_LABEL", "部分可见面板没有编号，可能造成图注对应不清。"));
        }

        if (nonEmptyLabels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != nonEmptyLabels.Length)
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "DUPLICATE_LABEL", "可见面板存在重复编号。"));
        }

        for (int firstIndex = 0; firstIndex < visiblePanels.Length; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < visiblePanels.Length; secondIndex++)
            {
                if (Overlaps(visiblePanels[firstIndex].DestinationRect, visiblePanels[secondIndex].DestinationRect))
                {
                    issues.Add(new(
                        FigurePreflightSeverity.Warning,
                        "PANEL_OVERLAP",
                        $"面板 {visiblePanels[firstIndex].Label} 与 {visiblePanels[secondIndex].Label} 发生重叠，请确认是否为有意叠放。",
                        visiblePanels[secondIndex].Label));
                }
            }
        }

        if (hasUnsavedChanges)
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "UNSAVED_CHANGES", "工程仍有未保存修改；建议先保存工程副本。"));
        }

        return new FigurePreflightResult(issues);
    }

    private static bool Overlaps(Geometry.PixelRect64 first, Geometry.PixelRect64 second) =>
        first.X < second.Right && first.Right > second.X &&
        first.Y < second.Bottom && first.Bottom > second.Y;

    private static bool IsHexColor(string? value)
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }
}
