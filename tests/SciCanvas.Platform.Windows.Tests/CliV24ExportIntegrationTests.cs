using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class CliV24ExportIntegrationTests
{
    [Fact]
    public async Task CliExport_PreservesCompositeScientificObjectsMeasurementsAndProvenance()
    {
        using var workspace = new CliV24TempWorkspace();
        string redPath = Path.Combine(workspace.Root, "Ti.png");
        string greenPath = Path.Combine(workspace.Root, "Al.png");
        string projectPath = Path.Combine(workspace.Root, "cli-v24.scicanvas");
        string outputDirectory = Path.Combine(workspace.Root, "output");
        Directory.CreateDirectory(outputDirectory);
        WriteGray16Png(redPath, [0, ushort.MaxValue]);
        WriteGray16Png(greenPath, [ushort.MaxValue, 0]);

        Guid redSourceId = Guid.NewGuid();
        Guid greenSourceId = Guid.NewGuid();
        Guid redChannelId = Guid.NewGuid();
        Guid greenChannelId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid panelId = Guid.NewGuid();
        Guid measurementId = Guid.NewGuid();
        Guid colorbarId = Guid.NewGuid();
        ProjectSourceSnapshot redSource = await CreateSourceSnapshotAsync(redSourceId, redPath, 3);
        ProjectSourceSnapshot greenSource = await CreateSourceSnapshotAsync(greenSourceId, greenPath, 4);
        var measurement = new ProjectMeasurementSnapshot
        {
            Id = measurementId,
            SourceAssetId = redSourceId,
            SourceRevision = 3,
            Kind = "length",
            X1 = 0,
            Y1 = 0,
            X2 = 1,
            Y2 = 0,
        };
        var project = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.4",
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            Title = "cli-v24",
            Canvas = new ProjectCanvasSnapshot
            {
                Width = 200,
                Height = 100,
                Background = "custom",
                BackgroundColor = "#FF000000",
            },
            Sources = [redSource, greenSource],
            MultiChannelGroups =
            [
                new ProjectMultiChannelAssetGroupSnapshot
                {
                    Id = groupId,
                    Name = "EDS composite",
                    ReferenceAssetId = redSourceId,
                    SameFieldOfViewConfirmed = true,
                    Members =
                    [
                        new ProjectChannelGroupMemberSnapshot
                        {
                            ChannelId = redChannelId,
                            AssetId = redSourceId,
                            SourceRevision = 3,
                            Name = "Ti",
                            NameOrigin = "user",
                            IsNameConfirmed = true,
                            Color = "#FFFF0000",
                            DisplayMinimum = 0,
                            DisplayMaximum = ushort.MaxValue,
                        },
                        new ProjectChannelGroupMemberSnapshot
                        {
                            ChannelId = greenChannelId,
                            AssetId = greenSourceId,
                            SourceRevision = 4,
                            Name = "Al",
                            NameOrigin = "user",
                            IsNameConfirmed = true,
                            Color = "#FF00FF00",
                            DisplayMinimum = 0,
                            DisplayMaximum = ushort.MaxValue,
                        },
                    ],
                },
            ],
            Layers =
            [
                new ProjectImageLayerSnapshot
                {
                    Id = panelId,
                    Name = "Panel a",
                    PanelLabel = "a",
                    SourceAssetId = redSourceId,
                    CompositeGroupId = groupId,
                    SourceRect = new ProjectPixelRectSnapshot { X = 0, Y = 0, Width = 2, Height = 1 },
                    Transform = new ProjectTransformSnapshot { X = 0, Y = 0, ScaleX = 50, ScaleY = 100 },
                },
            ],
            ExportProfiles =
            [
                new ProjectExportProfileSnapshot
                {
                    Id = Guid.Parse("B7D1C6D5-4B43-4C36-9A6F-7F6F2F4D5E22"),
                    Name = "Supplement PNG",
                    Format = "png",
                    Dpi = 300,
                    Scale = 1,
                    WriteProvenance = true,
                    BitDepth = 8,
                    PdfFontStrategy = "outlineText",
                },
            ],
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                TemplateId = "cli-v24-test",
                ExactSpacingPixels = 0,
                LayerSlots = new Dictionary<Guid, string> { [panelId] = "a" },
                ScientificObjects =
                [
                    new ProjectFigureScientificObjectSnapshot
                    {
                        Id = colorbarId,
                        Kind = "colorbar",
                        Points = "110,10;130,90",
                        Label = "Ti intensity",
                        Minimum = 0,
                        Maximum = ushort.MaxValue,
                        Unit = "counts",
                        Colormap = "viridis",
                        ChannelId = redChannelId,
                        ZIndex = 10,
                    },
                    new ProjectFigureScientificObjectSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Kind = "channelLegend",
                        Points = "140,10;195,55",
                        Label = "Channels",
                        ChannelEntries = "Ti|#FFFF0000;Al|#FF00FF00",
                        ZIndex = 11,
                    },
                ],
                MeasurementOverlays =
                [
                    new ProjectMeasurementOverlaySnapshot
                    {
                        Id = Guid.NewGuid(),
                        MeasurementId = measurementId,
                        PanelId = panelId,
                        SourceGeometry = measurement,
                        CalibrationRelationship = new ProjectMeasurementOverlayCalibrationSnapshot
                        {
                            SourceAssetId = redSourceId,
                            SourceRevision = 3,
                            UnitsPerPixelX = 0.5,
                            UnitsPerPixelY = 0.5,
                            Unit = "µm",
                        },
                        LabelOverride = "0.5 µm",
                        ZIndex = 12,
                    },
                ],
            },
        };
        await new JsonProjectStore().SaveAsync(projectPath, project);

        string cliAssembly = Path.Combine(AppContext.BaseDirectory, "SciCanvas.Cli.dll");
        Assert.True(File.Exists(cliAssembly), $"CLI assembly missing: {cliAssembly}");
        ProcessStartInfo startInfo = CreateCliStartInfo(cliAssembly, projectPath, outputDirectory);
        using Process process = Process.Start(startInfo)!;
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(0, process.ExitCode);
        Assert.Contains("RESULT\t1/1", stdout, StringComparison.Ordinal);
        string outputPath = Path.Combine(outputDirectory, "cli-v24_supplement-png.png");
        string provenancePath = Path.Combine(outputDirectory, "cli-v24_supplement-png.provenance.json");
        Assert.True(File.Exists(outputPath), stderr);
        Assert.True(File.Exists(provenancePath), stderr);

        BitmapSource exported = Load(outputPath);
        var converted = new FormatConvertedBitmap(exported, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        (int redDominance, int greenDominance) = FindMaximumChannelDominance(pixels);
        Assert.True(greenDominance > 100, $"Expected a green-dominant composite region, max dominance was {greenDominance}.");
        Assert.True(redDominance > 100, $"Expected a red-dominant composite region, max dominance was {redDominance}.");

        using JsonDocument provenance = JsonDocument.Parse(await File.ReadAllTextAsync(provenancePath));
        JsonElement root = provenance.RootElement;
        JsonElement channels = root.GetProperty("channels");
        Assert.Equal(2, channels.GetArrayLength());
        JsonElement redChannel = channels.EnumerateArray().Single(item =>
            item.GetProperty("channelId").GetGuid() == redChannelId);
        Assert.Equal(3, redChannel.GetProperty("sourceRevision").GetInt64());
        Assert.Equal(16, redChannel.GetProperty("bitDepth").GetInt32());
        Assert.Equal(ushort.MaxValue, redChannel.GetProperty("displayMaximum").GetDouble());
        Assert.Equal(redChannelId, root.GetProperty("colorbars")[0].GetProperty("channelId").GetGuid());
        Assert.Equal(2, root.GetProperty("channelLegends")[0].GetProperty("entries").GetArrayLength());
        Assert.Equal(1, root.GetProperty("measurementOverlays").GetArrayLength());
        Assert.Equal(2, root.GetProperty("scientificObjects").GetArrayLength());
    }
    private static async Task<ProjectSourceSnapshot> CreateSourceSnapshotAsync(
        Guid id,
        string path,
        long revision)
    {
        FileInfo info = new(path);
        return new ProjectSourceSnapshot
        {
            Id = id,
            DisplayName = info.Name,
            OriginalPath = path,
            SourceRevision = revision,
            Fingerprint = new ProjectFingerprintSnapshot
            {
                ByteLength = info.Length,
                LastWriteTimeUtc = info.LastWriteTimeUtc,
                Sha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),
            },
            Metadata = new ProjectImageMetadataSnapshot
            {
                Width = 2,
                Height = 1,
                Channels = 1,
                BitsPerChannel = 16,
                PixelFormat = "Gray16",
            },
        };
    }

    private static ProcessStartInfo CreateCliStartInfo(
        string cliAssembly,
        string projectPath,
        string outputDirectory)
    {
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
        return startInfo;
    }

    private static void WriteGray16Png(string path, IReadOnlyList<ushort> values)
    {
        byte[] pixels = new byte[values.Count * 2];
        for (int index = 0; index < values.Count; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(pixels.AsSpan(index * 2, 2), values[index]);
        }

        BitmapSource bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Gray16, null, pixels, 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        encoder.Save(output);
    }

    private static BitmapSource Load(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        BitmapFrame frame = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad).Frames[0];
        frame.Freeze();
        return frame;
    }

    private static (int Red, int Green) FindMaximumChannelDominance(byte[] pixels)
    {
        int red = 0;
        int green = 0;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            red = Math.Max(red, pixels[offset + 2] - pixels[offset + 1]);
            green = Math.Max(green, pixels[offset + 1] - pixels[offset + 2]);
        }

        return (red, green);
    }

    private sealed class CliV24TempWorkspace : IDisposable
    {
        public CliV24TempWorkspace()
        {
            Root = Path.Combine(Path.GetTempPath(), "SciCanvas.Cli.V24.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); } catch { }
        }
    }
}
