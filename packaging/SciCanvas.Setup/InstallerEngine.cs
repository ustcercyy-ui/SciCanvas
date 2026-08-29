using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

namespace SciCanvas.Setup;

internal sealed record InstallerProgress(int Percentage, string Message);

internal static class InstallerEngine
{
    private const string PayloadResourceName = "SciCanvas.Payload.zip";
    private const long FreeSpaceReserveBytes = 64L * 1024 * 1024;
    private static readonly Lazy<long> PayloadSize = new(CalculatePayloadUncompressedSize);

    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SciCanvas");

    public static long PayloadUncompressedSize => PayloadSize.Value;

    private static long CalculatePayloadUncompressedSize()
    {
        using Stream payload = OpenPayload();
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        return archive.Entries.Sum(entry => entry.Length);
    }

    public static string NormalizeInstallDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("请选择安装位置。");
        }

        string expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (!Path.IsPathFullyQualified(expanded))
        {
            throw new ArgumentException("安装位置必须是完整路径，例如 D:\\Apps\\SciCanvas。");
        }

        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expanded));
        string? root = Path.GetPathRoot(fullPath);
        if (string.Equals(
            fullPath,
            Path.TrimEndingDirectorySeparator(root ?? string.Empty),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("不能把磁盘根目录直接用作安装位置，请选择一个子文件夹。");
        }

        return fullPath;
    }

    public static void EnsureEnoughDiskSpace(string installDirectory, long payloadBytes)
    {
        string? root = Path.GetPathRoot(installDirectory);
        if (string.IsNullOrWhiteSpace(root) || root.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            var drive = new DriveInfo(root);
            long required = payloadBytes + FreeSpaceReserveBytes;
            if (drive.IsReady && drive.AvailableFreeSpace < required)
            {
                throw new IOException(
                    $"磁盘空间不足。至少需要 {FormatBytes(required)} 可用空间，当前仅有 {FormatBytes(drive.AvailableFreeSpace)}。");
            }
        }
        catch (ArgumentException)
        {
            // A provider-backed path may not expose DriveInfo. The copy operation
            // will still report a useful error if that destination cannot be used.
        }
    }

    public static void Install(
        InstallerOptions options,
        IProgress<InstallerProgress>? progress = null)
    {
        string installDirectory = NormalizeInstallDirectory(options.InstallDirectory);
        EnsureEnoughDiskSpace(installDirectory, PayloadUncompressedSize);
        EnsureApplicationIsClosed();

        string? extractionRoot = null;
        try
        {
            progress?.Report(new InstallerProgress(2, "正在准备安装文件…"));
            extractionRoot = Path.Combine(
                Path.GetTempPath(),
                "SciCanvasSetup",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractionRoot);
            ExtractPayload(extractionRoot, progress);

            progress?.Report(new InstallerProgress(48, "正在复制应用文件…"));
            RunInstallScript(extractionRoot, installDirectory, options);

            string executablePath = Path.Combine(installDirectory, "SciCanvas.App.exe");
            if (!File.Exists(executablePath))
            {
                throw new InvalidDataException("安装完成后未找到 SciCanvas.App.exe。");
            }

            progress?.Report(new InstallerProgress(100, "安装完成"));
        }
        finally
        {
            if (extractionRoot is not null)
            {
                TryDeleteDirectory(extractionRoot);
            }
        }
    }

    public static void LaunchApplication(string installDirectory)
    {
        string executablePath = Path.Combine(
            NormalizeInstallDirectory(installDirectory),
            "SciCanvas.App.exe");
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("未找到已安装的 SciCanvas.App.exe。", executablePath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = Path.GetDirectoryName(executablePath)!,
            UseShellExecute = true,
        });
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return $"{value:0.#} {units[unit]}";
    }

    public static void TryWriteFailureLog(Exception exception)
    {
        try
        {
            string logDirectory = Path.Combine(Path.GetTempPath(), "SciCanvasSetup");
            Directory.CreateDirectory(logDirectory);
            File.WriteAllText(
                Path.Combine(logDirectory, "install-error.log"),
                $"{DateTimeOffset.Now:O}{Environment.NewLine}{exception}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static Stream OpenPayload() => Assembly.GetExecutingAssembly()
        .GetManifestResourceStream(PayloadResourceName)
        ?? throw new InvalidOperationException("安装包内缺少 SciCanvas 发布内容。");

    private static void ExtractPayload(
        string extractionRoot,
        IProgress<InstallerProgress>? progress)
    {
        using Stream payload = OpenPayload();
        using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
        string root = Path.GetFullPath(extractionRoot + Path.DirectorySeparatorChar);
        int totalEntries = Math.Max(archive.Entries.Count, 1);

        for (int index = 0; index < archive.Entries.Count; index++)
        {
            ZipArchiveEntry entry = archive.Entries[index];
            string targetPath = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
            if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("安装包包含无效的路径。");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }

            int percentage = 5 + (int)(38d * (index + 1) / totalEntries);
            progress?.Report(new InstallerProgress(percentage, "正在解压安装文件…"));
        }
    }

    private static void RunInstallScript(
        string extractionRoot,
        string installDirectory,
        InstallerOptions options)
    {
        string installScript = Path.Combine(extractionRoot, "Install-SciCanvas.ps1");
        if (!File.Exists(installScript))
        {
            throw new InvalidDataException("安装包缺少安装脚本。");
        }

        string windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string powerShellPath = Path.Combine(
            windowsDirectory,
            "System32",
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");

        var startInfo = new ProcessStartInfo
        {
            FileName = File.Exists(powerShellPath) ? powerShellPath : "powershell.exe",
            WorkingDirectory = extractionRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(installScript);
        startInfo.ArgumentList.Add("-InstallRoot");
        startInfo.ArgumentList.Add(installDirectory);
        if (!options.CreateStartMenuShortcut)
        {
            startInfo.ArgumentList.Add("-NoStartMenuShortcut");
        }

        if (options.CreateDesktopShortcut)
        {
            startInfo.ArgumentList.Add("-CreateDesktopShortcut");
        }

        using Process process = Process.Start(startInfo)
            ?? throw new Win32Exception("无法启动安装脚本。");
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(standardOutput, standardError);

        if (process.ExitCode != 0)
        {
            string details = standardError.Result.Trim();
            if (details.Length == 0)
            {
                details = standardOutput.Result.Trim();
            }

            throw new InvalidOperationException(
                details.Length == 0
                    ? $"安装脚本执行失败，退出码：{process.ExitCode}。"
                    : $"安装脚本执行失败：{details}");
        }
    }

    private static void EnsureApplicationIsClosed()
    {
        Process[] processes = Process.GetProcessesByName("SciCanvas.App");
        try
        {
            if (processes.Length > 0)
            {
                throw new InvalidOperationException("SciCanvas 正在运行，请先关闭后再安装。");
            }
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
