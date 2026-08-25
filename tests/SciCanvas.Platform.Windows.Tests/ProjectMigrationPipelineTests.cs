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

        Assert.Equal("2.0", migrated.SchemaVersion);
        ProjectFigureSnapshot figure = Assert.Single(migrated.Workspace.Figures);
        Assert.Equal(migrated.Workspace.ActiveFigureId, figure.Id);
        Assert.Equal("Legacy figure", figure.Name);
        Assert.Equal([layerId], figure.LayerIds);
        Assert.Contains(migrated.AuditTrail, entry => entry.Command == "MigrateProject");
    }

    [Fact]
    public void MigrateToCurrent_IsIdempotent()
    {
        var current = new SciCanvasProjectDocument { SchemaVersion = "2.0" };

        Assert.Same(current, ProjectMigrationPipeline.MigrateToCurrent(current));
    }

    [Fact]
    public void MigrateToCurrent_RejectsUnknownSchema()
    {
        var unknown = new SciCanvasProjectDocument { SchemaVersion = "99.0" };

        Assert.Throws<NotSupportedException>(() =>
            ProjectMigrationPipeline.MigrateToCurrent(unknown));
    }
}
