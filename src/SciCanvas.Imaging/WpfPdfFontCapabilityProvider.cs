using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;

namespace SciCanvas.Imaging;

public sealed class WpfPdfFontCapabilityProvider : IPdfFontCapabilityProvider
{
    public static WpfPdfFontCapabilityProvider Instance { get; } = new();

    private readonly ConcurrentDictionary<(string Family, bool IsBold), PdfFontCapability> _cache = new();

    private WpfPdfFontCapabilityProvider()
    {
    }

    public PdfFontCapability GetCapability(string effectiveFont, bool isBold)
    {
        string family = effectiveFont?.Trim() ?? string.Empty;
        return _cache.GetOrAdd(
            (family.ToUpperInvariant(), isBold),
            _ => Inspect(family, isBold));
    }

    private static PdfFontCapability Inspect(string family, bool isBold)
    {
        if (family.Length == 0)
        {
            return Unavailable(family, installed: false);
        }

        var typeface = new Typeface(
            new FontFamily(family),
            FontStyles.Normal,
            isBold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface))
        {
            return Unavailable(family, installed: false);
        }
        if (glyphTypeface.StyleSimulations != StyleSimulations.None)
        {
            return Unavailable(family, installed: true);
        }

        try
        {
            using Stream input = glyphTypeface.GetFontStream();
            using var output = new MemoryStream();
            input.CopyTo(output);
            byte[] bytes = output.ToArray();
            bool supported = HasTrueTypeOutlines(bytes);
            OpenTypeEmbeddingRights rights = OpenTypeEmbeddingRightsReader.Read(bytes);
            return new PdfFontCapability(
                family,
                family,
                IsInstalled: true,
                IsSupportedFontFormat: supported,
                rights.Permission,
                rights.SubsettingPermitted,
                EmbeddingImplementationAvailable: supported);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or NotSupportedException or OverflowException)
        {
            return Unavailable(family, installed: true);
        }
    }

    private static PdfFontCapability Unavailable(string family, bool installed) => new(
        family.Length == 0 ? "Unknown" : family,
        family.Length == 0 ? "Unknown" : family,
        installed,
        IsSupportedFontFormat: false,
        FontEmbeddingPermission.Unknown,
        SubsettingPermitted: false,
        EmbeddingImplementationAvailable: false);

    private static bool HasTrueTypeOutlines(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 &&
        ((bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0 && bytes[3] == 0) ||
         (bytes[0] == (byte)'t' && bytes[1] == (byte)'r' &&
          bytes[2] == (byte)'u' && bytes[3] == (byte)'e'));
}
