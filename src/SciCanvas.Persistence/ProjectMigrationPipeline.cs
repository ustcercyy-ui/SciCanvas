namespace SciCanvas.Persistence;

/// <summary>
/// Explicit, idempotent migration boundary for project files. V2 adds workspace,
/// normalized crop, millimeter frames, source revisions and scientific validity;
/// V2.1 adds source-revision-bound scientific image analysis results; V2.2 adds
/// persisted threshold/particle analysis and reproducible automation parameters; V2.3
/// separates scientific stroke/fill/marker/label and annotation text/shape styles. All new
/// fields have deterministic defaults for legacy documents.
/// </summary>
public static class ProjectMigrationPipeline
{
    public const string CurrentVersion = "2.3";

    public static IReadOnlySet<string> SupportedVersions { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "0.1",
            "0.9",
            "1.1",
            "1.2",
            "2.0",
            "2.1",
            "2.2",
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
        string globalFontFamily = string.IsNullOrWhiteSpace(document.TemplateSnapshot?.GlobalStyle?.FontFamily)
            ? "Arial"
            : document.TemplateSnapshot.GlobalStyle.FontFamily.Trim();
        string globalTextColor = string.IsNullOrWhiteSpace(document.TemplateSnapshot?.GlobalStyle?.TextColor)
            ? "#FF111111"
            : document.TemplateSnapshot.GlobalStyle.TextColor;
        string globalShapeColor = string.IsNullOrWhiteSpace(document.TemplateSnapshot?.GlobalStyle?.ShapeColor)
            ? "#FFE53935"
            : document.TemplateSnapshot.GlobalStyle.ShapeColor;

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
            Measurements = document.Measurements
                .Select(measurement => MigrateMeasurement(measurement, globalFontFamily))
                .ToArray(),
            Analyses = document.Analyses,
            TemplateSnapshot = MigrateTemplateSnapshot(
                document.TemplateSnapshot,
                globalFontFamily,
                globalTextColor,
                globalShapeColor),
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

    private static ProjectMeasurementSnapshot MigrateMeasurement(
        ProjectMeasurementSnapshot measurement,
        string globalFontFamily) => new()
    {
        Id = measurement.Id,
        SourceAssetId = measurement.SourceAssetId,
        SourceRevision = Math.Max(1, measurement.SourceRevision),
        Kind = measurement.Kind,
        X1 = measurement.X1,
        Y1 = measurement.Y1,
        X2 = measurement.X2,
        Y2 = measurement.Y2,
        X3 = measurement.X3,
        Y3 = measurement.Y3,
        StrokeColor = measurement.StrokeColor,
        StrokeWidthPixels = measurement.StrokeWidthPixels,
        LineStyle = measurement.LineStyle,
        FillColor = measurement.StrokeColor,
        MarkerStrokeColor = measurement.StrokeColor,
        MarkerFillColor = "#FF11171F",
        MarkerSizePixels = measurement.MarkerSizePixels,
        ShowMarkers = measurement.ShowMarkers,
        ShowLabel = measurement.ShowLabel,
        LabelColor = measurement.StrokeColor,
        LabelFontFamily = globalFontFamily,
        LabelFontSizePt = 16.5,
        LabelIsBold = true,
        FillOpacityPercent = measurement.FillOpacityPercent,
        IsVisible = measurement.IsVisible,
        IsLocked = measurement.IsLocked,
        Points = measurement.Points,
    };

    private static ProjectTemplateSnapshot? MigrateTemplateSnapshot(
        ProjectTemplateSnapshot? template,
        string globalFontFamily,
        string globalTextColor,
        string globalShapeColor)
    {
        if (template is null)
        {
            return null;
        }

        return new ProjectTemplateSnapshot
        {
            TemplateId = template.TemplateId,
            WorkspaceMode = template.WorkspaceMode,
            SelectedSourceId = template.SelectedSourceId,
            ActiveCrop = template.ActiveCrop,
            LockCropSizeAcrossSources = template.LockCropSizeAcrossSources,
            CropOverlayVisible = template.CropOverlayVisible,
            SnappingEnabled = template.SnappingEnabled,
            SnapTolerancePixels = template.SnapTolerancePixels,
            ExactSpacingPixels = template.ExactSpacingPixels,
            AutoPanelLabelsEnabled = template.AutoPanelLabelsEnabled,
            ShowPanelLabels = template.ShowPanelLabels,
            PanelLabelSequence = template.PanelLabelSequence,
            LayerSlots = template.LayerSlots,
            ScaleBars = template.ScaleBars,
            Annotations = template.Annotations
                .Select(annotation => MigrateAnnotation(
                    annotation,
                    globalFontFamily,
                    globalTextColor,
                    globalShapeColor))
                .ToArray(),
            GlobalStyle = MigrateGlobalStyle(template.GlobalStyle),
            ScientificColors = template.ScientificColors,
        };
    }

    private static ProjectGlobalStyleSnapshot? MigrateGlobalStyle(ProjectGlobalStyleSnapshot? style)
    {
        if (style is null)
        {
            return null;
        }

        return new ProjectGlobalStyleSnapshot
        {
            FontFamily = style.FontFamily,
            FontSizePt = style.FontSizePt,
            StrokeWidthPt = style.StrokeWidthPt,
            TextColor = style.TextColor,
            ShapeColor = style.ShapeColor,
            ScaleBarColor = style.ScaleBarColor,
            PanelLabelFontFamily = style.FontFamily,
            PanelLabelFontSizePt = style.FontSizePt,
            PanelLabelTextColor = style.TextColor,
            PanelLabelIsBold = true,
            ScaleBarLabelColor = style.ScaleBarColor,
            ScaleBarFontFamily = style.FontFamily,
            ScaleBarFontSizePt = style.FontSizePt,
            ScaleBarLabelIsBold = true,
            ScaleBarThicknessPt = style.StrokeWidthPt,
        };
    }

    private static ProjectAnnotationSnapshot MigrateAnnotation(
        ProjectAnnotationSnapshot annotation,
        string globalFontFamily,
        string globalTextColor,
        string globalShapeColor)
    {
        bool isText = string.Equals(annotation.Kind, "text", StringComparison.OrdinalIgnoreCase);
        return new ProjectAnnotationSnapshot
        {
            Id = annotation.Id,
            Kind = annotation.Kind,
            X = annotation.X,
            Y = annotation.Y,
            EndX = annotation.EndX,
            EndY = annotation.EndY,
            Text = annotation.Text,
            Color = annotation.Color,
            StrokeColor = isText ? globalShapeColor : annotation.Color,
            FillColor = annotation.Color,
            FillOpacityPercent = 0,
            TextColor = isText ? annotation.Color : globalTextColor,
            FontFamily = globalFontFamily,
            FontSizePt = annotation.FontSizePt,
            StrokeWidthPt = annotation.StrokeWidthPt,
            IsBold = annotation.IsBold,
            Visible = annotation.Visible,
            Locked = annotation.Locked,
            ZIndex = annotation.ZIndex,
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
