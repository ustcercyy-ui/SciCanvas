using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SciCanvas.Core.Export;

public sealed record TrueTypeSubsetFont(
    byte[] FontBytes,
    string PostScriptName,
    string SubsetTag,
    ushort UnitsPerEm,
    short XMin,
    short YMin,
    short XMax,
    short YMax,
    short Ascender,
    short Descender,
    short CapHeight,
    double ItalicAngle,
    bool IsFixedPitch,
    IReadOnlyList<ushort> AdvanceWidths,
    IReadOnlySet<ushort> RetainedGlyphIds,
    int OriginalByteLength)
{
    public string PdfBaseFontName => $"{SubsetTag}+{PostScriptName}";
}

/// <summary>
/// Builds a deterministic sparse TrueType subset. Glyph IDs remain stable so a PDF
/// CIDToGIDMap can address the original IDs, while unused glyf programs are removed.
/// CFF, collections and variable-font glyph substitution are intentionally unsupported.
/// </summary>
public static class TrueTypeFontSubsetter
{
    private const uint SfntTrueType = 0x00010000;
    private const uint AppleTrueType = 0x74727565; // true
    private const uint TtcTag = 0x74746366; // ttcf
    private const uint OttoTag = 0x4F54544F; // OTTO
    private const uint GlyfTag = 0x676C7966;
    private const uint LocaTag = 0x6C6F6361;
    private const uint HeadTag = 0x68656164;
    private const uint HheaTag = 0x68686561;
    private const uint HmtxTag = 0x686D7478;
    private const uint MaxpTag = 0x6D617870;
    private const uint NameTag = 0x6E616D65;
    private const uint Os2Tag = 0x4F532F32;
    private const uint PostTag = 0x706F7374;
    private const uint CffTag = 0x43464620;
    private const uint Cff2Tag = 0x43464632;
    private const uint DsigTag = 0x44534947;
    private const uint FontChecksumMagic = 0xB1B0AFBA;

