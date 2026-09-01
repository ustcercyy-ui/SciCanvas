using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;

namespace SciCanvas.Imaging;

internal sealed class PdfEmbeddedFontRegistry(PdfFontStrategy strategy)
{
    private readonly Dictionary<string, EmbeddedFace> _faces = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _warnings = [];
    private readonly Dictionary<(string Family, bool IsBold), OutcomeState> _outcomes = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public IReadOnlyList<PdfFontExportOutcome> Outcomes => _outcomes
        .OrderBy(pair => pair.Key.Family, StringComparer.OrdinalIgnoreCase)
        .ThenBy(pair => pair.Key.IsBold)
        .Select(pair => new PdfFontExportOutcome(
            pair.Key.Family,
            pair.Key.IsBold,
            pair.Value.Embedded,
            pair.Value.Outlined,
            pair.Value.FallbackReasons.Count == 0
                ? null
                : string.Join("; ", pair.Value.FallbackReasons.Order(StringComparer.Ordinal))))
        .ToArray();

    public bool TryAppendText(
        StringBuilder content,
        string text,
        double x,
        double top,
        double fontSizePixels,
        string fontFamily,
        bool bold,
        Color fill,
        double scale,
        double pageHeight,
        Color? stroke = null,
        double strokeWidth = 0)
    {
        if (strategy == PdfFontStrategy.OutlineText || string.IsNullOrEmpty(text))
        {
            if (!string.IsNullOrEmpty(text))
            {
                RecordOutcome(fontFamily, bold, embedded: false, outlined: true);
            }
            return false;
        }

        EmbeddedFace? face = ResolveFace(fontFamily, bold);
        if (face is null)
        {
            return false;
        }

        var mapped = new List<(Rune Rune, ushort GlyphId)>();
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!face.GlyphTypeface.CharacterToGlyphMap.TryGetValue(rune.Value, out ushort glyphId) ||
                glyphId == 0)
            {
                return HandleUnavailable(
                    face.Family,
                    face.IsBold,
                    $"font “{fontFamily}” does not contain a directly mappable glyph for U+{rune.Value:X4}");
            }
            mapped.Add((rune, glyphId));
        }

        HashSet<ushort> candidateGlyphs = face.GlyphIds.Concat(mapped.Select(item => item.GlyphId)).ToHashSet();
        TrueTypeSubsetFont subset;
        try
        {
            subset = TrueTypeFontSubsetter.Build(face.FontBytes, candidateGlyphs);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or OverflowException)
        {
            return HandleUnavailable(face.Family, face.IsBold, exception.Message);
        }

        face.Subset = subset;
        face.GlyphIds.UnionWith(candidateGlyphs);
        var cids = new ushort[mapped.Count];
        for (int index = 0; index < mapped.Count; index++)
        {
            (Rune rune, ushort glyphId) = mapped[index];
            var key = new GlyphUnicodeKey(glyphId, rune.Value);
            if (!face.Cids.TryGetValue(key, out ushort cid))
            {
                if (face.Cids.Count >= ushort.MaxValue - 1)
                {
                    return HandleUnavailable(face.Family, face.IsBold, "a PDF font subset exceeded the 16-bit CID limit");
                }
                cid = checked((ushort)(face.Cids.Count + 1));
                face.Cids.Add(key, cid);
                face.GlyphByCid.Add(cid, glyphId);
                face.UnicodeByCid.Add(cid, rune.Value);
            }
            cids[index] = cid;
        }

        AppendColor(content, fill, fill: true);
        content.Append("q\n");
        if (stroke is Color strokeColor && strokeWidth > 0)
        {
            AppendColor(content, strokeColor, fill: false);
            content.Append($"{F(strokeWidth)} w ");
        }
        content.Append("BT\n");
        content.Append($"/{face.ResourceName} {F(fontSizePixels * scale)} Tf\n");
        content.Append(stroke is not null && strokeWidth > 0 ? "2 Tr\n" : "0 Tr\n");
        double baseline = top + face.GlyphTypeface.Baseline * fontSizePixels;
        content.Append($"1 0 0 1 {F(x * scale)} {F(pageHeight - baseline * scale)} Tm\n<");
        foreach (ushort cid in cids)
        {
            content.Append(cid.ToString("X4", CultureInfo.InvariantCulture));
        }
        content.Append("> Tj\nET\nQ\n");
        RecordOutcome(face.Family, face.IsBold, embedded: true, outlined: false);
        return true;
    }

    public IReadOnlyList<(string Name, int ObjectNumber)> AddPdfObjects(
        Func<byte[]?, int> addObject)
    {
        ArgumentNullException.ThrowIfNull(addObject);
        var resources = new List<(string Name, int ObjectNumber)>();
        foreach (EmbeddedFace face in _faces.Values.Where(item => item.Cids.Count > 0))
        {
            TrueTypeSubsetFont subset = face.Subset ??
                throw new InvalidOperationException("PDF embedded font has no validated subset.");
            int fontFile = addObject(BuildFlateStream(
                subset.FontBytes,
                $"/Length1 {subset.FontBytes.Length} "));
            int cidToGid = addObject(BuildFlateStream(BuildCidToGid(face)));
            int toUnicode = addObject(BuildStream(Encoding.ASCII.GetBytes(BuildToUnicode(face))));
            int descriptor = addObject(Encoding.ASCII.GetBytes(BuildDescriptor(subset, fontFile)));
            int descendant = addObject(Encoding.ASCII.GetBytes(
                BuildDescendantFont(face, subset, descriptor, cidToGid)));
            int type0 = addObject(Encoding.ASCII.GetBytes(
                $"<< /Type /Font /Subtype /Type0 /BaseFont /{subset.PdfBaseFontName} " +
                $"/Encoding /Identity-H /DescendantFonts [{descendant} 0 R] /ToUnicode {toUnicode} 0 R >>"));
            resources.Add((face.ResourceName, type0));
        }
        return resources;
    }

    private EmbeddedFace? ResolveFace(string fontFamily, bool bold)
    {
        string requested = string.IsNullOrWhiteSpace(fontFamily) ? "Arial" : fontFamily.Trim();
        var typeface = new Typeface(
            new FontFamily(requested),
            FontStyles.Normal,
            bold ? FontWeights.Bold : FontWeights.Normal,
            FontStretches.Normal);
        if (!typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface))
        {
            HandleUnavailable(requested, bold, $"effective font “{requested}” has no directly accessible TrueType face");
            return null;
        }
        if (glyphTypeface.StyleSimulations != StyleSimulations.None)
        {
            HandleUnavailable(requested, bold, $"font “{requested}” requires a simulated face that cannot be reliably embedded");
            return null;
        }

        string key = $"{glyphTypeface.FontUri.AbsoluteUri}|{bold}";
        if (_faces.TryGetValue(key, out EmbeddedFace? existing))
        {
            return existing;
        }

        byte[] fontBytes;
        try
        {
            using Stream input = glyphTypeface.GetFontStream();
            using var output = new MemoryStream();
            input.CopyTo(output);
            fontBytes = output.ToArray();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            HandleUnavailable(requested, bold, $"font “{requested}” could not be read: {exception.Message}");
            return null;
        }

        PdfFontPlan plan;
        try
        {
            OpenTypeEmbeddingRights rights = OpenTypeEmbeddingRightsReader.Read(fontBytes);
            bool supported = IsTrueTypeOutlines(fontBytes);
            plan = PdfFontStrategyPlanner.Plan(
                strategy,
                new PdfFontCapability(
                    requested,
                    requested,
                    IsInstalled: true,
                    IsSupportedFontFormat: supported,
                    rights.Permission,
                    rights.SubsettingPermitted,
                    EmbeddingImplementationAvailable: supported));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or NotSupportedException or OverflowException)
        {
            HandleUnavailable(requested, bold, exception.Message);
            return null;
        }

        if (!plan.CanExport)
        {
            throw new InvalidOperationException(plan.Error);
        }
        if (plan.RenderMode != PdfTextRenderMode.EmbeddedSubset)
        {
            if (plan.Warning is not null)
            {
                _warnings.Add(plan.Warning);
            }
            RecordOutcome(requested, bold, embedded: false, outlined: true, plan.FallbackReason);
            return null;
        }

        var face = new EmbeddedFace(
            $"F{_faces.Count + 1}",
            requested,
            bold,
            glyphTypeface,
            fontBytes);
        _faces.Add(key, face);
        return face;
    }

    private bool HandleUnavailable(string fontFamily, bool isBold, string reason)
    {
        RecordOutcome(fontFamily, isBold, embedded: false, outlined: true, reason);
        if (strategy == PdfFontStrategy.EmbedSubsetWhenPermitted)
        {
            throw new InvalidOperationException(
                $"PDF font embedding is required but unavailable: {reason}");
        }
        _warnings.Add($"PDF font embedding unavailable; text was outlined: {reason}");
        return false;
    }

    private void RecordOutcome(
        string fontFamily,
        bool isBold,
        bool embedded,
        bool outlined,
        string? fallbackReason = null)
    {
        string family = string.IsNullOrWhiteSpace(fontFamily) ? "Unknown" : fontFamily.Trim();
        var key = (family, isBold);
        if (!_outcomes.TryGetValue(key, out OutcomeState? outcome))
        {
            outcome = new OutcomeState();
            _outcomes.Add(key, outcome);
        }
        outcome.Embedded |= embedded;
        outcome.Outlined |= outlined;
        if (!string.IsNullOrWhiteSpace(fallbackReason))
        {
            outcome.FallbackReasons.Add(fallbackReason.Trim());
        }
    }

    private static bool IsTrueTypeOutlines(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4 &&
        ((bytes[0] == 0 && bytes[1] == 1 && bytes[2] == 0 && bytes[3] == 0) ||
         (bytes[0] == (byte)'t' && bytes[1] == (byte)'r' &&
          bytes[2] == (byte)'u' && bytes[3] == (byte)'e'));

    private static byte[] BuildCidToGid(EmbeddedFace face)
    {
        int maximumCid = face.GlyphByCid.Keys.Max();
        byte[] mapping = new byte[(maximumCid + 1) * 2];
        foreach ((ushort cid, ushort glyphId) in face.GlyphByCid)
        {
            mapping[cid * 2] = (byte)(glyphId >> 8);
            mapping[cid * 2 + 1] = (byte)glyphId;
        }
        return mapping;
    }

    private static string BuildToUnicode(EmbeddedFace face)
    {
        var cmap = new StringBuilder();
        cmap.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        cmap.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        cmap.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        cmap.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        KeyValuePair<ushort, int>[] mappings = face.UnicodeByCid.OrderBy(pair => pair.Key).ToArray();
        foreach (KeyValuePair<ushort, int>[] block in mappings.Chunk(100))
        {
            cmap.Append($"{block.Length} beginbfchar\n");
            foreach ((ushort cid, int scalar) in block)
            {
                cmap.Append($"<{cid:X4}> <{UnicodeHex(scalar)}>\n");
            }
            cmap.Append("endbfchar\n");
        }
        cmap.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return cmap.ToString();
    }

    private static string BuildDescriptor(TrueTypeSubsetFont subset, int fontFileObject)
    {
        int flags = 32 | (subset.IsFixedPitch ? 1 : 0) | (Math.Abs(subset.ItalicAngle) > 0.001 ? 64 : 0);
        return
            $"<< /Type /FontDescriptor /FontName /{subset.PdfBaseFontName} /Flags {flags} " +
            $"/FontBBox [{Metric(subset.XMin, subset)} {Metric(subset.YMin, subset)} " +
            $"{Metric(subset.XMax, subset)} {Metric(subset.YMax, subset)}] " +
            $"/ItalicAngle {F(subset.ItalicAngle)} /Ascent {Metric(subset.Ascender, subset)} " +
            $"/Descent {Metric(subset.Descender, subset)} /CapHeight {Metric(subset.CapHeight, subset)} " +
            $"/StemV 80 /FontFile2 {fontFileObject} 0 R >>";
    }

    private static string BuildDescendantFont(
        EmbeddedFace face,
        TrueTypeSubsetFont subset,
        int descriptorObject,
        int cidToGidObject)
    {
        var widths = new StringBuilder();
        foreach ((ushort cid, ushort glyphId) in face.GlyphByCid.OrderBy(pair => pair.Key))
        {
            widths.Append($"{cid} [{Metric(subset.AdvanceWidths[glyphId], subset)}] ");
        }
        return
            $"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{subset.PdfBaseFontName} " +
            "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
            $"/FontDescriptor {descriptorObject} 0 R /CIDToGIDMap {cidToGidObject} 0 R " +
            $"/DW 1000 /W [{widths}] >>";
    }

    private static int Metric(int value, TrueTypeSubsetFont subset) =>
        (int)Math.Round(value * 1000.0 / subset.UnitsPerEm, MidpointRounding.AwayFromZero);

    private static string UnicodeHex(int scalar)
    {
        string value = new Rune(scalar).ToString();
        return Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value));
    }

    private static byte[] BuildFlateStream(byte[] data, string additionalEntries = "")
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }
        byte[] payload = compressed.ToArray();
        byte[] header = Encoding.ASCII.GetBytes(
            $"<< {additionalEntries}/Filter /FlateDecode /Length {payload.Length} >>\nstream\n");
        return header.Concat(payload).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray();
    }

    private static byte[] BuildStream(byte[] data)
    {
        byte[] header = Encoding.ASCII.GetBytes($"<< /Length {data.Length} >>\nstream\n");
        return header.Concat(data).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray();
    }

    private static void AppendColor(StringBuilder content, Color color, bool fill)
    {
        string command = fill ? "rg" : "RG";
        content.Append(
            $"{F(color.R / 255.0)} {F(color.G / 255.0)} {F(color.B / 255.0)} {command} ");
    }

    private static string F(double value) =>
        value.ToString("0.###", CultureInfo.InvariantCulture);

    private sealed class EmbeddedFace(
        string resourceName,
        string family,
        bool isBold,
        GlyphTypeface glyphTypeface,
        byte[] fontBytes)
    {
        public string ResourceName { get; } = resourceName;
        public string Family { get; } = family;
        public bool IsBold { get; } = isBold;
        public GlyphTypeface GlyphTypeface { get; } = glyphTypeface;
        public byte[] FontBytes { get; } = fontBytes;
        public HashSet<ushort> GlyphIds { get; } = [0];
        public Dictionary<GlyphUnicodeKey, ushort> Cids { get; } = [];
        public Dictionary<ushort, ushort> GlyphByCid { get; } = [];
        public Dictionary<ushort, int> UnicodeByCid { get; } = [];
        public TrueTypeSubsetFont? Subset { get; set; }
    }

    private sealed class OutcomeState
    {
        public bool Embedded { get; set; }
        public bool Outlined { get; set; }
        public HashSet<string> FallbackReasons { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct GlyphUnicodeKey(ushort GlyphId, int UnicodeScalar);
}
