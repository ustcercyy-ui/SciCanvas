using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Export;

/// <summary>
/// Explainable rule-based assistance. These findings are suggestions and integrity
/// warnings, never automatic claims about scientific truth.
/// </summary>
public static class FigureAssistance
{
    public static FigurePreflightResult Review(
        FigureExportDocument document,
        IReadOnlyCollection<SourceAsset> projectSources,
        bool hasUnsavedChanges = false)
    {
        FigurePreflightResult preflight = FigurePreflight.Check(
            document,
            projectSources,
            hasUnsavedChanges);
        List<FigurePreflightIssue> issues = [.. preflight.Issues];
        FigureGlobalStyle style = document.GlobalStyle;

        foreach (FigureAnnotationExportItem annotation in document.Annotations.Where(item => item.IsVisible))
        {
            bool mismatch = annotation.Kind == "text"
                ? Math.Abs(annotation.FontSizePt - style.FontSizePt) > 0.01 ||
                  !SameColor(annotation.Color, style.TextColor)
                : Math.Abs(annotation.StrokeWidthPt - style.StrokeWidthPt) > 0.01 ||
                  !SameColor(annotation.Color, style.ShapeColor);
            if (mismatch)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Warning,
                    "STYLE_HARMONIZATION",
                    "标注样式与项目全局样式不一致；建议预览后一键统一。"));
                break;
            }
        }

        foreach (FigurePanelExportItem panel in document.Panels.Where(item => item.IsVisible))
        {
            ImageAdjustmentParameters adjustment = panel.Adjustments ?? new ImageAdjustmentParameters();
            bool extreme = Math.Abs(adjustment.Brightness) > 0.5 ||
                           Math.Abs(adjustment.Contrast) > 0.5 ||
                           adjustment.Gamma is < 0.5 or > 2 ||
                           adjustment.WhitePoint - adjustment.BlackPoint < 0.25 ||
                           adjustment.Invert;
            if (extreme)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Warning,
                    "INTEGRITY_EXTREME_ADJUSTMENT",
                    $"面板 {panel.Label} 使用较强的全局像素映射；请确认处理未改变结论，并在方法中披露。",
                    panel.Label));
            }

            double sourceArea = panel.Source.Metadata.PixelSize.Width *
                                (double)panel.Source.Metadata.PixelSize.Height;
            double cropFraction = panel.SourceRect.Width * (double)panel.SourceRect.Height /
                                  Math.Max(1, sourceArea);
            if (cropFraction < 0.05)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Info,
                    "INTEGRITY_NARROW_CROP",
                    $"面板 {panel.Label} 仅保留源图约 {cropFraction:P1}；建议保存包含上下文的补充图。",
                    panel.Label));
            }
        }

        foreach (IGrouping<Guid, FigurePanelExportItem> group in document.Panels
                     .Where(panel => panel.IsVisible)
                     .GroupBy(panel => panel.Source.Id))
        {
            if (group.Select(panel => panel.Adjustments).Distinct().Count() > 1)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Warning,
                    "INTEGRITY_INCONSISTENT_ADJUSTMENT",
                    $"同一源图 {group.First().Source.DisplayName} 在不同面板使用了不同处理参数；请确认比较仍然公平。"));
            }
        }

        double textContrast = ContrastRatio(style.TextColor, document.BackgroundColor);
        double shapeContrast = ContrastRatio(style.ShapeColor, document.BackgroundColor);
        if (textContrast < 3 || shapeContrast < 3)
        {
            issues.Add(new(
                FigurePreflightSeverity.Warning,
                "LOW_COLOR_CONTRAST",
                $"全局文字或图形与背景的对比度不足 3:1（文字 {textContrast:0.##}:1，图形 {shapeContrast:0.##}:1）。"));
        }

        issues.Add(new(
            FigurePreflightSeverity.Info,
            "INTEGRITY_NON_GENERATIVE_PIPELINE",
            "当前编辑链只包含裁剪、全局像素映射、测量与标注；不提供生成式填充、克隆、局部擦除或对象移除。"));
        return new FigurePreflightResult(issues);
    }

    private static bool SameColor(string first, string second) =>
        string.Equals(NormalizeColor(first), NormalizeColor(second), StringComparison.OrdinalIgnoreCase);

    private static double ContrastRatio(string foreground, string background)
    {
        (double r, double g, double b) first = ParseRgb(foreground);
        (double r, double g, double b) second = ParseRgb(background);
        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        return (Math.Max(firstLuminance, secondLuminance) + 0.05) /
               (Math.Min(firstLuminance, secondLuminance) + 0.05);
    }

    private static double RelativeLuminance((double r, double g, double b) color)
    {
        static double Linear(double value) => value <= 0.04045
            ? value / 12.92
            : Math.Pow((value + 0.055) / 1.055, 2.4);
        return 0.2126 * Linear(color.r) + 0.7152 * Linear(color.g) + 0.0722 * Linear(color.b);
    }

    private static (double r, double g, double b) ParseRgb(string color)
    {
        string normalized = NormalizeColor(color);
        return (
            Convert.ToByte(normalized[0..2], 16) / 255d,
            Convert.ToByte(normalized[2..4], 16) / 255d,
            Convert.ToByte(normalized[4..6], 16) / 255d);
    }

    private static string NormalizeColor(string color)
    {
        string value = color.Trim().TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..];
        }

        return value.Length == 6 && value.All(Uri.IsHexDigit) ? value : "000000";
    }
}
