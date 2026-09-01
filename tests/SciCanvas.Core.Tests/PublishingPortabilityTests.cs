using System.Buffers.Binary;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Tests;

public sealed class PublishingPortabilityTests
{
    [Fact]
    public void FontResolution_UsesExplicitSubstitutionWithoutMutatingRequestedFamily()
    {
        var service = new FontResolutionService(
            new FixedFontCatalog(["Arial", "Segoe UI"]));
        var rule = new FontSubstitutionRule("MissingFont123", "Arial");

        ResolvedFont resolved = service.Resolve("MissingFont123", [rule]);

        Assert.Equal("MissingFont123", resolved.RequestedFamily);
        Assert.Equal("Arial", resolved.EffectiveFamily);
        Assert.Equal(FontResolutionKind.ExplicitSubstitution, resolved.ResolutionKind);
        Assert.Equal("MissingFont123", rule.RequestedFontFamily);
    }

    [Fact]
    public void FontResolution_MissingSubstituteProducesAuditableSystemFallback()
    {
        var service = new FontResolutionService(new FixedFontCatalog(["Segoe UI"]));

        ResolvedFont resolved = service.Resolve(
            "MissingFont123",
            [new FontSubstitutionRule("MissingFont123", "AlsoMissing")]);

        Assert.Equal("MissingFont123", resolved.RequestedFamily);
        Assert.Equal("Segoe UI", resolved.EffectiveFamily);
        Assert.Equal(FontResolutionKind.SystemFallback, resolved.ResolutionKind);
        Assert.Contains("AlsoMissing", resolved.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void JournalPreset_ExportImportRoundTripsSemanticFields()
    {
        JournalExportPreset original = Preset("team-materials", "Team Materials");

        string json = JournalPresetPortability.ExportPreset(original);
        JournalPresetImportResult imported = JournalPresetPortability.Import([], json);
        JournalExportPreset actual = Assert.Single(imported.Presets);

        AssertPresetEqual(original, actual);
        Assert.Contains("\"formatVersion\": \"1.0\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void JournalPreset_PackPreviewAndCollisionRequireExplicitDecision()
    {
        JournalExportPreset first = Preset("team-single", "Single");
        JournalExportPreset second = Preset("team-double", "Double", 180);
        string json = JournalPresetPortability.ExportPack(
            "USTC Materials Lab Presets",
            [first, second],
            organization: "USTC Materials Lab");

        IReadOnlyList<JournalPresetImportPreview> preview =
            JournalPresetPortability.PreviewImport(json);

        Assert.Equal(2, preview.Count);
        Assert.Equal(180, preview[1].WidthMm);
        JournalPresetCollisionException collision = Assert.Throws<JournalPresetCollisionException>(() =>
            JournalPresetPortability.Import([first], json));
        Assert.Contains("team-single", collision.CollidingIds);

        JournalPresetImportResult generated = JournalPresetPortability.Import(
            [first],
            json,
            JournalPresetCollisionPolicy.GenerateNewId);
        Assert.Equal("team-single-2", generated.GeneratedIds["team-single"]);
        Assert.Equal(3, generated.Presets.Count);
    }

    [Fact]
    public void PdfFontPlanner_OutlineNeverDependsOnExternalFontFile()
    {
        PdfFontPlan plan = PdfFontStrategyPlanner.Plan(
            PdfFontStrategy.OutlineText,
            Capability(permission: FontEmbeddingPermission.Unknown, installed: false));

        Assert.True(plan.CanExport);
        Assert.True(plan.Outlined);
        Assert.False(plan.Embedded);
    }

    [Fact]
    public void PdfFontPlanner_PermittedFontSelectsSubsetEmbedding()
    {
        PdfFontPlan plan = PdfFontStrategyPlanner.Plan(
            PdfFontStrategy.EmbedSubsetWhenPermitted,
            Capability(permission: FontEmbeddingPermission.Editable));

        Assert.True(plan.CanExport);
        Assert.True(plan.Embedded);
        Assert.Equal(PdfTextRenderMode.EmbeddedSubset, plan.RenderMode);
    }

    [Fact]
    public void PdfFontPlanner_ForbiddenFontWarnsAndOutlinesOrErrorsWhenStrict()
    {
        PdfFontCapability forbidden = Capability(permission: FontEmbeddingPermission.Restricted);

        PdfFontPlan fallback = PdfFontStrategyPlanner.Plan(
            PdfFontStrategy.PreferEmbeddedWithOutlineFallback,
            forbidden);
        PdfFontPlan strict = PdfFontStrategyPlanner.Plan(
            PdfFontStrategy.EmbedSubsetWhenPermitted,
            forbidden);

        Assert.True(fallback.CanExport);
        Assert.True(fallback.Outlined);
        Assert.NotNull(fallback.Warning);
        Assert.False(strict.CanExport);
        Assert.NotNull(strict.Error);
    }

    [Theory]
    [InlineData(0x0000, FontEmbeddingPermission.Installable, true)]
    [InlineData(0x0008, FontEmbeddingPermission.Editable, true)]
    [InlineData(0x0002, FontEmbeddingPermission.Restricted, true)]
    [InlineData(0x0104, FontEmbeddingPermission.PreviewAndPrint, false)]
    [InlineData(0x0204, FontEmbeddingPermission.BitmapOnly, true)]
    public void OpenTypeRightsReader_ParsesOs2FsType(
        ushort fsType,
        FontEmbeddingPermission expectedPermission,
        bool expectedSubsetting)
    {
        OpenTypeEmbeddingRights rights = OpenTypeEmbeddingRightsReader.Read(FontWithFsType(fsType));

        Assert.Equal(fsType, rights.FsType);
        Assert.Equal(expectedPermission, rights.Permission);
        Assert.Equal(expectedSubsetting, rights.SubsettingPermitted);
    }

    private static PdfFontCapability Capability(
        FontEmbeddingPermission permission,
        bool installed = true) => new(
            "Requested",
            "Effective",
            installed,
            IsSupportedFontFormat: true,
            permission,
            SubsettingPermitted: true,
            EmbeddingImplementationAvailable: true);

    private static JournalExportPreset Preset(string id, string name, double width = 89) => new(
        id,
        name,
        width,
        120,
        600,
        "pdf",
        ["pdf", "tiff"],
        "RGB",
        25,
        "Team-authored publisher-neutral preset",
        ["Arial", "Helvetica"],
        0.5,
        "Recommendations are not represented as official publisher rules.",
        new JournalPresetSourceMetadata(
            SourceName: "Lab guide",
            SourceUrl: "https://example.test/guide",
            SourceUpdatedAt: DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            CreatedAt: DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            Author: "SciCanvas test",
            Organization: "USTC Materials Lab"));

    private static void AssertPresetEqual(JournalExportPreset expected, JournalExportPreset actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Name, actual.Name);
        Assert.Equal(expected.Description, actual.Description);
        Assert.Equal(expected.FigureWidthMm, actual.FigureWidthMm);
        Assert.Equal(expected.FigureHeightMm, actual.FigureHeightMm);
        Assert.Equal(expected.MinimumDpi, actual.MinimumDpi);
        Assert.Equal(expected.PreferredFormat, actual.PreferredFormat);
        Assert.Equal(expected.AllowedFormats, actual.AllowedFormats);
        Assert.Equal(expected.ColorMode, actual.ColorMode);
        Assert.Equal(expected.MaximumFileSizeMb, actual.MaximumFileSizeMb);
        Assert.Equal(expected.FontRecommendations, actual.FontRecommendations);
        Assert.Equal(expected.MinimumLineWidthPt, actual.MinimumLineWidthPt);
        Assert.Equal(expected.Notes, actual.Notes);
        Assert.Equal(expected.SourceMetadata, actual.SourceMetadata);
    }

    private static byte[] FontWithFsType(ushort fsType)
    {
        byte[] bytes = new byte[64];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(0, 4), 0x00010000);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(4, 2), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(12, 4), 0x4F532F32);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), 32);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(24, 4), 16);
        BinaryPrimitives.WriteUInt16BigEndian(bytes.AsSpan(40, 2), fsType);
        return bytes;
    }
}
