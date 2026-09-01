using System.Net;
using System.IO;
using System.Text;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public static class SubmissionQcReportWriter
{
    public static void WriteNew(string targetPath, UnifiedQcReport result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(result);

        string rows = string.Join(
            Environment.NewLine,
            result.Issues
                .OrderByDescending(issue => issue.Severity)
                .ThenBy(issue => issue.Code, StringComparer.Ordinal)
                .Select(issue =>
                    $"<tr class=\"{issue.Severity.ToString().ToLowerInvariant()}\"><td>{Encode(issue.Severity.ToString())}</td><td>{Encode(issue.Code)}</td><td>{Encode(issue.PanelLabel ?? string.Empty)}</td><td>{Encode(issue.Message)}</td></tr>"));
        if (rows.Length == 0)
        {
            rows = "<tr><td colspan=\"4\">No QC issues.</td></tr>";
        }

        string html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>SciCanvas Submission QC Report</title>
              <style>
                body { font: 14px system-ui; max-width: 1120px; margin: 32px auto; padding: 0 18px; color: #17212b; }
                table { border-collapse: collapse; width: 100%; } th, td { border: 1px solid #ccd3da; padding: 8px; text-align: left; vertical-align: top; }
                th { background: #eef2f5; } .error td:first-child { color: #b00020; font-weight: 700; } .warning td:first-child { color: #9a6400; font-weight: 700; }
                code { background: #eef2f5; padding: 2px 5px; border-radius: 3px; }
              </style>
            </head>
            <body>
              <h1>SciCanvas Submission QC Report</h1>
              <p><strong>{{Encode(result.Summary)}}</strong></p>
              <p>Errors block package generation. Warnings and information are retained here for audit.</p>
              <table><thead><tr><th>Severity</th><th>Rule</th><th>Panel</th><th>Message</th></tr></thead><tbody>
              {{rows}}
              </tbody></table>
            </body>
            </html>
            """;
        WriteNewText(targetPath, html);
    }

    public static void WriteNew(string targetPath, FigurePreflightResult result) =>
        WriteNew(targetPath, UnifiedQcReport.FromFigurePreflight(result));

    private static string Encode(string value) => WebUtility.HtmlEncode(value);

    internal static void WriteNewText(string targetPath, string content)
    {
        using var stream = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
