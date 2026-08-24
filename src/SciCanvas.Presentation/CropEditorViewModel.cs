using SciCanvas.Core.Cropping;
using SciCanvas.Core.Geometry;

namespace SciCanvas.Presentation;

public sealed class CropEditorViewModel : ObservableObject
{
    private PixelSize64? _sourceSize;
    private long _x;
    private long _y;
    private long _width = 1200;
    private long _height = 800;
    private bool _isConfigured;
    private bool _isValid;
    private string _validationMessage = "请先导入源图像。";

    public CropEditorViewModel()
    {
        AlignLeftCommand = new RelayCommand(AlignLeft);
        AlignHorizontalCenterCommand = new RelayCommand(AlignHorizontalCenter);
        AlignRightCommand = new RelayCommand(AlignRight);
        AlignTopCommand = new RelayCommand(AlignTop);
        AlignVerticalCenterCommand = new RelayCommand(AlignVerticalCenter);
        AlignBottomCommand = new RelayCommand(AlignBottom);
    }

    public event EventHandler? BoundsChanged;

    public RelayCommand AlignLeftCommand { get; }

    public RelayCommand AlignHorizontalCenterCommand { get; }

    public RelayCommand AlignRightCommand { get; }

    public RelayCommand AlignTopCommand { get; }

    public RelayCommand AlignVerticalCenterCommand { get; }

    public RelayCommand AlignBottomCommand { get; }

    public long X
    {
        get => _x;
        set => SetBounds(value, Y, Width, Height);
    }

    public long Y
    {
        get => _y;
        set => SetBounds(X, value, Width, Height);
    }

    public long Width
    {
        get => _width;
        set => SetBounds(X, Y, value, Height);
    }

    public long Height
    {
        get => _height;
        set => SetBounds(X, Y, Width, value);
    }

    public bool IsConfigured
    {
        get => _isConfigured;
        private set => SetProperty(ref _isConfigured, value);
    }

    public bool IsValid
    {
        get => _isValid;
        private set => SetProperty(ref _isValid, value);
    }

    public string ValidationMessage
    {
        get => _validationMessage;
        private set => SetProperty(ref _validationMessage, value);
    }

    public string SizeText => $"{Width:N0} × {Height:N0} px";

    public void ConfigureForSource(PixelSize64 sourceSize, bool preserveSize = false)
    {
        long nextWidth = preserveSize && IsConfigured
            ? Math.Min(Width, sourceSize.Width)
            : Math.Min(1200, sourceSize.Width);
        long nextHeight = preserveSize && IsConfigured
            ? Math.Min(Height, sourceSize.Height)
            : Math.Min(800, sourceSize.Height);
        long nextX = preserveSize && IsConfigured
            ? Math.Clamp(X, 0, sourceSize.Width - nextWidth)
            : (sourceSize.Width - nextWidth) / 2;
        long nextY = preserveSize && IsConfigured
            ? Math.Clamp(Y, 0, sourceSize.Height - nextHeight)
            : (sourceSize.Height - nextHeight) / 2;

        _sourceSize = sourceSize;
        IsConfigured = true;
        SetBoundsCore(nextX, nextY, nextWidth, nextHeight, forceNotifications: true);
    }

    public bool RestoreForSource(PixelSize64 sourceSize, PixelRect64 crop)
    {
        CropValidationResult validation = CropBoundsValidator.Validate(crop, sourceSize);
        if (!validation.IsValid)
        {
            return false;
        }

        _sourceSize = sourceSize;
        IsConfigured = true;
        SetBoundsCore(crop.X, crop.Y, crop.Width, crop.Height, forceNotifications: true);
        return true;
    }

    public void Reset()
    {
        _sourceSize = null;
        IsConfigured = false;
        SetBoundsCore(0, 0, 1200, 800, forceNotifications: true);
    }

    public bool SetBounds(long x, long y, long width, long height) =>
        SetBoundsCore(x, y, width, height, forceNotifications: false);

    public bool TryGetCrop(out PixelRect64 crop)
    {
        try
        {
            crop = new PixelRect64(X, Y, Width, Height);
            return _sourceSize is not null && CropBoundsValidator.Validate(crop, _sourceSize.Value).IsValid;
        }
        catch (ArgumentOutOfRangeException)
        {
            crop = default;
            return false;
        }
        catch (OverflowException)
        {
            crop = default;
            return false;
        }
    }

    private void AlignLeft()
    {
        if (_sourceSize is not null)
        {
            X = 0;
        }
    }

    private void AlignHorizontalCenter()
    {
        if (_sourceSize is not null)
        {
            X = Math.Max(0, (_sourceSize.Value.Width - Width) / 2);
        }
    }

    private void AlignRight()
    {
        if (_sourceSize is not null)
        {
            X = Math.Max(0, _sourceSize.Value.Width - Width);
        }
    }

    private void AlignTop()
    {
        if (_sourceSize is not null)
        {
            Y = 0;
        }
    }

    private void AlignVerticalCenter()
    {
        if (_sourceSize is not null)
        {
            Y = Math.Max(0, (_sourceSize.Value.Height - Height) / 2);
        }
    }

    private void AlignBottom()
    {
        if (_sourceSize is not null)
        {
            Y = Math.Max(0, _sourceSize.Value.Height - Height);
        }
    }

    private bool SetBoundsCore(
        long x,
        long y,
        long width,
        long height,
        bool forceNotifications)
    {
        bool xChanged = _x != x;
        bool yChanged = _y != y;
        bool widthChanged = _width != width;
        bool heightChanged = _height != height;
        if (!forceNotifications && !xChanged && !yChanged && !widthChanged && !heightChanged)
        {
            return false;
        }

        _x = x;
        _y = y;
        _width = width;
        _height = height;

        if (forceNotifications || xChanged)
        {
            OnPropertyChanged(nameof(X));
        }
        if (forceNotifications || yChanged)
        {
            OnPropertyChanged(nameof(Y));
        }
        if (forceNotifications || widthChanged)
        {
            OnPropertyChanged(nameof(Width));
        }
        if (forceNotifications || heightChanged)
        {
            OnPropertyChanged(nameof(Height));
        }
        if (forceNotifications || widthChanged || heightChanged)
        {
            OnPropertyChanged(nameof(SizeText));
        }

        Validate();
        BoundsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void Validate()
    {
        if (_sourceSize is null)
        {
            IsValid = false;
            ValidationMessage = "请先导入源图像。";
            return;
        }

        try
        {
            PixelRect64 crop = new(X, Y, Width, Height);
            CropValidationResult result = CropBoundsValidator.Validate(crop, _sourceSize.Value);
            IsValid = result.IsValid;
            ValidationMessage = result.IsValid ? "裁剪区域有效。" : result.Message!;
        }
        catch (ArgumentOutOfRangeException)
        {
            IsValid = false;
            ValidationMessage = "裁剪坐标不能为负数，宽高必须大于0。";
        }
        catch (OverflowException)
        {
            IsValid = false;
            ValidationMessage = "裁剪坐标超出支持范围。";
        }
    }
}
