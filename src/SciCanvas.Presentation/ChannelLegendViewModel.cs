using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

/// <summary>Typed Presentation adapter for canonical ChannelLegendObject.</summary>
public sealed class ChannelLegendViewModel : ObservableObject
{
    private string _itemsText = "DAPI|#FF4FC3F7;GFP|#FF66BB6A";
    private string _fontFamily = "Arial";
    private double _fontSizePt = 7;
    private bool _isBold;
    private string _textColor = "#FFFFFFFF";
    private string _backgroundColor = "#FF000000";
    private double _backgroundOpacityPercent = 80;
    private string _borderColor = "#FFFFFFFF";
    private double _borderWidthPt = 1.25;
    private double _paddingPixels = 5;

    public event EventHandler? Changed;

    public string ItemsText { get => _itemsText; set => SetAndNotify(ref _itemsText, value?.Trim() ?? string.Empty); }

    public string FontFamily { get => _fontFamily; set => SetAndNotify(ref _fontFamily, value?.Trim() ?? string.Empty); }

    public double FontSizePt { get => _fontSizePt; set => SetAndNotify(ref _fontSizePt, value); }

    public bool IsBold { get => _isBold; set => SetAndNotify(ref _isBold, value); }

    public string TextColor { get => _textColor; set => SetAndNotify(ref _textColor, value?.Trim() ?? string.Empty); }

    public string BackgroundColor { get => _backgroundColor; set => SetAndNotify(ref _backgroundColor, value?.Trim() ?? string.Empty); }

    public double BackgroundOpacityPercent { get => _backgroundOpacityPercent; set => SetAndNotify(ref _backgroundOpacityPercent, value); }

    public string BorderColor { get => _borderColor; set => SetAndNotify(ref _borderColor, value?.Trim() ?? string.Empty); }

    public double BorderWidthPt { get => _borderWidthPt; set => SetAndNotify(ref _borderWidthPt, value); }

    public double PaddingPixels { get => _paddingPixels; set => SetAndNotify(ref _paddingPixels, value); }

    public IReadOnlyList<FigureChannelLegendEntry> Items
    {
        get
        {
            try
            {
                return ParseItems(ItemsText);
            }
            catch (FormatException)
            {
                return [];
            }
        }
    }

    public void Restore(
        string itemsText,
        string fontFamily,
        double fontSizePt,
        bool isBold,
        string textColor,
        string backgroundColor,
        double backgroundOpacityPercent,
        string borderColor,
        double borderWidthPt,
        double paddingPixels)
    {
        _itemsText = itemsText?.Trim() ?? string.Empty;
        _fontFamily = fontFamily?.Trim() ?? string.Empty;
        _fontSizePt = fontSizePt;
        _isBold = isBold;
        _textColor = textColor?.Trim() ?? string.Empty;
        _backgroundColor = backgroundColor?.Trim() ?? string.Empty;
        _backgroundOpacityPercent = backgroundOpacityPercent;
        _borderColor = borderColor?.Trim() ?? string.Empty;
        _borderWidthPt = borderWidthPt;
        _paddingPixels = paddingPixels;
        OnPropertyChanged(string.Empty);
        NotifyChanged();
    }

    public ChannelLegendObject CreateModel(Guid id)
    {
        IReadOnlyList<FigureChannelLegendEntry> items = ParseItems(ItemsText);
        return new ChannelLegendObject
        {
            Id = id,
            Items = items.Select(item => new ChannelLegendItem(
                item.ChannelId,
                item.Label,
                item.Color)).ToArray(),
            TextStyle = new TextStyle(FontFamily, FontSizePt, IsBold, TextColor),
            ContainerStyle = new ShapeStyle(
                BorderColor,
                BackgroundColor,
                BackgroundOpacityPercent,
                BorderWidthPt),
            PaddingPixels = PaddingPixels,
        }.EnsureValid();
    }

    public FigureChannelLegendExportSpec CreateExportSpec()
    {
        ChannelLegendObject model = CreateModel(Guid.NewGuid());
        return new FigureChannelLegendExportSpec(
            model.Items.Select(item => new FigureChannelLegendEntry(
                item.Label,
                item.Color,
                item.ChannelId)).ToArray(),
            model.TextStyle.FontFamily,
            model.TextStyle.FontSizePt,
            model.TextStyle.IsBold,
            model.TextStyle.Color,
            model.ContainerStyle.FillColor,
            model.ContainerStyle.FillOpacityPercent,
            model.ContainerStyle.StrokeColor,
            model.ContainerStyle.StrokeWidthPt,
            model.PaddingPixels).EnsureValid();
    }

    private static IReadOnlyList<FigureChannelLegendEntry> ParseItems(string value)
    {
        var items = new List<FigureChannelLegendEntry>();
        foreach (string token in value.Split(
                     ';',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = token.Split('|', StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                items.Add(new FigureChannelLegendEntry(parts[0], parts[1]));
                continue;
            }

            if (parts.Length == 3 && Guid.TryParse(parts[0], out Guid channelId))
            {
                items.Add(new FigureChannelLegendEntry(parts[1], parts[2], channelId));
                continue;
            }

            throw new FormatException(
                "Channel Legend items 必须使用 label|color 或 channelId|label|color；分隔。");
        }

        foreach (FigureChannelLegendEntry item in items)
        {
            item.EnsureValid();
        }

        return items;
    }

    private void SetAndNotify<T>(ref T field, T value)
    {
        if (SetProperty(ref field, value))
        {
            NotifyChanged();
        }
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(Items));
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
