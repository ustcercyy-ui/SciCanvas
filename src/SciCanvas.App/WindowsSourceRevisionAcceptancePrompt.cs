using System.Windows;
using SciCanvas.Presentation;

namespace SciCanvas.App;

internal sealed class WindowsSourceRevisionAcceptancePrompt : ISourceRevisionAcceptancePrompt
{
    public bool ConfirmAcceptance(SourceRevisionAcceptanceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string message =
            $"即将把工程中的源图记录更新为当前磁盘文件：\n\n" +
            $"文件：{request.DisplayName}\n" +
            $"路径：{request.Path}\n\n" +
            $"旧 SHA-256：{request.PreviousFingerprint.Sha256}\n" +
            $"新 SHA-256：{request.ProposedFingerprint.Sha256}\n\n" +
            $"旧尺寸：{request.PreviousWidth:N0} × {request.PreviousHeight:N0} px\n" +
            $"新尺寸：{request.ProposedWidth:N0} × {request.ProposedHeight:N0} px\n\n" +
            "此操作不会写入源文件，但会更新工程指纹、预览和元数据，清空旧撤销历史，并写入审计轨迹。\n" +
            "只有在你确认磁盘文件就是应采用的新实验版本时才选择“是”。";

        return MessageBox.Show(
                   message,
                   "明确接受源图新版本",
                   MessageBoxButton.YesNo,
                   MessageBoxImage.Warning,
                   MessageBoxResult.No) == MessageBoxResult.Yes;
    }
}
