using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsBatchExportFolderPicker : IBatchExportFolderPicker
{
    public string? PickExportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择批量输出文件夹",
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
