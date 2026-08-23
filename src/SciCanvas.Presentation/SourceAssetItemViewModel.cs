using System.Windows.Media.Imaging;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;

namespace SciCanvas.Presentation;

public sealed class SourceAssetItemViewModel : ObservableObject
{
    private SourceAsset _asset;
    private BitmapSource _preview;
    private readonly Dictionary<int, BitmapSource> _framePreviews = [];

    public SourceAssetItemViewModel(SourceAsset asset, BitmapSource preview)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
        _framePreviews[0] = _preview;
    }

    public SourceAsset Asset => _asset;

    public BitmapSource Preview => _preview;

    public string DisplayName => Asset.DisplayName;

    public string OriginalPath => Asset.OriginalPath;

    public long Width => Asset.Metadata.PixelSize.Width;

    public long Height => Asset.Metadata.PixelSize.Height;

    public int FrameCount => Math.Max(1, Asset.Metadata.FrameCount);

    public string DimensionsText => $"{Width:N0} × {Height:N0} px";

    public string FormatText => Asset.Metadata.FrameCount > 1
        ? $"{Asset.Metadata.BitsPerChannel}-bit · {Asset.Metadata.Channels}通道 · {Asset.Metadata.FrameCount}页"
        : $"{Asset.Metadata.BitsPerChannel}-bit · {Asset.Metadata.Channels}通道";

    public string FileSizeText => FormatBytes(Asset.Fingerprint.ByteLength);

    public string DpiText => Asset.Metadata.DpiX is double dpiX && Asset.Metadata.DpiY is double dpiY
        ? $"{dpiX:0.#}×{dpiY:0.#} dpi"
        : "DPI 未提供";

    public string DetailsText => $"{FormatText} · {DpiText} · {FileSizeText}";
    public BitmapSource GetFramePreview(int frameIndex)
    {
        if (frameIndex < 0 || frameIndex >= FrameCount)
        {
            throw new ArgumentOutOfRangeException(nameof(frameIndex));
        }

        if (_framePreviews.TryGetValue(frameIndex, out BitmapSource? cached))
        {
            return cached;
        }

        BitmapSource loaded = WpfImageFramePreviewLoader.Load(OriginalPath, 1400, frameIndex);
        _framePreviews[frameIndex] = loaded;
        return loaded;
    }

    public string Sha256Short => Asset.Fingerprint.Sha256[..12];

    internal void AcceptRevision(SourceAsset asset, BitmapSource preview)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(preview);
        if (asset.Id != Asset.Id)
        {
            throw new InvalidOperationException("接受源图新版本时必须保留工程内源图 ID。");
        }

        _asset = asset;
        _preview = preview;
        _framePreviews.Clear();
        _framePreviews[0] = preview;
        OnPropertyChanged(nameof(Asset));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(OriginalPath));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DimensionsText));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(FileSizeText));
        OnPropertyChanged(nameof(DpiText));
        OnPropertyChanged(nameof(DetailsText));
        OnPropertyChanged(nameof(Sha256Short));
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
