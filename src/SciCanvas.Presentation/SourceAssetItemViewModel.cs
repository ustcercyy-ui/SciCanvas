using System.Windows.Media.Imaging;
using SciCanvas.Core.Sources;

namespace SciCanvas.Presentation;

public sealed class SourceAssetItemViewModel : ObservableObject
{
    private SourceAsset _asset;
    private BitmapSource _preview;

    public SourceAssetItemViewModel(SourceAsset asset, BitmapSource preview)
    {
        _asset = asset ?? throw new ArgumentNullException(nameof(asset));
        _preview = preview ?? throw new ArgumentNullException(nameof(preview));
    }

    public SourceAsset Asset => _asset;

    public BitmapSource Preview => _preview;

    public string DisplayName => Asset.DisplayName;

    public string OriginalPath => Asset.OriginalPath;

    public long Width => Asset.Metadata.PixelSize.Width;

    public long Height => Asset.Metadata.PixelSize.Height;

    public string DimensionsText => $"{Width:N0} × {Height:N0} px";

    public string FormatText => $"{Asset.Metadata.BitsPerChannel}-bit · {Asset.Metadata.Channels}通道";

    public string FileSizeText => FormatBytes(Asset.Fingerprint.ByteLength);

    public string DetailsText => $"{FormatText} · {FileSizeText}";

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
        OnPropertyChanged(nameof(Asset));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(OriginalPath));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(DimensionsText));
        OnPropertyChanged(nameof(FormatText));
        OnPropertyChanged(nameof(FileSizeText));
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
