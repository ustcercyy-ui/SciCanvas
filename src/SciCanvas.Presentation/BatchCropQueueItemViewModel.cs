using System.IO;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Presentation;

public sealed class BatchCropQueueItemViewModel : ObservableObject
{
    private string _statusText = "等待";
    private string? _outputPath;

    public BatchCropQueueItemViewModel(SourceAssetItemViewModel source, PixelRect64 crop)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Crop = crop;
    }

    public SourceAssetItemViewModel Source { get; }

    public PixelRect64 Crop { get; }

    public string DisplayName => Source.DisplayName;

    public string CropSummary => $"X {Crop.X} · Y {Crop.Y} · {Crop.Width} × {Crop.Height} px";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string? OutputPath
    {
        get => _outputPath;
        private set => SetProperty(ref _outputPath, value);
    }

    internal void MarkWaiting()
    {
        OutputPath = null;
        StatusText = "等待";
    }

    internal void MarkValidating() => StatusText = "正在校验源文件…";

    internal void MarkExporting(string outputPath)
    {
        OutputPath = outputPath;
        StatusText = "正在导出…";
    }

    internal void MarkCompleted(string outputPath)
    {
        OutputPath = outputPath;
        StatusText = $"完成 · {Path.GetFileName(outputPath)}";
    }

    internal void MarkFailed(string message) => StatusText = $"失败 · {message}";
}
