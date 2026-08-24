using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class CliExportIntegrationTests
{
    [Fact]
    public async Task CliExportsSavedProjectAndReturnsSuccess()
    {
        using var workspace = new CliTempWorkspace();
        string sourcePath = Path.Combine(workspace.Root, "source.png");
        string projectPath = Path.Combine(workspace.Root, "cli.scicanvas");
        string outputDirectory = Path.Combine(workspace.Root, "output");
        Directory.CreateDirectory(outputDirectory);
        CreateSolidPng(sourcePath);
        var sourceInfo = new FileInfo(sourcePath);
        Guid sourceId = Guid.NewGuid();
        Guid layerId = Guid.NewGuid();
        var project = new SciCanvasProjectDocument
        {
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Title = "cli",
            Canvas = new ProjectCanvasSnapshot
            {
                Width = 2,
                Height = 2,
                Background = "custom",
                BackgroundColor = "#FFFFFFFF",
            },
            Sources = [new ProjectSourceSnapshot
            {
                Id = sourceId,
                DisplayName = "source.png",
                OriginalPath = sourcePath,
                Fingerprint = new ProjectFingerprintSnapshot
                {
                    ByteLength = sourceInfo.Length,
                    LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc,
                    Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(sourcePath))),
                },
                Metadata = new ProjectImageMetadataSnapshot
                {
                    Width = 2,
                    Height = 2,
                    Channels = 4,
                    BitsPerChannel = 8,
                    PixelFormat = "Bgra32",
                },
            }],
            Layers = [new ProjectImageLayerSnapshot
            {
                Id = layerId,
                Name = "panel a",
                PanelLabel = string.Empty,
                SourceAssetId = sourceId,
                SourceRect = new ProjectPixelRectSnapshot { X = 0, Y = 0, Width = 2, Height = 2 },
                Transform = new ProjectTransformSnapshot { X = 0, Y = 0, ScaleX = 1, ScaleY = 1 },
            }],
            ExportProfiles = [new ProjectExportProfileSnapshot
            {
                Id = Guid.Parse("4757F9DE-FE43-47F6-9675-690BE0A431E0"),
                Name = "主图",
                Format = "tiff",
                Dpi = 300,
                Scale = 1,
                WriteProvenance = true,
                BitDepth = 16,
                WriteAuditReport = true,
            }],
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                TemplateId = "cli-test",
                ExactSpacingPixels = 0,
                LayerSlots = new Dictionary<Guid, string> { [layerId] = "a" },
            },
        };
        await new JsonProjectStore().SaveAsync(projectPath, project);

        string cliAssembly = Path.Combine(AppContext.BaseDirectory, "SciCanvas.Cli.dll");
        Assert.True(File.Exists(cliAssembly), $"CLI assembly missing: {cliAssembly}");
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(cliAssembly);
        startInfo.ArgumentList.Add("export");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(projectPath);
        startInfo.ArgumentList.Add("--output-dir");
        startInfo.ArgumentList.Add(outputDirectory);
        using Process process = Process.Start(startInfo)!;
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("RESULT\t1/1", stdout, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "cli_main-tiff.tiff")), stderr);
        Assert.True(File.Exists(Path.Combine(outputDirectory, "cli_main-tiff.provenance.json")), stderr);
    }

    private static void CreateSolidPng(string path)
    {
        byte[] pixels =
        [
            0x20, 0x40, 0x80, 0xFF,
            0x20, 0x40, 0x80, 0xFF,
            0x20, 0x40, 0x80, 0xFF,
            0x20, 0x40, 0x80, 0xFF,
        ];
        BitmapSource bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private sealed class CliTempWorkspace : IDisposable
    {
        public CliTempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "SciCanvas.Cli.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
