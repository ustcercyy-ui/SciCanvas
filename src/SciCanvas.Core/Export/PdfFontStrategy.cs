using System.Buffers.Binary;

namespace SciCanvas.Core.Export;

public enum PdfFontStrategy
{
    OutlineText,
    EmbedSubsetWhenPermitted,
    PreferEmbeddedWithOutlineFallback,
}

public enum PdfTextRenderMode
{
    Outline,
    EmbeddedSubset,
}

public enum FontEmbeddingPermission
{
    Unknown,
    Installable,
    PreviewAndPrint,
    Editable,
    Restricted,
    BitmapOnly,
}

public sealed record PdfFontCapability(
    string RequestedFont,
    string EffectiveFont,
    bool IsInstalled,
    bool IsSupportedFontFormat,
    FontEmbeddingPermission EmbeddingPermission,
    bool SubsettingPermitted,
    bool EmbeddingImplementationAvailable);

public sealed record PdfFontPlan(
    PdfFontStrategy Strategy,
    PdfTextRenderMode RenderMode,
    bool CanExport,
    bool Embedded,
    bool Outlined,
    string? Warning = null,
    string? Error = null,
    string? FallbackReason = null);

public static class PdfFontStrategyPlanner
{
    public static PdfFontPlan Plan(PdfFontStrategy strategy, PdfFontCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        if (!Enum.IsDefined(strategy) || string.IsNullOrWhiteSpace(capability.RequestedFont) ||
            string.IsNullOrWhiteSpace(capability.EffectiveFont))
        {
            throw new InvalidOperationException("PDF font strategy 或 capability 无效。");
        }

        if (strategy == PdfFontStrategy.OutlineText)
        {
            return new PdfFontPlan(strategy, PdfTextRenderMode.Outline, true, false, true);
        }

        string? reason = GetEmbeddingBlockReason(capability);
        if (reason is null)
        {
            return new PdfFontPlan(strategy, PdfTextRenderMode.EmbeddedSubset, true, true, false);
        }

        if (strategy == PdfFontStrategy.EmbedSubsetWhenPermitted)
        {
            return new PdfFontPlan(
                strategy,
                PdfTextRenderMode.EmbeddedSubset,
                false,
                false,
                false,
                Error: $"PDF font embedding is required but unavailable: {reason}",
                FallbackReason: reason);
        }

        return new PdfFontPlan(
            strategy,
            PdfTextRenderMode.Outline,
            true,
            false,
            true,
            Warning: $"PDF font embedding unavailable; text was outlined: {reason}",
            FallbackReason: reason);
    }

    private static string? GetEmbeddingBlockReason(PdfFontCapability capability)
    {
        if (!capability.IsInstalled)
        {
            return "effective font is not installed";
        }

        if (!capability.IsSupportedFontFormat)
        {
            return "font file format is not supported";
        }

        if (capability.EmbeddingPermission is FontEmbeddingPermission.Restricted or FontEmbeddingPermission.BitmapOnly)
        {
            return $"font embedding permission is {capability.EmbeddingPermission}";
        }

        if (capability.EmbeddingPermission == FontEmbeddingPermission.Unknown)
        {
            return "font embedding permission is unknown";
        }

        if (!capability.SubsettingPermitted)
        {
            return "font license forbids subsetting";
        }

        return capability.EmbeddingImplementationAvailable
            ? null
            : "the current PDF writer has no reliable subset/ToUnicode implementation";
    }
}

public sealed record OpenTypeEmbeddingRights(
    ushort FsType,
    FontEmbeddingPermission Permission,
    bool SubsettingPermitted,
    bool BitmapEmbeddingOnly);

/// <summary>Reads only the documented OS/2 fsType licensing bits from an sfnt font.</summary>
public static class OpenTypeEmbeddingRightsReader
{
    private const uint Os2Tag = 0x4F532F32;

    public static OpenTypeEmbeddingRights Read(ReadOnlySpan<byte> fontBytes)
    {
        if (fontBytes.Length < 12)
        {
            throw new InvalidDataException("OpenType/TrueType header is truncated.");
        }

        uint signature = BinaryPrimitives.ReadUInt32BigEndian(fontBytes);
        if (signature is not (0x00010000 or 0x4F54544F))
        {
            throw new NotSupportedException("Only single-font TrueType/OpenType sfnt files are supported for permission inspection.");
        }

        ushort tableCount = BinaryPrimitives.ReadUInt16BigEndian(fontBytes.Slice(4, 2));
        int directoryLength = checked(12 + tableCount * 16);
        if (directoryLength > fontBytes.Length)
        {
            throw new InvalidDataException("OpenType table directory is truncated.");
        }

        for (int tableIndex = 0; tableIndex < tableCount; tableIndex++)
        {
            int entry = 12 + tableIndex * 16;
            if (BinaryPrimitives.ReadUInt32BigEndian(fontBytes.Slice(entry, 4)) != Os2Tag)
            {
                continue;
            }

            uint offset = BinaryPrimitives.ReadUInt32BigEndian(fontBytes.Slice(entry + 8, 4));
            uint length = BinaryPrimitives.ReadUInt32BigEndian(fontBytes.Slice(entry + 12, 4));
            if (length < 10 || offset > int.MaxValue || offset + 10 > fontBytes.Length)
            {
                throw new InvalidDataException("OS/2 table is truncated or outside the font file.");
            }

            ushort fsType = BinaryPrimitives.ReadUInt16BigEndian(fontBytes.Slice(checked((int)offset + 8), 2));
            bool restricted = (fsType & 0x0002) != 0;
            bool previewPrint = (fsType & 0x0004) != 0;
            bool editable = (fsType & 0x0008) != 0;
            bool noSubsetting = (fsType & 0x0100) != 0;
            bool bitmapOnly = (fsType & 0x0200) != 0;
            FontEmbeddingPermission permission = restricted
                ? FontEmbeddingPermission.Restricted
                : bitmapOnly
                    ? FontEmbeddingPermission.BitmapOnly
                    : editable
                        ? FontEmbeddingPermission.Editable
                        : previewPrint
                            ? FontEmbeddingPermission.PreviewAndPrint
                            : FontEmbeddingPermission.Installable;
            return new OpenTypeEmbeddingRights(fsType, permission, !noSubsetting, bitmapOnly);
        }

        throw new InvalidDataException("Font has no OS/2 table; embedding permission cannot be established.");
    }
}
