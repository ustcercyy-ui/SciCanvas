using SciCanvas.Core.Sources;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

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
    string? PanelLabel = null,
    Guid? SourceId = null,
    Guid? ObjectId = null);

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

public sealed record FigurePreflightConfiguration
{
    public int MinimumEffectiveDpi { get; init; } = 300;

    public bool BlockUnverifiedSources { get; init; } = true;

    public double MaximumScaleBarWidthFraction { get; init; } = 0.8;

    public FigurePreflightConfiguration Validate()
    {
        if (MinimumEffectiveDpi is < 1 or > 2400 ||
            !double.IsFinite(MaximumScaleBarWidthFraction) ||
            MaximumScaleBarWidthFraction is <= 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumEffectiveDpi));
        }

        return this;
    }
}

public sealed record FigurePreflightContext(
    FigureExportDocument Document,
    string? TargetFormat = null,
    FigureExportProfile? Profile = null,
    PanelLabelScheme LabelScheme = PanelLabelScheme.Custom,
    IFontCatalog? FontCatalog = null,
    IPdfFontCapabilityProvider? PdfFontCapabilityProvider = null)
{
    public int BitDepth => Profile?.BitDepth ?? Document.BitDepth;

    public string? EffectiveTargetFormat
    {
        get
        {
            string? format = Profile?.Format ?? TargetFormat;
            return string.IsNullOrWhiteSpace(format)
                ? null
                : format.Trim().TrimStart('.').ToLowerInvariant() switch
                {
                    "tif" => "tiff",
                    "jpeg" => "jpg",
                    var normalized => normalized,
                };
        }
    }

    public bool IsSixteenBitTiff =>
        BitDepth == 16 && EffectiveTargetFormat == "tiff";
}

/// <summary>Deterministic, side-effect-free checks run before a figure export.</summary>
public static class FigurePreflight
{
    public static FigurePreflightResult Check(
        FigureExportDocument document,
        IReadOnlyCollection<SourceAsset> projectSources,
        bool hasUnsavedChanges = false,
        FigurePreflightConfiguration? configuration = null)
        => Check(
            new FigurePreflightContext(
                document,
                document.BitDepth == 16 ? "tiff" : null),
            projectSources,
            hasUnsavedChanges,
            configuration);

