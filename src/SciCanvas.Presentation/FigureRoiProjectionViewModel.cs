using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

/// <summary>
/// Figure preview for a canonical ROI reference. It owns no scientific geometry:
/// every refresh resolves the current <see cref="RoiObject"/> through its panel.
/// </summary>
public sealed class FigureRoiProjectionViewModel : ObservableObject
{
    private readonly RoiFigureProjectionObject _projection;
    private RoiObject _canonicalRoi;
    private FigurePanelViewModel _panel;
    private FigureRoiProjectionGeometry _geometry;

    public FigureRoiProjectionViewModel(
        RoiFigureProjectionObject projection,
        RoiObject canonicalRoi,
        FigurePanelViewModel panel)
    {
        _projection = projection ?? throw new ArgumentNullException(nameof(projection));
        _canonicalRoi = canonicalRoi ?? throw new ArgumentNullException(nameof(canonicalRoi));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _geometry = FigureRoiProjectionMapper.Map(CreateExportItem(), ToExportPanel(panel), panel.FigureDpi);
    }

    public RoiFigureProjectionObject Projection => _projection;

    public RoiObject CanonicalRoi => _canonicalRoi;

    public Guid Id => _projection.Id;

    public Guid RoiId => _projection.RoiId;

    public Guid PanelId => _projection.PanelId ?? Guid.Empty;

    public Guid AssetId => _projection.AssetId ?? Guid.Empty;

    public long SourceRevision => _projection.SourceRevision ?? 0;

    public int ZIndex => _projection.ZIndex;

    public bool IsVisible => _projection.IsVisible;

    public Visibility RectangleVisibility => IsVisible && _geometry.Kind == RoiGeometryKind.Rectangle
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EllipseVisibility => IsVisible && _geometry.Kind == RoiGeometryKind.Ellipse
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PolygonVisibility => IsVisible && _geometry.Kind == RoiGeometryKind.Polygon
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PolylineVisibility => IsVisible && _geometry.Kind == RoiGeometryKind.Polyline
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LabelVisibility => IsVisible && !string.IsNullOrWhiteSpace(_geometry.Style.Label)
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double ShapeX => _geometry.Points.Count >= 2
        ? Math.Min(_geometry.Points[0].X, _geometry.Points[1].X)
        : 0;

    public double ShapeY => _geometry.Points.Count >= 2
        ? Math.Min(_geometry.Points[0].Y, _geometry.Points[1].Y)
        : 0;

    public double ShapeWidth => _geometry.Points.Count >= 2
        ? Math.Abs(_geometry.Points[1].X - _geometry.Points[0].X)
        : 0;

    public double ShapeHeight => _geometry.Points.Count >= 2
        ? Math.Abs(_geometry.Points[1].Y - _geometry.Points[0].Y)
        : 0;

    public PointCollection Points
    {
        get
        {
            var points = new PointCollection(
                _geometry.Points.Select(point => new Point(point.X, point.Y)));
            points.Freeze();
            return points;
        }
    }

    public Brush StrokeBrush => CreateBrush(_geometry.Style.Shape.StrokeColor, Colors.DeepSkyBlue);

