using System.Text.Json;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ProjectMigrationPipelineTests
{
    [Fact]
    public void MigrateToCurrent_From26_AddsEmptyDataAssetsAndAuditEntry()
    {
        var document = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.6",
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(document);

        Assert.Equal("3.0", migrated.SchemaVersion);
        Assert.Empty(migrated.DataAssets);
        Assert.Empty(migrated.Plots);
        ProjectAuditEntrySnapshot audit = Assert.Single(migrated.AuditTrail);
        Assert.Equal("MigrateProject", audit.Command);
        Assert.Equal("2.6", audit.Parameters["from"]);
        Assert.Equal("3.0", audit.Parameters["to"]);
    }

    [Fact]
    public void MigrateToCurrent_From27_PreservesDataAssetsAndAddsEmptyPlots()
    {
        var document = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.7",
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(document);

        Assert.Equal("3.0", migrated.SchemaVersion);
        Assert.Same(document.DataAssets, migrated.DataAssets);
        Assert.Empty(migrated.Plots);
        ProjectAuditEntrySnapshot audit = Assert.Single(migrated.AuditTrail);
        Assert.Equal("2.7", audit.Parameters["from"]);
        Assert.Equal("3.0", audit.Parameters["to"]);
    }

    [Fact]
    public void MigrateToCurrent_From28_PreservesPlotsWithEmptyOperations()
    {
        IReadOnlyList<ProjectPlotSnapshot> plots =
        [
            new ProjectPlotSnapshot
            {
                Id = Guid.NewGuid(),
                Name = "Legacy plot",
            },
        ];
        var document = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.8",
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Plots = plots,
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(document);

        Assert.Equal("3.0", migrated.SchemaVersion);
        Assert.Same(plots, migrated.Plots);
        Assert.Null(migrated.Plots[0].Filter);
        Assert.Empty(migrated.Plots[0].Transforms);
        ProjectAuditEntrySnapshot audit = Assert.Single(migrated.AuditTrail);
        Assert.Equal("2.8", audit.Parameters["from"]);
        Assert.Equal("3.0", audit.Parameters["to"]);
    }

    [Fact]
    public void MigrateToCurrent_UpgradesLegacyDocumentAndCreatesWorkspace()
    {
        Guid projectId = Guid.NewGuid();
        Guid layerId = Guid.NewGuid();
        var legacy = new SciCanvasProjectDocument
        {
            SchemaVersion = "1.2",
            ProjectId = projectId,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
            Title = "Legacy figure",
            Canvas = new ProjectCanvasSnapshot { Width = 1200, Height = 900 },
            Layers =
            [
                new ProjectImageLayerSnapshot { Id = layerId },
            ],
            TemplateSnapshot = new ProjectTemplateSnapshot { TemplateId = "journal-2x2" },
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(legacy);

        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, migrated.SchemaVersion);
        ProjectFigureSnapshot figure = Assert.Single(migrated.Workspace.Figures);
        Assert.Equal(migrated.Workspace.ActiveFigureId, figure.Id);
        Assert.Equal("Legacy figure", figure.Name);
        Assert.Equal([layerId], figure.LayerIds);
        Assert.Contains(migrated.AuditTrail, entry => entry.Command == "MigrateProject");
    }

    [Fact]
    public void MigrateToCurrent_UsesDeterministicAuditTimestampAndSemanticResult()
    {
        var legacy = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.2",
            ProjectId = Guid.Parse("4A53BD2B-4BDD-4C95-8580-AD96D2AC3A71"),
            Title = "Deterministic legacy",
            UpdatedAt = new DateTimeOffset(2026, 8, 27, 5, 6, 7, TimeSpan.Zero),
        };

        SciCanvasProjectDocument first = ProjectMigrationPipeline.MigrateToCurrent(legacy);
        SciCanvasProjectDocument second = ProjectMigrationPipeline.MigrateToCurrent(legacy);

        Assert.Equal(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        ProjectAuditEntrySnapshot audit = Assert.Single(first.AuditTrail);
        Assert.Equal(legacy.UpdatedAt, audit.Timestamp);
    }
    [Fact]
    public void MigrateToCurrent_IsIdempotent()
    {
        var current = new SciCanvasProjectDocument { SchemaVersion = ProjectMigrationPipeline.CurrentVersion };

        Assert.Same(current, ProjectMigrationPipeline.MigrateToCurrent(current));
    }

    [Fact]
    public void MigrateToCurrent_UpgradesV20WithDeterministicEmptyAnalyses()
    {
        var version20 = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.0",
            ProjectId = Guid.NewGuid(),
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(version20);

        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, migrated.SchemaVersion);
        Assert.Empty(migrated.Analyses);
        Assert.Contains(migrated.AuditTrail, entry =>
            entry.Command == "MigrateProject" &&
            Equals(entry.Parameters["from"], "2.0") &&
            Equals(entry.Parameters["to"], ProjectMigrationPipeline.CurrentVersion));
        Assert.Same(migrated, ProjectMigrationPipeline.MigrateToCurrent(migrated));
    }

    [Fact]
    public void MigrateToCurrent_UpgradesV21AndPreservesExistingAnalyses()
    {
        var analysis = new ProjectScientificAnalysisSnapshot
        {
            Id = Guid.NewGuid(),
            SourceAssetId = Guid.NewGuid(),
            Kind = "roiStatistics",
        };
        var version21 = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.1",
            ProjectId = Guid.NewGuid(),
            Analyses = [analysis],
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(version21);

        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, migrated.SchemaVersion);
        Assert.Same(analysis, Assert.Single(migrated.Analyses));
        Assert.Contains(migrated.AuditTrail, entry =>
            entry.Command == "MigrateProject" && Equals(entry.Parameters["from"], "2.1"));
    }

    [Fact]
    public void MigrateToCurrent_RejectsUnknownSchema()
    {
        var unknown = new SciCanvasProjectDocument { SchemaVersion = "99.0" };

        Assert.Throws<NotSupportedException>(() =>
            ProjectMigrationPipeline.MigrateToCurrent(unknown));
    }

    [Fact]
    public void MigrateToCurrent_UpgradesV22ScientificStylesWithoutChangingLegacyAppearance()
    {
        Guid sourceId = Guid.NewGuid();
        var version22 = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.2",
            ProjectId = Guid.NewGuid(),
            Measurements =
            [
                new ProjectMeasurementSnapshot
                {
                    Id = Guid.NewGuid(),
                    SourceAssetId = sourceId,
                    Kind = "rectangle",
                    StrokeColor = "#FF123456",
                    FillOpacityPercent = 21,
                },
            ],
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                GlobalStyle = new ProjectGlobalStyleSnapshot
                {
                    FontFamily = "Consolas",
                    FontSizePt = 9,
                    StrokeWidthPt = 1.5,
                    TextColor = "#FF101112",
                    ShapeColor = "#FF202122",
                    ScaleBarColor = "#FFF0F1F2",
                },
                Annotations =
                [
                    new ProjectAnnotationSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Kind = "text",
                        Text = "legacy",
                        Color = "#FF123456",
                    },
                    new ProjectAnnotationSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Kind = "rectangle",
                        Color = "#FFABCDEF",
                    },
                ],
            },
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(version22);

        ProjectMeasurementSnapshot measurement = Assert.Single(migrated.Measurements);
        Assert.Equal("#FF123456", measurement.StrokeColor);
        Assert.Equal("#FF123456", measurement.FillColor);
        Assert.Equal("#FF123456", measurement.MarkerStrokeColor);
        Assert.Equal("#FF123456", measurement.LabelColor);
        Assert.Equal("Consolas", measurement.LabelFontFamily);
        Assert.Equal(21, measurement.FillOpacityPercent);

        ProjectAnnotationSnapshot text = migrated.TemplateSnapshot!.Annotations.Single(item => item.Kind == "text");
        Assert.Equal("#FF123456", text.TextColor);
        Assert.Equal("Consolas", text.FontFamily);
        ProjectAnnotationSnapshot rectangle = migrated.TemplateSnapshot.Annotations.Single(item => item.Kind == "rectangle");
        Assert.Equal("#FFABCDEF", rectangle.StrokeColor);
        Assert.Equal(0, rectangle.FillOpacityPercent);

        ProjectGlobalStyleSnapshot style = Assert.IsType<ProjectGlobalStyleSnapshot>(migrated.TemplateSnapshot.GlobalStyle);
        Assert.Equal("Consolas", style.PanelLabelFontFamily);
        Assert.Equal("#FFF0F1F2", style.ScaleBarLabelColor);
        Assert.Equal(1.5, style.ScaleBarThicknessPt);
    }

    [Fact]
    public void MigrateToCurrent_UpgradesV24FigureRoiWithoutInventingCanonicalReference()
    {
        Guid legacyObjectId = Guid.NewGuid();
        var version24 = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.4",
            ProjectId = Guid.NewGuid(),
            Rois =
            [
                new ProjectRoiSnapshot
                {
                    Id = Guid.NewGuid(),
                    AssetId = Guid.NewGuid(),
                    SourceRevision = 3,
                    SourceGeometry =
                    [
                        new ProjectMeasurementPointSnapshot { X = 1, Y = 2 },
                        new ProjectMeasurementPointSnapshot { X = 8, Y = 2 },
                        new ProjectMeasurementPointSnapshot { X = 4, Y = 9 },
                    ],
                    Style = new ProjectRoiStyleSnapshot
                    {
                        Label = "Canonical ROI",
                        LabelFont = "Consolas",
                    },
                },
            ],
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                ScientificObjects =
                [
                    new ProjectFigureScientificObjectSnapshot
                    {
                        Id = legacyObjectId,
                        Kind = "Roi",
                        Points = "10,10; 30,10; 20,30",
                        Label = "Legacy visual ROI",
                        StrokeColor = "#FF123456",
                        FillColor = "#FF654321",
                        FillOpacityPercent = 18,
                    },
                ],
            },
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(version24);

        Assert.Equal(ProjectMigrationPipeline.CurrentVersion, migrated.SchemaVersion);
        ProjectFigureScientificObjectSnapshot polygon = Assert.Single(migrated.TemplateSnapshot!.ScientificObjects);
        Assert.Equal(legacyObjectId, polygon.Id);
        Assert.Equal("PolygonAnnotation", polygon.Kind);
        Assert.Equal("10,10; 30,10; 20,30", polygon.Points);
        Assert.Equal("Legacy visual ROI", polygon.Label);
        Assert.Equal("#FF123456", polygon.StrokeColor);
        Assert.Empty(migrated.TemplateSnapshot.RoiProjections);
        ProjectRoiStyleSnapshot roiStyle = Assert.Single(migrated.Rois).Style;
        Assert.Equal("Consolas", roiStyle.LabelFont);
        Assert.Equal(7, roiStyle.LabelFontSizePt);
        Assert.False(roiStyle.LabelIsBold);
        Assert.Same(migrated, ProjectMigrationPipeline.MigrateToCurrent(migrated));
    }

    [Fact]
    public void MigrateToCurrent_ComputesLegacyPropagatedRoiCoverageFromGeometry()
    {
        Guid referenceAssetId = Guid.NewGuid();
        Guid targetAssetId = Guid.NewGuid();
        Guid referenceRoiId = Guid.NewGuid();
        Guid targetRoiId = Guid.NewGuid();
        var document = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.4",
            ProjectId = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-08-28T00:00:00Z"),
            Sources =
            [
                Source(referenceAssetId),
                Source(targetAssetId),
            ],
            Rois =
            [
                new ProjectRoiSnapshot
                {
                    Id = referenceRoiId,
                    AssetId = referenceAssetId,
                    SourceRevision = 1,
                    GeometryKind = "polygon",
                    SourceGeometry = Points((2, 2), (6, 2), (6, 6), (2, 6)),
                },
                new ProjectRoiSnapshot
                {
                    Id = targetRoiId,
                    AssetId = targetAssetId,
                    SourceRevision = 1,
                    GeometryKind = "polygon",
                    SourceGeometry = Points((-2, 2), (2, 2), (2, 6), (-2, 6)),
                    Propagation = new ProjectRoiPropagationSnapshot
                    {
                        ReferenceRoiId = referenceRoiId,
                        TargetRoiId = targetRoiId,
                        LinkGroupId = Guid.NewGuid(),
                        MappingId = Guid.NewGuid(),
                    },
                },
            ],
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(document);
        ProjectRoiSnapshot target = migrated.Rois.Single(roi => roi.Id == targetRoiId);

        Assert.Equal("reviewrequired", target.Validity.State);
        Assert.Equal(0.5, target.Propagation!.TargetCoverageFraction, 10);
    }

    [Fact]
    public void MigrateToCurrent_NormalizesPreSelectorV25OnceWithoutRewritingCanonicalData()
    {
        Guid referenceAssetId = Guid.NewGuid();
        Guid rgbAssetId = Guid.NewGuid();
        Guid referenceChannelId = Guid.NewGuid();
        Guid blueChannelId = Guid.NewGuid();
        ProjectSourceSnapshot reference = Source(referenceAssetId);
        ProjectSourceSnapshot rgb = new()
        {
            Id = rgbAssetId,
            SourceRevision = 2,
            Metadata = new ProjectImageMetadataSnapshot
            {
                Width = 2,
                Height = 1,
                Channels = 3,
                BitsPerChannel = 8,
                PixelFormat = "Rgb24",
            },
        };
        IReadOnlyList<ProjectMeasurementSnapshot> measurements =
        [
            new ProjectMeasurementSnapshot
            {
                Id = Guid.NewGuid(),
                SourceAssetId = referenceAssetId,
                SourceRevision = 1,
                Kind = "rectangle",
                LabelFontFamily = "Custom Canonical Font",
            },
        ];
        IReadOnlyList<ProjectAuditEntrySnapshot> audit =
        [
            new ProjectAuditEntrySnapshot
            {
                Timestamp = DateTimeOffset.UnixEpoch,
                Command = "Existing",
            },
        ];
        var document = new SciCanvasProjectDocument
        {
            SchemaVersion = "2.5",
            ProjectId = Guid.NewGuid(),
            Sources = [reference, rgb],
            Measurements = measurements,
            AuditTrail = audit,
            TemplateSnapshot = new ProjectTemplateSnapshot
            {
                ScientificObjects =
                [
                    new ProjectFigureScientificObjectSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Kind = "Colorbar",
                        Points = "10,10;30,100",
                        Minimum = 10,
                        Maximum = 110,
                        ChannelId = blueChannelId,
                    },
                    new ProjectFigureScientificObjectSnapshot
                    {
                        Id = Guid.NewGuid(),
                        Kind = "ChannelLegend",
                        Points = "40,10;140,80",
                        ChannelEntries = "Blue|#FF0000FF",
                    },
                ],
            },
            MultiChannelGroups =
            [
                new ProjectMultiChannelAssetGroupSnapshot
                {
                    Id = Guid.NewGuid(),
                    Name = "Legacy 2.5 group",
                    ReferenceAssetId = referenceAssetId,
                    SameFieldOfViewConfirmed = true,
                    Members =
                    [
                        new ProjectChannelGroupMemberSnapshot
                        {
                            ChannelId = referenceChannelId,
                            AssetId = referenceAssetId,
                            SourceRevision = 1,
                            FrameIndex = 0,
                            Name = "Reference",
                            Color = "#FFFFFFFF",
                            NameOrigin = "user",
                            IsNameConfirmed = true,
                        },
                        new ProjectChannelGroupMemberSnapshot
                        {
                            ChannelId = blueChannelId,
                            AssetId = rgbAssetId,
                            SourceRevision = 2,
                            FrameIndex = 0,
                            Name = "Legacy component",
                            Color = "#FF0000FF",
                            NameOrigin = "user",
                            IsNameConfirmed = true,
                        },
                    ],
                },
            ],
        };

        SciCanvasProjectDocument migrated = ProjectMigrationPipeline.MigrateToCurrent(document);

        Assert.NotSame(document, migrated);
        Assert.Same(measurements, migrated.Measurements);
        Assert.Equal(2, migrated.AuditTrail.Count);
        Assert.Same(audit[0], migrated.AuditTrail[0]);
        Assert.Equal("MigrateProject", migrated.AuditTrail[1].Command);
        Assert.Equal("2.5", migrated.AuditTrail[1].Parameters["from"]);
        Assert.Equal("3.0", migrated.AuditTrail[1].Parameters["to"]);
        ProjectChannelGroupMemberSnapshot[] members =
            Assert.Single(migrated.MultiChannelGroups).Members.ToArray();
        Assert.Equal("externalAsset", members[0].PlaneSelector?.SourceKind);
        Assert.Equal("interleavedComponent", members[1].PlaneSelector?.SourceKind);
        Assert.Equal(0, members[1].PlaneSelector?.ComponentIndex);
        Assert.All(members, member => Assert.Equal("viridis", member.Colormap));
        ProjectFigureScientificObjectSnapshot[] scientificObjects =
            migrated.TemplateSnapshot!.ScientificObjects.ToArray();
        Assert.Equal("Linked", scientificObjects[0].ColorbarBindingState);
        Assert.Equal("Vertical", scientificObjects[0].Orientation);
        Assert.Equal(5, scientificObjects[0].Ticks.Count);
        Assert.Equal(10, scientificObjects[0].Ticks[0].Value);
        Assert.Equal(110, scientificObjects[0].Ticks[^1].Value);
        Assert.Equal(5, scientificObjects[1].ChannelLegendPadding);
        Assert.Same(migrated, ProjectMigrationPipeline.MigrateToCurrent(migrated));
    }

    private static ProjectSourceSnapshot Source(Guid id) => new()
    {
        Id = id,
        SourceRevision = 1,
        Metadata = new ProjectImageMetadataSnapshot
        {
            Width = 10,
            Height = 10,
            Channels = 1,
            BitsPerChannel = 8,
            PixelFormat = "Gray8",
        },
    };

    private static IReadOnlyList<ProjectMeasurementPointSnapshot> Points(
        params (double X, double Y)[] points) =>
        points.Select(point => new ProjectMeasurementPointSnapshot
        {
            X = point.X,
            Y = point.Y,
        }).ToArray();
}