    public static TrueTypeSubsetFont Build(
        ReadOnlySpan<byte> fontBytes,
        IEnumerable<ushort> requestedGlyphIds)
    {
        ArgumentNullException.ThrowIfNull(requestedGlyphIds);
        if (fontBytes.Length < 12)
        {
            throw new InvalidDataException("TrueType header is truncated.");
        }

        uint signature = ReadU32(fontBytes, 0);
        if (signature == TtcTag)
        {
            throw new NotSupportedException("TrueType/OpenType collections are unsupported; a single-font sfnt file is required.");
        }
        if (signature == OttoTag)
        {
            throw new NotSupportedException("OpenType CFF outlines are unsupported for reliable PDF subsetting.");
        }
        if (signature is not (SfntTrueType or AppleTrueType))
        {
            throw new NotSupportedException("Only single-font TrueType outlines are supported for PDF subsetting.");
        }

        Dictionary<uint, TableRecord> directory = ReadDirectory(fontBytes);
        if (directory.ContainsKey(CffTag) || directory.ContainsKey(Cff2Tag))
        {
            throw new NotSupportedException("OpenType CFF outlines are unsupported for reliable PDF subsetting.");
        }

        TableRecord head = Required(directory, HeadTag);
        TableRecord maxp = Required(directory, MaxpTag);
        TableRecord hhea = Required(directory, HheaTag);
        TableRecord hmtx = Required(directory, HmtxTag);
        TableRecord loca = Required(directory, LocaTag);
        TableRecord glyf = Required(directory, GlyfTag);
        TableRecord name = Required(directory, NameTag);
        TableRecord os2 = Required(directory, Os2Tag);
        TableRecord post = Required(directory, PostTag);
        RequireLength(head, 54, "head");
        RequireLength(maxp, 6, "maxp");
        RequireLength(hhea, 36, "hhea");
        RequireLength(os2, 10, "OS/2");
        RequireLength(post, 16, "post");

        ushort glyphCount = ReadU16(fontBytes, maxp.Offset + 4);
        if (glyphCount == 0)
        {
            throw new InvalidDataException("TrueType maxp table has no glyphs.");
        }

        short locaFormat = ReadI16(fontBytes, head.Offset + 50);
        if (locaFormat is not (0 or 1))
        {
            throw new InvalidDataException("TrueType head.indexToLocFormat is invalid.");
        }

        uint[] glyphOffsets = ReadGlyphOffsets(fontBytes, loca, glyphCount, locaFormat);
        ValidateGlyphOffsets(glyphOffsets, glyf.Length);
        HashSet<ushort> glyphs = requestedGlyphIds
            .Where(glyphId => glyphId < glyphCount)
            .ToHashSet();
        glyphs.Add(0);
        AddCompositeDependencies(fontBytes, glyf, glyphOffsets, glyphs, glyphCount);

        byte[] subsetGlyf = BuildGlyf(fontBytes, glyf, glyphOffsets, glyphs, out uint[] subsetOffsets);
        byte[] subsetLoca = BuildLoca(subsetOffsets, locaFormat);
        var tables = new Dictionary<uint, byte[]>();
        foreach ((uint tag, TableRecord table) in directory)
        {
            if (tag != DsigTag)
            {
                tables[tag] = fontBytes.Slice(table.Offset, table.Length).ToArray();
            }
        }
        tables[GlyfTag] = subsetGlyf;
        tables[LocaTag] = subsetLoca;
        Array.Clear(tables[HeadTag], 8, 4);
        byte[] subsetBytes = BuildSfnt(signature, tables, HeadTag);

        ushort unitsPerEm = ReadU16(fontBytes, head.Offset + 18);
        if (unitsPerEm is < 16 or > 16384)
        {
            throw new InvalidDataException("TrueType unitsPerEm is invalid.");
        }

        ushort numberOfHMetrics = ReadU16(fontBytes, hhea.Offset + 34);
        ushort[] widths = ReadAdvanceWidths(fontBytes, hmtx, glyphCount, numberOfHMetrics);
        string postScriptName = ReadPostScriptName(fontBytes, name);
        string subsetTag = CreateSubsetTag(fontBytes, glyphs);
        short ascender = ReadI16(fontBytes, hhea.Offset + 4);
        short descender = ReadI16(fontBytes, hhea.Offset + 6);
        short capHeight = os2.Length >= 90 && ReadU16(fontBytes, os2.Offset) >= 2
            ? ReadI16(fontBytes, os2.Offset + 88)
            : ascender;
        double italicAngle = ReadI32(fontBytes, post.Offset + 4) / 65536.0;
        bool fixedPitch = ReadU32(fontBytes, post.Offset + 12) != 0;

        return new TrueTypeSubsetFont(
            subsetBytes,
            SanitizePostScriptName(postScriptName),
            subsetTag,
            unitsPerEm,
            ReadI16(fontBytes, head.Offset + 36),
            ReadI16(fontBytes, head.Offset + 38),
            ReadI16(fontBytes, head.Offset + 40),
            ReadI16(fontBytes, head.Offset + 42),
            ascender,
            descender,
            capHeight,
            italicAngle,
            fixedPitch,
            widths,
            glyphs,
            fontBytes.Length);
    }

    private static Dictionary<uint, TableRecord> ReadDirectory(ReadOnlySpan<byte> bytes)
    {
        ushort count = ReadU16(bytes, 4);
        int directoryLength = checked(12 + count * 16);
        if (directoryLength > bytes.Length)
        {
            throw new InvalidDataException("TrueType table directory is truncated.");
        }

        var result = new Dictionary<uint, TableRecord>();
        for (int index = 0; index < count; index++)
        {
            int entry = 12 + index * 16;
            uint tag = ReadU32(bytes, entry);
            uint offsetValue = ReadU32(bytes, entry + 8);
            uint lengthValue = ReadU32(bytes, entry + 12);
            if (offsetValue > int.MaxValue || lengthValue > int.MaxValue ||
                (ulong)offsetValue + lengthValue > (ulong)bytes.Length)
            {
                throw new InvalidDataException($"TrueType table {TagText(tag)} is outside the font file.");
            }
            if (!result.TryAdd(tag, new TableRecord((int)offsetValue, (int)lengthValue)))
            {
                throw new InvalidDataException($"TrueType table {TagText(tag)} is duplicated.");
            }
        }
        return result;
    }

