using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsSubmissionPackageFolderPicker : ISubmissionPackageFolderPicker
{
    public string? PickSubmissionPackageFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择一个全新或空的投稿包文件夹（不会覆盖已有内容）",
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
