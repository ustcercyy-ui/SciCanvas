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

        Validate(document);
        return document;
    }

    public async Task SaveAsync(
        string path,
        SciCanvasProjectDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
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
        if (document.SchemaVersion is not ("0.1" or "0.9"))
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