    private static uint[] ReadGlyphOffsets(
        ReadOnlySpan<byte> bytes,
        TableRecord loca,
        ushort glyphCount,
        short format)
    {
        int entrySize = format == 0 ? 2 : 4;
        int required = checked((glyphCount + 1) * entrySize);
        if (loca.Length < required)
        {
            throw new InvalidDataException("TrueType loca table is truncated.");
        }

        var offsets = new uint[glyphCount + 1];
        for (int index = 0; index < offsets.Length; index++)
        {
            offsets[index] = format == 0
                ? checked((uint)ReadU16(bytes, loca.Offset + index * 2) * 2)
                : ReadU32(bytes, loca.Offset + index * 4);
        }
        return offsets;
    }

    private static void ValidateGlyphOffsets(IReadOnlyList<uint> offsets, int glyfLength)
    {
        uint previous = 0;
        foreach (uint offset in offsets)
        {
            if (offset < previous || offset > glyfLength)
            {
                throw new InvalidDataException("TrueType loca offsets are invalid.");
            }
            previous = offset;
        }
    }

    private static void AddCompositeDependencies(
        ReadOnlySpan<byte> bytes,
        TableRecord glyf,
        IReadOnlyList<uint> offsets,
        HashSet<ushort> glyphs,
        ushort glyphCount)
    {
        var pending = new Queue<ushort>(glyphs);
        while (pending.TryDequeue(out ushort glyphId))
        {
            int start = checked(glyf.Offset + (int)offsets[glyphId]);
            int length = checked((int)(offsets[glyphId + 1] - offsets[glyphId]));
            if (length == 0)
            {
                continue;
            }
            if (length < 10)
            {
                throw new InvalidDataException($"TrueType glyph {glyphId} is truncated.");
            }
            if (ReadI16(bytes, start) >= 0)
            {
                continue;
            }

            int cursor = start + 10;
            int end = start + length;
            bool more;
            do
            {
                if (cursor + 4 > end)
                {
                    throw new InvalidDataException($"Composite TrueType glyph {glyphId} is truncated.");
                }
                ushort flags = ReadU16(bytes, cursor);
                ushort component = ReadU16(bytes, cursor + 2);
                if (component >= glyphCount)
                {
                    throw new InvalidDataException($"Composite TrueType glyph {glyphId} references an invalid glyph.");
                }
                if (glyphs.Add(component))
                {
                    pending.Enqueue(component);
                }
                cursor += 4;
                cursor += (flags & 0x0001) != 0 ? 4 : 2;
                if ((flags & 0x0008) != 0)
                {
                    cursor += 2;
                }
                else if ((flags & 0x0040) != 0)
                {
                    cursor += 4;
                }
                else if ((flags & 0x0080) != 0)
                {
                    cursor += 8;
                }
                if (cursor > end)
                {
                    throw new InvalidDataException($"Composite TrueType glyph {glyphId} has invalid component flags.");
                }
                more = (flags & 0x0020) != 0;
            }
            while (more);
        }
    }

    private static byte[] BuildGlyf(
        ReadOnlySpan<byte> bytes,
        TableRecord glyf,
        IReadOnlyList<uint> offsets,
        IReadOnlySet<ushort> glyphs,
        out uint[] subsetOffsets)
    {
        subsetOffsets = new uint[offsets.Count];
        using var output = new MemoryStream();
        for (ushort glyphId = 0; glyphId < offsets.Count - 1; glyphId++)
        {
            subsetOffsets[glyphId] = checked((uint)output.Position);
            if (!glyphs.Contains(glyphId))
            {
                continue;
            }
            int start = checked(glyf.Offset + (int)offsets[glyphId]);
            int length = checked((int)(offsets[glyphId + 1] - offsets[glyphId]));
            output.Write(bytes.Slice(start, length));
            while ((output.Position & 3) != 0)
            {
                output.WriteByte(0);
            }
        }
        subsetOffsets[^1] = checked((uint)output.Position);
        return output.ToArray();
    }

