using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsTemplateFilePicker : ITemplateFilePicker
{
    public string? PickTemplatePath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入 SciCanvas 用户模板",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "SciCanvas 模板 JSON|*.json|所有文件|*.*",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