    public static FigurePreflightResult Check(
        FigurePreflightContext context,
        IReadOnlyCollection<SourceAsset> projectSources,
        bool hasUnsavedChanges = false,
        FigurePreflightConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        FigureExportDocument document = context.Document;
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(projectSources);
        configuration = (configuration ?? new FigurePreflightConfiguration()).Validate();
        List<FigurePreflightIssue> issues = [];

        if (context.EffectiveTargetFormat == "pdf" &&
            document.PdfFontStrategy != PdfFontStrategy.OutlineText)
        {
            if (context.PdfFontCapabilityProvider is null)
            {
                bool strict = document.PdfFontStrategy == PdfFontStrategy.EmbedSubsetWhenPermitted;
                issues.Add(new FigurePreflightIssue(
                    strict ? FigurePreflightSeverity.Error : FigurePreflightSeverity.Warning,
                    strict ? "PDF_FONT_CAPABILITY_UNAVAILABLE" : "PDF_FONT_OUTLINE_FALLBACK",
                    strict
                        ? "Strict PDF font embedding requires an available platform font capability provider."
                        : "PDF font capability could not be inspected during preflight; the writer will outline any font it cannot legally and reliably subset."));
            }
            else
            {
                foreach (FontUsage usage in FontUsageCollector.Collect(document, includeHidden: false))
                {
                    PdfFontCapability capability = context.PdfFontCapabilityProvider.GetCapability(
                        usage.RequestedFont,
                        usage.IsBold);
                    PdfFontPlan plan = PdfFontStrategyPlanner.Plan(document.PdfFontStrategy, capability);
                    if (plan.CanExport && plan.Warning is null)
                    {
                        continue;
                    }

                    bool strict = !plan.CanExport;
                    issues.Add(new FigurePreflightIssue(
                        strict ? FigurePreflightSeverity.Error : FigurePreflightSeverity.Warning,
                        strict ? "PDF_FONT_EMBEDDING_UNAVAILABLE" : "PDF_FONT_OUTLINE_FALLBACK",
                        $"{usage.UsageKind} font “{usage.RequestedFont}” " +
                        (strict ? plan.Error : plan.Warning),
                        usage.PanelLabel,
                        ObjectId: usage.ObjectId));
                }
            }
        }

        if (TryGetAlpha(document.BackgroundColor, out byte backgroundAlpha) &&
            backgroundAlpha < byte.MaxValue)
        {
            issues.Add(context.IsSixteenBitTiff
                ? new FigurePreflightIssue(
                    FigurePreflightSeverity.Error,
                    "TRANSPARENT_BACKGROUND_UNSUPPORTED",
                    "16-bit RGB TIFF 不支持透明或半透明画布；请选择 alpha=255 的不透明背景。")
                : new FigurePreflightIssue(
                    FigurePreflightSeverity.Info,
                    "TRANSPARENT_BACKGROUND",
                    "画布背景含透明度；投稿系统转码时可能显示为黑色，请核对期刊要求。"));
        }

        if (document.Panels.Count == 0 && document.PlotPanels.Count == 0)
        {
            issues.Add(new(FigurePreflightSeverity.Error, "NO_PANELS", "拼版中没有可导出的图像或 Plot 面板。"));
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

            if (panel.Source.LinkState != SourceLinkState.Verified &&
                configuration.BlockUnverifiedSources)
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
            if (effectiveDpi < configuration.MinimumEffectiveDpi)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Warning,
                    "LOW_EFFECTIVE_DPI",
                    $"面板 {panel.Label} 的有效分辨率约 {effectiveDpi:0} dpi，低于项目阈值 {configuration.MinimumEffectiveDpi} dpi。",
                    panel.Label));
            }

            foreach (FigureScaleBarExportSpec scaleBar in panel.EffectiveScaleBars)
            {
                double sourcePixels;
                try
                {
                    scaleBar.Calibration.EnsureValid();
                    scaleBar.DisplayLength.EnsureValid();
                    sourcePixels = scaleBar.SourcePixelLength;
                }
                catch (Exception exception) when (exception is ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    issues.Add(new(
                        FigurePreflightSeverity.Error,
                        "INVALID_SCALE_BAR",
                        $"面板 {panel.Label} 的比例尺显示单位无法换算到校准单位。",
                        panel.Label));
                    continue;
                }

                if (!double.IsFinite(sourcePixels) || sourcePixels <= 0)
                {
                    issues.Add(new(FigurePreflightSeverity.Error, "INVALID_SCALE_BAR", $"面板 {panel.Label} 的比例尺校准参数无效。", panel.Label));
                }
                else if (sourcePixels > panel.SourceRect.Width * configuration.MaximumScaleBarWidthFraction)
                {
                    issues.Add(new(FigurePreflightSeverity.Error, "SCALE_BAR_TOO_LONG", $"面板 {panel.Label} 的比例尺超过图像宽度 80%。", panel.Label));
                }
            }

            if (panel.Adjustments is { IsValid: false })
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_ADJUSTMENT", $"面板 {panel.Label} 的图像处理参数无效。", panel.Label));
            }
            if (context.IsSixteenBitTiff &&
                string.Equals((panel.Adjustments ?? new()).Normalize().Channel, "alpha", StringComparison.Ordinal))
            {
                issues.Add(new(
                    FigurePreflightSeverity.Error,
                    "ALPHA_CHANNEL_UNSUPPORTED_16BIT",
                    "16-bit RGB TIFF cannot represent the selected alpha-channel view.",
                    panel.Label,
                    panel.Source.Id));
            }

            try
            {
                panel.StyleOverride?.EnsureValid();
            }
            catch (InvalidOperationException)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Error,
                    "INVALID_PANEL_STYLE",
                    $"面板 {panel.Label} 的局部样式覆盖包含无效字体、字号、线宽或颜色。",
                    panel.Label));
            }
        }

        foreach (FigurePlotPanelExportItem panel in document.PlotPanels)
        {
            if (!panel.IsVisible)
            {
                issues.Add(new(FigurePreflightSeverity.Warning, "HIDDEN_PLOT_PANEL",
                    $"Plot 面板 {panel.Label} 已隐藏，不会进入导出。", panel.Label));
            }
            if (panel.DestinationRect.X < 0 || panel.DestinationRect.Y < 0 ||
                panel.DestinationRect.Right > document.WidthPixels ||
                panel.DestinationRect.Bottom > document.HeightPixels)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "PLOT_PANEL_OUT_OF_BOUNDS",
                    $"Plot 面板 {panel.Label} 超出画布边界。", panel.Label));
            }
            try
            {
                panel.EnsureValid();
                _ = panel.ResolveTypography(document.GlobalStyle);
                if (panel.Plot.PlotType == PlotKind.Heatmap)
                {
                    HeatmapDomain domain = HeatmapDomainBuilder.Build(panel.Plot, panel.Projection);
                    issues.AddRange(domain.Issues.Select(issue => new FigurePreflightIssue(
                        issue.Severity == HeatmapDomainIssueSeverity.Warning
                            ? FigurePreflightSeverity.Warning
                            : FigurePreflightSeverity.Info,
                        issue.Code,
                        issue.Message,
                        panel.Label,
                        ObjectId: panel.Plot.Id)));
                }
            }
            catch (HeatmapDomainException exception)
            {
                issues.Add(new FigurePreflightIssue(
                    FigurePreflightSeverity.Error,
                    exception.Code,
                    exception.Message,
                    panel.Label,
                    ObjectId: panel.Plot.Id));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_PLOT_PANEL",
                    $"Plot 面板 {panel.Label} 无效：{exception.Message}", panel.Label,
                    ObjectId: panel.Plot.Id));
            }
        }

        foreach (IGrouping<string, SourceAsset> duplicateSources in projectSources
                     .Where(source => !string.IsNullOrWhiteSpace(source.Fingerprint.Sha256))
                     .GroupBy(source => source.Fingerprint.Sha256, StringComparer.OrdinalIgnoreCase)
                     .Where(group => group.Count() > 1))
        {
            issues.Add(new(
                FigurePreflightSeverity.Warning,
                "EXACT_DUPLICATE_SOURCE",
                $"多个 Asset 引用了完全相同的源图内容：{string.Join(", ", duplicateSources.Select(source => source.DisplayName))}。"));
        }

        FigurePanelExportItem[] integrityPanels = document.Panels.Where(panel => panel.IsVisible).ToArray();
        for (int firstIndex = 0; firstIndex < integrityPanels.Length; firstIndex++)
        {
            for (int secondIndex = firstIndex + 1; secondIndex < integrityPanels.Length; secondIndex++)
            {
                FigurePanelExportItem first = integrityPanels[firstIndex];
                FigurePanelExportItem second = integrityPanels[secondIndex];
                if (first.Source.Id != second.Source.Id || first.FrameIndex != second.FrameIndex)
                {
                    continue;
                }

                if (first.SourceRect == second.SourceRect)
                {
                    issues.Add(new(
                        FigurePreflightSeverity.Warning,
                        "EXACT_DUPLICATE_CROP",
                        $"Panels ({first.Label}) and ({second.Label}) use the exact same source crop.",
                        second.Label));
                    continue;
                }

                long intersectionWidth = Math.Max(
                    0,
                    Math.Min(first.SourceRect.Right, second.SourceRect.Right) -
                    Math.Max(first.SourceRect.X, second.SourceRect.X));
                long intersectionHeight = Math.Max(
                    0,
                    Math.Min(first.SourceRect.Bottom, second.SourceRect.Bottom) -
                    Math.Max(first.SourceRect.Y, second.SourceRect.Y));
                long intersectionArea = intersectionWidth * intersectionHeight;
                long smallerArea = Math.Min(
                    first.SourceRect.Width * first.SourceRect.Height,
                    second.SourceRect.Width * second.SourceRect.Height);
                if (smallerArea > 0 && intersectionArea / (double)smallerArea > 0.90)
                {
                    issues.Add(new(
                        FigurePreflightSeverity.Warning,
                        "STRONG_CROP_OVERLAP",
                        $"Panels ({first.Label}) and ({second.Label}) reuse more than 90% of the same source crop.",
                        second.Label));
                }
            }
        }

        foreach (FigureMeasurementOverlayExportItem overlay in document.MeasurementOverlays.Where(item => item.IsVisible))
        {
            FigurePanelExportItem[] matchingPanels = document.Panels
                .Where(panel => panel.IsVisible && panel.PanelId == overlay.PanelId)
                .ToArray();
            try
            {
                if (matchingPanels.Length != 1)
                {
                    throw new InvalidOperationException("Measurement Overlay 必须绑定到一个可见的 Figure Panel。");
                }

                _ = FigureMeasurementOverlayMapper.Map(overlay.ScientificObject, matchingPanels[0]);
            }
            catch (InvalidOperationException exception)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Error,
                    "INVALID_MEASUREMENT_OVERLAY",
                    exception.Message,
                    matchingPanels.FirstOrDefault()?.Label,
                    overlay.SourceAssetId,
                    overlay.Id));
                continue;
            }

        }
        foreach (FigureRoiProjectionExportItem projection in document.RoiProjections.Where(item => item.IsVisible))
        {
            FigurePanelExportItem[] matchingPanels = document.Panels
                .Where(panel => panel.IsVisible && panel.PanelId == projection.PanelId)
                .ToArray();
            try
            {
                if (matchingPanels.Length != 1)
                {
                    throw new InvalidOperationException(
                        "ROI Figure Projection 必须绑定到一个可见且唯一的 Figure Panel。");
                }

                _ = FigureRoiProjectionMapper.Map(projection, matchingPanels[0], document.Dpi);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or ArgumentException)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Error,
                    "INVALID_ROI_PROJECTION",
                    exception.Message,
                    matchingPanels.FirstOrDefault()?.Label,
                    projection.AssetId,
                    projection.Id));
                continue;
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

            if (!ScientificStyleColor.ValidateColor(annotation.StrokeColor) ||
                !ScientificStyleColor.ValidateColor(annotation.FillColor) ||
                !ScientificStyleColor.ValidateColor(annotation.TextColor) ||
                !double.IsFinite(annotation.FillOpacityPercent) ||
                annotation.FillOpacityPercent is < 0 or > 100 ||
                string.IsNullOrWhiteSpace(annotation.FontFamily) ||
                annotation.FontFamily.Length > 128 ||
                !double.IsFinite(annotation.FontSizePt) || annotation.FontSizePt is < 4 or > 72 ||
                !double.IsFinite(annotation.StrokeWidthPt) || annotation.StrokeWidthPt is < 0.25 or > 10)
            {
                issues.Add(new(FigurePreflightSeverity.Error, "INVALID_ANNOTATION_STYLE", "存在颜色、字号或线宽无效的标注。"));
            }

        }

        foreach (FigureScientificObjectExportItem scientificObject in document.ScientificObjects.Where(item => item.IsVisible))
        {
            try
            {
                scientificObject.EnsureValid(document.WidthPixels, document.HeightPixels);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                issues.Add(new(
                    FigurePreflightSeverity.Error,
                    "INVALID_SCIENTIFIC_OBJECT",
                    exception.Message,
                    ObjectId: scientificObject.Id));
            }
        }
        if (context.FontCatalog is not null)
        {
            foreach (FontUsage usage in FontUsageCollector.Collect(document, includeHidden: false)
                         .Where(usage => !context.FontCatalog.IsInstalled(usage.RequestedFont)))
            {
                string? panelLabel = usage.PanelLabel ?? (usage.PanelId is Guid panelId
                    ? document.Panels.FirstOrDefault(panel => panel.PanelId == panelId)?.Label ??
                      document.PlotPanels.FirstOrDefault(panel => panel.PanelId == panelId)?.Label
                    : null);
                issues.Add(new(
                    FigurePreflightSeverity.Warning,
                    "FONT_MISSING",
                    $"{usage.UsageKind} font “{usage.RequestedFont}” is not installed on this system. Export will use a fallback font.",
                    panelLabel,
                    ObjectId: usage.ObjectId));
            }
        }

        FontUsage[] visibleFontUsages = FontUsageCollector.Collect(document, includeHidden: false).ToArray();
        if (visibleFontUsages
            .Where(usage => usage.UsageKind == FontUsageKind.PanelLabel &&
                            (usage.PanelId.HasValue || usage.PanelLabel is not null))
            .Select(usage => usage.RequestedFont)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            issues.Add(new(
                FigurePreflightSeverity.Warning,
                "MIXED_PANEL_LABEL_FONT",
                "可见 Panel 使用了不一致的 Panel Label 字体；请确认这是有意的局部覆盖。"));
        }

        if (visibleFontUsages
            .Where(usage => usage.UsageKind == FontUsageKind.ScaleBarText &&
                            (usage.PanelId.HasValue || usage.PanelLabel is not null))
            .Select(usage => usage.RequestedFont)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
        {
            issues.Add(new(
                FigurePreflightSeverity.Warning,
                "MIXED_SCALE_BAR_FONT",
                "可见 Panel 使用了不一致的比例尺字体；请确认这是有意的局部覆盖。"));
        }

        PreflightPanel[] visiblePanels = document.Panels
            .Where(panel => panel.IsVisible)
            .Select(panel => new PreflightPanel(panel.PanelId, panel.Label, panel.DestinationRect))
            .Concat(document.PlotPanels
                .Where(panel => panel.IsVisible)
                .Select(panel => new PreflightPanel(panel.PanelId, panel.Label, panel.DestinationRect)))
            .ToArray();
        string[] nonEmptyLabels = visiblePanels
            .Select(panel => panel.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .ToArray();
        if (context.LabelScheme != PanelLabelScheme.None &&
            nonEmptyLabels.Length > 0 && nonEmptyLabels.Length < visiblePanels.Length)
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "MISSING_LABEL", "部分可见面板没有编号，可能造成图注对应不清。"));
        }

        if (context.LabelScheme != PanelLabelScheme.None &&
            nonEmptyLabels.Distinct(StringComparer.OrdinalIgnoreCase).Count() != nonEmptyLabels.Length)
        {
            issues.Add(new(FigurePreflightSeverity.Warning, "DUPLICATE_LABEL", "可见面板存在重复编号。"));
        }

        if (context.LabelScheme is not (PanelLabelScheme.None or PanelLabelScheme.Custom))
        {
            PreflightPanel[] readingOrder = visiblePanels
                .OrderBy(panel => panel.DestinationRect.Y)
                .ThenBy(panel => panel.DestinationRect.X)
                .ThenBy(panel => panel.DestinationRect.Width)
                .ToArray();
            for (int index = 0; index < readingOrder.Length; index++)
            {
                string expected = PanelLabelGenerator.Generate(index, context.LabelScheme);
                string observed = PanelLabelGenerator.NormalizeForComparison(readingOrder[index].Label);
                if (!string.Equals(expected, observed, StringComparison.Ordinal))
                {
                    issues.Add(new FigurePreflightIssue(
                        FigurePreflightSeverity.Warning,
                        "LABEL_SEQUENCE",
                        $"面板阅读顺序第 {index + 1} 项应使用编号 {expected}。",
                        readingOrder[index].Label));
                }
            }
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

    private sealed record PreflightPanel(
        Guid Id,
        string Label,
        Geometry.PixelRect64 DestinationRect);

    private static bool IsHexColor(string? value)
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }

    private static bool TryGetAlpha(string? color, out byte alpha)
    {
        alpha = 0;
        string hex = color?.Trim().TrimStart('#') ?? string.Empty;
        if (hex.Length == 6 && hex.All(Uri.IsHexDigit))
        {
            alpha = byte.MaxValue;
            return true;
        }

        return hex.Length == 8 &&
               hex.All(Uri.IsHexDigit) &&
               byte.TryParse(
                   hex.AsSpan(0, 2),
                   System.Globalization.NumberStyles.HexNumber,
                   System.Globalization.CultureInfo.InvariantCulture,
                   out alpha);
    }
}
