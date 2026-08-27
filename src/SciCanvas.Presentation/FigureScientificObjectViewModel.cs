using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

/// <summary>Editable presentation adapter for canonical PR 3 scientific figure objects.</summary>
public sealed class FigureScientificObjectViewModel : ObservableObject
{
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private readonly int _dpi;
    private string _pointsText;
    private string _label;
    private string _strokeColor = "#FFFFB300";
    private string _fillColor = "#FFFFB300";
    private double _fillOpacityPercent = 12;
    private string _textColor = "#FFFFFFFF";
    private string _fontFamily = "Arial";
    private double _fontSizePt = 7;
    private double _strokeWidthPt = 1.25;
    private bool _isBold = true;
    private bool _isVisible = true;
    private bool _isLocked;
    private int _zIndex;
    private double _minimum;
    private double _maximum = 1;
    private string _unit = "a.u.";
    private string _colormap = "viridis";
    private string _channelEntriesText = "DAPI|#FF4FC3F7; GFP|#FF66BB6A";

    public FigureScientificObjectViewModel(
        FigureScientificObjectKind kind,
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
        (_pointsText, _label) = kind switch
        {
            FigureScientificObjectKind.PolygonAnnotation =>
                (DefaultPolygon(canvasWidth, canvasHeight), "Polygon"),
            FigureScientificObjectKind.Roi =>
                (DefaultPolygon(canvasWidth, canvasHeight), "ROI"),
            FigureScientificObjectKind.DirectionMarker =>
                ($"{canvasWidth * 0.25:0.###},{canvasHeight * 0.75:0.###};{canvasWidth * 0.48:0.###},{canvasHeight * 0.75:0.###}", "N"),
            FigureScientificObjectKind.Colorbar =>
                ($"{canvasWidth * 0.80:0.###},{canvasHeight * 0.18:0.###};{canvasWidth * 0.84:0.###},{canvasHeight * 0.62:0.###}", "Intensity"),
            FigureScientificObjectKind.ChannelLegend =>
                ($"{canvasWidth * 0.66:0.###},{canvasHeight * 0.16:0.###};{canvasWidth * 0.92:0.###},{canvasHeight * 0.30:0.###}", "Channels"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public Guid Id { get; }

    public FigureScientificObjectKind Kind { get; }

    public string KindDisplayName => Kind switch
    {
        FigureScientificObjectKind.PolygonAnnotation => "多边形标注",
        FigureScientificObjectKind.Roi => "规范 ROI",
        FigureScientificObjectKind.DirectionMarker => "方向标记",
        FigureScientificObjectKind.Colorbar => "色条",
        FigureScientificObjectKind.ChannelLegend => "通道图例",
        _ => "科研对象",
    };

    public string Summary => $"{KindDisplayName} · {Label}";

    public Visibility PolygonVisibility => Kind is FigureScientificObjectKind.PolygonAnnotation or FigureScientificObjectKind.Roi
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DirectionVisibility => Kind == FigureScientificObjectKind.DirectionMarker
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ColorbarVisibility => Kind == FigureScientificObjectKind.Colorbar
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChannelLegendVisibility => Kind == FigureScientificObjectKind.ChannelLegend
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RangeVisibility => Kind == FigureScientificObjectKind.Colorbar
        ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ChannelEntriesVisibility => Kind == FigureScientificObjectKind.ChannelLegend
        ? Visibility.Visible : Visibility.Collapsed;

    public string PointsText
    {
        get => _pointsText;
        set
        {
            if (SetProperty(ref _pointsText, value?.Trim() ?? string.Empty))
            {
                NotifyGeometryChanged();
            }
        }
    }

    public string Label
    {
        get => _label;
        set
        {
            if (SetProperty(ref _label, value?.Trim() ?? string.Empty))
            {
                NotifyChanged();
            }
        }
    }

    public string StrokeColor
    {
        get => _strokeColor;
        set
        {
            if (SetProperty(ref _strokeColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(StrokeBrush));
                NotifyChanged();
            }
        }
    }

    public string FillColor
    {
        get => _fillColor;
        set
        {
            if (SetProperty(ref _fillColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(FillBrush));
                NotifyChanged();
            }
        }
    }

    public double FillOpacityPercent
    {
        get => _fillOpacityPercent;
        set
        {
            if (SetProperty(ref _fillOpacityPercent, value))
            {
                OnPropertyChanged(nameof(FillBrush));
                NotifyChanged();
            }
        }
    }

    public string TextColor
    {
        get => _textColor;
        set
        {
            if (SetProperty(ref _textColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(TextBrush));
                NotifyChanged();
            }
        }
    }

    public string FontFamily { get => _fontFamily; set { if (SetProperty(ref _fontFamily, value?.Trim() ?? string.Empty)) NotifyChanged(); } }
    public double FontSizePt { get => _fontSizePt; set { if (SetProperty(ref _fontSizePt, value)) { OnPropertyChanged(nameof(FontSizePixels)); NotifyChanged(); } } }
    public double StrokeWidthPt { get => _strokeWidthPt; set { if (SetProperty(ref _strokeWidthPt, value)) { OnPropertyChanged(nameof(StrokeWidthPixels)); NotifyChanged(); } } }
    public bool IsBold { get => _isBold; set { if (SetProperty(ref _isBold, value)) NotifyChanged(); } }
    public bool IsVisible { get => _isVisible; set { if (SetProperty(ref _isVisible, value)) NotifyChanged(); } }
    public bool IsLocked { get => _isLocked; set { if (SetProperty(ref _isLocked, value)) NotifyChanged(); } }
    public int ZIndex { get => _zIndex; set { if (SetProperty(ref _zIndex, value)) NotifyChanged(); } }
    public double Minimum { get => _minimum; set { if (SetProperty(ref _minimum, value)) NotifyChanged(); } }
    public double Maximum { get => _maximum; set { if (SetProperty(ref _maximum, value)) NotifyChanged(); } }
    public string Unit { get => _unit; set { if (SetProperty(ref _unit, value?.Trim() ?? string.Empty)) NotifyChanged(); } }
    public string Colormap { get => _colormap; set { if (SetProperty(ref _colormap, value?.Trim() ?? string.Empty)) { OnPropertyChanged(nameof(ColorbarBrush)); NotifyChanged(); } } }
    public string ChannelEntriesText { get => _channelEntriesText; set { if (SetProperty(ref _channelEntriesText, value?.Trim() ?? string.Empty)) { OnPropertyChanged(nameof(ChannelEntries)); NotifyChanged(); } } }

    public IReadOnlyList<string> ColormapChoices { get; } = ["viridis", "magma", "grayscale"];

    public IReadOnlyList<FigureChannelLegendEntry> ChannelEntries => ParseChannelEntries(ChannelEntriesText);

    public PointCollection PolygonPoints => new(ParsePointsOrEmpty().Select(point => new Point(point.X, point.Y)));

    public Geometry DirectionGeometry
    {
        get
        {
            FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
            if (points.Length != 2)
            {
                return Geometry.Empty;
            }

            double dx = points[1].X - points[0].X;
            double dy = points[1].Y - points[0].Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            if (length < 0.001)
            {
                return Geometry.Empty;
            }

            double unitX = dx / length;
            double unitY = dy / length;
            double head = Math.Max(12, StrokeWidthPixels * 5);
            var geometry = new StreamGeometry();
            using StreamGeometryContext context = geometry.Open();
            context.BeginFigure(new Point(points[0].X, points[0].Y), false, false);
            context.LineTo(new Point(points[1].X - unitX * head, points[1].Y - unitY * head), true, false);
            Point left = new(points[1].X - unitX * head - unitY * head * 0.55, points[1].Y - unitY * head + unitX * head * 0.55);
            Point right = new(points[1].X - unitX * head + unitY * head * 0.55, points[1].Y - unitY * head - unitX * head * 0.55);
            context.BeginFigure(new Point(points[1].X, points[1].Y), true, true);
            context.LineTo(left, true, false);
            context.LineTo(right, true, false);
            geometry.Freeze();
            return geometry;
        }
    }

    public Rect Bounds
    {
        get
        {
            FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
            if (points.Length < 2)
            {
                return Rect.Empty;
            }
            double left = Math.Min(points[0].X, points[1].X);
            double top = Math.Min(points[0].Y, points[1].Y);
            return new Rect(left, top, Math.Abs(points[1].X - points[0].X), Math.Abs(points[1].Y - points[0].Y));
        }
    }

    public Brush StrokeBrush => CreateBrush(StrokeColor, Brushes.Orange);
    public Brush TextBrush => CreateBrush(TextColor, Brushes.White);
    public Brush FillBrush => CreateBrush(FillColor, Brushes.Orange, FillOpacityPercent);
    public Brush ColorbarBrush => CreateGradientBrush(Colormap);
    public double FontSizePixels => Math.Clamp(FontSizePt, 4, 72) / 72.0 * _dpi;
    public double StrokeWidthPixels => Math.Clamp(StrokeWidthPt, 0.25, 10) / 72.0 * _dpi;
    public bool IsValid => TryCreateExportItem(out _);
    public string ValidationMessage => TryCreateExportItem(out string message)
        ? "科研对象参数有效 · 将跨 raster/vector 一致导出"
        : message;

    public FigureScientificObjectExportItem CreateExportItem()
    {
        if (!TryCreateExportItem(out string message, out FigureScientificObjectExportItem? item))
        {
            throw new InvalidOperationException(message);
        }

        return item!;
    }

    public void Restore(
        string pointsText,
        string label,
        string strokeColor,
        string fillColor,
        double fillOpacityPercent,
        string textColor,
        string fontFamily,
        double fontSizePt,
        double strokeWidthPt,
        bool isBold,
        bool isVisible,
        bool isLocked,
        double minimum,
        double maximum,
        string unit,
        string colormap,
        string channelEntriesText)
    {
        PointsText = pointsText;
        Label = label;
        StrokeColor = strokeColor;
        FillColor = fillColor;
        FillOpacityPercent = fillOpacityPercent;
        TextColor = textColor;
        FontFamily = fontFamily;
        FontSizePt = fontSizePt;
        StrokeWidthPt = strokeWidthPt;
        IsBold = isBold;
        IsVisible = isVisible;
        IsLocked = isLocked;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        Colormap = colormap;
        ChannelEntriesText = channelEntriesText;
    }

    /// <summary>Scales canonical final-canvas point text while keeping invalid draft text editable.</summary>
    public static string ScalePointsText(
        string pointsText,
        double scaleX,
        double scaleY,
        int targetCanvasWidth,
        int targetCanvasHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCanvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetCanvasHeight);
        if (!double.IsFinite(scaleX) || !double.IsFinite(scaleY) || scaleX <= 0 || scaleY <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleX));
        }

        try
        {
            string[] tokens = (pointsText ?? string.Empty).Split(
                ';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
            {
                return pointsText ?? string.Empty;
            }

            return string.Join(';', tokens.Select(token =>
            {
                string[] pair = token.Split(',', StringSplitOptions.TrimEntries);
                if (pair.Length != 2 ||
                    !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                    !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
                {
                    throw new FormatException();
                }

                double scaledX = Math.Clamp(x * scaleX, 0, targetCanvasWidth);
                double scaledY = Math.Clamp(y * scaleY, 0, targetCanvasHeight);
                return $"{scaledX:R},{scaledY:R}";
            }));
        }
        catch (FormatException)
        {
            return pointsText ?? string.Empty;
        }
    }
    private bool TryCreateExportItem(out string message) => TryCreateExportItem(out message, out _);

    private bool TryCreateExportItem(out string message, out FigureScientificObjectExportItem? item)
    {
        item = null;
        try
        {
            FigureScientificPoint[] points = ParsePoints();
            var candidate = new FigureScientificObjectExportItem(
                Id, Kind, points, Label, StrokeColor, FillColor, FillOpacityPercent,
                TextColor, FontFamily, FontSizePt, StrokeWidthPt, IsBold, IsVisible,
                ZIndex, Minimum, Maximum, Unit, Colormap, ChannelEntries);
            candidate.EnsureValid(_canvasWidth, _canvasHeight);
            item = candidate;
            message = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            message = exception.Message;
            return false;
        }
    }

    private IEnumerable<FigureScientificPoint> ParsePointsOrEmpty()
    {
        try { return ParsePoints(); }
        catch (FormatException) { return []; }
    }

    private FigureScientificPoint[] ParsePoints()
    {
        string[] tokens = PointsText.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new FormatException("几何点格式为 x,y; x,y；请至少输入所需点数。");
        }

        return tokens.Select(token =>
        {
            string[] pair = token.Split(',', StringSplitOptions.TrimEntries);
            if (pair.Length != 2 ||
                !double.TryParse(pair[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(pair[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            {
                throw new FormatException("几何点必须使用以分号分隔的 x,y 坐标，且采用小数点。");
            }
            return new FigureScientificPoint(x, y);
        }).ToArray();
    }

    private static IReadOnlyList<FigureChannelLegendEntry> ParseChannelEntries(string value)
    {
        List<FigureChannelLegendEntry> entries = [];
        foreach (string token in value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = token.Split('|', 2, StringSplitOptions.TrimEntries);
            if (pair.Length == 2)
            {
                entries.Add(new FigureChannelLegendEntry(pair[0], pair[1]));
            }
        }
        return entries;
    }

    private static string DefaultPolygon(int width, int height) => string.Join(';',
        new[]
        {
            (width * 0.25, height * 0.28),
            (width * 0.48, height * 0.22),
            (width * 0.56, height * 0.45),
            (width * 0.33, height * 0.52),
        }.Select(point => $"{point.Item1:0.###},{point.Item2:0.###}"));

    private static Brush CreateBrush(string value, Brush fallback, double opacityPercent = 100)
    {
        if (!ScientificStyleColor.TryParseColor(value, out ScientificColorValue color))
        {
            return fallback;
        }
        byte alpha = (byte)Math.Round(color.Alpha * Math.Clamp(opacityPercent, 0, 100) / 100.0);
        var brush = new SolidColorBrush(Color.FromArgb(alpha, color.Red, color.Green, color.Blue));
        brush.Freeze();
        return brush;
    }

    private static Brush CreateGradientBrush(string value)
    {
        Color[] colors = value.ToLowerInvariant() switch
        {
            "magma" => [Color.FromRgb(0, 0, 4), Color.FromRgb(115, 20, 117), Color.FromRgb(252, 136, 97), Color.FromRgb(252, 253, 191)],
            "grayscale" => [Colors.Black, Colors.White],
            _ => [Color.FromRgb(68, 1, 84), Color.FromRgb(59, 82, 139), Color.FromRgb(33, 145, 140), Color.FromRgb(94, 201, 98), Color.FromRgb(253, 231, 37)],
        };
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
        for (int index = 0; index < colors.Length; index++)
        {
            brush.GradientStops.Add(new GradientStop(colors[index], index / (double)Math.Max(1, colors.Length - 1)));
        }
        brush.Freeze();
        return brush;
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(PolygonPoints));
        OnPropertyChanged(nameof(DirectionGeometry));
        OnPropertyChanged(nameof(Bounds));
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
    }
}