    private static byte[] BuildLoca(IReadOnlyList<uint> offsets, short format)
    {
        byte[] result = new byte[offsets.Count * (format == 0 ? 2 : 4)];
        for (int index = 0; index < offsets.Count; index++)
        {
            if (format == 0)
            {
                if ((offsets[index] & 1) != 0 || offsets[index] / 2 > ushort.MaxValue)
                {
                    throw new NotSupportedException("The sparse subset no longer fits the source font's short loca format.");
                }
                BinaryPrimitives.WriteUInt16BigEndian(
                    result.AsSpan(index * 2, 2),
                    checked((ushort)(offsets[index] / 2)));
            }
            else
            {
                BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(index * 4, 4), offsets[index]);
            }
        }
        return result;
    }

    private static ushort[] ReadAdvanceWidths(
        ReadOnlySpan<byte> bytes,
        TableRecord hmtx,
        ushort glyphCount,
        ushort metricCount)
    {
        if (metricCount is 0 || metricCount > glyphCount ||
            hmtx.Length < metricCount * 4 + (glyphCount - metricCount) * 2)
        {
            throw new InvalidDataException("TrueType hmtx table is truncated or inconsistent with hhea.");
        }
        var result = new ushort[glyphCount];
        ushort last = 0;
        for (int glyphId = 0; glyphId < glyphCount; glyphId++)
        {
            if (glyphId < metricCount)
            {
                last = ReadU16(bytes, hmtx.Offset + glyphId * 4);
            }
            result[glyphId] = last;
        }
        return result;
    }

    private static string ReadPostScriptName(ReadOnlySpan<byte> bytes, TableRecord name)
    {
        if (name.Length < 6)
        {
            throw new InvalidDataException("TrueType name table is truncated.");
        }
        ushort count = ReadU16(bytes, name.Offset + 2);
        ushort storageOffset = ReadU16(bytes, name.Offset + 4);
        if (6 + count * 12 > name.Length || storageOffset > name.Length)
        {
            throw new InvalidDataException("TrueType name table records are truncated.");
        }

        string? fallback = null;
        for (int index = 0; index < count; index++)
        {
            int record = name.Offset + 6 + index * 12;
            ushort platform = ReadU16(bytes, record);
            ushort encoding = ReadU16(bytes, record + 2);
            ushort nameId = ReadU16(bytes, record + 6);
            ushort length = ReadU16(bytes, record + 8);
            ushort offset = ReadU16(bytes, record + 10);
            if (nameId != 6 || storageOffset + offset + length > name.Length)
            {
                continue;
            }
            ReadOnlySpan<byte> value = bytes.Slice(name.Offset + storageOffset + offset, length);
            string decoded = platform is 0 or 3
                ? DecodeBigEndianUnicode(value)
                : Encoding.Latin1.GetString(value);
            if (string.IsNullOrWhiteSpace(decoded))
            {
                continue;
            }
            if (platform == 3 && encoding is 1 or 10)
            {
                return decoded;
            }
            fallback ??= decoded;
        }
        return fallback ?? "SciCanvasSubsetFont";
    }

    private static string DecodeBigEndianUnicode(ReadOnlySpan<byte> bytes)
    {
        if ((bytes.Length & 1) != 0)
        {
            return string.Empty;
        }
        char[] chars = new char[bytes.Length / 2];
        for (int index = 0; index < chars.Length; index++)
        {
            chars[index] = (char)BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(index * 2, 2));
        }
        return new string(chars);
    }

    private static byte[] BuildSfnt(uint signature, IReadOnlyDictionary<uint, byte[]> tables, uint headTag)
    {
        KeyValuePair<uint, byte[]>[] ordered = tables.OrderBy(pair => pair.Key).ToArray();
        ushort count = checked((ushort)ordered.Length);
        int power = 1;
        ushort selector = 0;
        while (power * 2 <= count)
        {
            power *= 2;
            selector++;
        }
        ushort searchRange = checked((ushort)(power * 16));
        ushort rangeShift = checked((ushort)(count * 16 - searchRange));
        int cursor = 12 + count * 16;
        var placements = new Dictionary<uint, (int Offset, int Length, uint Checksum)>();
        foreach ((uint tag, byte[] data) in ordered)
        {
            cursor = Align4(cursor);
            placements[tag] = (cursor, data.Length, Checksum(data));
            cursor = checked(cursor + Align4(data.Length));
        }

        byte[] output = new byte[cursor];
        WriteU32(output, 0, signature);
        WriteU16(output, 4, count);
        WriteU16(output, 6, searchRange);
        WriteU16(output, 8, selector);
        WriteU16(output, 10, rangeShift);
        for (int index = 0; index < ordered.Length; index++)
        {
            uint tag = ordered[index].Key;
            (int offset, int length, uint checksum) = placements[tag];
            int entry = 12 + index * 16;
            WriteU32(output, entry, tag);
            WriteU32(output, entry + 4, checksum);
            WriteU32(output, entry + 8, checked((uint)offset));
            WriteU32(output, entry + 12, checked((uint)length));
            ordered[index].Value.CopyTo(output, offset);
        }

        int headOffset = placements[headTag].Offset;
        uint adjustment = unchecked(FontChecksumMagic - Checksum(output));
        WriteU32(output, headOffset + 8, adjustment);
        return output;
    }

    private static string CreateSubsetTag(ReadOnlySpan<byte> fontBytes, IEnumerable<ushort> glyphs)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(fontBytes);
        Span<byte> glyphBuffer = stackalloc byte[2];
        foreach (ushort glyph in glyphs.Order())
        {
            BinaryPrimitives.WriteUInt16BigEndian(glyphBuffer, glyph);
            hash.AppendData(glyphBuffer);
        }
        byte[] digest = hash.GetHashAndReset();
        return new string(digest.Take(6).Select(value => (char)('A' + value % 26)).ToArray());
    }

    private static string SanitizePostScriptName(string value)
    {
        string result = new(value
            .Where(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_')
            .ToArray());
        return result.Length == 0 ? "SciCanvasSubsetFont" : result;
    }

    private static TableRecord Required(IReadOnlyDictionary<uint, TableRecord> tables, uint tag) =>
        tables.TryGetValue(tag, out TableRecord value)
            ? value
            : throw new NotSupportedException($"TrueType font is missing required {TagText(tag)} table.");

    private static void RequireLength(TableRecord table, int minimum, string name)
    {
        if (table.Length < minimum)
        {
            throw new InvalidDataException($"TrueType {name} table is truncated.");
        }
    }

    private static int Align4(int value) => checked((value + 3) & ~3);

    private static uint Checksum(ReadOnlySpan<byte> bytes)
    {
        uint result = 0;
        for (int offset = 0; offset < Align4(bytes.Length); offset += 4)
        {
            uint word = 0;
            for (int index = 0; index < 4; index++)
            {
                int source = offset + index;
                word = (word << 8) | (source < bytes.Length ? bytes[source] : 0u);
            }
            result = unchecked(result + word);
        }
        return result;
    }

    private static string TagText(uint tag) =>
        Encoding.ASCII.GetString(
            [(byte)(tag >> 24), (byte)(tag >> 16), (byte)(tag >> 8), (byte)tag]);

    private static ushort ReadU16(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes, offset, 2);
        return BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset, 2));
    }

    private static short ReadI16(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes, offset, 2);
        return BinaryPrimitives.ReadInt16BigEndian(bytes.Slice(offset, 2));
    }

    private static uint ReadU32(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes, offset, 4);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
    }

    private static int ReadI32(ReadOnlySpan<byte> bytes, int offset)
    {
        EnsureRange(bytes, offset, 4);
        return BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4));
    }

    private static void WriteU16(Span<byte> bytes, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(offset, 2), value);

    private static void WriteU32(Span<byte> bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32BigEndian(bytes.Slice(offset, 4), value);

    private static void EnsureRange(ReadOnlySpan<byte> bytes, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > bytes.Length - length)
        {
            throw new InvalidDataException("TrueType table read is outside the font file.");
        }
    }

    private readonly record struct TableRecord(int Offset, int Length);
}
