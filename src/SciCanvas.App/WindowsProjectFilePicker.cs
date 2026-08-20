using System.IO;
using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsProjectFilePicker : IProjectFilePicker
{
    private const string ProjectFilter = "SciCanvas 工程|*.scicanvas";

    public string? PickProjectToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 SciCanvas 工程",
            Filter = ProjectFilter,
            DefaultExt = ".scicanvas",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickProjectToSave(string suggestedFileName, string? currentPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "保存 SciCanvas 工程",
            Filter = ProjectFilter,
            DefaultExt = ".scicanvas",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true,
            FileName = suggestedFileName,
            InitialDirectory = currentPath is null ? null : Path.GetDirectoryName(currentPath),
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