    public Brush FillBrush
    {
        get
        {
            Color color = ParseColor(_geometry.Style.Shape.FillColor, Colors.DeepSkyBlue);
            color.A = (byte)Math.Round(
                color.A * _geometry.Style.Shape.FillOpacityPercent / 100.0);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public double StrokeWidthPixels => Math.Max(0.25, _geometry.StrokeWidthPixels);

    public string Label => _geometry.Style.Label ?? string.Empty;

    public double LabelX => _geometry.LabelAnchor.X + 4;

    public double LabelY => _geometry.LabelAnchor.Y - _geometry.LabelFontSizePixels - 4;

    public string LabelFontFamily => _geometry.Style.LabelStyle.FontFamily;

    public double LabelFontSizePixels => _geometry.LabelFontSizePixels;

    public FontWeight LabelFontWeight => _geometry.Style.LabelStyle.IsBold
        ? FontWeights.Bold
        : FontWeights.Normal;

    public Brush LabelBrush => CreateBrush(_geometry.Style.LabelStyle.Color, Colors.DeepSkyBlue);

    public string ReferenceText =>
        $"ROI {RoiId.ToString("N")[..8]} · Panel {PanelId.ToString("N")[..8]} · revision {SourceRevision}";

    public FigureRoiProjectionExportItem CreateExportItem() => new(_projection, _canonicalRoi);

    public void RefreshLayout(FigurePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Id != PanelId)
        {
            return;
        }

        _panel = panel;
        _geometry = FigureRoiProjectionMapper.Map(CreateExportItem(), ToExportPanel(panel), panel.FigureDpi);
        NotifyGeometryChanged();
    }

    public void ValidateCanonicalRoi(RoiObject canonicalRoi)
    {
        ArgumentNullException.ThrowIfNull(canonicalRoi);
        if (canonicalRoi.Id != RoiId)
        {
            throw new InvalidOperationException(
                "ROI Figure Projection 只能验证其引用的 canonical ROI。");
        }

        _ = FigureRoiProjectionMapper.Map(
            new FigureRoiProjectionExportItem(_projection, canonicalRoi),
            ToExportPanel(_panel),
            _panel.FigureDpi);
    }

    public void ValidatePanel(FigurePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Id != PanelId)
        {
            return;
        }

        _ = FigureRoiProjectionMapper.Map(
            CreateExportItem(),
            ToExportPanel(panel),
            panel.FigureDpi);
    }

    public void UpdateCanonicalRoi(RoiObject canonicalRoi)
    {
        ArgumentNullException.ThrowIfNull(canonicalRoi);
        if (canonicalRoi.Id != RoiId)
        {
            throw new InvalidOperationException(
                "ROI Figure Projection 只能刷新其引用的 canonical ROI。");
        }

        FigureRoiProjectionGeometry mapped = FigureRoiProjectionMapper.Map(
            new FigureRoiProjectionExportItem(_projection, canonicalRoi),
            ToExportPanel(_panel),
            _panel.FigureDpi);
        _canonicalRoi = canonicalRoi.EnsureValid();
        _geometry = mapped;
        NotifyGeometryChanged();
        OnPropertyChanged(nameof(CanonicalRoi));
        OnPropertyChanged(nameof(ReferenceText));
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(RectangleVisibility));
        OnPropertyChanged(nameof(EllipseVisibility));
        OnPropertyChanged(nameof(PolygonVisibility));
        OnPropertyChanged(nameof(PolylineVisibility));
        OnPropertyChanged(nameof(LabelVisibility));
        OnPropertyChanged(nameof(ShapeX));
        OnPropertyChanged(nameof(ShapeY));
        OnPropertyChanged(nameof(ShapeWidth));
        OnPropertyChanged(nameof(ShapeHeight));
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(StrokeBrush));
        OnPropertyChanged(nameof(FillBrush));
        OnPropertyChanged(nameof(StrokeWidthPixels));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(LabelX));
        OnPropertyChanged(nameof(LabelY));
        OnPropertyChanged(nameof(LabelFontFamily));
        OnPropertyChanged(nameof(LabelFontSizePixels));
        OnPropertyChanged(nameof(LabelFontWeight));
        OnPropertyChanged(nameof(LabelBrush));
    }

    private static FigurePanelExportItem ToExportPanel(FigurePanelViewModel panel) => new(
        panel.Source.Asset,
        panel.SourceRect,
        panel.DestinationRect,
        panel.Label,
        panel.IsVisible,
        panel.CreateScaleBarExportSpec(),
        panel.Adjustments,
        panel.FrameIndex,
        panel.IsInset,
        panel.StyleOverride,
        panel.Id,
        SourceRevision: panel.Source.SourceRevision);

    private static Brush CreateBrush(string value, Color fallback)
    {
        var brush = new SolidColorBrush(ParseColor(value, fallback));
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}
