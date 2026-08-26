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

        Assert.Equal("2.2", migrated.SchemaVersion);
        ProjectFigureSnapshot figure = Assert.Single(migrated.Workspace.Figures);
        Assert.Equal(migrated.Workspace.ActiveFigureId, figure.Id);
        Assert.Equal("Legacy figure", figure.Name);
        Assert.Equal([layerId], figure.LayerIds);
        Assert.Contains(migrated.AuditTrail, entry => entry.Command == "MigrateProject");
    }

    [Fact]
    public void MigrateToCurrent_IsIdempotent()
    {
        var current = new SciCanvasProjectDocument { SchemaVersion = "2.2" };

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

        Assert.Equal("2.2", migrated.SchemaVersion);
        Assert.Empty(migrated.Analyses);
        Assert.Contains(migrated.AuditTrail, entry =>
            entry.Command == "MigrateProject" &&
            Equals(entry.Parameters["from"], "2.0") &&
            Equals(entry.Parameters["to"], "2.2"));
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

        Assert.Equal("2.2", migrated.SchemaVersion);
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
}
