using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsExportFilePicker : IExportFilePicker
{
    public string? PickNewExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出新的裁剪图像（不覆盖已有文件）",
            FileName = suggestedFileName,
            DefaultExt = ".tif",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = false,
            Filter = "TIFF（推荐）|*.tif|PNG|*.png|BMP|*.bmp|JPEG（有损）|*.jpg",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
