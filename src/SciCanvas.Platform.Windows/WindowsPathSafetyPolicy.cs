using SciCanvas.Core.Export;
using SciCanvas.Core.Sources;

namespace SciCanvas.Platform.Windows;

public sealed class WindowsPathSafetyPolicy : IPathSafetyPolicy
{
    private readonly WindowsFileIdentityProvider _fileIdentityProvider;

    public WindowsPathSafetyPolicy(WindowsFileIdentityProvider? fileIdentityProvider = null)
    {
        _fileIdentityProvider = fileIdentityProvider ?? new WindowsFileIdentityProvider();
    }

    public Task<ExportPathDecision> ValidateExportTargetAsync(
        string targetPath,
        IReadOnlyCollection<SourceAsset> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sources);
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedTarget;
        try
        {
            normalizedTarget = Path.GetFullPath(targetPath);
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Task.FromResult(ExportPathDecision.Reject(
                ExportPathRejectionReason.InvalidPath,
                "导出路径无效。"));
        }

        if (Directory.Exists(normalizedTarget))
        {
            return Task.FromResult(ExportPathDecision.Reject(
                ExportPathRejectionReason.TargetIsDirectory,
                "导出目标必须是文件，不能是文件夹。",
                normalizedTarget));
        }

        foreach (SourceAsset source in sources)
        {
            string normalizedSource;
            try
            {
                normalizedSource = Path.GetFullPath(source.OriginalPath);
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (string.Equals(
                    normalizedTarget,
                    normalizedSource,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(ExportPathDecision.Reject(
                    ExportPathRejectionReason.SameAsSourcePath,
                    "导出目标与源文件路径相同。请选择新的输出文件。",
                    normalizedTarget));
            }
        }

        if (File.Exists(normalizedTarget))
        {
            string? targetFileId = _fileIdentityProvider.TryGetFileId(normalizedTarget);
            if (targetFileId is not null)
            {
                foreach (SourceAsset source in sources)
                {
                    string? sourceFileId = source.Fingerprint.WindowsFileId ??
                                           _fileIdentityProvider.TryGetFileId(source.OriginalPath);

                    if (sourceFileId is not null && string.Equals(
                            targetFileId,
                            sourceFileId,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return Task.FromResult(ExportPathDecision.Reject(
                            ExportPathRejectionReason.SameAsSourceFile,
                            "导出目标与源文件指向同一个磁盘文件，操作已阻止。",
                            normalizedTarget));
                    }
                }
            }
        }

        return Task.FromResult(ExportPathDecision.Allow(normalizedTarget));
    }
}

