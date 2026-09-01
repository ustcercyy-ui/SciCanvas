using Microsoft.Win32;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public sealed class WindowsTabularDataFilePicker : ITabularDataFilePicker
{
    public string? PickTabularDataFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "只读导入科研表格",
            Filter = "科研表格|*.csv;*.tsv;*.xlsx|CSV|*.csv|TSV|*.tsv|Excel 工作簿|*.xlsx",
            Multiselect = false,
            CheckFileExists = true,
            CheckPathExists = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
