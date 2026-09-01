using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public sealed record FigureMeasurementOverlayMarker(double X, double Y);

/// <summary>
/// Presentation projection of a typed <see cref="MeasurementOverlayObject"/>.
/// Its preview geometry is calculated by the same Core mapper used by all exporters.
/// </summary>
public sealed class FigureMeasurementOverlayViewModel : ObservableObject
{
    private readonly MeasurementOverlayObject _scientificObject;
    private FigurePanelViewModel _panel;
    private FigureMeasurementOverlayGeometry _geometry;

    public FigureMeasurementOverlayViewModel(
        MeasurementOverlayObject scientificObject,
        FigurePanelViewModel panel)
    {
        _scientificObject = scientificObject ?? throw new ArgumentNullException(nameof(scientificObject));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _geometry = FigureMeasurementOverlayMapper.Map(_scientificObject, ToExportPanel(_panel));
    }

    public MeasurementOverlayObject ScientificObject => _scientificObject;

    public Guid Id => _scientificObject.Id;

    public Guid MeasurementId => _scientificObject.MeasurementId;

    public Guid SourceAssetId => _scientificObject.AssetId ?? Guid.Empty;

    public long SourceRevision => _scientificObject.SourceRevision ?? 0;

    public Guid PanelId => _scientificObject.PanelId ?? Guid.Empty;

    public ScientificMeasurementKind Kind => _scientificObject.SourceGeometry.Kind;

    public bool IsVisible => _scientificObject.IsVisible;

    public int ZIndex => _scientificObject.ZIndex;

    public Visibility LengthVisibility => IsVisible && Kind == ScientificMeasurementKind.Length
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility AngleVisibility => IsVisible && Kind == ScientificMeasurementKind.Angle
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RectangleVisibility => IsVisible && Kind == ScientificMeasurementKind.RectangleRoi
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility CircleVisibility => IsVisible && Kind == ScientificMeasurementKind.CircleRoi
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PolylineVisibility => IsVisible && Kind == ScientificMeasurementKind.Polyline
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility MarkerVisibility => IsVisible && _scientificObject.Style.ShowMarkers
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LabelVisibility => IsVisible && _scientificObject.Style.ShowLabel
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double PointAX => _geometry.PointA.X;

    public double PointAY => _geometry.PointA.Y;

    public double PointBX => _geometry.PointB.X;

    public double PointBY => _geometry.PointB.Y;

    public double PointCX => (_geometry.PointC ?? _geometry.PointB).X;

    public double PointCY => (_geometry.PointC ?? _geometry.PointB).Y;

    public double RectangleX => Math.Min(PointAX, PointBX);

    public double RectangleY => Math.Min(PointAY, PointBY);

    public double RectangleWidth => Math.Abs(PointBX - PointAX);

    public double RectangleHeight => Math.Abs(PointBY - PointAY);

    public double LabelX => _geometry.LabelAnchor.X;

    public double LabelY => _geometry.LabelAnchor.Y;

    public string Label => FigureMeasurementOverlayMapper.CreateLabel(_scientificObject);

    public string LabelFontFamily => _scientificObject.Style.LabelFontFamily;

    public double LabelFontSizePixels => _scientificObject.Style.LabelFontSizePt / 72.0 * _panel.FigureDpi;

    public FontWeight LabelFontWeight => _scientificObject.Style.LabelIsBold
        ? FontWeights.Bold
        : FontWeights.Normal;

    public Brush StrokeBrush => CreateBrush(_scientificObject.Style.StrokeColor, Colors.DeepSkyBlue);

    public Brush FillBrush
    {
        get
        {
            Color color = ParseColor(_scientificObject.Style.FillColor, Colors.DeepSkyBlue);
            color.A = (byte)Math.Round(color.A * _scientificObject.Style.FillOpacityPercent / 100.0);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public Brush MarkerStrokeBrush => CreateBrush(_scientificObject.Style.MarkerStrokeColor, Colors.DeepSkyBlue);

    public Brush MarkerFillBrush => CreateBrush(_scientificObject.Style.MarkerFillColor, Colors.Black);

    public Brush LabelBrush => CreateBrush(_scientificObject.Style.LabelColor, Colors.DeepSkyBlue);

    public double StrokeWidthPixels => Math.Max(0.25, _geometry.StrokeWidthPixels);

    public double MarkerSizePixels => Math.Max(2, _geometry.MarkerSizePixels);

    public double MarkerOffsetPixels => MarkerSizePixels / 2;

    public DoubleCollection? StrokeDashArray => _scientificObject.Style.LineStyle switch
    {
        "dash" => Freeze([5, 3]),
        "dot" => Freeze([1, 2]),
        "dash-dot" => Freeze([5, 2, 1, 2]),
        _ => null,
    };

    public PointCollection AnglePoints
    {
        get
        {
            var points = new PointCollection
            {
                new Point(PointAX, PointAY),
                new Point(PointBX, PointBY),
                new Point(PointCX, PointCY),
            };
            points.Freeze();
            return points;
        }
    }
    public PointCollection PolylinePoints
    {
        get
        {
            var points = new PointCollection(_geometry.PathPoints.Count);
            foreach (MeasurementPoint point in _geometry.PathPoints)
            {
                points.Add(new Point(point.X, point.Y));
            }
            points.Freeze();
            return points;
        }
    }

    public IReadOnlyList<FigureMeasurementOverlayMarker> Markers => GetMarkers()
        .Select(point => new FigureMeasurementOverlayMarker(
            point.X - MarkerOffsetPixels,
            point.Y - MarkerOffsetPixels))
        .ToArray();

    public FigureMeasurementOverlayExportItem CreateExportItem() => new(_scientificObject);

    public void RefreshLayout(FigurePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.Id != PanelId)
        {
            return;
        }

        _panel = panel;
        _geometry = FigureMeasurementOverlayMapper.Map(_scientificObject, ToExportPanel(panel));
        OnPropertyChanged(nameof(PointAX));
        OnPropertyChanged(nameof(PointAY));
        OnPropertyChanged(nameof(PointBX));
        OnPropertyChanged(nameof(PointBY));
        OnPropertyChanged(nameof(PointCX));
        OnPropertyChanged(nameof(PointCY));
        OnPropertyChanged(nameof(RectangleX));
        OnPropertyChanged(nameof(RectangleY));
        OnPropertyChanged(nameof(RectangleWidth));
        OnPropertyChanged(nameof(RectangleHeight));
        OnPropertyChanged(nameof(LabelX));
        OnPropertyChanged(nameof(LabelY));
        OnPropertyChanged(nameof(LabelFontSizePixels));
        OnPropertyChanged(nameof(StrokeWidthPixels));
        OnPropertyChanged(nameof(MarkerSizePixels));
        OnPropertyChanged(nameof(MarkerOffsetPixels));
        OnPropertyChanged(nameof(AnglePoints));
        OnPropertyChanged(nameof(PolylinePoints));
        OnPropertyChanged(nameof(Markers));
    }

    private IReadOnlyList<MeasurementPoint> GetMarkers() => Kind switch
    {
        ScientificMeasurementKind.Angle when _geometry.PointC is MeasurementPoint pointC =>
            [_geometry.PointA, _geometry.PointB, pointC],
        ScientificMeasurementKind.Polyline => _geometry.PathPoints,
        _ => [_geometry.PointA, _geometry.PointB],
    };

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

    private static DoubleCollection Freeze(double[] values)
    {
        var collection = new DoubleCollection(values);
        collection.Freeze();
        return collection;
    }

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
