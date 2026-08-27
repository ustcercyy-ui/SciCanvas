using System.Text.Json;
using SciCanvas.Persistence;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class ProjectMigrationPipelineTests
{
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
}
