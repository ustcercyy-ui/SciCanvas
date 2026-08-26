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

    public string? PickNewFigureExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出新的拼版文件（不覆盖已有文件）",
            FileName = suggestedFileName,
            DefaultExt = ".tif",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = false,
            Filter =
                "TIFF（推荐位图）|*.tif|PDF（可编辑矢量）|*.pdf|SVG（可编辑矢量）|*.svg|PNG|*.png|BMP|*.bmp|JPEG（有损）|*.jpg",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickNewMeasurementExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出新的测量表（不覆盖已有文件）",
            FileName = suggestedFileName,
            DefaultExt = ".csv",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = false,
            Filter = "CSV 测量表|*.csv|Excel 工作簿|*.xlsx",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickNewAnalysisExportPath(string suggestedFileName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出新的科学图像分析表（不覆盖已有文件）",
            FileName = suggestedFileName,
            DefaultExt = ".csv",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = false,
            Filter = "CSV 分析表|*.csv|Excel 工作簿|*.xlsx",
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
