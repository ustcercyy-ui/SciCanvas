using SciCanvas.Core.Sources;

namespace SciCanvas.Core.Export;

public interface IPathSafetyPolicy
{
    Task<ExportPathDecision> ValidateExportTargetAsync(
        string targetPath,
        IReadOnlyCollection<SourceAsset> sources,
        CancellationToken cancellationToken = default);
}

