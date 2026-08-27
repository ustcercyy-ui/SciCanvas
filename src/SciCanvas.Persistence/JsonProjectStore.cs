using System.Text.Json;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Persistence;

public sealed class JsonProjectStore : IProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public async Task<SciCanvasProjectDocument> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        string fullPath = NormalizeProjectPath(path);
        await using FileStream input = new(fullPath, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        SciCanvasProjectDocument document = await JsonSerializer.DeserializeAsync<SciCanvasProjectDocument>(
            input,
            JsonOptions,
            cancellationToken) ?? throw new InvalidDataException("工程文件为空或结构无效。");

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(document);
        Validate(migrated);
        return migrated;
    }

    public async Task SaveAsync(
        string path,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        document = ProjectMigrationPipeline.MigrateToCurrent(document);
        Validate(document);

        string fullPath = NormalizeProjectPath(path);
        string directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("工程路径缺少父目录。");
        if (!Directory.Exists(directory))
        {
            throw new DirectoryNotFoundException("工程保存目录不存在。");
        }

        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
        bool temporaryCreated = false;

        try
        {
            await using (var output = new FileStream(temporaryPath, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
            }))
            {
                temporaryCreated = true;
                await JsonSerializer.SerializeAsync(output, document, JsonOptions, cancellationToken);
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }

            if (File.Exists(fullPath))
            {
                string backupPath = fullPath + ".bak";
                File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, fullPath);
            }

            temporaryCreated = false;
        }
        finally
        {
            if (temporaryCreated)
            {
                TryDeleteTemporaryFile(temporaryPath);
            }
        }
    }

    private static string NormalizeProjectPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(path);
        if (!string.Equals(Path.GetExtension(fullPath), ".scicanvas", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("工程文件扩展名必须是 .scicanvas。");
        }

        return fullPath;
    }

    private static void Validate(SciCanvasProjectDocument document)
    {
        if (document.SchemaVersion != ProjectMigrationPipeline.CurrentVersion)
        {
            throw new NotSupportedException($"暂不支持工程版本 {document.SchemaVersion}。");
        }

        if (document.ProjectId == Guid.Empty ||
            document.CreatedAt == default ||
            document.UpdatedAt == default ||
            document.Canvas.Width <= 0 ||
            document.Canvas.Height <= 0)
        {
            throw new InvalidDataException("工程文件缺少必要的项目或画布信息。");
        }

        if (document.Sources.Select(source => source.Id).Distinct().Count() != document.Sources.Count)
        {
            throw new InvalidDataException("工程包含重复的源图像 ID。");
        }

        HashSet<Guid> sourceIds = document.Sources.Select(source => source.Id).ToHashSet();
        foreach (ProjectSourceSnapshot source in document.Sources)
        {
            if (string.IsNullOrWhiteSpace(source.OriginalPath) ||
                source.SourceRevision < 1 ||
                source.Fingerprint.Sha256.Length != 64 ||
                !source.Fingerprint.Sha256.All(Uri.IsHexDigit) ||
                source.Metadata.Width <= 0 ||
                source.Metadata.Height <= 0)
            {
                throw new InvalidDataException($"源图像 {source.DisplayName} 的工程记录无效。");
            }
        }

        foreach (ProjectImageLayerSnapshot layer in document.Layers)
        {
            if (!sourceIds.Contains(layer.SourceAssetId) ||
                layer.SourceRect.X < 0 || layer.SourceRect.Y < 0 ||
                layer.SourceRect.Width <= 0 || layer.SourceRect.Height <= 0 ||
                layer.Transform.ScaleX <= 0 || layer.Transform.ScaleY <= 0)
            {
                throw new InvalidDataException($"图层 {layer.Name} 的工程记录无效。");
            }

            if (layer.NormalizedCrop is { } crop &&
                (!double.IsFinite(crop.X) || !double.IsFinite(crop.Y) ||
                 !double.IsFinite(crop.Width) || !double.IsFinite(crop.Height) ||
                 crop.X < 0 || crop.Y < 0 || crop.Width <= 0 || crop.Height <= 0 ||
                 crop.X + crop.Width > 1.000000001 || crop.Y + crop.Height > 1.000000001))
            {
                throw new InvalidDataException($"图层 {layer.Name} 的标准化裁剪区域无效。");
            }

            if (layer.FrameMm is { } frame &&
                (!double.IsFinite(frame.X) || !double.IsFinite(frame.Y) ||
                 !double.IsFinite(frame.Width) || !double.IsFinite(frame.Height) ||
                 frame.X < 0 || frame.Y < 0 || frame.Width <= 0 || frame.Height <= 0))
            {
                throw new InvalidDataException($"图层 {layer.Name} 的毫米 Frame 无效。");
            }

            if (layer.FitMode is not ("fit" or "fill" or "manual") ||
                !double.IsFinite(layer.RotationDegrees) ||
                layer.ScientificValidity.State is not ("valid" or "warning" or "invalid" or "reviewrequired"))
            {
                throw new InvalidDataException($"图层 {layer.Name} 的 V2 工作区状态无效。");
            }

            if (layer.StyleOverride is { } styleOverride && !IsValidPanelStyleOverride(styleOverride))
            {
                throw new InvalidDataException($"图层 {layer.Name} 的 Panel 局部样式覆盖无效。");
            }
        }

        ProjectWorkspaceSnapshot workspace = document.Workspace;
        if (workspace.MinimumEffectiveDpi is < 1 or > 2400 ||
            !double.IsFinite(workspace.AlignmentToleranceMm) || workspace.AlignmentToleranceMm < 0 ||
            !double.IsFinite(workspace.SpacingToleranceMm) || workspace.SpacingToleranceMm < 0 ||
            workspace.Figures.Select(figure => figure.Id).Distinct().Count() != workspace.Figures.Count)
        {
            throw new InvalidDataException("工程工作区配置无效。");
        }

        if (workspace.Figures.Count > 0 &&
            (!workspace.Figures.Any(figure => figure.Id == workspace.ActiveFigureId) ||
             workspace.Figures.Any(figure =>
                 figure.Id == Guid.Empty ||
                 string.IsNullOrWhiteSpace(figure.Name) ||
                 !double.IsFinite(figure.WidthMm) || figure.WidthMm <= 0 ||
                 !double.IsFinite(figure.HeightMm) || figure.HeightMm <= 0 ||
                 figure.Dpi is < 1 or > 2400 ||
                 figure.LayerIds.Any(layerId => document.Layers.All(layer => layer.Id != layerId)))))
        {
            throw new InvalidDataException("工程 Figure 列表无效。");
        }

        foreach (ProjectCalibrationSnapshot calibration in document.Calibrations)
        {
            if (!sourceIds.Contains(calibration.SourceAssetId) ||
                !double.IsFinite(calibration.UnitsPerPixelX) || calibration.UnitsPerPixelX < 0 ||
                !double.IsFinite(calibration.UnitsPerPixelY) || calibration.UnitsPerPixelY < 0 ||
                string.IsNullOrWhiteSpace(calibration.Unit))
            {
                throw new InvalidDataException("工程包含无效的源图尺度标定记录。");
            }
        }

        if (document.Calibrations.Select(item => item.SourceAssetId).Distinct().Count() !=
            document.Calibrations.Count)
        {
            throw new InvalidDataException("同一源图像存在重复的尺度标定记录。");
        }

        foreach (ProjectMeasurementSnapshot measurement in document.Measurements)
        {
            bool coordinatesFinite =
                double.IsFinite(measurement.X1) && double.IsFinite(measurement.Y1) &&
                double.IsFinite(measurement.X2) && double.IsFinite(measurement.Y2) &&
                (!measurement.X3.HasValue || double.IsFinite(measurement.X3.Value)) &&
                (!measurement.Y3.HasValue || double.IsFinite(measurement.Y3.Value));
            bool pathValid = measurement.Points.All(point =>
                double.IsFinite(point.X) && double.IsFinite(point.Y));
            if (measurement.Id == Guid.Empty ||
                !sourceIds.Contains(measurement.SourceAssetId) ||
                measurement.SourceRevision < 1 ||
                document.Sources.Any(source =>
                    source.Id == measurement.SourceAssetId &&
                    measurement.SourceRevision > source.SourceRevision) ||
                !coordinatesFinite ||
                !pathValid ||
                (string.Equals(measurement.Kind, "polyline", StringComparison.OrdinalIgnoreCase) &&
                 measurement.Points.Count < 2) ||
                !IsHexColor(measurement.StrokeColor) ||
                !double.IsFinite(measurement.StrokeWidthPixels) ||
                measurement.StrokeWidthPixels is < 1 or > 12 ||
                measurement.LineStyle is not ("solid" or "dash" or "dot" or "dash-dot") ||
                !IsHexColor(measurement.FillColor) ||
                !IsHexColor(measurement.MarkerStrokeColor) ||
                !IsHexColor(measurement.MarkerFillColor) ||
                !double.IsFinite(measurement.MarkerSizePixels) ||
                measurement.MarkerSizePixels is < 8 or > 48 ||
                !IsHexColor(measurement.LabelColor) ||
                string.IsNullOrWhiteSpace(measurement.LabelFontFamily) ||
                measurement.LabelFontFamily.Length > 128 ||
                !double.IsFinite(measurement.LabelFontSizePt) ||
                measurement.LabelFontSizePt is < 4 or > 72 ||
                !double.IsFinite(measurement.FillOpacityPercent) ||
                measurement.FillOpacityPercent is < 0 or > 100)
            {
                throw new InvalidDataException("工程包含无效的科学测量记录。");
            }
        }

        if (document.Measurements.Select(item => item.Id).Distinct().Count() != document.Measurements.Count)
        {
            throw new InvalidDataException("工程包含重复的科学测量 ID。");
        }

        Dictionary<Guid, ProjectSourceSnapshot> sourcesById = document.Sources.ToDictionary(source => source.Id);
        foreach (ProjectScientificAnalysisSnapshot analysis in document.Analyses)
        {
            if (analysis.Id == Guid.Empty ||
                !sourcesById.TryGetValue(analysis.SourceAssetId, out ProjectSourceSnapshot? analysisSource) ||
                analysis.SourceRevision < 1 || analysis.SourceRevision > analysisSource.SourceRevision ||
                analysis.FrameIndex < 0 || analysis.FrameIndex >= Math.Max(1, analysisSource.Metadata.FrameCount) ||
                analysis.SourceBitDepth is not (8 or 16) ||
                string.IsNullOrWhiteSpace(analysis.AnalyzerId) ||
                analysis.AnalyzedAt == default ||
                analysis.Channel is not ("luminance" or "red" or "green" or "blue" or "alpha") ||
                analysis.Validity.State is not ("valid" or "reviewrequired" or "invalid"))
            {
                throw new InvalidDataException("工程包含无效或无法溯源的科学图像分析记录。");
            }

            if (analysis.Kind == "roiStatistics")
            {
                ProjectPixelRectSnapshot? region = analysis.Region;
                bool statisticsValid =
                    analysis.PixelCount is long pixelCount && pixelCount > 0 &&
                    analysis.Minimum is double minimum && double.IsFinite(minimum) &&
                    analysis.Maximum is double maximum && double.IsFinite(maximum) && maximum >= minimum &&
                    analysis.Mean is double mean && double.IsFinite(mean) && mean >= minimum && mean <= maximum &&
                    analysis.StandardDeviation is double standardDeviation &&
                    double.IsFinite(standardDeviation) && standardDeviation >= 0 &&
                    analysis.IntegratedIntensity is double integratedIntensity &&
                    double.IsFinite(integratedIntensity);
                bool regionValid = region is not null &&
                    region.X >= 0 && region.Y >= 0 && region.Width > 0 && region.Height > 0 &&
                    region.X + region.Width <= analysisSource.Metadata.Width &&
                    region.Y + region.Height <= analysisSource.Metadata.Height &&
                    analysis.PixelCount == region.Width * region.Height;
                bool histogramValid = analysis.Histogram.Count > 0 &&
                    analysis.Histogram.All(bin =>
                        double.IsFinite(bin.LowerBound) && double.IsFinite(bin.UpperBound) &&
                        bin.UpperBound >= bin.LowerBound && bin.Count >= 0) &&
                    analysis.Histogram.Sum(bin => bin.Count) == analysis.PixelCount;
                if (!statisticsValid || !regionValid || !histogramValid || analysis.Samples.Count != 0)
                {
                    throw new InvalidDataException("工程包含无效的 ROI 强度统计记录。");
                }
            }
            else if (analysis.Kind == "lineProfile")
            {
                bool samplesValid = analysis.Samples.Count >= 2 &&
                    analysis.Samples.All(sample =>
                        sample.Index >= 0 &&
                        double.IsFinite(sample.PixelX) && double.IsFinite(sample.PixelY) &&
                        double.IsFinite(sample.DistancePixels) && sample.DistancePixels >= 0 &&
                        (!sample.PhysicalDistance.HasValue ||
                         double.IsFinite(sample.PhysicalDistance.Value) && sample.PhysicalDistance.Value >= 0) &&
                        double.IsFinite(sample.RawIntensity) &&
                        double.IsFinite(sample.NormalizedIntensity) &&
                        sample.NormalizedIntensity is >= 0 and <= 1);
                if (!samplesValid || string.IsNullOrWhiteSpace(analysis.DistanceUnit) ||
                    analysis.Region is not null || analysis.Histogram.Count != 0)
                {
                    throw new InvalidDataException("工程包含无效的线强度剖面记录。");
                }
            }
            else if (analysis.Kind == "particleAnalysis")
            {
                ProjectPixelRectSnapshot? region = analysis.Region;
                bool regionValid = region is not null &&
                    region.X >= 0 && region.Y >= 0 && region.Width > 0 && region.Height > 0 &&
                    region.X + region.Width <= analysisSource.Metadata.Width &&
                    region.Y + region.Height <= analysisSource.Metadata.Height;
                bool optionsValid = analysis.AnalysisMode is
                        "brightParticles" or "darkParticles" or "darkPores" or "brightPhase" or
                        "grainRegions" or "darkCracks" or "brightLamellae" &&
                    analysis.UseAutomaticThreshold.HasValue &&
                    analysis.ThresholdNormalized is double requestedThreshold &&
                    double.IsFinite(requestedThreshold) && requestedThreshold is >= 0 and <= 1 &&
                    analysis.AppliedThresholdNormalized is double appliedThreshold &&
                    double.IsFinite(appliedThreshold) && appliedThreshold is >= 0 and <= 1 &&
                    analysis.MinimumAreaPixels is >= 1 and <= 10_000_000 &&
                    analysis.MaximumCandidates is >= 1 and <= 100_000 &&
                    analysis.ForegroundPixelCount is >= 0 &&
                    analysis.TotalPixelCount is > 0 &&
                    analysis.ForegroundPixelCount <= analysis.TotalPixelCount &&
                    region is not null && analysis.TotalPixelCount == region.Width * region.Height;
                double maximumRaw = analysis.SourceBitDepth == 16 ? ushort.MaxValue : byte.MaxValue;
                bool particlesValid = region is not null &&
                    analysis.MaximumCandidates is int maximumCandidates &&
                    analysis.Particles.Count <= maximumCandidates &&
                    analysis.Particles.Select(particle => particle.Id).Distinct().Count() ==
                    analysis.Particles.Count &&
                    analysis.Particles.All(particle =>
                        particle.Id > 0 &&
                        particle.Bounds.X >= region.X && particle.Bounds.Y >= region.Y &&
                        particle.Bounds.Width > 0 && particle.Bounds.Height > 0 &&
                        particle.Bounds.X + particle.Bounds.Width <= region.X + region.Width &&
                        particle.Bounds.Y + particle.Bounds.Height <= region.Y + region.Height &&
                        double.IsFinite(particle.CentroidX) && double.IsFinite(particle.CentroidY) &&
                        particle.CentroidX >= particle.Bounds.X &&
                        particle.CentroidX < particle.Bounds.X + particle.Bounds.Width &&
                        particle.CentroidY >= particle.Bounds.Y &&
                        particle.CentroidY < particle.Bounds.Y + particle.Bounds.Height &&
                        particle.AreaPixels > 0 && particle.PerimeterPixels > 0 &&
                        double.IsFinite(particle.MeanIntensity) &&
                        particle.MeanIntensity is >= 0 and <= 1 &&
                        double.IsFinite(particle.RawMeanIntensity) &&
                        particle.RawMeanIntensity is >= 0 && particle.RawMeanIntensity <= maximumRaw &&
                        double.IsFinite(particle.AspectRatio) && particle.AspectRatio >= 1 &&
                        double.IsFinite(particle.FeretMaximumPixels) && particle.FeretMaximumPixels > 0 &&
                        double.IsFinite(particle.FeretMinimumPixels) && particle.FeretMinimumPixels > 0 &&
                        particle.FeretMaximumPixels >= particle.FeretMinimumPixels);
                if (!regionValid || !optionsValid || !particlesValid ||
                    analysis.Histogram.Count != 0 || analysis.Samples.Count != 0)
                {
                    throw new InvalidDataException("工程包含无效的阈值或颗粒形貌分析记录。");
                }
            }
            else
            {
                throw new InvalidDataException($"工程包含未知图像分析类型：{analysis.Kind}");
            }
        }

        if (document.Analyses.Select(item => item.Id).Distinct().Count() != document.Analyses.Count)
        {
            throw new InvalidDataException("工程包含重复的科学图像分析 ID。");
        }

        foreach (ProjectGuideSnapshot guide in document.Guides)
        {
            bool vertical = string.Equals(guide.Orientation, "vertical", StringComparison.OrdinalIgnoreCase);
            bool horizontal = string.Equals(guide.Orientation, "horizontal", StringComparison.OrdinalIgnoreCase);
            double maximum = vertical ? document.Canvas.Width : document.Canvas.Height;
            if ((!vertical && !horizontal) || !double.IsFinite(guide.Position) ||
                guide.Position < 0 || guide.Position > maximum)
            {
                throw new InvalidDataException("工程包含无效或越界的参考线记录。");
            }
        }

        if (document.TemplateSnapshot is { } template &&
            (!double.IsFinite(template.SnapTolerancePixels) ||
             template.SnapTolerancePixels is < 1 or > 100 ||
             template.ExactSpacingPixels < 0 ||
             template.ExactSpacingPixels > Math.Max(document.Canvas.Width, document.Canvas.Height)))
        {
            throw new InvalidDataException("工程包含无效的吸附或精确间距设置。");
        }

        foreach (ProjectAnnotationSnapshot annotation in document.TemplateSnapshot?.Annotations ?? [])
        {
            bool knownKind = annotation.Kind is "text" or "arrow" or "line" or "rectangle" or "ellipse";
            if (annotation.Id == Guid.Empty ||
                !knownKind ||
                !double.IsFinite(annotation.X) || !double.IsFinite(annotation.Y) ||
                !double.IsFinite(annotation.EndX) || !double.IsFinite(annotation.EndY) ||
                !IsHexColor(annotation.StrokeColor) ||
                !IsHexColor(annotation.FillColor) ||
                !IsHexColor(annotation.TextColor) ||
                !double.IsFinite(annotation.FillOpacityPercent) ||
                annotation.FillOpacityPercent is < 0 or > 100 ||
                string.IsNullOrWhiteSpace(annotation.FontFamily) ||
                annotation.FontFamily.Length > 128 ||
                !double.IsFinite(annotation.FontSizePt) ||
                annotation.FontSizePt is < 4 or > 72 ||
                !double.IsFinite(annotation.StrokeWidthPt) ||
                annotation.StrokeWidthPt is < 0.25 or > 10 ||
                annotation.ZIndex < 0)
            {
                throw new InvalidDataException("工程包含无效的标注样式或几何记录。");
            }
        }

        if (document.TemplateSnapshot?.GlobalStyle is { } style &&
            (string.IsNullOrWhiteSpace(style.FontFamily) || style.FontFamily.Length > 128 ||
             !double.IsFinite(style.FontSizePt) || style.FontSizePt is < 4 or > 72 ||
             !double.IsFinite(style.StrokeWidthPt) || style.StrokeWidthPt is < 0.25 or > 10 ||
             !IsHexColor(style.TextColor) || !IsHexColor(style.ShapeColor) ||
             !IsHexColor(style.ScaleBarColor) ||
             string.IsNullOrWhiteSpace(style.PanelLabelFontFamily) ||
             style.PanelLabelFontFamily.Length > 128 ||
             !double.IsFinite(style.PanelLabelFontSizePt) ||
             style.PanelLabelFontSizePt is < 4 or > 72 ||
             !IsHexColor(style.PanelLabelTextColor) ||
             string.IsNullOrWhiteSpace(style.ScaleBarFontFamily) ||
             style.ScaleBarFontFamily.Length > 128 ||
             !double.IsFinite(style.ScaleBarFontSizePt) ||
             style.ScaleBarFontSizePt is < 4 or > 72 ||
             !IsHexColor(style.ScaleBarLabelColor) ||
             !double.IsFinite(style.ScaleBarThicknessPt) ||
             style.ScaleBarThicknessPt is < 0.25 or > 10))
        {
            throw new InvalidDataException("工程包含无效的全局图样式。字体须为 4–72 pt，线宽须为 0.25–10 pt，颜色须为 HEX。 ");
        }

        ProjectScientificColorSnapshot[] scientificColors =
            document.TemplateSnapshot?.ScientificColors.ToArray() ?? [];
        if (scientificColors.Any(color =>
                color.Id == Guid.Empty ||
                string.IsNullOrWhiteSpace(color.Name) ||
                color.Name.Trim().Length > 64 ||
                !IsHexColor(color.Color)) ||
            scientificColors.Select(color => color.Id).Distinct().Count() != scientificColors.Length ||
            scientificColors.Select(color => color.Name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != scientificColors.Length)
        {
            throw new InvalidDataException("工程包含无效或重复的科研颜色字典条目。");
        }
    }

    private static bool IsHexColor(string? value)
        => ScientificStyleColor.ValidateColor(value);

    private static bool IsValidPanelStyleOverride(ProjectPanelStyleOverrideSnapshot style)
    {
        bool hasOverride = style.PanelLabel is not null ||
                           style.ScaleBarText is not null ||
                           style.ScaleBar is not null;
        bool scaleBarValid = style.ScaleBar is null ||
            (style.ScaleBar.DefaultPosition is
                 "bottomLeft" or "bottomRight" or "topLeft" or "topRight" or "custom" &&
             double.IsFinite(style.ScaleBar.BarThicknessPt) &&
             style.ScaleBar.BarThicknessPt is >= 0.25 and <= 10 &&
             IsHexColor(style.ScaleBar.Color));
        return hasOverride &&
               IsValidTextStyle(style.PanelLabel) &&
               IsValidTextStyle(style.ScaleBarText) &&
               scaleBarValid;
    }

    private static bool IsValidTextStyle(ProjectTextStyleSnapshot? style) => style is null ||
        (!string.IsNullOrWhiteSpace(style.FontFamily) &&
         style.FontFamily.Length <= 128 &&
         double.IsFinite(style.FontSizePt) &&
         style.FontSizePt is >= 4 and <= 72 &&
         IsHexColor(style.Color));

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
