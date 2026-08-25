namespace SciCanvas.Persistence;

/// <summary>
/// Explicit, idempotent migration boundary for project files. V2 adds workspace,
/// normalized crop, millimeter frames, source revisions and scientific validity;
/// all new fields have deterministic defaults for legacy documents.
/// </summary>
public static class ProjectMigrationPipeline
{
    public const string CurrentVersion = "2.0";

    public static IReadOnlySet<string> SupportedVersions { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "0.1",
            "0.9",
            "1.1",
            "1.2",
            CurrentVersion,
        };

    public static SciCanvasProjectDocument MigrateToCurrent(SciCanvasProjectDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!SupportedVersions.Contains(document.SchemaVersion))
        {
            throw new NotSupportedException($"暂不支持工程版本 {document.SchemaVersion}。");
        }

        if (document.SchemaVersion == CurrentVersion)
        {
            return document;
        }

        Guid figureId = CreateStableFigureId(document.ProjectId);
        ProjectWorkspaceSnapshot workspace = document.Workspace.Figures.Count > 0
            ? document.Workspace
            : new ProjectWorkspaceSnapshot
            {
                ActiveFigureId = figureId,
                Figures =
                [
                    new ProjectFigureSnapshot
                    {
                        Id = figureId,
                        Name = string.IsNullOrWhiteSpace(document.Title) ? "Figure 1" : document.Title,
                        WidthMm = document.Canvas.Width / 300.0 * 25.4,
                        HeightMm = document.Canvas.Height / 300.0 * 25.4,
                        Dpi = 300,
                        TemplateId = document.TemplateSnapshot?.TemplateId ?? string.Empty,
                        LayerIds = document.Layers.Select(layer => layer.Id).ToArray(),
                    },
                ],
            };

        return new SciCanvasProjectDocument
        {
            SchemaVersion = CurrentVersion,
            ProjectId = document.ProjectId,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            Title = document.Title,
            Canvas = document.Canvas,
            Sources = document.Sources,
            Layers = document.Layers,
            CropPresets = document.CropPresets,
            Guides = document.Guides,
            ExportProfiles = document.ExportProfiles,
            Calibrations = document.Calibrations,
            Measurements = document.Measurements,
            TemplateSnapshot = document.TemplateSnapshot,
            AuditTrail = document.AuditTrail
                .Concat(
                [
                    new ProjectAuditEntrySnapshot
                    {
                        Timestamp = DateTimeOffset.UtcNow,
                        Command = "MigrateProject",
                        Parameters = new Dictionary<string, object?>
                        {
                            ["from"] = document.SchemaVersion,
                            ["to"] = CurrentVersion,
                        },
                    },
                ])
                .ToArray(),
            Workspace = workspace,
        };
    }

    private static Guid CreateStableFigureId(Guid projectId)
    {
        byte[] bytes = projectId.ToByteArray();
        bytes[0] ^= 0x53;
        bytes[1] ^= 0x43;
        bytes[2] ^= 0x49;
        return new Guid(bytes);
    }
}
