namespace SciCanvas.Core.Export;

public sealed record ExportPathDecision(
    bool IsAllowed,
    string? NormalizedTargetPath,
    ExportPathRejectionReason? RejectionReason,
    string Message)
{
    public static ExportPathDecision Allow(string normalizedTargetPath)
    {
        return new ExportPathDecision(true, normalizedTargetPath, null, "导出目标安全。");
    }

    public static ExportPathDecision Reject(
        ExportPathRejectionReason reason,
        string message,
        string? normalizedTargetPath = null)
    {
        return new ExportPathDecision(false, normalizedTargetPath, reason, message);
    }
}

