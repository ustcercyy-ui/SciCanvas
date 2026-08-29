using SciCanvas.Core.Geometry;

namespace SciCanvas.Core.Workspace;

/// <summary>
/// Canonical navigation target for every QC issue.  The legacy FigureId/PanelId/
/// AssetId/ObjectId properties on <see cref="QcResult"/> remain compatibility
/// projections of this value.
/// </summary>
public sealed record QcIssueLocation(
    Guid? ProjectId = null,
    Guid? FigureId = null,
    Guid? PanelId = null,
    Guid? AssetId = null,
    Guid? ScientificObjectId = null,
    Guid? MeasurementId = null,
    Guid? AnalysisResultId = null,
    Guid? ChannelId = null,
    Guid? LinkGroupId = null,
    Guid? MappingId = null,
    PixelRect64? SourceRegion = null,
    FigureRectMm? FigureRegion = null)
{
    public bool IsEmpty =>
        ProjectId is null && FigureId is null && PanelId is null && AssetId is null &&
        ScientificObjectId is null && MeasurementId is null && AnalysisResultId is null &&
        ChannelId is null && LinkGroupId is null && MappingId is null &&
        SourceRegion is null && FigureRegion is null;
}
