using System.Text.Json;

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
                !coordinatesFinite ||
                !pathValid ||
                (string.Equals(measurement.Kind, "polyline", StringComparison.OrdinalIgnoreCase) &&
                 measurement.Points.Count < 2) ||
                !IsHexColor(measurement.StrokeColor) ||
                !double.IsFinite(measurement.StrokeWidthPixels) ||
                measurement.StrokeWidthPixels is < 1 or > 12 ||
                measurement.LineStyle is not ("solid" or "dash" or "dot" or "dash-dot") ||
                !double.IsFinite(measurement.MarkerSizePixels) ||
                measurement.MarkerSizePixels is < 8 or > 48 ||
                !double.IsFinite(measurement.FillOpacityPercent) ||
                measurement.FillOpacityPercent is < 0 or > 60)
            {
                throw new InvalidDataException("工程包含无效的科学测量记录。");
            }
        }

        if (document.Measurements.Select(item => item.Id).Distinct().Count() != document.Measurements.Count)
        {
            throw new InvalidDataException("工程包含重复的科学测量 ID。");
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

        if (document.TemplateSnapshot?.GlobalStyle is { } style &&
            (string.IsNullOrWhiteSpace(style.FontFamily) || style.FontFamily.Length > 128 ||
             !double.IsFinite(style.FontSizePt) || style.FontSizePt is < 4 or > 72 ||
             !double.IsFinite(style.StrokeWidthPt) || style.StrokeWidthPt is < 0.25 or > 10 ||
             !IsHexColor(style.TextColor) || !IsHexColor(style.ShapeColor) ||
             !IsHexColor(style.ScaleBarColor)))
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
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        return hex.Length is 6 or 8 && hex.All(Uri.IsHexDigit);
    }

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
