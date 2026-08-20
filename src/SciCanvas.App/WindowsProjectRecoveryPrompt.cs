using System.IO;
using System.Windows;
using SciCanvas.Persistence;
using SciCanvas.Presentation;

namespace SciCanvas.App;

internal sealed class WindowsProjectRecoveryPrompt : IProjectRecoveryPrompt
{
    public bool ShouldRestore(ProjectRecoveryCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        string projectLabel = candidate.OriginalProjectPath is null
            ? "未命名工程"
            : Path.GetFileName(candidate.OriginalProjectPath);
        string message =
            $"检测到 {projectLabel} 的自动保存副本。\n\n" +
            $"自动保存时间：{candidate.LastWriteTimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n\n" +
            "选择“是”恢复编辑内容；选择“否”放弃并删除该恢复副本。\n" +
            "无论选择哪项，科研源图都不会被修改。";

        return MessageBox.Show(
                   message,
                   "SciCanvas 自动恢复",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Question,
                   MessageBoxResult.Yes) == MessageBoxResult.Yes;
    }
}
