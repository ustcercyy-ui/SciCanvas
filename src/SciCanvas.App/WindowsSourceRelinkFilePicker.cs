using System.IO;
using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsSourceRelinkFilePicker : ISourceRelinkFilePicker
{
    public string? PickReplacement(
        string displayName,
        string originalPath,
        string expectedSha256)
    {
        string? originalDirectory = Path.GetDirectoryName(originalPath);
        var dialog = new OpenFileDialog
        {
            Title = $"重新链接源图 · {displayName} · SHA-256 {expectedSha256[..12]}",
            Filter = "科研图像|*.tif;*.tiff;*.png;*.jpg;*.jpeg;*.bmp|所有文件|*.*",
            InitialDirectory = Directory.Exists(originalDirectory) ? originalDirectory : null,
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
