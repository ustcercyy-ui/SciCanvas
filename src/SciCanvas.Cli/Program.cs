using System.IO;
using System.Security.Cryptography;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using SciCanvas.Persistence;

namespace SciCanvas.Cli;

internal static class Program
{
    private const int Success = 0;
    private const int UsageError = 2;
    private const int ValidationError = 3;
    private const int ExportError = 4;

    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            PrintUsage();
            return args.Length == 0 ? UsageError : Success;
        }

        if (!string.Equals(args[0], "export", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"未知命令：{args[0]}");
            PrintUsage();
            return UsageError;
        }

        try
        {
            CliOptions options = CliOptions.Parse(args[1..]);
            SciCanvasProjectDocument project = await new JsonProjectStore().LoadAsync(options.ProjectPath);
            FigureExportProfile[] profiles = ResolveProfiles(project, options.ProfileSelectors);
            if (options.ListProfiles)
            {
                foreach (FigureExportProfile profile in profiles)
                {
                    Console.WriteLine($"{profile.Id}\t{profile.Name}\t{profile.Format}\t{profile.Dpi} dpi\t{profile.BitDepth}-bit");
                }
                return Success;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            SourceAsset[] sources = await LoadAndVerifySourcesAsync(project.Sources);
            FigureExportDocument baseDocument = BuildDocument(project, sources);
            var exporter = new WpfFigureExporter();
            int completed = 0;
            List<string> failures = [];

            foreach (FigureExportProfile profile in profiles)
            {
                try
                {
                    FigureExportDocument variant = profile.Apply(baseDocument);
                    FigurePreflightResult preflight = FigurePreflight.Check(variant, sources, hasUnsavedChanges: false);
                    if (preflight.HasErrors)
                    {
                        throw new InvalidDataException(string.Join(
                            Environment.NewLine,
                            preflight.Issues
                                .Where(issue => issue.Severity == FigurePreflightSeverity.Error)
                                .Select(issue => issue.Message)));
                    }

                    string targetPath = CreateTargetPath(options.OutputDirectory, project.Title, profile);
                    EnsureNewSafeTarget(targetPath, sources);
                    Console.WriteLine($"EXPORT\t{profile.Name}\t{Path.GetFileName(targetPath)}");
                    await exporter.ExportAsync(variant, targetPath);

                    if (profile.WriteProvenance && !options.DisableProvenance)
                    {
                        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
                            variant,
                            targetPath,
                            typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.2.0-alpha",
                            sources,
                            preflight,
                            profile.Id,
                            profile.Name);
                        FigureProvenanceWriter.WriteJson(provenance, Path.ChangeExtension(targetPath, ".provenance.json"));
                        FigureProvenanceWriter.WriteHtml(provenance, Path.ChangeExtension(targetPath, ".export-report.html"));
                    }

                    completed++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    InvalidOperationException or NotSupportedException or ArgumentException)
                {
                    failures.Add($"{profile.Name}：{exception.Message}");
                }
            }

            Console.WriteLine($"RESULT\t{completed}/{profiles.Length}");
            foreach (string failure in failures)
            {
                Console.Error.WriteLine($"ERROR\t{failure}");
            }
            return failures.Count == 0 ? Success : ExportError;
        }
        catch (ArgumentException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return UsageError;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine(exception.Message);
            return ValidationError;
        }
    }

    private static async Task<SourceAsset[]> LoadAndVerifySourcesAsync(
        IReadOnlyList<ProjectSourceSnapshot> snapshots)
    {
        var sources = new SourceAsset[snapshots.Count];
        for (int index = 0; index < snapshots.Count; index++)
        {
            ProjectSourceSnapshot snapshot = snapshots[index];
            string path = Path.GetFullPath(snapshot.OriginalPath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException($"源图不存在：{path}", path);
            }

            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            string sha256 = Convert.ToHexString(await SHA256.HashDataAsync(stream));
            if (!string.Equals(sha256, snapshot.Fingerprint.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"源图 SHA-256 已变化：{snapshot.DisplayName}");
            }

            sources[index] = new SourceAsset(
                snapshot.Id,
                snapshot.DisplayName,
                path,
                new SourceFingerprint(
                    snapshot.Fingerprint.ByteLength,
                    snapshot.Fingerprint.LastWriteTimeUtc,
                    sha256,
                    snapshot.Fingerprint.WindowsFileId),
                ToMetadata(snapshot.Metadata),
                SourceLinkState.Verified);
        }
        return sources;
    }

    private static FigureExportDocument BuildDocument(
        SciCanvasProjectDocument project,
        IReadOnlyList<SourceAsset> sources)
    {
        Dictionary<Guid, SourceAsset> sourceMap = sources.ToDictionary(source => source.Id);
        IReadOnlyDictionary<Guid, ProjectScaleBarSnapshot> scaleBars =
            project.TemplateSnapshot?.ScaleBars ?? new Dictionary<Guid, ProjectScaleBarSnapshot>();
        FigurePanelExportItem[] panels = project.Layers
            .OrderBy(layer => layer.ZIndex)
            .Select(layer =>
            {
                if (!sourceMap.TryGetValue(layer.SourceAssetId, out SourceAsset? source))
                {
                    throw new InvalidDataException($"图层 {layer.Name} 引用了不存在的源图。");
                }

                PixelRect64 sourceRect = ToRect(layer.SourceRect);
                var destination = new PixelRect64(
                    Math.Max(0, (long)Math.Round(layer.Transform.X)),
                    Math.Max(0, (long)Math.Round(layer.Transform.Y)),
                    Math.Max(1, (long)Math.Round(sourceRect.Width * layer.Transform.ScaleX)),
                    Math.Max(1, (long)Math.Round(sourceRect.Height * layer.Transform.ScaleY)));
                FigureScaleBarExportSpec? scaleBar = scaleBars.TryGetValue(layer.Id, out ProjectScaleBarSnapshot? value) && value.Enabled
                    ? new FigureScaleBarExportSpec(
                        value.PhysicalUnitsPerSourcePixel,
                        value.PhysicalLength,
                        value.Unit,
                        value.ShowLabel)
                    : null;
                ProjectImageAdjustmentSnapshot? adjustment = layer.Adjustments.FirstOrDefault();
                return new FigurePanelExportItem(
                    source,
                    sourceRect,
                    destination,
                    layer.PanelLabel ?? string.Empty,
                    layer.Visible,
                    scaleBar,
                    adjustment is null ? null : ToAdjustment(adjustment),
                    layer.FrameIndex);
            })
            .ToArray();

        FigureAnnotationExportItem[] annotations = (project.TemplateSnapshot?.Annotations ?? [])
            .OrderBy(annotation => annotation.ZIndex)
            .Select(annotation => new FigureAnnotationExportItem(
                annotation.Kind,
                annotation.X,
                annotation.Y,
                annotation.EndX,
                annotation.EndY,
                annotation.Text,
                annotation.Color,
                annotation.FontSizePt,
                annotation.StrokeWidthPt,
                annotation.IsBold,
                annotation.Visible,
                annotation.ZIndex))
            .ToArray();
        string background = project.Canvas.BackgroundColor ?? project.Canvas.Background switch
        {
            "black" => "#FF000000",
            "transparent" => "#00FFFFFF",
            _ => "#FFFFFFFF",
        };
        return new FigureExportDocument(
            project.Canvas.Width,
            project.Canvas.Height,
            dpi: ResolveCanvasDpi(project),
            panels,
            annotations,
            background);
    }

    private static int ResolveCanvasDpi(SciCanvasProjectDocument project) =>
        project.ExportProfiles.FirstOrDefault()?.Dpi is > 0 and var dpi ? dpi : 300;

    private static FigureExportProfile[] ResolveProfiles(
        SciCanvasProjectDocument project,
        IReadOnlyList<string> selectors)
    {
        FigureExportProfile[] all = project.ExportProfiles.Count == 0
            ? FigureExportProfile.BuiltIns.ToArray()
            : project.ExportProfiles.Select(ToProfile).ToArray();
        if (selectors.Count == 0)
        {
            return all;
        }

        FigureExportProfile[] selected = all.Where(profile => selectors.Any(selector =>
                string.Equals(selector, profile.Id, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(selector, profile.Name, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (selected.Length != selectors.Count)
        {
            throw new ArgumentException("至少一个 --profile 未匹配工程中的预设 ID 或名称。使用 --list-profiles 查看可用值。");
        }
        return selected;
    }

    private static FigureExportProfile ToProfile(ProjectExportProfileSnapshot snapshot) => new(
        SnapshotIdToKey(snapshot.Id),
        snapshot.Name,
        snapshot.Format,
        snapshot.Dpi,
        snapshot.Scale,
        snapshot.WidthPixels,
        snapshot.HeightPixels,
        snapshot.WriteProvenance,
        snapshot.BitDepth ?? 8);

    private static string SnapshotIdToKey(Guid id) => id switch
    {
        var value when value == Guid.Parse("4757F9DE-FE43-47F6-9675-690BE0A431E0") => "main-tiff",
        var value when value == Guid.Parse("B7D1C6D5-4B43-4C36-9A6F-7F6F2F4D5E22") => "supplement-png",
        var value when value == Guid.Parse("F6A3B8E8-9B8D-4BA0-A9D9-5AF1BA58C44F") => "thumbnail-png",
        _ => id.ToString("D"),
    };

    private static ImageMetadata ToMetadata(ProjectImageMetadataSnapshot snapshot) => new(
        new PixelSize64(snapshot.Width, snapshot.Height),
        snapshot.Channels,
        snapshot.BitsPerChannel,
        snapshot.PixelFormat,
        snapshot.DpiX,
        snapshot.DpiY,
        snapshot.PhysicalSizeX,
        snapshot.PhysicalSizeY,
        snapshot.PhysicalUnit,
        snapshot.IccProfileName,
        snapshot.FrameCount,
        snapshot.Ome is null ? null : new OmeImageMetadata(
            snapshot.Ome.DimensionOrder,
            snapshot.Ome.PixelType,
            snapshot.Ome.SizeZ,
            snapshot.Ome.SizeC,
            snapshot.Ome.SizeT,
            snapshot.Ome.PhysicalSizeX,
            snapshot.Ome.PhysicalSizeY,
            snapshot.Ome.PhysicalSizeZ,
            snapshot.Ome.PhysicalSizeXUnit,
            snapshot.Ome.PhysicalSizeYUnit,
            snapshot.Ome.PhysicalSizeZUnit,
            snapshot.Ome.TimeIncrement,
            snapshot.Ome.TimeIncrementUnit,
            snapshot.Ome.ChannelNames,
            snapshot.Ome.XmlSha256));

    private static ImageAdjustmentParameters ToAdjustment(ProjectImageAdjustmentSnapshot snapshot) => new()
    {
        Brightness = snapshot.Brightness,
        Contrast = snapshot.Contrast,
        Gamma = snapshot.Gamma,
        BlackPoint = snapshot.BlackPoint,
        WhitePoint = snapshot.WhitePoint,
        Invert = snapshot.Invert,
        Grayscale = snapshot.Grayscale,
        Channel = snapshot.Channel,
    };

    private static PixelRect64 ToRect(ProjectPixelRectSnapshot snapshot) =>
        new(snapshot.X, snapshot.Y, snapshot.Width, snapshot.Height);

    private static string CreateTargetPath(string folder, string? title, FigureExportProfile profile)
    {
        string stem = SanitizeFileName(string.IsNullOrWhiteSpace(title) ? "figure" : title);
        string suffix = SanitizeFileName(profile.Id);
        return Path.GetFullPath(Path.Combine(folder, $"{stem}_{suffix}{profile.Extension}"));
    }

    private static string SanitizeFileName(string value)
    {
        HashSet<char> invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string result = string.Concat(value.Select(character => invalid.Contains(character) ? '_' : character)).Trim();
        return string.IsNullOrWhiteSpace(result) ? "figure" : result;
    }

    private static void EnsureNewSafeTarget(string targetPath, IReadOnlyList<SourceAsset> sources)
    {
        if (File.Exists(targetPath))
        {
            throw new IOException($"目标已存在，拒绝覆盖：{targetPath}");
        }
        if (sources.Any(source => string.Equals(
                Path.GetFullPath(source.OriginalPath),
                targetPath,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("导出目标不能与源图路径相同。");
        }
    }

    private static void PrintUsage() => Console.WriteLine(
        "SciCanvas CLI\n" +
        "  export --project <file.scicanvas> --output-dir <folder> [--profile <id-or-name>]... [--no-provenance]\n" +
        "  export --project <file.scicanvas> --list-profiles\n\n" +
        "退出码：0 成功，2 参数错误，3 工程/源图验证失败，4 部分或全部导出失败。");

    private sealed record CliOptions(
        string ProjectPath,
        string OutputDirectory,
        IReadOnlyList<string> ProfileSelectors,
        bool DisableProvenance,
        bool ListProfiles)
    {
        public static CliOptions Parse(IReadOnlyList<string> args)
        {
            string? project = null;
            string? output = null;
            List<string> profiles = [];
            bool noProvenance = false;
            bool listProfiles = false;
            for (int index = 0; index < args.Count; index++)
            {
                string option = args[index];
                switch (option.ToLowerInvariant())
                {
                    case "--project":
                        project = ReadValue(args, ref index, option);
                        break;
                    case "--output-dir":
                        output = ReadValue(args, ref index, option);
                        break;
                    case "--profile":
                        profiles.Add(ReadValue(args, ref index, option));
                        break;
                    case "--no-provenance":
                        noProvenance = true;
                        break;
                    case "--list-profiles":
                        listProfiles = true;
                        break;
                    default:
                        throw new ArgumentException($"未知参数：{option}");
                }
            }

            if (string.IsNullOrWhiteSpace(project))
            {
                throw new ArgumentException("缺少 --project。", nameof(args));
            }
            if (!listProfiles && string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("缺少 --output-dir。", nameof(args));
            }
            return new CliOptions(
                Path.GetFullPath(project),
                string.IsNullOrWhiteSpace(output) ? Environment.CurrentDirectory : Path.GetFullPath(output),
                profiles,
                noProvenance,
                listProfiles);
        }

        private static string ReadValue(IReadOnlyList<string> args, ref int index, string option)
        {
            if (++index >= args.Count || string.IsNullOrWhiteSpace(args[index]))
            {
                throw new ArgumentException($"{option} 缺少值。");
            }
            return args[index];
        }
    }
}
