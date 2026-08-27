using System.Globalization;
using System.IO;
using System.Text;

namespace SciCanvas.Presentation;

public static class SourceManifestWriter
{
    private static readonly string[] Headers =
    [
        "AssetId",
        "DisplayName",
        "OriginalPath",
        "SHA256",
        "SourceRevision",
        "Width",
        "Height",
        "BitDepth",
        "Channels",
        "FrameCount",
        "OME",
        "Calibration",
        "LinkState",
    ];

    public static void WriteNew(
        string targetPath,
        IEnumerable<SourceAssetItemViewModel> sources)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(sources);

        using var stream = new FileStream(
            targetPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        writer.WriteLine(string.Join(',', Headers.Select(Escape)));
        foreach (SourceAssetItemViewModel item in sources.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var asset = item.Asset;
            var metadata = asset.Metadata;
            string calibration = item.Calibration.IsCalibrated
                ? $"{item.Calibration.UnitsPerPixelX.ToString("0.###############", CultureInfo.InvariantCulture)} × {item.Calibration.UnitsPerPixelY.ToString("0.###############", CultureInfo.InvariantCulture)} {item.Calibration.Unit}/px ({item.Calibration.Origin})"
                : "Uncalibrated";
            string ome = metadata.Ome is null
                ? "No"
                : $"Yes; {metadata.Ome.Summary}; XML SHA-256={metadata.Ome.XmlSha256}";
            string[] values =
            [
                asset.Id.ToString("D"),
                asset.DisplayName,
                asset.OriginalPath,
                asset.Fingerprint.Sha256,
                item.SourceRevision.ToString(CultureInfo.InvariantCulture),
                metadata.PixelSize.Width.ToString(CultureInfo.InvariantCulture),
                metadata.PixelSize.Height.ToString(CultureInfo.InvariantCulture),
                metadata.BitsPerChannel.ToString(CultureInfo.InvariantCulture),
                metadata.Channels.ToString(CultureInfo.InvariantCulture),
                metadata.FrameCount.ToString(CultureInfo.InvariantCulture),
                ome,
                calibration,
                asset.LinkState.ToString(),
            ];
            writer.WriteLine(string.Join(',', values.Select(Escape)));
        }

        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private static string Escape(string? value)
    {
        string normalized = value ?? string.Empty;
        return normalized.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{normalized.Replace("\"", "\"\"")}\""
            : normalized;
    }
}
