using System.Buffers.Binary;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Imaging;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class PdfTrueTypeEmbeddingTests
{
    [Fact]
    public void TrueTypeSubsetter_RemovesUnusedGlyphProgramsAndBuildsDeterministicValidSfnt()
    {
        WpfTestHost.Invoke(() =>
        {
            var typeface = new Typeface(
                new FontFamily("Arial"),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal);
            Assert.True(typeface.TryGetGlyphTypeface(out GlyphTypeface glyphTypeface));
            using Stream stream = glyphTypeface.GetFontStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            byte[] original = buffer.ToArray();
            ushort[] requestedGlyphs = "Subset Ω é 123"
                .EnumerateRunes()
                .Select(rune => glyphTypeface.CharacterToGlyphMap[rune.Value])
                .Distinct()
                .ToArray();

            TrueTypeSubsetFont first = TrueTypeFontSubsetter.Build(original, requestedGlyphs);
            TrueTypeSubsetFont second = TrueTypeFontSubsetter.Build(original, requestedGlyphs.Reverse());

            Assert.True(first.FontBytes.Length < original.Length);
            Assert.True(first.RetainedGlyphIds.Count < glyphTypeface.GlyphCount);
            Assert.All(requestedGlyphs, glyphId => Assert.Contains(glyphId, first.RetainedGlyphIds));
            Assert.Equal(first.SubsetTag, second.SubsetTag);
            Assert.Equal(first.FontBytes, second.FontBytes);
            Assert.Equal(0xB1B0AFBAu, SfntChecksum(first.FontBytes));
        });
    }

    [Fact]
    public void WpfCapabilityProvider_ReportsArialTrueTypeEmbeddingRights()
    {
        WpfTestHost.Invoke(() =>
        {
            PdfFontCapability capability = WpfPdfFontCapabilityProvider.Instance.GetCapability(
                "Arial",
                isBold: false);

            Assert.True(capability.IsInstalled);
            Assert.True(capability.IsSupportedFontFormat);
            Assert.True(capability.EmbeddingImplementationAvailable);
            Assert.DoesNotContain(
                capability.EmbeddingPermission,
                new[] { FontEmbeddingPermission.Restricted, FontEmbeddingPermission.BitmapOnly });
        });
    }

    [Fact]
    public void TrueTypeSubsetter_ExplicitlyRejectsCffOpenType()
    {
        byte[] cffHeader = new byte[12];
        "OTTO"u8.CopyTo(cffHeader);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => TrueTypeFontSubsetter.Build(cffHeader, [0]));

        Assert.Contains("CFF", exception.Message, StringComparison.Ordinal);
    }

    private static uint SfntChecksum(ReadOnlySpan<byte> bytes)
    {
        uint sum = 0;
        int paddedLength = (bytes.Length + 3) & ~3;
        Span<byte> wordBytes = stackalloc byte[4];
        for (int offset = 0; offset < paddedLength; offset += 4)
        {
            wordBytes.Clear();
            int count = Math.Min(4, bytes.Length - offset);
            if (count > 0)
            {
                bytes.Slice(offset, count).CopyTo(wordBytes);
            }
            sum = unchecked(sum + BinaryPrimitives.ReadUInt32BigEndian(wordBytes));
        }
        return sum;
    }
}
