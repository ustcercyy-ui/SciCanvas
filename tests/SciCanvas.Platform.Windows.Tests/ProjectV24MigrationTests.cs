using System.Text.Json;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ProjectV24MigrationTests
{
    [Fact]
    public void MigrateV23_PreservesAllVisualAndPublishingSemanticsAndAddsOnlyV24Defaults()
    {
        Guid sourceId = Guid.NewGuid();
        Guid layerId = Guid.NewGuid();
        Guid groupId = Guid.NewGuid();
        Guid channelId = Guid.NewGuid();
        var source = new ProjectSourceSnapshot
        {
            Id = sourceId,
            DisplayName = "source.tif",
            OriginalPath = "source.tif",
            SourceRevision = 7,
        };
        var layer = new ProjectImageLayerSnapshot
        {
            Id = layerId,
            Name = "Panel custom",
            PanelLabel = "Z",
            SourceAssetId = sourceId,
            SourceRect = new ProjectPixelRectSnapshot { X = 11, Y = 13, Width = 101, Height = 73 },
            CropLinkGroupId = Guid.NewGuid(),
            CompositeGroupId = groupId,
            StyleOverride = new ProjectPanelStyleOverrideSnapshot
            {
                PanelLabel = new ProjectTextStyleSnapshot
                {
                    FontFamily = "Times New Roman",
                    FontSizePt = 11,
                    Color = "#FFABCDEF",
                    IsBold = true,
                },
                ScaleBarText = new ProjectTextStyleSnapshot
                {
                    FontFamily = "Cambria",
                    FontSizePt = 8,
                    Color = "#FF010203",
                },
                ScaleBar = new ProjectScaleBarStyleSnapshot
                {
                    Color = "#FFFEDCBA",
                    BarThicknessPt = 2.25,
                    DefaultPosition = "topLeft",
                },
            },
        };
        var measurement = new ProjectMeasurementSnapshot
        {
            Id = Guid.NewGuid(),
            SourceAssetId = sourceId,
            SourceRevision = 7,
            Kind = "rectangle",
            StrokeColor = "#FF123456",
            StrokeWidthPixels = 3.5,
            FillColor = "#AA654321",
            FillOpacityPercent = 37,
            MarkerStrokeColor = "#FF112233",
            MarkerFillColor = "#FF445566",
            MarkerSizePixels = 9,
            LabelColor = "#FF778899",
            LabelFontFamily = "Helvetica Neue",
            LabelFontSizePt = 13,
            LabelIsBold = false,
        };
        var annotation = new ProjectAnnotationSnapshot
        {
            Id = Guid.NewGuid(),
            Kind = "rectangle",
            StrokeColor = "#FF0A0B0C",
            FillColor = "#CC102030",
            FillOpacityPercent = 61,
            TextColor = "#FFEEEEEE",
            FontFamily = "Georgia",
            FontSizePt = 10,
            StrokeWidthPt = 1.75,
        };
        var profile = new ProjectExportProfileSnapshot
        {
            Id = Guid.NewGuid(),
            Name = "Submission PDF",
            Format = "pdf",
            Dpi = 600,
            WidthPixels = 2400,
            HeightPixels = 1800,
            WriteProvenance = true,
            WriteAuditReport = true,
            JournalPresetId = "team-preset",
            PdfFontStrategy = "preferEmbeddedWithOutlineFallback",
        };
        var template = new ProjectTemplateSnapshot
        {
            TemplateId = "custom",
            GlobalStyle = new ProjectGlobalStyleSnapshot
            {
                FontFamily = "Georgia",
                FontSizePt = 9,
                StrokeWidthPt = 1.5,
                TextColor = "#FF111213",
                ShapeColor = "#FF212223",
                ScaleBarColor = "#FFF1F2F3",
                PanelLabelFontFamily = "Times New Roman",
                PanelLabelFontSizePt = 12,
                PanelLabelTextColor = "#FFABCDEF",
                ScaleBarFontFamily = "Cambria",
                ScaleBarFontSizePt = 8,
                ScaleBarLabelColor = "#FF010203",
                ScaleBarThicknessPt = 2.25,
            },
            Annotations = [annotation],
            LayerSlots = new Dictionary<Guid, string> { [layerId] = "main" },
            ScaleBars = new Dictionary<Guid, ProjectScaleBarSnapshot>
            {
                [layerId] = new ProjectScaleBarSnapshot
                {
                    Enabled = true,
                    PhysicalUnitsPerSourcePixel = 0.25,
                    PhysicalLength = 2,
                    Unit = "µm",
                    CalibrationUnit = "µm",
                    Anchor = "topLeft",
                    ShowLabel = true,
                },
            },
        };
        var v23 = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.3",
            ProjectId = Guid.NewGuid(),
            UpdatedAt = new DateTimeOffset(2026, 8, 29, 1, 2, 3, TimeSpan.Zero),
            Sources = [source],
            Layers = [layer],
            Measurements = [measurement],
            ExportProfiles = [profile],
            TemplateSnapshot = template,
            FontSubstitutions =
            [
                new ProjectFontSubstitutionSnapshot
                {
                    Requested = "Helvetica Neue",
                    Substitute = "Arial",
                },
            ],
            MultiChannelGroups =
            [
                new ProjectMultiChannelAssetGroupSnapshot
                {
                    Id = groupId,
                    Name = "EDS",
                    ReferenceAssetId = sourceId,
                    SameFieldOfViewConfirmed = true,
                    Members =
                    [
                        new ProjectChannelGroupMemberSnapshot
                        {
                            ChannelId = channelId,
                            AssetId = sourceId,
                            Name = "Ti",
                            NameOrigin = "user",
                            IsNameConfirmed = true,
                            Color = "#FFFF0000",
                            DisplayMaximum = 65535,
                        },
                    ],
                },
            ],
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(v23);

        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, migrated.SchemaVersion);
        Assert.Equal(JsonSerializer.Serialize(v23.Layers), JsonSerializer.Serialize(migrated.Layers));
        Assert.Equal(JsonSerializer.Serialize(v23.Measurements), JsonSerializer.Serialize(migrated.Measurements));
        Assert.Equal(JsonSerializer.Serialize(v23.ExportProfiles), JsonSerializer.Serialize(migrated.ExportProfiles));
        Assert.Equal(JsonSerializer.Serialize(v23.TemplateSnapshot), JsonSerializer.Serialize(migrated.TemplateSnapshot));
        Assert.Equal("Helvetica Neue", Assert.Single(migrated.FontSubstitutions).Requested);
        ProjectChannelGroupMemberSnapshot migratedMember =
            Assert.Single(Assert.Single(migrated.MultiChannelGroups).Members);
        Assert.Equal(7, migratedMember.SourceRevision);
        Assert.Equal("externalAsset", migratedMember.PlaneSelector?.SourceKind);
        Assert.Equal(0, migratedMember.PlaneSelector?.FrameIndex);
        Assert.Null(migratedMember.PlaneSelector?.ComponentIndex);
        Assert.Equal(groupId, Assert.Single(migrated.Layers).CompositeGroupId);
        Assert.Equal(v23.UpdatedAt, Assert.Single(migrated.AuditTrail).Timestamp);
        Assert.Same(migrated, ProjectMigrationPipeline.MigrateToCurrent(migrated));
    }
}
