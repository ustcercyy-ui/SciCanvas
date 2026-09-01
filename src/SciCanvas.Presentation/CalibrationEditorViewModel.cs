using SciCanvas.Core.Science;
using SciCanvas.Core.Images;

namespace SciCanvas.Presentation;

public sealed class CalibrationEditorViewModel : ObservableObject
{
    private readonly Guid _sourceAssetId;
    private double _unitsPerPixelX;
    private double _unitsPerPixelY;
    private string _unit = "µm";
    private CalibrationOrigin _origin;
    private double _referenceStartX;
    private double _referenceStartY;
    private double _referenceEndX;
    private double _referenceEndY;
    private double _referencePhysicalLength = 1;
    private bool _hasReferenceLine;
    private bool _isRestoring;
    private string? _metadataReviewMessage;
    private readonly double _sourceMaximumX;
    private readonly double _sourceMaximumY;

    public CalibrationEditorViewModel(Guid sourceAssetId, ImageMetadata metadata)
        : this(
            sourceAssetId,
            metadata?.PhysicalSizeX,
            metadata?.PhysicalSizeY,
            metadata?.PhysicalUnit,
            metadata?.PixelSize.Width ?? double.PositiveInfinity,
            metadata?.PixelSize.Height ?? double.PositiveInfinity)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        SetFromMetadata(ImageMetadataCalibrationMapper.Map(sourceAssetId, metadata));
    }

    public CalibrationEditorViewModel(
        Guid sourceAssetId,
        double? metadataUnitsPerPixelX,
        double? metadataUnitsPerPixelY,
        string? metadataUnit,
        double sourceWidth = double.PositiveInfinity,
        double sourceHeight = double.PositiveInfinity)
    {
        if (sourceAssetId == Guid.Empty)
        {
            throw new ArgumentException("源图像 ID 不能为空。", nameof(sourceAssetId));
        }

        _sourceAssetId = sourceAssetId;
        _sourceMaximumX = double.IsFinite(sourceWidth) && sourceWidth > 0
            ? Math.Max(0, sourceWidth - 1)
            : double.PositiveInfinity;
        _sourceMaximumY = double.IsFinite(sourceHeight) && sourceHeight > 0
            ? Math.Max(0, sourceHeight - 1)
            : double.PositiveInfinity;
        AvailableUnits = ScientificLengthUnits.Supported;
        ApplyReferenceCommand = new RelayCommand(ApplyReference, () => CanApplyReference);
        ClearCalibrationCommand = new RelayCommand(ClearCalibration, () => IsCalibrated);
        SetReferenceHorizontalCommand = new RelayCommand(
            () => ReferenceAngleDegrees = 0,
            () => HasReferenceLine);
        SetReferenceVerticalCommand = new RelayCommand(
            () => ReferenceAngleDegrees = 90,
            () => HasReferenceLine);
        SetFromMetadata(metadataUnitsPerPixelX, metadataUnitsPerPixelY, metadataUnit);
    }

    public event EventHandler? Changed;

    public event EventHandler? EditCompleted;

    public IReadOnlyList<string> AvailableUnits { get; }

    public RelayCommand ApplyReferenceCommand { get; }

    public RelayCommand ClearCalibrationCommand { get; }

    public RelayCommand SetReferenceHorizontalCommand { get; }

    public RelayCommand SetReferenceVerticalCommand { get; }

    public double UnitsPerPixelX
    {
        get => _unitsPerPixelX;
        set => SetAxisCalibration(ref _unitsPerPixelX, value, nameof(UnitsPerPixelX));
    }

    public double UnitsPerPixelY
    {
        get => _unitsPerPixelY;
        set => SetAxisCalibration(ref _unitsPerPixelY, value, nameof(UnitsPerPixelY));
    }

    public string Unit
    {
        get => _unit;
        set
        {
            string normalized;
            try
            {
                normalized = ScientificLengthUnits.Normalize(value);
            }
            catch (ArgumentException)
            {
                normalized = (value ?? string.Empty).Trim();
            }

            if (SetProperty(ref _unit, normalized))
            {
                PromoteToManual();
                NotifyCalibrationChanged();
            }
        }
    }

    public CalibrationOrigin Origin => _origin;

    public string OriginText => Origin switch
    {
        CalibrationOrigin.Metadata => "Metadata",
        CalibrationOrigin.Manual => "手动标定",
        CalibrationOrigin.Linked => "关联图同步",
        _ => "未标定",
    };

    public bool IsCalibrated => Calibration.IsValid;

    public bool IsAnisotropic => Calibration.IsAnisotropic;

    public string StatusText => IsCalibrated
        ? IsAnisotropic
            ? $"已标定 · X/Y 尺度不同 · {UnitsPerPixelX:0.######}/{UnitsPerPixelY:0.######} {Unit}/px"
            : $"已标定 · {UnitsPerPixelX:0.######} {Unit}/px"
        : _metadataReviewMessage is not null
            ? $"Metadata 需复核 · {_metadataReviewMessage}"
            : "未标定 · 测量将以 px 显示，比例尺不可用";

    public string ValidatorText => Calibration.ValidationMessage;

    public double ReferenceStartX
    {
        get => _referenceStartX;
        private set => SetProperty(ref _referenceStartX, value);
    }

    public double ReferenceStartY
    {
        get => _referenceStartY;
        private set => SetProperty(ref _referenceStartY, value);
    }

    public double ReferenceEndX
    {
        get => _referenceEndX;
        private set => SetProperty(ref _referenceEndX, value);
    }

    public double ReferenceEndY
    {
        get => _referenceEndY;
        private set => SetProperty(ref _referenceEndY, value);
    }

    public bool HasReferenceLine
    {
        get => _hasReferenceLine;
        private set
        {
            if (SetProperty(ref _hasReferenceLine, value))
            {
                OnPropertyChanged(nameof(ReferenceLineVisibility));
            }
        }
    }

    public System.Windows.Visibility ReferenceLineVisibility => HasReferenceLine
        ? System.Windows.Visibility.Visible
        : System.Windows.Visibility.Collapsed;

    public double ReferencePixelLength => Math.Sqrt(
        Math.Pow(ReferenceEndX - ReferenceStartX, 2) +
        Math.Pow(ReferenceEndY - ReferenceStartY, 2));

    public double ReferenceAngleDegrees
    {
        get => HasReferenceLine
            ? NormalizeAngle(Math.Atan2(
                ReferenceEndY - ReferenceStartY,
                ReferenceEndX - ReferenceStartX) * 180.0 / Math.PI)
            : 0;
        set => SetReferenceAngle(value);
    }

    public double ReferencePhysicalLength
    {
        get => _referencePhysicalLength;
        set
        {
            if (SetProperty(ref _referencePhysicalLength, value))
            {
                NotifyReferenceChanged();
            }
        }
    }

    public string ReferenceLabel => ReferencePixelLength > 0
        ? $"{ReferencePhysicalLength:0.###} {Unit} = {ReferencePixelLength:0.#} px"
        : "拖动建立参考线";

    public double ReferenceLabelX => Math.Min(ReferenceStartX, ReferenceEndX) + 10;

    public double ReferenceLabelY => Math.Max(0, Math.Min(ReferenceStartY, ReferenceEndY) - 32);

    public bool CanApplyReference =>
        HasReferenceLine && ReferencePixelLength >= 1 &&
        double.IsFinite(ReferencePhysicalLength) && ReferencePhysicalLength > 0 &&
        !string.IsNullOrWhiteSpace(Unit);

    public SpatialCalibration Calibration => new(
        _sourceAssetId,
        UnitsPerPixelX,
        UnitsPerPixelY,
        Unit,
        Origin,
        HasReferenceLine ? ReferencePixelLength : null,
        HasReferenceLine ? ReferencePhysicalLength : null);

    public void BeginReferenceLine(double x, double y)
    {
        ReferenceStartX = x;
        ReferenceStartY = y;
        ReferenceEndX = x;
        ReferenceEndY = y;
        HasReferenceLine = true;
        NotifyReferenceChanged();
    }

    public void UpdateReferenceLine(double x, double y)
    {
        if (!HasReferenceLine)
        {
            return;
        }

        ReferenceEndX = x;
        ReferenceEndY = y;
        NotifyReferenceChanged();
    }

    public void CompleteReferenceLine()
    {
        if (ReferencePixelLength < 1)
        {
            HasReferenceLine = false;
        }

        NotifyReferenceChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void SetReferenceAngle(double angleDegrees)
    {
        if (!HasReferenceLine || !double.IsFinite(angleDegrees))
        {
            return;
        }

        double length = ReferencePixelLength;
        if (length < 0.001)
        {
            return;
        }

        double radians = NormalizeAngle(angleDegrees) * Math.PI / 180.0;
        double cosine = Math.Cos(radians);
        double sine = Math.Sin(radians);
        double maximumLength = Math.Min(
            Math.Abs(cosine) < 1e-9 ? double.PositiveInfinity : _sourceMaximumX / Math.Abs(cosine),
            Math.Abs(sine) < 1e-9 ? double.PositiveInfinity : _sourceMaximumY / Math.Abs(sine));
        length = Math.Min(length, maximumLength);

        double centerX = (ReferenceStartX + ReferenceEndX) / 2;
        double centerY = (ReferenceStartY + ReferenceEndY) / 2;
        double halfX = cosine * length / 2;
        double halfY = sine * length / 2;
        double startX = centerX - halfX;
        double startY = centerY - halfY;
        double endX = centerX + halfX;
        double endY = centerY + halfY;
        double shiftX = double.IsFinite(_sourceMaximumX)
            ? Math.Clamp(0, -Math.Min(startX, endX), _sourceMaximumX - Math.Max(startX, endX))
            : 0;
        double shiftY = double.IsFinite(_sourceMaximumY)
            ? Math.Clamp(0, -Math.Min(startY, endY), _sourceMaximumY - Math.Max(startY, endY))
            : 0;

        ReferenceStartX = startX + shiftX;
        ReferenceStartY = startY + shiftY;
        ReferenceEndX = endX + shiftX;
        ReferenceEndY = endY + shiftY;
        NotifyReferenceChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void Restore(
        SpatialCalibration calibration,
        double referenceStartX = 0,
        double referenceStartY = 0,
        double referenceEndX = 0,
        double referenceEndY = 0)
    {
        if (calibration.SourceAssetId != _sourceAssetId)
        {
            throw new InvalidOperationException("标定记录与源图像不匹配。");
        }

        _isRestoring = true;
        try
        {
            _unitsPerPixelX = calibration.UnitsPerPixelX;
            _unitsPerPixelY = calibration.UnitsPerPixelY;
            _unit = string.IsNullOrWhiteSpace(calibration.Unit) ? "µm" : calibration.Unit;
            _origin = calibration.Origin;
            _metadataReviewMessage = null;
            _referencePhysicalLength = calibration.ReferencePhysicalLength ?? 1;
            _referenceStartX = referenceStartX;
            _referenceStartY = referenceStartY;
            _referenceEndX = referenceEndX;
            _referenceEndY = referenceEndY;
            _hasReferenceLine = calibration.ReferencePixelLength is > 0 &&
                                Distance(referenceStartX, referenceStartY, referenceEndX, referenceEndY) > 0;
        }
        finally
        {
            _isRestoring = false;
        }

        NotifyAll();
    }

    public void RefreshMetadataCalibration(
        double? metadataUnitsPerPixelX,
        double? metadataUnitsPerPixelY,
        string? metadataUnit)
    {
        if (Origin is CalibrationOrigin.Manual or CalibrationOrigin.Linked)
        {
            return;
        }

        SetFromMetadata(metadataUnitsPerPixelX, metadataUnitsPerPixelY, metadataUnit);
        NotifyAll();
    }

    public void RefreshMetadataCalibration(ImageMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if (Origin is CalibrationOrigin.Manual or CalibrationOrigin.Linked)
        {
            return;
        }

        SetFromMetadata(ImageMetadataCalibrationMapper.Map(_sourceAssetId, metadata));
        NotifyAll();
    }

    private void ApplyReference()
    {
        if (!CanApplyReference)
        {
            return;
        }

        SpatialCalibration calibration = SpatialCalibration.FromReference(
            _sourceAssetId,
            ReferencePixelLength,
            ReferencePhysicalLength,
            Unit);
        _unitsPerPixelX = calibration.UnitsPerPixelX;
        _unitsPerPixelY = calibration.UnitsPerPixelY;
        _origin = CalibrationOrigin.Manual;
        _metadataReviewMessage = null;
        NotifyAll();
        NotifyCalibrationChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ClearCalibration()
    {
        _unitsPerPixelX = 0;
        _unitsPerPixelY = 0;
        _origin = CalibrationOrigin.None;
        _metadataReviewMessage = null;
        NotifyAll();
        NotifyCalibrationChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void SetAxisCalibration(ref double field, double value, string propertyName)
    {
        if (EqualityComparer<double>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        PromoteToManual();
        NotifyCalibrationChanged();
    }

    private void PromoteToManual()
    {
        if (!_isRestoring && _origin != CalibrationOrigin.Manual)
        {
            _origin = CalibrationOrigin.Manual;
            _metadataReviewMessage = null;
            OnPropertyChanged(nameof(Origin));
            OnPropertyChanged(nameof(OriginText));
        }
    }

    private void SetFromMetadata(double? x, double? y, string? unit)
    {
        _isRestoring = true;
        try
        {
            bool validX = x is > 0 && double.IsFinite(x.Value);
            bool validY = y is > 0 && double.IsFinite(y.Value);
            bool validUnit = !string.IsNullOrWhiteSpace(unit);
            _unitsPerPixelX = validX && validY && validUnit ? x!.Value : 0;
            _unitsPerPixelY = validX && validY && validUnit ? y!.Value : 0;
            _unit = validUnit ? ScientificLengthUnits.Normalize(unit) : "µm";
            _origin = _unitsPerPixelX > 0 && _unitsPerPixelY > 0 && validUnit
                ? CalibrationOrigin.Metadata
                : CalibrationOrigin.None;
            _metadataReviewMessage = null;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void SetFromMetadata(MetadataCalibrationMapping mapping)
    {
        _isRestoring = true;
        try
        {
            SpatialCalibration calibration = mapping.Calibration;
            _unitsPerPixelX = mapping.IsAvailable ? calibration.UnitsPerPixelX : 0;
            _unitsPerPixelY = mapping.IsAvailable ? calibration.UnitsPerPixelY : 0;
            _unit = mapping.IsAvailable ? calibration.Unit : "µm";
            _origin = mapping.IsAvailable ? CalibrationOrigin.Metadata : CalibrationOrigin.None;
            _metadataReviewMessage = mapping.State == MetadataCalibrationState.ReviewRequired
                ? mapping.ReviewMessage
                : null;
        }
        finally
        {
            _isRestoring = false;
        }
    }

    private void NotifyReferenceChanged()
    {
        OnPropertyChanged(nameof(ReferencePixelLength));
        OnPropertyChanged(nameof(ReferenceLabel));
        OnPropertyChanged(nameof(ReferenceLabelX));
        OnPropertyChanged(nameof(ReferenceLabelY));
        OnPropertyChanged(nameof(ReferenceAngleDegrees));
        OnPropertyChanged(nameof(CanApplyReference));
        ApplyReferenceCommand.NotifyCanExecuteChanged();
        SetReferenceHorizontalCommand.NotifyCanExecuteChanged();
        SetReferenceVerticalCommand.NotifyCanExecuteChanged();
        if (!_isRestoring)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NotifyCalibrationChanged()
    {
        OnPropertyChanged(nameof(Calibration));
        OnPropertyChanged(nameof(IsCalibrated));
        OnPropertyChanged(nameof(IsAnisotropic));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ValidatorText));
        OnPropertyChanged(nameof(ReferenceLabel));
        ClearCalibrationCommand.NotifyCanExecuteChanged();
        ApplyReferenceCommand.NotifyCanExecuteChanged();
        if (!_isRestoring)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(UnitsPerPixelX));
        OnPropertyChanged(nameof(UnitsPerPixelY));
        OnPropertyChanged(nameof(Unit));
        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(ReferenceStartX));
        OnPropertyChanged(nameof(ReferenceStartY));
        OnPropertyChanged(nameof(ReferenceEndX));
        OnPropertyChanged(nameof(ReferenceEndY));
        OnPropertyChanged(nameof(ReferencePhysicalLength));
        OnPropertyChanged(nameof(HasReferenceLine));
        OnPropertyChanged(nameof(ReferenceLineVisibility));
        NotifyReferenceChanged();
        NotifyCalibrationChanged();
    }

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt(Math.Pow(x2 - x1, 2) + Math.Pow(y2 - y1, 2));

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
