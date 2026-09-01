using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public sealed record FigureScientificVertexHandle(
    int Index,
    double X,
    double Y,
    bool IsSelected);

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
    private Guid? _channelId;
    private bool _isSelected;
    private int _selectedPolygonVertexIndex = -1;
    private IReadOnlyDictionary<Guid, ChannelGroupMember> _availableChannels =
        new Dictionary<Guid, ChannelGroupMember>();

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
        Colorbar = kind == FigureScientificObjectKind.Colorbar
            ? new ColorbarViewModel()
            : null;
        ChannelLegend = kind == FigureScientificObjectKind.ChannelLegend
            ? new ChannelLegendViewModel()
            : null;
        if (Colorbar is not null)
        {
            Colorbar.Changed += OnColorbarChanged;
        }
        if (ChannelLegend is not null)
        {
            ChannelLegend.Changed += OnChannelLegendChanged;
        }
        (_pointsText, _label) = kind switch
        {
            FigureScientificObjectKind.PolygonAnnotation =>
                (DefaultPolygon(canvasWidth, canvasHeight), "Polygon"),
            FigureScientificObjectKind.DirectionMarker =>
                ($"{canvasWidth * 0.25:0.###},{canvasHeight * 0.75:0.###};{canvasWidth * 0.48:0.###},{canvasHeight * 0.75:0.###}", "N"),
            FigureScientificObjectKind.Colorbar =>
                ($"{canvasWidth * 0.80:0.###},{canvasHeight * 0.18:0.###};{canvasWidth * 0.84:0.###},{canvasHeight * 0.62:0.###}", "Intensity"),
            FigureScientificObjectKind.ChannelLegend =>
                ($"{canvasWidth * 0.66:0.###},{canvasHeight * 0.16:0.###};{canvasWidth * 0.92:0.###},{canvasHeight * 0.30:0.###}", "Channels"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        SetHorizontalDirectionCommand = new RelayCommand(
            () => DirectionAngleDegrees = 0,
            () => Kind == FigureScientificObjectKind.DirectionMarker && !IsLocked);
        SetVerticalDirectionCommand = new RelayCommand(
            () => DirectionAngleDegrees = 90,
            () => Kind == FigureScientificObjectKind.DirectionMarker && !IsLocked);
    }

    public Guid Id { get; }

    public FigureScientificObjectKind Kind { get; }

    public ColorbarViewModel? Colorbar { get; }

    public ChannelLegendViewModel? ChannelLegend { get; }

    public RelayCommand SetHorizontalDirectionCommand { get; }

    public RelayCommand SetVerticalDirectionCommand { get; }

    public string KindDisplayName => Kind switch
    {
        FigureScientificObjectKind.PolygonAnnotation => "Polygon Annotation",
        FigureScientificObjectKind.DirectionMarker => "方向标记",
        FigureScientificObjectKind.Colorbar => "色条",
        FigureScientificObjectKind.ChannelLegend => "通道图例",
        _ => "科研对象",
    };

    public string Summary => $"{KindDisplayName} · {Label}";

    public Visibility PolygonVisibility => Kind == FigureScientificObjectKind.PolygonAnnotation
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

    public Visibility DirectionEditorVisibility => Kind == FigureScientificObjectKind.DirectionMarker
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
        get => ChannelLegend?.BorderColor ?? _strokeColor;
        set
        {
            if (ChannelLegend is not null)
            {
                ChannelLegend.BorderColor = value;
                return;
            }
            if (SetProperty(ref _strokeColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(StrokeBrush));
                NotifyChanged();
            }
        }
    }

    public string FillColor
    {
        get => ChannelLegend?.BackgroundColor ?? _fillColor;
        set
        {
            if (ChannelLegend is not null)
            {
                ChannelLegend.BackgroundColor = value;
                return;
            }
            if (SetProperty(ref _fillColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(FillBrush));
                NotifyChanged();
            }
        }
    }

    public double FillOpacityPercent
    {
        get => ChannelLegend?.BackgroundOpacityPercent ?? _fillOpacityPercent;
        set
        {
            if (ChannelLegend is not null)
            {
                ChannelLegend.BackgroundOpacityPercent = value;
                return;
            }
            if (SetProperty(ref _fillOpacityPercent, value))
            {
                OnPropertyChanged(nameof(FillBrush));
                NotifyChanged();
            }
        }
    }

    public string TextColor
    {
        get => ChannelLegend?.TextColor ?? _textColor;
        set
        {
            if (ChannelLegend is not null)
            {
                ChannelLegend.TextColor = value;
                return;
            }
            if (SetProperty(ref _textColor, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(TextBrush));
                NotifyChanged();
            }
        }
    }

    public string FontFamily
    {
        get => ChannelLegend?.FontFamily ?? _fontFamily;
        set
        {
            if (ChannelLegend is not null) { ChannelLegend.FontFamily = value; return; }
            if (SetProperty(ref _fontFamily, value?.Trim() ?? string.Empty)) NotifyChanged();
        }
    }
    public double FontSizePt
    {
        get => ChannelLegend?.FontSizePt ?? _fontSizePt;
        set
        {
            if (ChannelLegend is not null) { ChannelLegend.FontSizePt = value; return; }
            if (SetProperty(ref _fontSizePt, value)) { OnPropertyChanged(nameof(FontSizePixels)); NotifyChanged(); }
        }
    }
    public double StrokeWidthPt
    {
        get => ChannelLegend?.BorderWidthPt ?? _strokeWidthPt;
        set
        {
            if (ChannelLegend is not null) { ChannelLegend.BorderWidthPt = value; return; }
            if (SetProperty(ref _strokeWidthPt, value)) { OnPropertyChanged(nameof(StrokeWidthPixels)); NotifyChanged(); }
        }
    }
    public bool IsBold
    {
        get => ChannelLegend?.IsBold ?? _isBold;
        set
        {
            if (ChannelLegend is not null) { ChannelLegend.IsBold = value; return; }
            if (SetProperty(ref _isBold, value)) NotifyChanged();
        }
    }
    public bool IsVisible { get => _isVisible; set { if (SetProperty(ref _isVisible, value)) NotifyChanged(); } }
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(SelectionVisibility));
                OnPropertyChanged(nameof(ResizeHandleVisibility));
                OnPropertyChanged(nameof(PolygonVertexHandlesVisibility));
                SetHorizontalDirectionCommand.NotifyCanExecuteChanged();
                SetVerticalDirectionCommand.NotifyCanExecuteChanged();
                NotifyChanged();
            }
        }
    }
    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (SetProperty(ref _isSelected, value))
            {
                if (!value)
                {
                    _selectedPolygonVertexIndex = -1;
                    OnPropertyChanged(nameof(SelectedPolygonVertexIndex));
                    OnPropertyChanged(nameof(PolygonVertexHandles));
                }
                OnPropertyChanged(nameof(SelectionVisibility));
                OnPropertyChanged(nameof(ResizeHandleVisibility));
                OnPropertyChanged(nameof(PolygonVertexHandlesVisibility));
            }
        }
    }
    public int ZIndex { get => _zIndex; set { if (SetProperty(ref _zIndex, value)) NotifyChanged(); } }
    public double Minimum { get => Colorbar?.Minimum ?? _minimum; set { if (Colorbar is not null) { Colorbar.Minimum = value; return; } if (SetProperty(ref _minimum, value)) NotifyChanged(); } }
    public double Maximum { get => Colorbar?.Maximum ?? _maximum; set { if (Colorbar is not null) { Colorbar.Maximum = value; return; } if (SetProperty(ref _maximum, value)) NotifyChanged(); } }
    public string Unit { get => Colorbar?.Unit ?? _unit; set { if (Colorbar is not null) { Colorbar.Unit = value; return; } if (SetProperty(ref _unit, value?.Trim() ?? string.Empty)) NotifyChanged(); } }
    public string Colormap { get => Colorbar?.Colormap ?? _colormap; set { if (Colorbar is not null) { Colorbar.Colormap = value; return; } if (SetProperty(ref _colormap, value?.Trim() ?? string.Empty)) { OnPropertyChanged(nameof(ColorbarBrush)); NotifyChanged(); } } }
    public string ChannelEntriesText { get => ChannelLegend?.ItemsText ?? _channelEntriesText; set { if (ChannelLegend is not null) { ChannelLegend.ItemsText = value; return; } if (SetProperty(ref _channelEntriesText, value?.Trim() ?? string.Empty)) { OnPropertyChanged(nameof(ChannelEntries)); NotifyChanged(); } } }

    public IReadOnlyList<string> ColormapChoices => ScientificColormap.Supported;

    public IReadOnlyList<FigureChannelLegendEntry> ChannelEntries =>
        ChannelLegend?.Items ?? ParseChannelEntries(ChannelEntriesText);

    public Guid? ChannelId
    {
        get => Colorbar?.ChannelId ?? _channelId;
        set
        {
            if (Colorbar is not null)
            {
                if (value is Guid channelId && _availableChannels.TryGetValue(channelId, out ChannelGroupMember? channel))
                {
                    Colorbar.LinkToChannel(channel);
                }
                else
                {
                    Colorbar.ChannelId = value;
                }
                return;
            }
            if (SetProperty(ref _channelId, value))
            {
                NotifyChanged();
            }
        }
    }

    public ColorbarBindingState ColorbarBindingState
    {
        get => Colorbar?.BindingState ?? ColorbarBindingState.Detached;
        set
        {
            if (Colorbar is null)
            {
                return;
            }

            Colorbar.BindingState = value;
            if (value == ColorbarBindingState.Linked &&
                Colorbar.ChannelId is Guid channelId &&
                _availableChannels.TryGetValue(channelId, out ChannelGroupMember? channel))
            {
                Colorbar.SynchronizeLinkedChannel(channel);
            }
        }
    }

    public FigureObjectOrientation ColorbarOrientation
    {
        get => Colorbar?.Orientation ?? FigureObjectOrientation.Vertical;
        set { if (Colorbar is not null) Colorbar.Orientation = value; }
    }

    public string ColorbarTicksText
    {
        get => Colorbar?.TicksText ?? string.Empty;
        set { if (Colorbar is not null) Colorbar.TicksText = value; }
    }

    public IReadOnlyList<ColorbarBindingState> ColorbarBindingStateChoices =>
        Colorbar?.BindingStateChoices ?? [];

    public IReadOnlyList<FigureObjectOrientation> ColorbarOrientationChoices =>
        Colorbar?.OrientationChoices ?? [];

    public bool CanEditColorbarRange => Colorbar?.CanEditRange ?? false;

    public IReadOnlyList<ColorbarTick> ColorbarTicksAscending =>
        Colorbar?.Ticks.OrderBy(tick => tick.Value).ToArray() ?? [];

    public IReadOnlyList<ColorbarTick> ColorbarTicksDescending =>
        Colorbar?.Ticks.OrderByDescending(tick => tick.Value).ToArray() ?? [];

    public Visibility VerticalColorbarVisibility =>
        Kind == FigureScientificObjectKind.Colorbar &&
        ColorbarOrientation == FigureObjectOrientation.Vertical
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Visibility HorizontalColorbarVisibility =>
        Kind == FigureScientificObjectKind.Colorbar &&
        ColorbarOrientation == FigureObjectOrientation.Horizontal
            ? Visibility.Visible
            : Visibility.Collapsed;

    public double ChannelLegendPadding
    {
        get => ChannelLegend?.PaddingPixels ?? 5;
        set { if (ChannelLegend is not null) ChannelLegend.PaddingPixels = value; }
    }

    public PointCollection PolygonPoints => new(ParsePointsOrEmpty().Select(point => new Point(point.X, point.Y)));

    public int SelectedPolygonVertexIndex => _selectedPolygonVertexIndex;

    public IReadOnlyList<FigureScientificVertexHandle> PolygonVertexHandles =>
        Kind == FigureScientificObjectKind.PolygonAnnotation
            ? ParsePointsOrEmpty()
                .Select((point, index) => new FigureScientificVertexHandle(
                    index,
                    point.X,
                    point.Y,
                    index == _selectedPolygonVertexIndex))
                .ToArray()
            : [];

    public Visibility PolygonVertexHandlesVisibility =>
        Kind == FigureScientificObjectKind.PolygonAnnotation && IsSelected && !IsLocked
            ? Visibility.Visible
            : Visibility.Collapsed;

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
            double left = points.Min(point => point.X);
            double top = points.Min(point => point.Y);
            double right = points.Max(point => point.X);
            double bottom = points.Max(point => point.Y);
            return new Rect(left, top, right - left, bottom - top);
        }
    }

    public double BoundsRight => Bounds.IsEmpty ? 0 : Bounds.Right;

    public double BoundsBottom => Bounds.IsEmpty ? 0 : Bounds.Bottom;

    public Visibility SelectionVisibility => IsSelected && !IsLocked && !Bounds.IsEmpty
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility ResizeHandleVisibility =>
        Kind != FigureScientificObjectKind.PolygonAnnotation
            ? SelectionVisibility
            : Visibility.Collapsed;

    public double DirectionAngleDegrees
    {
        get
        {
            FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
            return Kind == FigureScientificObjectKind.DirectionMarker && points.Length == 2
                ? NormalizeAngle(Math.Atan2(
                    points[1].Y - points[0].Y,
                    points[1].X - points[0].X) * 180.0 / Math.PI)
                : 0;
        }
        set => SetDirectionAngle(value);
    }

    public Brush StrokeBrush => CreateBrush(StrokeColor, Brushes.Orange);
    public Brush TextBrush => CreateBrush(TextColor, Brushes.White);
    public Brush FillBrush => CreateBrush(FillColor, Brushes.Orange, FillOpacityPercent);
    public Brush ColorbarBrush => CreateGradientBrush(Colormap, ColorbarOrientation);
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
        string channelEntriesText,
        Guid? channelId = null,
        ColorbarBindingState? colorbarBindingState = null,
        FigureObjectOrientation colorbarOrientation = FigureObjectOrientation.Vertical,
        string? colorbarTicksText = null,
        double channelLegendPadding = 5)
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
        ChannelId = channelId;
        Colorbar?.Restore(
            minimum,
            maximum,
            unit,
            colormap,
            channelId,
            colorbarBindingState ??
            (channelId.HasValue ? ColorbarBindingState.Linked : ColorbarBindingState.Detached),
            colorbarOrientation,
            colorbarTicksText);
        ChannelLegend?.Restore(
            channelEntriesText,
            fontFamily,
            fontSizePt,
            isBold,
            textColor,
            fillColor,
            fillOpacityPercent,
            strokeColor,
            strokeWidthPt,
            channelLegendPadding);
    }

    public ColorbarObject? CreateColorbarModel() =>
        Colorbar?.CreateModel(Id);

    public ChannelLegendObject? CreateChannelLegendModel() =>
        ChannelLegend?.CreateModel(Id);

    public void SynchronizeLinkedColorbar(ChannelGroupMember channel)
    {
        Colorbar?.SynchronizeLinkedChannel(channel);
    }

    public void SetAvailableChannels(IEnumerable<ChannelGroupMember> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _availableChannels = channels
            .GroupBy(channel => channel.ChannelId)
            .ToDictionary(group => group.Key, group => group.Last());
        if (Colorbar?.ChannelId is Guid channelId &&
            _availableChannels.TryGetValue(channelId, out ChannelGroupMember? channel))
        {
            Colorbar.SynchronizeLinkedChannel(channel);
        }
    }

    public void LinkColorbarToChannel(ChannelGroupMember channel)
    {
        Colorbar?.LinkToChannel(channel);
    }

    public void MoveBy(double deltaX, double deltaY)
    {
        if (IsLocked || !double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return;
        }

        FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
        if (points.Length == 0)
        {
            return;
        }

        double adjustedX = Math.Clamp(
            deltaX,
            -points.Min(point => point.X),
            _canvasWidth - points.Max(point => point.X));
        double adjustedY = Math.Clamp(
            deltaY,
            -points.Min(point => point.Y),
            _canvasHeight - points.Max(point => point.Y));
        PointsText = FormatPoints(points.Select(point =>
            new FigureScientificPoint(point.X + adjustedX, point.Y + adjustedY)));
    }

    public bool TrySelectPolygonVertex(int index)
    {
        int count = ParsePointsOrEmpty().Count();
        if (Kind != FigureScientificObjectKind.PolygonAnnotation ||
            !IsSelected || IsLocked || index < 0 || index >= count)
        {
            return false;
        }

        if (_selectedPolygonVertexIndex != index)
        {
            _selectedPolygonVertexIndex = index;
            OnPropertyChanged(nameof(SelectedPolygonVertexIndex));
            OnPropertyChanged(nameof(PolygonVertexHandles));
        }
        return true;
    }

    public void ClearSelectedPolygonVertex()
    {
        if (_selectedPolygonVertexIndex == -1)
        {
            return;
        }

        _selectedPolygonVertexIndex = -1;
        OnPropertyChanged(nameof(SelectedPolygonVertexIndex));
        OnPropertyChanged(nameof(PolygonVertexHandles));
    }

    public bool TrySetPolygonPoints(IEnumerable<FigureScientificPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        return TryApplyPolygonPoints(points.ToArray());
    }

    public bool TryMovePolygonVertex(int index, double x, double y)
    {
        FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
        if (index < 0 || index >= points.Length)
        {
            return false;
        }

        FigureScientificPoint[] candidate = [.. points];
        candidate[index] = new FigureScientificPoint(x, y);
        if (!TryApplyPolygonPoints(candidate))
        {
            return false;
        }

        TrySelectPolygonVertex(index);
        return true;
    }

    public bool TryInsertPolygonVertex(double x, double y, out int insertedIndex)
    {
        insertedIndex = -1;
        FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
        if (Kind != FigureScientificObjectKind.PolygonAnnotation || IsLocked || points.Length < 3 ||
            !double.IsFinite(x) || !double.IsFinite(y))
        {
            return false;
        }

        var point = new FigureScientificPoint(x, y);
        int segmentIndex = FindNearestPolygonSegment(points, point);
        insertedIndex = segmentIndex + 1;
        List<FigureScientificPoint> candidate = [.. points];
        candidate.Insert(insertedIndex, point);
        if (!TryApplyPolygonPoints(candidate))
        {
            insertedIndex = -1;
            return false;
        }

        TrySelectPolygonVertex(insertedIndex);
        return true;
    }

    public bool TryDeletePolygonVertex(int index)
    {
        FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
        if (Kind != FigureScientificObjectKind.PolygonAnnotation || IsLocked ||
            points.Length <= 3 || index < 0 || index >= points.Length)
        {
            return false;
        }

        List<FigureScientificPoint> candidate = [.. points];
        candidate.RemoveAt(index);
        if (!TryApplyPolygonPoints(candidate))
        {
            return false;
        }

        int nextIndex = Math.Min(index, candidate.Count - 1);
        TrySelectPolygonVertex(nextIndex);
        return true;
    }

    public void SetResizePoint(double x, double y)
    {
        if (IsLocked || !double.IsFinite(x) || !double.IsFinite(y))
        {
            return;
        }

        FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
        if (points.Length < 2)
        {
            return;
        }

        x = Math.Clamp(x, 0, _canvasWidth);
        y = Math.Clamp(y, 0, _canvasHeight);
        if (Kind == FigureScientificObjectKind.DirectionMarker)
        {
            if (Distance(points[0], new FigureScientificPoint(x, y)) >= 5)
            {
                points[1] = new FigureScientificPoint(x, y);
                PointsText = FormatPoints(points);
            }
            return;
        }

        Rect bounds = Bounds;
        if (bounds.IsEmpty)
        {
            return;
        }

        double targetWidth = Math.Max(5, x - bounds.Left);
        double targetHeight = Math.Max(5, y - bounds.Top);
        double scaleX = targetWidth / Math.Max(1e-9, bounds.Width);
        double scaleY = targetHeight / Math.Max(1e-9, bounds.Height);
        PointsText = FormatPoints(points.Select(point => new FigureScientificPoint(
            Math.Clamp(bounds.Left + (point.X - bounds.Left) * scaleX, 0, _canvasWidth),
            Math.Clamp(bounds.Top + (point.Y - bounds.Top) * scaleY, 0, _canvasHeight))));
    }

    public void SetDirectionAngle(double angleDegrees)
    {
        if (IsLocked || Kind != FigureScientificObjectKind.DirectionMarker || !double.IsFinite(angleDegrees))
        {
            return;
        }

        FigureScientificPoint[] points = ParsePointsOrEmpty().ToArray();
        if (points.Length != 2)
        {
            return;
        }

        double length = Distance(points[0], points[1]);
        if (length < 0.001)
        {
            return;
        }

        double radians = NormalizeAngle(angleDegrees) * Math.PI / 180.0;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double maximumLengthX = Math.Abs(cosine) < 1e-9
            ? double.PositiveInfinity
            : (cosine > 0 ? _canvasWidth - points[0].X : points[0].X) / Math.Abs(cosine);
        double maximumLengthY = Math.Abs(sine) < 1e-9
            ? double.PositiveInfinity
            : (sine > 0 ? _canvasHeight - points[0].Y : points[0].Y) / Math.Abs(sine);
        length = Math.Min(length, Math.Min(maximumLengthX, maximumLengthY));
        points[1] = new FigureScientificPoint(
            points[0].X + cosine * length,
            points[0].Y + sine * length);
        PointsText = FormatPoints(points);
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
            FigureColorbarExportSpec? colorbar = Colorbar?.CreateExportSpec();
            FigureChannelLegendExportSpec? channelLegend = ChannelLegend?.CreateExportSpec();
            var candidate = new FigureScientificObjectExportItem(
                Id, Kind, points, Label, StrokeColor, FillColor, FillOpacityPercent,
                TextColor, FontFamily, FontSizePt, StrokeWidthPt, IsBold, IsVisible,
                ZIndex, Minimum, Maximum, Unit, Colormap, ChannelEntries,
                ChannelId: ChannelId,
                Colorbar: colorbar,
                ChannelLegend: channelLegend);
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

    private static Brush CreateGradientBrush(
        string value,
        FigureObjectOrientation orientation)
    {
        Color[] colors = value.ToLowerInvariant() switch
        {
            "magma" => [Color.FromRgb(0, 0, 4), Color.FromRgb(115, 20, 117), Color.FromRgb(252, 136, 97), Color.FromRgb(252, 253, 191)],
            "plasma" => [Color.FromRgb(13, 8, 135), Color.FromRgb(126, 3, 168), Color.FromRgb(204, 71, 120), Color.FromRgb(248, 149, 64), Color.FromRgb(240, 249, 33)],
            "inferno" => [Color.FromRgb(0, 0, 4), Color.FromRgb(87, 16, 110), Color.FromRgb(188, 55, 84), Color.FromRgb(249, 142, 8), Color.FromRgb(252, 255, 164)],
            "cividis" => [Color.FromRgb(0, 32, 77), Color.FromRgb(40, 72, 110), Color.FromRgb(87, 108, 116), Color.FromRgb(145, 143, 111), Color.FromRgb(253, 234, 69)],
            "turbo" => [Color.FromRgb(48, 18, 59), Color.FromRgb(50, 104, 210), Color.FromRgb(44, 203, 128), Color.FromRgb(245, 210, 65), Color.FromRgb(180, 4, 38)],
            "grayscale" => [Colors.Black, Colors.White],
            _ => [Color.FromRgb(68, 1, 84), Color.FromRgb(59, 82, 139), Color.FromRgb(33, 145, 140), Color.FromRgb(94, 201, 98), Color.FromRgb(253, 231, 37)],
        };
        var brush = new LinearGradientBrush
        {
            StartPoint = orientation == FigureObjectOrientation.Vertical
                ? new Point(0, 1)
                : new Point(0, 0),
            EndPoint = orientation == FigureObjectOrientation.Vertical
                ? new Point(0, 0)
                : new Point(1, 0),
        };
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
        OnPropertyChanged(nameof(PolygonVertexHandles));
        OnPropertyChanged(nameof(DirectionGeometry));
        OnPropertyChanged(nameof(Bounds));
        OnPropertyChanged(nameof(BoundsRight));
        OnPropertyChanged(nameof(BoundsBottom));
        OnPropertyChanged(nameof(SelectionVisibility));
        OnPropertyChanged(nameof(ResizeHandleVisibility));
        OnPropertyChanged(nameof(DirectionAngleDegrees));
        NotifyChanged();
    }

    private bool TryApplyPolygonPoints(IReadOnlyList<FigureScientificPoint> points)
    {
        if (Kind != FigureScientificObjectKind.PolygonAnnotation || IsLocked || points.Count < 3 ||
            points.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                                point.X < 0 || point.X > _canvasWidth ||
                                point.Y < 0 || point.Y > _canvasHeight) ||
            Math.Abs(SignedArea(points)) < 12.5)
        {
            return false;
        }

        PointsText = FormatPoints(points);
        return true;
    }

    private static int FindNearestPolygonSegment(
        IReadOnlyList<FigureScientificPoint> points,
        FigureScientificPoint point)
    {
        int nearest = 0;
        double nearestDistance = double.PositiveInfinity;
        for (int index = 0; index < points.Count; index++)
        {
            double distance = DistanceToSegment(point, points[index], points[(index + 1) % points.Count]);
            if (distance < nearestDistance)
            {
                nearest = index;
                nearestDistance = distance;
            }
        }
        return nearest;
    }

    private static double DistanceToSegment(
        FigureScientificPoint point,
        FigureScientificPoint start,
        FigureScientificPoint end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= 1e-12)
        {
            return Distance(point, start);
        }

        double t = Math.Clamp(
            ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared,
            0,
            1);
        return Distance(point, new FigureScientificPoint(start.X + t * deltaX, start.Y + t * deltaY));
    }

    private static double SignedArea(IReadOnlyList<FigureScientificPoint> points)
    {
        double area = 0;
        for (int index = 0; index < points.Count; index++)
        {
            FigureScientificPoint current = points[index];
            FigureScientificPoint next = points[(index + 1) % points.Count];
            area += current.X * next.Y - next.X * current.Y;
        }
        return area / 2;
    }

    private void NotifyChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    private void OnColorbarChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(Minimum));
        OnPropertyChanged(nameof(Maximum));
        OnPropertyChanged(nameof(Unit));
        OnPropertyChanged(nameof(Colormap));
        OnPropertyChanged(nameof(ChannelId));
        OnPropertyChanged(nameof(ColorbarBindingState));
        OnPropertyChanged(nameof(ColorbarOrientation));
        OnPropertyChanged(nameof(ColorbarTicksText));
        OnPropertyChanged(nameof(ColorbarTicksAscending));
        OnPropertyChanged(nameof(ColorbarTicksDescending));
        OnPropertyChanged(nameof(VerticalColorbarVisibility));
        OnPropertyChanged(nameof(HorizontalColorbarVisibility));
        OnPropertyChanged(nameof(ColorbarBindingStateChoices));
        OnPropertyChanged(nameof(ColorbarOrientationChoices));
        OnPropertyChanged(nameof(CanEditColorbarRange));
        OnPropertyChanged(nameof(ColorbarBrush));
        NotifyChanged();
    }

    private void OnChannelLegendChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ChannelEntriesText));
        OnPropertyChanged(nameof(ChannelEntries));
        OnPropertyChanged(nameof(FontFamily));
        OnPropertyChanged(nameof(FontSizePt));
        OnPropertyChanged(nameof(FontSizePixels));
        OnPropertyChanged(nameof(IsBold));
        OnPropertyChanged(nameof(TextColor));
        OnPropertyChanged(nameof(TextBrush));
        OnPropertyChanged(nameof(FillColor));
        OnPropertyChanged(nameof(FillOpacityPercent));
        OnPropertyChanged(nameof(FillBrush));
        OnPropertyChanged(nameof(StrokeColor));
        OnPropertyChanged(nameof(StrokeBrush));
        OnPropertyChanged(nameof(StrokeWidthPt));
        OnPropertyChanged(nameof(StrokeWidthPixels));
        OnPropertyChanged(nameof(ChannelLegendPadding));
        NotifyChanged();
    }

    private static string FormatPoints(IEnumerable<FigureScientificPoint> points) =>
        string.Join(';', points.Select(point =>
            $"{point.X.ToString("R", CultureInfo.InvariantCulture)},{point.Y.ToString("R", CultureInfo.InvariantCulture)}"));

    private static double Distance(FigureScientificPoint first, FigureScientificPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double NormalizeAngle(double value)
    {
        double normalized = value % 360;
        if (normalized >= 180)
        {
            normalized -= 360;
        }
        else if (normalized < -180)
        {
            normalized += 360;
        }

        return normalized;
    }
}
