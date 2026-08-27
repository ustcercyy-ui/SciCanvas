using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

/// <summary>One additional physical scale bar on a panel. Calibration remains owned by the panel.</summary>
public sealed class FigureAdditionalScaleBarViewModel : ObservableObject
{
    private double _physicalLength;
    private string _unit;
    private bool _showLabel = true;
    private bool _isVisible = true;
    private ScaleBarAnchor _anchor;
    private FigurePanelViewModel? _panel;

    public FigureAdditionalScaleBarViewModel(
        double physicalLength,
        string unit,
        ScaleBarAnchor anchor = ScaleBarAnchor.BottomRight,
        bool showLabel = true,
        bool isVisible = true,
        Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        _physicalLength = physicalLength;
        _unit = unit?.Trim() ?? string.Empty;
        _anchor = anchor;
        _showLabel = showLabel;
        _isVisible = isVisible;
        RemoveCommand = new RelayCommand(
            () => _panel?.RemoveAdditionalScaleBar(this),
            () => _panel is { IsLocked: false });
    }

    public event EventHandler? Changed;

    public RelayCommand RemoveCommand { get; }

    internal void Attach(FigurePanelViewModel panel)
    {
        _panel = panel;
        RemoveCommand.NotifyCanExecuteChanged();
        RefreshLayout();
    }

    public bool HasRenderablePreview => IsVisible && Geometry is not null;

    public double PreviewX => Geometry?.Left ?? 0;

    public double PreviewY => (Geometry?.Y ?? 0) - EffectiveThicknessPixels / 2;

    public double PreviewWidth => Geometry is { } geometry ? geometry.Right - geometry.Left : 0;

    public double LabelPreviewY => Geometry?.LabelTop ?? 0;

    public Brush EffectiveBrush => _panel?.EffectiveScaleBarBrush ?? Brushes.White;

    public Brush EffectiveLabelBrush => _panel?.EffectiveScaleBarLabelBrush ?? Brushes.White;

    public string EffectiveFontFamily => _panel?.EffectiveScaleBarFontFamily ?? "Arial";

    public double EffectiveFontSizePixels => _panel?.EffectiveScaleBarFontSizePixels ?? 12;

    public FontWeight EffectiveFontWeight => _panel?.EffectiveScaleBarLabelFontWeight ?? FontWeights.Bold;

    public double EffectiveThicknessPixels => _panel?.EffectiveScaleBarThicknessPixels ?? 2;
    public Guid Id { get; }

    public IReadOnlyList<string> AvailableUnits => ScientificLengthUnits.Supported;

    public IReadOnlyList<ScaleBarAnchor> AvailableAnchors { get; } =
        [ScaleBarAnchor.BottomRight, ScaleBarAnchor.BottomLeft, ScaleBarAnchor.TopRight, ScaleBarAnchor.TopLeft];

    public double PhysicalLength
    {
        get => _physicalLength;
        set
        {
            if (SetProperty(ref _physicalLength, value))
            {
                NotifyChanged();
            }
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _unit, normalized))
            {
                NotifyChanged();
            }
        }
    }

    public bool ShowLabel
    {
        get => _showLabel;
        set
        {
            if (SetProperty(ref _showLabel, value))
            {
                NotifyChanged();
            }
        }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (SetProperty(ref _isVisible, value))
            {
                NotifyChanged();
            }
        }
    }

    public ScaleBarAnchor Anchor
    {
        get => _anchor;
        set
        {
            if (SetProperty(ref _anchor, value))
            {
                NotifyChanged();
            }
        }
    }

    public string Label => new ScientificLength(PhysicalLength, Unit).DisplayText;

    internal FigureScaleBarExportSpec ToExportSpec(double unitsPerSourcePixel, string calibrationUnit) => new(
        unitsPerSourcePixel,
        PhysicalLength,
        Unit,
        ShowLabel,
        calibrationUnit,
        Anchor,
        Id);

    private FigureScaleBarGeometry? Geometry => _panel?.TryGetScaleBarPreviewGeometry(Id);

    internal void RefreshLayout()
    {
        OnPropertyChanged(nameof(HasRenderablePreview));
        OnPropertyChanged(nameof(PreviewX));
        OnPropertyChanged(nameof(PreviewY));
        OnPropertyChanged(nameof(PreviewWidth));
        OnPropertyChanged(nameof(LabelPreviewY));
        OnPropertyChanged(nameof(EffectiveBrush));
        OnPropertyChanged(nameof(EffectiveLabelBrush));
        OnPropertyChanged(nameof(EffectiveFontFamily));
        OnPropertyChanged(nameof(EffectiveFontSizePixels));
        OnPropertyChanged(nameof(EffectiveFontWeight));
        OnPropertyChanged(nameof(EffectiveThicknessPixels));
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(Label));
        RefreshLayout();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}