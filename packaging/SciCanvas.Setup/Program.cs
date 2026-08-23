using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace SciCanvas.Setup;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        string? extractionRoot = null;
        try
        {
            extractionRoot = Path.Combine(
                Path.GetTempPath(),
                "SciCanvasSetup",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(extractionRoot);

            using Stream payload = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("SciCanvas.Payload.zip")
                ?? throw new InvalidOperationException("安装包内缺少 SciCanvas 发布内容。");
            using var archive = new ZipArchive(payload, ZipArchiveMode.Read, leaveOpen: false);
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string targetPath = Path.GetFullPath(Path.Combine(extractionRoot, entry.FullName));
                string root = Path.GetFullPath(extractionRoot + Path.DirectorySeparatorChar);
                if (!targetPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("安装包包含无效的路径。");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(targetPath);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                entry.ExtractToFile(targetPath, overwrite: true);
            }

            string installScript = Path.Combine(extractionRoot, "Install-SciCanvas.cmd");
            if (!File.Exists(installScript))
            {
                throw new InvalidDataException("安装包缺少安装脚本。");
            }

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                Arguments = $"/d /c call \"{installScript}\"",
                WorkingDirectory = extractionRoot,
                UseShellExecute = true,
            });
            process?.WaitForExit();
            return process?.ExitCode ?? 1;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show(
                $"SciCanvas 安装失败：{exception.Message}",
                "SciCanvas 安装程序",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (extractionRoot is not null)
            {
                try
                {
                    Directory.Delete(extractionRoot, recursive: true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
