using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Science;

namespace SciCanvas.Presentation;

public sealed class ScientificMeasurementViewModel : ObservableObject
{
    private MeasurementPoint _pointA;
    private MeasurementPoint _pointB;
    private MeasurementPoint? _pointC;
    private SpatialCalibration? _calibration;
    private bool _isSelected;
    private string _strokeColor = "#FF22C7E8";
    private double _strokeWidthPixels = 3;
    private string _lineStyle = "solid";
    private double _markerSizePixels = 18;
    private bool _showMarkers = true;
    private bool _showLabel = true;
    private double _fillOpacityPercent = 8;
    private bool _isVisible = true;
    private bool _isLocked;
    private int _number;
    private readonly List<MeasurementPoint> _pathPoints;

    public ScientificMeasurementViewModel(
        Guid id,
        Guid sourceAssetId,
        string sourceName,
        ScientificMeasurementKind kind,
        MeasurementPoint pointA,
        MeasurementPoint pointB,
        MeasurementPoint? pointC,
        SpatialCalibration? calibration,
        int number,
        IReadOnlyList<MeasurementPoint>? pathPoints = null)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        SourceAssetId = sourceAssetId;
        SourceName = sourceName;
        Kind = kind;
        _pointA = pointA;
        _pointB = pointB;
        _pointC = pointC;
        _calibration = calibration;
        _number = number;
        _pathPoints = kind == ScientificMeasurementKind.Polyline
            ? (pathPoints is { Count: >= 2 } ? pathPoints.ToList() : [pointA, pointB])
            : [];
    }

    public event EventHandler? Changed;

    public Guid Id { get; }

    public Guid SourceAssetId { get; }

    public string SourceName { get; }

    public ScientificMeasurementKind Kind { get; }

    public int Number
    {
        get => _number;
        set => SetProperty(ref _number, Math.Max(1, value));
    }

    public double X1 => _pointA.X;
    public double Y1 => _pointA.Y;
    public double X2 => _pointB.X;
    public double Y2 => _pointB.Y;
    public double X3 => _pointC?.X ?? _pointB.X;
    public double Y3 => _pointC?.Y ?? _pointB.Y;

    public bool IsLength => Kind == ScientificMeasurementKind.Length;
    public bool IsAngle => Kind == ScientificMeasurementKind.Angle;
    public bool IsRectangle => Kind == ScientificMeasurementKind.RectangleRoi;
    public bool IsCircle => Kind == ScientificMeasurementKind.CircleRoi;
    public bool IsPolyline => Kind == ScientificMeasurementKind.Polyline;

    public Visibility LengthVisibility => IsVisible && IsLength ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AngleVisibility => IsVisible && IsAngle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RectangleVisibility => IsVisible && IsRectangle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CircleVisibility => IsVisible && IsCircle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PolylineVisibility => IsVisible && IsPolyline ? Visibility.Visible : Visibility.Collapsed;

    public PointCollection PolylinePoints
    {
        get
        {
            var points = new PointCollection(_pathPoints.Count);
            foreach (MeasurementPoint point in _pathPoints)
            {
                points.Add(new Point(point.X, point.Y));
            }

            points.Freeze();
            return points;
        }
    }

    public IReadOnlyList<MeasurementPoint> PathPoints => _pathPoints.ToArray();

    public double RectangleX => Math.Min(X1, X2);
    public double RectangleY => Math.Min(Y1, Y2);
    public double RectangleWidth => Math.Abs(X2 - X1);
    public double RectangleHeight => Math.Abs(Y2 - Y1);

    public double SelectionHandleSizePixels => Math.Max(24, MarkerSizePixels + 6);

    public double PointAHandleX => X1 - SelectionHandleSizePixels / 2;

    public double PointAHandleY => Y1 - SelectionHandleSizePixels / 2;

    public double PointBHandleX => X2 - SelectionHandleSizePixels / 2;

    public double PointBHandleY => Y2 - SelectionHandleSizePixels / 2;

    public double PointCHandleX => X3 - SelectionHandleSizePixels / 2;

    public double PointCHandleY => Y3 - SelectionHandleSizePixels / 2;

    public double PointAMarkerX => X1 - MarkerSizePixels / 2;

    public double PointAMarkerY => Y1 - MarkerSizePixels / 2;

    public double PointBMarkerX => X2 - MarkerSizePixels / 2;

    public double PointBMarkerY => Y2 - MarkerSizePixels / 2;

    public double PointCMarkerX => X3 - MarkerSizePixels / 2;

    public double PointCMarkerY => Y3 - MarkerSizePixels / 2;

    public double LabelX => Kind switch
    {
        ScientificMeasurementKind.Angle => X2 + 14,
        ScientificMeasurementKind.RectangleRoi => RectangleX + 10,
        ScientificMeasurementKind.CircleRoi => RectangleX + 10,
        ScientificMeasurementKind.Polyline => X2 + 10,
        _ => (X1 + X2) / 2 + 10,
    };

    public double LabelY => Kind switch
    {
        ScientificMeasurementKind.Angle => Math.Max(0, Y2 - 34),
        ScientificMeasurementKind.RectangleRoi => Math.Max(0, RectangleY - 32),
        ScientificMeasurementKind.CircleRoi => Math.Max(0, RectangleY - 32),
        ScientificMeasurementKind.Polyline => Math.Max(0, Y2 - 32),
        _ => Math.Max(0, (Y1 + Y2) / 2 - 32),
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(OverlayStroke));
                OnPropertyChanged(nameof(OverlayThickness));
                OnPropertyChanged(nameof(SelectionHandleVisibility));
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
                NotifyVisibilityChanged();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(SelectionHandleVisibility));
                OnPropertyChanged(nameof(LayerStateText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string StrokeColor
    {
        get => _strokeColor;
        set
        {
            string normalized = string.IsNullOrWhiteSpace(value) ? "#FF22C7E8" : value.Trim();
            if (SetProperty(ref _strokeColor, normalized))
            {
                OnPropertyChanged(nameof(OverlayStroke));
                OnPropertyChanged(nameof(OverlayFill));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string LineStyle
    {
        get => _lineStyle;
        set
        {
            string normalized = NormalizeLineStyle(value);
            if (SetProperty(ref _lineStyle, normalized))
            {
                OnPropertyChanged(nameof(StrokeDashArray));
                OnPropertyChanged(nameof(LineStyleText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string LineStyleText => LineStyle switch
    {
        "dash" => "虚线",
        "dot" => "点线",
        "dash-dot" => "点划线",
        _ => "实线",
    };

    public DoubleCollection? StrokeDashArray
    {
        get
        {
            double[]? values = LineStyle switch
            {
                "dash" => [5, 3],
                "dot" => [1, 2],
                "dash-dot" => [5, 2, 1, 2],
                _ => null,
            };
            if (values is null)
            {
                return null;
            }

            var collection = new DoubleCollection(values);
            collection.Freeze();
            return collection;
        }
    }

    public double MarkerSizePixels
    {
        get => _markerSizePixels;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 8, 48) : 18;
            if (SetProperty(ref _markerSizePixels, normalized))
            {
                NotifyHandleGeometryChanged();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowMarkers
    {
        get => _showMarkers;
        set
        {
            if (SetProperty(ref _showMarkers, value))
            {
                OnPropertyChanged(nameof(MarkerVisibility));
                Changed?.Invoke(this, EventArgs.Empty);
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
                OnPropertyChanged(nameof(LabelVisibility));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double FillOpacityPercent
    {
        get => _fillOpacityPercent;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 0, 60) : 8;
            if (SetProperty(ref _fillOpacityPercent, normalized))
            {
                OnPropertyChanged(nameof(OverlayFill));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public Visibility MarkerVisibility => IsVisible && ShowMarkers
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LabelVisibility => IsVisible && ShowLabel
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility SelectionHandleVisibility => IsVisible && IsSelected && !IsLocked
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PointCMarkerVisibility => IsAngle && MarkerVisibility == Visibility.Visible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PointCHandleVisibility => IsAngle && SelectionHandleVisibility == Visibility.Visible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double StrokeWidthPixels
    {
        get => _strokeWidthPixels;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 1, 12) : 3;
            if (SetProperty(ref _strokeWidthPixels, normalized))
            {
                OnPropertyChanged(nameof(OverlayThickness));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public Brush OverlayStroke => IsSelected
        ? CreateBrush("#FFFFD740", Brushes.Gold)
        : CreateBrush(StrokeColor, Brushes.DeepSkyBlue);

    public double OverlayThickness => IsSelected ? StrokeWidthPixels + 2 : StrokeWidthPixels;

    public Brush OverlayFill
    {
        get
        {
            Color baseColor = TryParseColor(StrokeColor, out Color parsed)
                ? parsed
                : Color.FromRgb(34, 199, 232);
            baseColor.A = (byte)Math.Round(FillOpacityPercent / 100.0 * byte.MaxValue);
            var brush = new SolidColorBrush(baseColor);
            brush.Freeze();
            return brush;
        }
    }

    public string TypeText => Kind switch
    {
        ScientificMeasurementKind.Length => "长度",
        ScientificMeasurementKind.Angle => "角度",
        ScientificMeasurementKind.RectangleRoi => "矩形 ROI",
        ScientificMeasurementKind.CircleRoi => "圆形测量",
        ScientificMeasurementKind.Polyline => "折线",
        _ => Kind.ToString(),
    };

    public string LayerStateText => IsLocked
        ? $"{TypeText} {Number} · 已锁定"
        : $"{TypeText} {Number} · {LineStyleText}";

    public ScientificMeasurementVisualStyle VisualStyle => new()
    {
        StrokeColor = StrokeColor,
        StrokeWidthPixels = StrokeWidthPixels,
        LineStyle = LineStyle,
        MarkerSizePixels = MarkerSizePixels,
        ShowMarkers = ShowMarkers,
        ShowLabel = ShowLabel,
        FillOpacityPercent = FillOpacityPercent,
        IsVisible = IsVisible,
        IsLocked = IsLocked,
    };

    public ScientificMeasurement Measurement => new(
        Id,
        SourceAssetId,
        Kind,
        _pointA,
        _pointB,
        _pointC,
        $"{TypeText} {Number}",
        _pathPoints.Count == 0 ? null : _pathPoints.ToArray());

    public bool IsValid => Measurement.IsValid;

    public double? NumericValue => Measurement.PhysicalValue(_calibration);

    public string UnitText => Kind switch
    {
        ScientificMeasurementKind.Angle => "°",
        ScientificMeasurementKind.RectangleRoi when _calibration?.IsValid == true => $"{_calibration.Unit}²",
        ScientificMeasurementKind.CircleRoi when _calibration?.IsValid == true => _calibration.Unit,
        ScientificMeasurementKind.Length when _calibration?.IsValid == true => _calibration.Unit,
        ScientificMeasurementKind.Polyline when _calibration?.IsValid == true => _calibration.Unit,
        ScientificMeasurementKind.RectangleRoi => "px²",
        _ => "px",
    };

    public string ValueText
    {
        get
        {
            if (Kind == ScientificMeasurementKind.RectangleRoi &&
                Measurement.PhysicalRectangle(_calibration) is { } rectangle)
            {
                return $"{rectangle.Width:0.###} × {rectangle.Height:0.###} {_calibration!.Unit}";
            }

            if (Kind == ScientificMeasurementKind.CircleRoi)
            {
                double diameter = NumericValue ?? Measurement.PixelValue;
                return $"Ø {diameter:0.###} {UnitText}";
            }

            double value = NumericValue ?? Measurement.PixelValue;
            return $"{value:0.###} {UnitText}";
        }
    }

    public string PixelValueText => Kind switch
    {
        ScientificMeasurementKind.Angle => $"{Measurement.PixelValue:0.###}°",
        ScientificMeasurementKind.RectangleRoi => $"{Measurement.PixelValue:0.###} px²",
        ScientificMeasurementKind.CircleRoi => $"Ø {Measurement.PixelValue:0.###} px",
        ScientificMeasurementKind.Polyline => $"{Measurement.PixelValue:0.###} px · {_pathPoints.Count} points",
        _ => $"{Measurement.PixelValue:0.###} px",
    };

    public string AreaPerimeterText
    {
        get
        {
            if (Kind is not (ScientificMeasurementKind.RectangleRoi or ScientificMeasurementKind.CircleRoi))
            {
                return string.Empty;
            }

            double area = Measurement.PhysicalArea(_calibration) ?? Measurement.PixelArea;
            double perimeter = Measurement.PhysicalPerimeter(_calibration) ?? Measurement.PixelPerimeter;
            string lengthUnit = _calibration?.IsValid == true ? _calibration.Unit : "px";
            return $"Area {area:0.###} {lengthUnit}² · Perimeter {perimeter:0.###} {lengthUnit}";
        }
    }

    public string CsvValue => (NumericValue ?? Measurement.PixelValue)
        .ToString("0.######", CultureInfo.InvariantCulture);

    public void UpdatePointB(double x, double y)
    {
        _pointB = new MeasurementPoint(x, y);
        if (IsPolyline && _pathPoints.Count > 0)
        {
            _pathPoints[^1] = _pointB;
        }
        NotifyGeometryChanged();
    }

    public void UpdatePointA(double x, double y)
    {
        _pointA = new MeasurementPoint(x, y);
        if (IsPolyline && _pathPoints.Count > 0)
        {
            _pathPoints[0] = _pointA;
        }
        NotifyGeometryChanged();
    }

    public void UpdatePointC(double x, double y)
    {
        _pointC = new MeasurementPoint(x, y);
        NotifyGeometryChanged();
    }

    public void MoveBy(double deltaX, double deltaY, double sourceWidth, double sourceHeight)
    {
        if (IsLocked || !double.IsFinite(deltaX) || !double.IsFinite(deltaY) ||
            sourceWidth <= 0 || sourceHeight <= 0)
        {
            return;
        }

        IReadOnlyList<MeasurementPoint> points = IsPolyline
            ? _pathPoints
            : _pointC is MeasurementPoint pointC
                ? [_pointA, _pointB, pointC]
                : [_pointA, _pointB];
        double minimumX = points.Min(point => point.X);
        double maximumX = points.Max(point => point.X);
        double minimumY = points.Min(point => point.Y);
        double maximumY = points.Max(point => point.Y);
        double adjustedX = Math.Clamp(deltaX, -minimumX, Math.Max(0, sourceWidth - 1 - maximumX));
        double adjustedY = Math.Clamp(deltaY, -minimumY, Math.Max(0, sourceHeight - 1 - maximumY));
        if (Math.Abs(adjustedX) < 0.0001 && Math.Abs(adjustedY) < 0.0001)
        {
            return;
        }

        _pointA = Translate(_pointA, adjustedX, adjustedY);
        _pointB = Translate(_pointB, adjustedX, adjustedY);
        if (_pointC is MeasurementPoint third)
        {
            _pointC = Translate(third, adjustedX, adjustedY);
        }

        for (int index = 0; index < _pathPoints.Count; index++)
        {
            _pathPoints[index] = Translate(_pathPoints[index], adjustedX, adjustedY);
        }

        NotifyGeometryChanged();
    }

    public void RestoreVisualStyle(ScientificMeasurementVisualStyle? style)
    {
        style ??= ScientificMeasurementVisualStyle.Default;
        _strokeColor = string.IsNullOrWhiteSpace(style.StrokeColor)
            ? ScientificMeasurementVisualStyle.Default.StrokeColor
            : style.StrokeColor.Trim();
        _strokeWidthPixels = double.IsFinite(style.StrokeWidthPixels)
            ? Math.Clamp(style.StrokeWidthPixels, 1, 12)
            : ScientificMeasurementVisualStyle.Default.StrokeWidthPixels;
        _lineStyle = NormalizeLineStyle(style.LineStyle);
        _markerSizePixels = double.IsFinite(style.MarkerSizePixels)
            ? Math.Clamp(style.MarkerSizePixels, 8, 48)
            : ScientificMeasurementVisualStyle.Default.MarkerSizePixels;
        _showMarkers = style.ShowMarkers;
        _showLabel = style.ShowLabel;
        _fillOpacityPercent = double.IsFinite(style.FillOpacityPercent)
            ? Math.Clamp(style.FillOpacityPercent, 0, 60)
            : ScientificMeasurementVisualStyle.Default.FillOpacityPercent;
        _isVisible = style.IsVisible;
        _isLocked = style.IsLocked;
        OnPropertyChanged(string.Empty);
    }

    public void CommitPolylinePoint(double x, double y)
    {
        if (!IsPolyline || _pathPoints.Count < 2)
        {
            return;
        }

        MeasurementPoint point = new(x, y);
        _pathPoints[^1] = point;
        _pathPoints.Add(point);
        _pointB = point;
        NotifyGeometryChanged();
    }

    public void CompletePolyline(double x, double y)
    {
        if (!IsPolyline || _pathPoints.Count < 2)
        {
            return;
        }

        MeasurementPoint point = new(x, y);
        _pathPoints[^1] = point;
        while (_pathPoints.Count > 2 &&
               Distance(_pathPoints[^1], _pathPoints[^2]) < 0.001)
        {
            _pathPoints.RemoveAt(_pathPoints.Count - 1);
        }

        _pointB = _pathPoints[^1];
        NotifyGeometryChanged();
    }

    public void RefreshCalibration(SpatialCalibration? calibration)
    {
        _calibration = calibration;
        OnPropertyChanged(nameof(Measurement));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(NumericValue));
        OnPropertyChanged(nameof(UnitText));
        OnPropertyChanged(nameof(ValueText));
        OnPropertyChanged(nameof(PixelValueText));
        OnPropertyChanged(nameof(AreaPerimeterText));
        OnPropertyChanged(nameof(CsvValue));
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(X1));
        OnPropertyChanged(nameof(Y1));
        OnPropertyChanged(nameof(X2));
        OnPropertyChanged(nameof(Y2));
        OnPropertyChanged(nameof(X3));
        OnPropertyChanged(nameof(Y3));
        OnPropertyChanged(nameof(RectangleX));
        OnPropertyChanged(nameof(RectangleY));
        OnPropertyChanged(nameof(RectangleWidth));
        OnPropertyChanged(nameof(RectangleHeight));
        OnPropertyChanged(nameof(PolylinePoints));
        OnPropertyChanged(nameof(PathPoints));
        OnPropertyChanged(nameof(LabelX));
        OnPropertyChanged(nameof(LabelY));
        NotifyHandleGeometryChanged();
        OnPropertyChanged(nameof(LayerStateText));
        RefreshCalibration(_calibration);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyVisibilityChanged()
    {
        OnPropertyChanged(nameof(LengthVisibility));
        OnPropertyChanged(nameof(AngleVisibility));
        OnPropertyChanged(nameof(RectangleVisibility));
        OnPropertyChanged(nameof(CircleVisibility));
        OnPropertyChanged(nameof(PolylineVisibility));
        OnPropertyChanged(nameof(MarkerVisibility));
        OnPropertyChanged(nameof(LabelVisibility));
        OnPropertyChanged(nameof(SelectionHandleVisibility));
        OnPropertyChanged(nameof(PointCMarkerVisibility));
        OnPropertyChanged(nameof(PointCHandleVisibility));
        OnPropertyChanged(nameof(LayerStateText));
    }

    private void NotifyHandleGeometryChanged()
    {
        OnPropertyChanged(nameof(SelectionHandleSizePixels));
        OnPropertyChanged(nameof(PointAHandleX));
        OnPropertyChanged(nameof(PointAHandleY));
        OnPropertyChanged(nameof(PointBHandleX));
        OnPropertyChanged(nameof(PointBHandleY));
        OnPropertyChanged(nameof(PointCHandleX));
        OnPropertyChanged(nameof(PointCHandleY));
        OnPropertyChanged(nameof(PointAMarkerX));
        OnPropertyChanged(nameof(PointAMarkerY));
        OnPropertyChanged(nameof(PointBMarkerX));
        OnPropertyChanged(nameof(PointBMarkerY));
        OnPropertyChanged(nameof(PointCMarkerX));
        OnPropertyChanged(nameof(PointCMarkerY));
    }

    private static MeasurementPoint Translate(MeasurementPoint point, double deltaX, double deltaY) =>
        new(point.X + deltaX, point.Y + deltaY);

    private static string NormalizeLineStyle(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "dash" => "dash",
        "dot" => "dot",
        "dash-dot" => "dash-dot",
        _ => "solid",
    };

    private static Brush CreateBrush(string value, Brush fallback)
    {
        if (!TryParseColor(value, out Color color))
        {
            return fallback;
        }

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static bool TryParseColor(string value, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(value);
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or NotSupportedException or ArgumentException)
        {
            color = default;
            return false;
        }
    }

    private static double Distance(MeasurementPoint first, MeasurementPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}
