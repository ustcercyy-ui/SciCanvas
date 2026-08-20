using System.Windows;

namespace SciCanvas.Presentation;

public enum FigureGuideOrientation
{
    Vertical,
    Horizontal,
}

public sealed class FigureGuideViewModel : ObservableObject
{
    private readonly int _canvasWidth;
    private readonly int _canvasHeight;
    private double _position;
    private bool _isLocked;
    private bool _isSelected;

    public FigureGuideViewModel(
        FigureGuideOrientation orientation,
        int canvasWidth,
        int canvasHeight,
        double position,
        bool isLocked = false,
        Guid? id = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        Orientation = orientation;
        _canvasWidth = canvasWidth;
        _canvasHeight = canvasHeight;
        Id = id ?? Guid.NewGuid();
        _position = ClampPosition(position);
        _isLocked = isLocked;
    }

    public Guid Id { get; }

    public FigureGuideOrientation Orientation { get; }

    public string OrientationKey => Orientation == FigureGuideOrientation.Vertical
        ? "vertical"
        : "horizontal";

    public string OrientationDisplayName => Orientation == FigureGuideOrientation.Vertical
        ? "垂直参考线"
        : "水平参考线";

    public double Position
    {
        get => _position;
        set
        {
            if (IsLocked)
            {
                OnPropertyChanged();
                return;
            }

            if (SetProperty(ref _position, ClampPosition(value)))
            {
                NotifyGeometryChanged();
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
                OnPropertyChanged(nameof(CanEditPosition));
            }
        }
    }

    public bool CanEditPosition => !IsLocked;

    public bool IsSelected
    {
        get => _isSelected;
        internal set => SetProperty(ref _isSelected, value);
    }

    public double CanvasLeft => Orientation == FigureGuideOrientation.Vertical ? Position - 1 : 0;

    public double CanvasTop => Orientation == FigureGuideOrientation.Horizontal ? Position - 1 : 0;

    public double LineWidth => Orientation == FigureGuideOrientation.Vertical ? 2 : _canvasWidth;

    public double LineHeight => Orientation == FigureGuideOrientation.Horizontal ? 2 : _canvasHeight;

    public string Summary => $"{OrientationDisplayName} · {Position:0.##} px";

    private double ClampPosition(double value)
    {
        double finite = double.IsFinite(value) ? value : 0;
        double maximum = Orientation == FigureGuideOrientation.Vertical
            ? _canvasWidth
            : _canvasHeight;
        return Math.Clamp(finite, 0, maximum);
    }

    private void NotifyGeometryChanged()
    {
        OnPropertyChanged(nameof(CanvasLeft));
        OnPropertyChanged(nameof(CanvasTop));
        OnPropertyChanged(nameof(Summary));
    }
}
