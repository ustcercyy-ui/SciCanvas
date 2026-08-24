using SciCanvas.Core.Science;

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

    public CalibrationEditorViewModel(
        Guid sourceAssetId,
        double? metadataUnitsPerPixelX,
        double? metadataUnitsPerPixelY,
        string? metadataUnit)
    {
        if (sourceAssetId == Guid.Empty)
        {
            throw new ArgumentException("源图像 ID 不能为空。", nameof(sourceAssetId));
        }

        _sourceAssetId = sourceAssetId;
        AvailableUnits = ScientificLengthUnits.Supported;
        ApplyReferenceCommand = new RelayCommand(ApplyReference, () => CanApplyReference);
        ClearCalibrationCommand = new RelayCommand(ClearCalibration, () => IsCalibrated);
        SetFromMetadata(metadataUnitsPerPixelX, metadataUnitsPerPixelY, metadataUnit);
    }

    public event EventHandler? Changed;

    public event EventHandler? EditCompleted;

    public IReadOnlyList<string> AvailableUnits { get; }

    public RelayCommand ApplyReferenceCommand { get; }

    public RelayCommand ClearCalibrationCommand { get; }

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
        NotifyAll();
        NotifyCalibrationChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ClearCalibration()
    {
        _unitsPerPixelX = 0;
        _unitsPerPixelY = 0;
        _origin = CalibrationOrigin.None;
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
            _unitsPerPixelX = validX ? x!.Value : 0;
            _unitsPerPixelY = validY ? y!.Value : validX ? x!.Value : 0;
            _unit = string.IsNullOrWhiteSpace(unit)
                ? "µm"
                : ScientificLengthUnits.Normalize(unit);
            _origin = _unitsPerPixelX > 0 && _unitsPerPixelY > 0
                ? CalibrationOrigin.Metadata
                : CalibrationOrigin.None;
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
        OnPropertyChanged(nameof(CanApplyReference));
        ApplyReferenceCommand.NotifyCanExecuteChanged();
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
}
