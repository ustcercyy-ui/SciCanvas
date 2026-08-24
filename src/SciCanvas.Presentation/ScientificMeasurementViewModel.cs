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

    public Visibility LengthVisibility => IsLength ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AngleVisibility => IsAngle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RectangleVisibility => IsRectangle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CircleVisibility => IsCircle ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PolylineVisibility => IsPolyline ? Visibility.Visible : Visibility.Collapsed;

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
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

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

    public string OverlayStroke => IsSelected ? "#FFFFD740" : StrokeColor;

    public double OverlayThickness => IsSelected ? StrokeWidthPixels + 2 : StrokeWidthPixels;

    public string TypeText => Kind switch
    {
        ScientificMeasurementKind.Length => "长度",
        ScientificMeasurementKind.Angle => "角度",
        ScientificMeasurementKind.RectangleRoi => "矩形 ROI",
        ScientificMeasurementKind.CircleRoi => "圆形测量",
        ScientificMeasurementKind.Polyline => "折线",
        _ => Kind.ToString(),
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
        NotifyGeometryChanged();
    }

    public void UpdatePointC(double x, double y)
    {
        _pointC = new MeasurementPoint(x, y);
        NotifyGeometryChanged();
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
        RefreshCalibration(_calibration);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static double Distance(MeasurementPoint first, MeasurementPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}
