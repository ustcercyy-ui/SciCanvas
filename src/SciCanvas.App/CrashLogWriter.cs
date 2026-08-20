using System.IO;
using System.Text;

namespace SciCanvas.App;

internal static class CrashLogWriter
{
    private static readonly object SyncRoot = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SciCanvas",
        "Logs",
        "scicanvas.log");

    public static void Write(string source, Exception exception)
    {
        try
        {
            lock (SyncRoot)
            {
                string? directory = Path.GetDirectoryName(LogPath);
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                var entry = new StringBuilder()
                    .AppendLine(new string('-', 80))
                    .AppendLine($"UTC: {DateTimeOffset.UtcNow:O}")
                    .AppendLine($"Source: {source}")
                    .AppendLine(exception.ToString())
                    .ToString();

                File.AppendAllText(LogPath, entry, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never become a second application failure.
        }
    }
}
