using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsImageFilePicker : IImageFilePicker
{
    public IReadOnlyList<string> PickImageFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "只读导入科研图像",
            Filter = "科研图像|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp|TIFF|*.tif;*.tiff|PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp|所有文件|*.*",
            Multiselect = true,
            CheckFileExists = true,
            CheckPathExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }
}
