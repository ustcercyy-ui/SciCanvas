using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;

namespace SciCanvas.Presentation;

public enum FigureAnnotationKind
{
    Text,
    Arrow,
    Line,
    Rectangle,
    Ellipse,
}

public sealed partial class FigureAnnotationViewModel : ObservableObject
{
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private readonly int _dpi;
    private double _x;
    private double _y;
    private double _endX;
    private double _endY;
    private string _text;
    private string _color;
    private double _fontSizePt;
    private double _strokeWidthPt;
    private bool _isBold;
    private bool _isVisible = true;
    private bool _isLocked;
    private bool _isSelected;
    private int _zIndex;

    public FigureAnnotationViewModel(
        FigureAnnotationKind kind,
        int canvasWidth,
        int canvasHeight,
        int dpi,
        int zIndex,
        Guid? id = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);
        Kind = kind;
        Id = id ?? Guid.NewGuid();
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        _dpi = dpi;
        _zIndex = zIndex;
        _x = canvasWidth * 0.35;
        _y = canvasHeight * 0.25;
        _endX = canvasWidth * 0.65;
        _endY = kind is FigureAnnotationKind.Rectangle or FigureAnnotationKind.Ellipse
            ? canvasHeight * 0.45
            : canvasHeight * 0.25;
        _text = kind == FigureAnnotationKind.Text ? "文字标注" : string.Empty;
        _color = kind == FigureAnnotationKind.Text ? "#FF111111" : "#FFE53935";
        _fontSizePt = 7;
        _strokeWidthPt = 1.25;
    }

    public Guid Id { get; }

    public FigureAnnotationKind Kind { get; }

    public string KindKey => Kind switch
    {
        FigureAnnotationKind.Text => "text",
        FigureAnnotationKind.Arrow => "arrow",
        FigureAnnotationKind.Line => "line",
        FigureAnnotationKind.Rectangle => "rectangle",
        FigureAnnotationKind.Ellipse => "ellipse",
        _ => throw new InvalidOperationException("不支持的标注类型。"),
    };

    public string KindDisplayName => Kind switch
    {
        FigureAnnotationKind.Text => "文字",
        FigureAnnotationKind.Arrow => "箭头",
        FigureAnnotationKind.Line => "直线",
        FigureAnnotationKind.Rectangle => "矩形",
        FigureAnnotationKind.Ellipse => "椭圆",
        _ => "标注",
    };

    public Visibility TextVisibility => Kind == FigureAnnotationKind.Text
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ArrowVisibility => Kind == FigureAnnotationKind.Arrow
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility LineVisibility => Kind == FigureAnnotationKind.Line
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility RectangleVisibility => Kind == FigureAnnotationKind.Rectangle
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EllipseVisibility => Kind == FigureAnnotationKind.Ellipse
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ShapeVisibility => Kind is FigureAnnotationKind.Rectangle or FigureAnnotationKind.Ellipse
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EndpointVisibility => Kind == FigureAnnotationKind.Text
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility StrokeVisibility => Kind == FigureAnnotationKind.Text
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string EndXLabel => Kind is FigureAnnotationKind.Arrow or FigureAnnotationKind.Line ? "终点 X" : "右下 X";

    public string EndYLabel => Kind is FigureAnnotationKind.Arrow or FigureAnnotationKind.Line ? "终点 Y" : "右下 Y";

    public double X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value))
            {
                NotifyGeometryChanged();
            }
        }
    }

    public double Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value))
            {
                NotifyGeometryChanged();
            }
        }
    }

    public double EndX
    {
        get => _endX;
        set
        {
            if (SetProperty(ref _endX, value))
            {
                NotifyGeometryChanged();
            }
        }
    }

    public double EndY
    {
        get => _endY;
        set
        {
            if (SetProperty(ref _endY, value))
            {
                NotifyGeometryChanged();
            }
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (SetProperty(ref _text, value ?? string.Empty))
            {
                NotifyValidationChanged();
            }
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _color, normalized))
            {
                OnPropertyChanged(nameof(PreviewBrush));
                NotifyValidationChanged();
            }
        }
    }

    public double FontSizePt
    {
        get => _fontSizePt;
        set
        {
            if (SetProperty(ref _fontSizePt, value))
            {
                OnPropertyChanged(nameof(FontSizePixels));
                NotifyValidationChanged();
            }
        }
    }

    public double StrokeWidthPt
    {
        get => _strokeWidthPt;
        set
        {
            if (SetProperty(ref _strokeWidthPt, value))
            {
                OnPropertyChanged(nameof(StrokeWidthPixels));
                OnPropertyChanged(nameof(SelectionStrokeWidthPixels));
                OnPropertyChanged(nameof(ArrowGeometry));
                NotifyValidationChanged();
            }
        }
    }

    public bool IsBold
    {
        get => _isBold;
        set => SetProperty(ref _isBold, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set => SetProperty(ref _isLocked, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    public int ZIndex
    {
        get => _zIndex;
        set => SetProperty(ref _zIndex, value);
    }

    public double FontSizePixels => NormalizeRange(FontSizePt, 4, 72, 7) / 72.0 * _dpi;

    public double StrokeWidthPixels => NormalizeRange(StrokeWidthPt, 0.25, 10, 1) / 72.0 * _dpi;

    public double SelectionStrokeWidthPixels => StrokeWidthPixels + Math.Max(4, _dpi / 75.0);

    public double ShapeWidth => Math.Max(0, EndX - X);

    public double ShapeHeight => Math.Max(0, EndY - Y);

    public Geometry ArrowGeometry => CreateArrowGeometry();

    public Geometry LineGeometry => CreateLineGeometry();

    public Brush PreviewBrush
    {
        get
        {
            try
            {
                if (ColorConverter.ConvertFromString(Color) is SolidColorBrush brush)
                {
                    brush.Freeze();
                    return brush;
                }
            }
            catch (FormatException)
            {
            }

            return Brushes.Magenta;
        }
    }

    public string Summary => Kind switch
    {
        FigureAnnotationKind.Text => $"文字 · {(string.IsNullOrWhiteSpace(Text) ? "空" : Text)}",
        FigureAnnotationKind.Arrow => $"箭头 · ({X:0}, {Y:0}) → ({EndX:0}, {EndY:0})",
        FigureAnnotationKind.Line => $"直线 · ({X:0}, {Y:0}) — ({EndX:0}, {EndY:0})",
        FigureAnnotationKind.Rectangle => $"矩形 · {ShapeWidth:0} × {ShapeHeight:0} px",
        FigureAnnotationKind.Ellipse => $"椭圆 · {ShapeWidth:0} × {ShapeHeight:0} px",
        _ => "标注",
    };

    public bool IsValid
    {
        get
        {
            if (!AreCoordinatesFiniteAndInsideCanvas())
            {
                return false;
            }

            if (!ColorPattern().IsMatch(Color) ||
                !double.IsFinite(FontSizePt) || FontSizePt is < 4 or > 72 ||
                !double.IsFinite(StrokeWidthPt) || StrokeWidthPt is < 0.25 or > 10)
            {
                return false;
            }

            return Kind switch
            {
                FigureAnnotationKind.Text => !string.IsNullOrWhiteSpace(Text),
                FigureAnnotationKind.Arrow or FigureAnnotationKind.Line => Distance(EndX - X, EndY - Y) >= 5,
                FigureAnnotationKind.Rectangle or FigureAnnotationKind.Ellipse =>
                    ShapeWidth >= 5 && ShapeHeight >= 5,
                _ => false,
            };
        }
    }

    public string ValidationMessage
    {
        get
        {
            if (!AreCoordinatesFiniteAndInsideCanvas())
            {
                return "标注坐标必须位于当前画布内。";
            }

            if (!ColorPattern().IsMatch(Color))
            {
                return "颜色必须使用 #RRGGBB 或 #AARRGGBB。";
            }

            if (!double.IsFinite(FontSizePt) || FontSizePt is < 4 or > 72)
            {
                return "文字大小范围为 4–72 pt。";
            }

            if (!double.IsFinite(StrokeWidthPt) || StrokeWidthPt is < 0.25 or > 10)
            {
                return "线宽范围为 0.25–10 pt。";
            }

            if (Kind == FigureAnnotationKind.Text && string.IsNullOrWhiteSpace(Text))
            {
                return "文字标注不能为空。";
            }

            if (Kind is FigureAnnotationKind.Arrow or FigureAnnotationKind.Line &&
                Distance(EndX - X, EndY - Y) < 5)
            {
                return "直线或箭头的起点与终点距离至少为 5 px。";
            }

            if (Kind is FigureAnnotationKind.Rectangle or FigureAnnotationKind.Ellipse &&
                (ShapeWidth < 5 || ShapeHeight < 5))
            {
                return "形状宽度和高度均至少为 5 px，右下坐标必须大于左上坐标。";
            }

            return "标注参数有效 · 将按最终画布坐标导出";
        }
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        if (IsLocked || !double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return;
        }

        bool hasEndpoint = Kind != FigureAnnotationKind.Text;
        double minX = hasEndpoint ? Math.Min(X, EndX) : X;
        double maxX = hasEndpoint ? Math.Max(X, EndX) : X;
        double minY = hasEndpoint ? Math.Min(Y, EndY) : Y;
        double maxY = hasEndpoint ? Math.Max(Y, EndY) : Y;
        double clampedX = Math.Clamp(deltaX, -minX, _canvasWidth - maxX);
        double clampedY = Math.Clamp(deltaY, -minY, _canvasHeight - maxY);
        X += clampedX;
        Y += clampedY;
        if (hasEndpoint)
        {
            EndX += clampedX;
            EndY += clampedY;
        }
    }

    public FigureAnnotationExportItem CreateExportItem()
    {
        if (!IsValid)
        {
            throw new InvalidOperationException($"{KindDisplayName}标注参数无效：{ValidationMessage}");
        }

        return new FigureAnnotationExportItem(
            KindKey,
            X,
            Y,
            EndX,
            EndY,
            Text,
            Color,
            FontSizePt,
            StrokeWidthPt,
            IsBold,
            IsVisible,
            ZIndex);
    }

    private Geometry CreateArrowGeometry()
    {
        if (Kind != FigureAnnotationKind.Arrow ||
            !double.IsFinite(X) || !double.IsFinite(Y) ||
            !double.IsFinite(EndX) || !double.IsFinite(EndY))
        {
            return Geometry.Empty;
        }

        double dx = EndX - X;
        double dy = EndY - Y;
        double length = Distance(dx, dy);
        if (length < 0.001)
        {
            return Geometry.Empty;
        }

        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Max(StrokeWidthPixels * 4, 10.0 / 72.0 * _dpi);
        double halfWidth = headLength * 0.52;
        Point tip = new(dx, dy);
        Point baseCenter = new(dx - unitX * headLength, dy - unitY * headLength);
        Point left = new(baseCenter.X - unitY * halfWidth, baseCenter.Y + unitX * halfWidth);
        Point right = new(baseCenter.X + unitY * halfWidth, baseCenter.Y - unitX * halfWidth);

        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(new Point(0, 0), isFilled: false, isClosed: false);
            context.LineTo(baseCenter, isStroked: true, isSmoothJoin: false);
            context.BeginFigure(tip, isFilled: true, isClosed: true);
            context.LineTo(left, isStroked: true, isSmoothJoin: false);
            context.LineTo(right, isStroked: true, isSmoothJoin: false);
        }

        geometry.Freeze();
        return geometry;
    }

    private Geometry CreateLineGeometry()
    {
        if (Kind != FigureAnnotationKind.Line ||
            !double.IsFinite(X) || !double.IsFinite(Y) ||
            !double.IsFinite(EndX) || !double.IsFinite(EndY))
        {
            return Geometry.Empty;
        }

        var geometry = new LineGeometry(new Point(0, 0), new Point(EndX - X, EndY - Y));
        geometry.Freeze();
        return geometry;
    }

    private bool AreCoordinatesFiniteAndInsideCanvas()
    {
        bool startValid = double.IsFinite(X) && double.IsFinite(Y) &&
                          X >= 0 && X <= _canvasWidth && Y >= 0 && Y <= _canvasHeight;
        if (!startValid || Kind == FigureAnnotationKind.Text)
        {
            return startValid;
        }

        return double.IsFinite(EndX) && double.IsFinite(EndY) &&
               EndX >= 0 && EndX <= _canvasWidth && EndY >= 0 && EndY <= _canvasHeight;
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(ArrowGeometry));
        OnPropertyChanged(nameof(LineGeometry));
        OnPropertyChanged(nameof(ShapeWidth));
        OnPropertyChanged(nameof(ShapeHeight));
        OnPropertyChanged(nameof(Summary));
        NotifyValidationChanged();
    }

    private void NotifyValidationChanged()
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
        OnPropertyChanged(nameof(Summary));
    }

    private static double Distance(double deltaX, double deltaY) =>
        Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

    private static double NormalizeRange(double value, double minimum, double maximum, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;

    [GeneratedRegex("^#(?:[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColorPattern();
}
