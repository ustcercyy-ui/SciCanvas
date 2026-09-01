using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public sealed class FigurePlotPanelViewModel : ObservableObject
{
    private PlotObject _plot;
    private TabularDataAsset _dataAsset;
    private long _x;
    private long _y;
    private long _width;
    private long _height;
    private string _label;
    private bool _isVisible = true;
    private bool _isLocked;
    private bool _isSelected;
    private int _zIndex;
    private StyleOverride? _styleOverride;
    private FigurePlotTypographyOverride? _typographyOverride;
    private FigureGlobalStyle _inheritedStyle = FigureGlobalStyle.Default;
    private readonly int _figureDpi;

    public FigurePlotPanelViewModel(
        PlotObject plot,
        TabularDataAsset dataAsset,
        PixelRect64 destination,
        string label,
        int zIndex,
        int figureDpi,
        Guid? id = null)
    {
        Id = id is { } value && value != Guid.Empty ? value : Guid.NewGuid();
        _plot = plot ?? throw new ArgumentNullException(nameof(plot));
        _dataAsset = dataAsset ?? throw new ArgumentNullException(nameof(dataAsset));
        plot.EnsureValid(dataAsset);
        _x = destination.X;
        _y = destination.Y;
        _width = destination.Width;
        _height = destination.Height;
        _label = label?.Trim() ?? string.Empty;
        _zIndex = zIndex;
        _figureDpi = figureDpi > 0 ? figureDpi : throw new ArgumentOutOfRangeException(nameof(figureDpi));
    }

    public Guid Id { get; }
    public Guid PlotId => Plot.Id;
    public Guid DataAssetId => DataAsset.Id;
    public long SourceRevision => DataAsset.SourceRevision;

    public PlotObject Plot
    {
        get => _plot;
        private set
        {
            if (SetProperty(ref _plot, value))
            {
                OnPropertyChanged(nameof(PlotId));
                OnPropertyChanged(nameof(PlotName));
                OnPropertyChanged(nameof(PlotTypeText));
                OnPropertyChanged(nameof(SourceRevision));
                OnPropertyChanged(nameof(PreviewPlot));
            }
        }
    }

    public TabularDataAsset DataAsset
    {
        get => _dataAsset;
        private set
        {
            if (SetProperty(ref _dataAsset, value))
            {
                OnPropertyChanged(nameof(DataAssetId));
                OnPropertyChanged(nameof(DataAssetName));
                OnPropertyChanged(nameof(SourceRevision));
            }
        }
    }

    public string PlotName => Plot.Name;
    public string DataAssetName => DataAsset.Name;
    public string PlotTypeText => Plot.PlotType.ToString();
    public PlotObject PreviewPlot
    {
        get
        {
            PlotTypography typography = FigurePlotPanelExportItem.ResolveTypography(
                _inheritedStyle,
                StyleOverride,
                TypographyOverride).Value;
            return Plot with { Typography = typography };
        }
    }

    public long X { get => _x; set => SetProperty(ref _x, Math.Max(0, value)); }
    public long Y { get => _y; set => SetProperty(ref _y, Math.Max(0, value)); }
    public long Width { get => _width; set => SetProperty(ref _width, Math.Max(120, value)); }
    public long Height { get => _height; set => SetProperty(ref _height, Math.Max(100, value)); }
    public PixelRect64 DestinationRect => new(X, Y, Width, Height);

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value?.Trim() ?? string.Empty);
    }

    public bool IsVisible { get => _isVisible; set => SetProperty(ref _isVisible, value); }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(ResizeHandleVisibility));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(ResizeHandleVisibility));
            }
        }
    }

    public int ZIndex { get => _zIndex; set => SetProperty(ref _zIndex, value); }
    public StyleOverride? StyleOverride => _styleOverride;
    public FigurePlotTypographyOverride? TypographyOverride => _typographyOverride;
    public Visibility ResizeHandleVisibility =>
        IsSelected && !IsLocked ? Visibility.Visible : Visibility.Collapsed;

    public FigureGlobalStyle EffectivePanelStyle =>
        _inheritedStyle.ResolvePanelOverride(StyleOverride);
    public string EffectivePanelLabelFontFamily =>
        EffectivePanelStyle.EffectivePanelLabelFontFamily;
    public double EffectivePanelLabelFontSizePixels =>
        Math.Max(12, EffectivePanelStyle.EffectivePanelLabelFontSizePt / 72.0 * _figureDpi);
    public FontWeight EffectivePanelLabelFontWeight =>
        EffectivePanelStyle.PanelLabelIsBold ? FontWeights.Bold : FontWeights.Normal;
    public Brush EffectivePanelLabelBrush =>
        CreateBrush(EffectivePanelStyle.EffectivePanelLabelTextColor);

    public FigurePlotPanelExportItem CreateExportItem(bool showLabel) =>
        FigurePlotPanelExportItem.Create(
            Plot,
            DataAsset,
            DestinationRect,
            showLabel ? Label : string.Empty,
            IsVisible,
            StyleOverride,
            TypographyOverride,
            ZIndex,
            Id);

    public void UpdatePlot(PlotObject plot, TabularDataAsset dataAsset)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(dataAsset);
        if (plot.Id != PlotId)
        {
            throw new InvalidOperationException("只能用相同 ID 的 Plot 更新 Figure Plot panel。");
        }
        plot.EnsureValid(dataAsset);
        DataAsset = dataAsset;
        Plot = plot;
    }

    public void RestoreState(
        bool isVisible,
        bool isLocked,
        StyleOverride? styleOverride,
        FigurePlotTypographyOverride? typographyOverride)
    {
        styleOverride?.EnsureValid();
        typographyOverride?.EnsureValid();
        _styleOverride = styleOverride;
        _typographyOverride = typographyOverride;
        IsVisible = isVisible;
        IsLocked = isLocked;
        NotifyStyleChanged();
    }

    internal void UpdateInheritedStyle(FigureGlobalStyle style)
    {
        _inheritedStyle = style ?? FigureGlobalStyle.Default;
        NotifyStyleChanged();
    }

    private void NotifyStyleChanged()
    {
        OnPropertyChanged(nameof(StyleOverride));
        OnPropertyChanged(nameof(TypographyOverride));
        OnPropertyChanged(nameof(EffectivePanelStyle));
        OnPropertyChanged(nameof(EffectivePanelLabelFontFamily));
        OnPropertyChanged(nameof(EffectivePanelLabelFontSizePixels));
        OnPropertyChanged(nameof(EffectivePanelLabelFontWeight));
        OnPropertyChanged(nameof(EffectivePanelLabelBrush));
        OnPropertyChanged(nameof(PreviewPlot));
    }

    private static Brush CreateBrush(string color)
    {
        ScientificColorValue value = ScientificStyleColor.TryParseColor(color, out ScientificColorValue parsed)
            ? parsed
            : new ScientificColorValue(255, 17, 17, 17);
        var brush = new SolidColorBrush(Color.FromArgb(value.Alpha, value.Red, value.Green, value.Blue));
        brush.Freeze();
        return brush;
    }
}
