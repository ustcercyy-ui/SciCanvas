using System.Windows.Media.Imaging;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Cropping;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;
using SciCanvas.Templates;

namespace SciCanvas.Presentation;

public sealed class FigurePanelViewModel : ObservableObject
{
    private long _x;
    private long _y;
    private long _width;
    private long _height;
    private bool _isVisible = true;
    private bool _isLocked;
    private bool _isSelected;
    private int _zIndex;
    private bool _showScaleBar;
    private double _physicalUnitsPerSourcePixel;
    private double _scaleBarPhysicalLength = 1;
    private string _scaleBarUnit = "µm";
    private bool _scaleBarShowLabel = true;
    private bool _isAspectRatioLocked;
    private bool _isUpdatingSize;
    private BitmapSource _preview;
    private string _label;
    private ImageAdjustmentParameters _adjustments = new();
    private int _frameIndex;
    private Guid? _cropLinkGroupId;
    private readonly int _figureDpi;
    private PanelFitMode _fitMode = PanelFitMode.Manual;
    private PixelRect64 _manualCropPixels;
    private double _rotationDegrees;
    private ScientificValidity _replacementValidity = ScientificValidity.Valid;
    private FigureGlobalStyle _inheritedGlobalStyle = FigureGlobalStyle.Default;
    private StyleOverride? _styleOverride;

    public FigurePanelViewModel(
        SourceAssetItemViewModel source,
        PixelRect64 sourceRect,
        TemplateSlotLayout slot,
        int zIndex,
        int figureDpi,
        Guid? id = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Id = id ?? Guid.NewGuid();
        SourceRect = sourceRect;
        _figureDpi = Math.Max(1, figureDpi);
        _manualCropPixels = sourceRect;
        SlotId = slot.Id;
        _label = slot.Label;
        Role = slot.Role;
        MinimumEffectiveDpi = slot.MinimumEffectiveDpi;
        RequiresScaleBar = slot.RequireScaleBar;
        _isAspectRatioLocked = slot.LockAspectRatio;
        HelpText = slot.HelpText;
        _x = slot.PixelRect.X;
        _y = slot.PixelRect.Y;
        _width = slot.PixelRect.Width;
        _height = slot.PixelRect.Height;
        _zIndex = zIndex;
        _physicalUnitsPerSourcePixel = source.Asset.Metadata.PhysicalSizeX ?? 0;
        _scaleBarUnit = string.IsNullOrWhiteSpace(source.Asset.Metadata.PhysicalUnit)
            ? "µm"
            : source.Asset.Metadata.PhysicalUnit;
        if (_physicalUnitsPerSourcePixel > 0)
        {
            _scaleBarPhysicalLength = ChooseReadablePhysicalLength(
                sourceRect.Width * _physicalUnitsPerSourcePixel * 0.2);
        }
        _preview = CreateCropPreview(source, sourceRect, _adjustments, _frameIndex);
    }

    public SourceAssetItemViewModel Source { get; private set; }

    public StyleOverride? StyleOverride => _styleOverride;

    public FigureGlobalStyle EffectiveStyle => _inheritedGlobalStyle.ResolvePanelOverride(_styleOverride);

    public string EffectivePanelLabelFontFamily => EffectiveStyle.EffectivePanelLabelFontFamily;

    public double EffectivePanelLabelFontSizePixels =>
        Math.Max(12, EffectiveStyle.EffectivePanelLabelFontSizePt / 72.0 * _figureDpi);

    public Brush EffectivePanelLabelTextBrush => CreateStyleBrush(EffectiveStyle.EffectivePanelLabelTextColor);

    public FontWeight EffectivePanelLabelFontWeight =>
        EffectiveStyle.PanelLabelIsBold ? FontWeights.Bold : FontWeights.Normal;

    public string EffectiveScaleBarFontFamily => EffectiveStyle.EffectiveScaleBarFontFamily;

    public double EffectiveScaleBarFontSizePixels =>
        Math.Max(12, EffectiveStyle.EffectiveScaleBarFontSizePt / 72.0 * _figureDpi);

    public Brush EffectiveScaleBarLabelBrush => CreateStyleBrush(EffectiveStyle.EffectiveScaleBarLabelColor);

    public FontWeight EffectiveScaleBarLabelFontWeight =>
        EffectiveStyle.ScaleBarLabelIsBold ? FontWeights.Bold : FontWeights.Normal;

    public double EffectiveScaleBarThicknessPixels =>
        Math.Max(1, EffectiveStyle.EffectiveScaleBarThicknessPt / 72.0 * _figureDpi);

    public Brush EffectiveScaleBarBrush => CreateStyleBrush(EffectiveStyle.ScaleBarColor);

    public Guid Id { get; }

    public PixelRect64 SourceRect { get; private set; }

    public NormalizedRect NormalizedCrop => NormalizedRect.FromSourcePixels(
        SourceRect,
        Source.Asset.Metadata.PixelSize.Width,
        Source.Asset.Metadata.PixelSize.Height);

    public FigureRectMm FrameMm => new(XMm, YMm, WidthMm, HeightMm);

    public PanelFitMode FitMode
    {
        get => _fitMode;
        set
        {
            if (_fitMode == value)
            {
                return;
            }

            if (_fitMode == PanelFitMode.Manual)
            {
                _manualCropPixels = SourceRect;
            }

            _fitMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FitModeText));
            ApplyFitModeCrop();
        }
    }

    public IReadOnlyList<PanelFitMode> AvailableFitModes { get; } =
        [PanelFitMode.Fit, PanelFitMode.Fill, PanelFitMode.Manual];

    public string FitModeText => FitMode switch
    {
        PanelFitMode.Fit => "Fit · 完整显示",
        PanelFitMode.Fill => "Fill · 填满画框",
        _ => "Manual Crop · 手动裁剪",
    };

    public double RotationDegrees
    {
        get => _rotationDegrees;
        set
        {
            double normalized = double.IsFinite(value)
                ? ((value % 360) + 360) % 360
                : 0;
            SetProperty(ref _rotationDegrees, normalized);
        }
    }

    public ScientificValidity ReplacementValidity
    {
        get => _replacementValidity;
        private set
        {
            if (Equals(_replacementValidity, value))
            {
                return;
            }

            _replacementValidity = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RequiresScientificReview));
            OnPropertyChanged(nameof(ScientificValidityText));
        }
    }

    public bool RequiresScientificReview =>
        ReplacementValidity.State is ScientificValidityState.ReviewRequired or ScientificValidityState.Invalid;

    public string ScientificValidityText => ReplacementValidity.State switch
    {
        ScientificValidityState.Valid => "科学对象有效",
        ScientificValidityState.Warning => $"警告 · {string.Join("；", ReplacementValidity.Reasons)}",
        ScientificValidityState.Invalid => $"无效 · {string.Join("；", ReplacementValidity.Reasons)}",
        _ => $"需复核 · {string.Join("；", ReplacementValidity.Reasons)}",
    };

    internal void RestoreWorkspaceState(
        PanelFitMode fitMode,
        double rotationDegrees,
        ScientificValidity validity)
    {
        RotationDegrees = rotationDegrees;
        ReplacementValidity = validity ?? ScientificValidity.Valid;
        FitMode = fitMode;
    }

    public bool IsInset => SlotId.StartsWith("inset:", StringComparison.Ordinal);

    public Guid? CropLinkGroupId
    {
        get => _cropLinkGroupId;
        set
        {
            if (SetProperty(ref _cropLinkGroupId, value))
            {
                OnPropertyChanged(nameof(IsCropLinked));
                OnPropertyChanged(nameof(CropLinkStatusText));
            }
        }
    }

    public bool IsCropLinked => CropLinkGroupId.HasValue;

    public string CropLinkStatusText => CropLinkGroupId.HasValue
        ? $"关联裁剪 · {CropLinkGroupId.Value.ToString("N")[..8]}"
        : "独立裁剪";
    public int FrameIndex
    {
        get => _frameIndex;
        set
        {
            int normalized = Math.Clamp(value, 0, FrameCount - 1);
            if (SetProperty(ref _frameIndex, normalized))
            {
                OnPropertyChanged(nameof(FrameNumber));
                OnPropertyChanged(nameof(FrameStatusText));
                RefreshPreview();
            }
        }
    }

    public int FrameNumber
    {
        get => FrameIndex + 1;
        set => FrameIndex = value - 1;
    }

    public int FrameCount => Source.FrameCount;

    public Visibility FrameSelectionVisibility =>
        FrameCount > 1 ? Visibility.Visible : Visibility.Collapsed;

    public string FrameStatusText => FrameCount > 1
        ? $"多页图像 · 第 {FrameNumber}/{FrameCount} 帧（导出将使用此帧）"
        : "单帧图像";

    public BitmapSource Preview => _preview;

    public ImageAdjustmentParameters Adjustments
    {
        get => _adjustments;
        set
        {
            ImageAdjustmentParameters normalized = (value ?? new()).Normalize();
            if (Equals(_adjustments, normalized))
            {
                return;
            }

            _adjustments = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Brightness));
            OnPropertyChanged(nameof(BrightnessPercent));
            OnPropertyChanged(nameof(Contrast));
            OnPropertyChanged(nameof(ContrastPercent));
            OnPropertyChanged(nameof(Gamma));
            OnPropertyChanged(nameof(BlackPoint));
            OnPropertyChanged(nameof(WhitePoint));
            OnPropertyChanged(nameof(Invert));
            OnPropertyChanged(nameof(Grayscale));
            OnPropertyChanged(nameof(Channel));
            OnPropertyChanged(nameof(AdjustmentStatusText));
            RefreshPreview();
        }
    }

    public double Brightness { get => _adjustments.Brightness; set => UpdateAdjustment(a => a with { Brightness = value }); }

    /// <summary>Brightness expressed as a user-facing percentage from -100 to +100.</summary>
    public double BrightnessPercent
    {
        get => Brightness * 100;
        set => Brightness = value / 100;
    }
    public double Contrast { get => _adjustments.Contrast; set => UpdateAdjustment(a => a with { Contrast = value }); }

    /// <summary>Contrast expressed as a user-facing percentage from -100 to +100.</summary>
    public double ContrastPercent
    {
        get => Contrast * 100;
        set => Contrast = value / 100;
    }
    public double Gamma { get => _adjustments.Gamma; set => UpdateAdjustment(a => a with { Gamma = value }); }
    public double BlackPoint { get => _adjustments.BlackPoint; set => UpdateAdjustment(a => a with { BlackPoint = value }); }
    public double WhitePoint { get => _adjustments.WhitePoint; set => UpdateAdjustment(a => a with { WhitePoint = value }); }
    public bool Invert { get => _adjustments.Invert; set => UpdateAdjustment(a => a with { Invert = value }); }
    public bool Grayscale { get => _adjustments.Grayscale; set => UpdateAdjustment(a => a with { Grayscale = value }); }
    public string Channel { get => _adjustments.Channel; set => UpdateAdjustment(a => a with { Channel = value }); }

    public string AdjustmentStatusText => _adjustments.ValidationMessage;

    public void ReplaceSource(SourceAssetItemViewModel source, PixelRect64 sourceRect)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!CropBoundsValidator.Validate(sourceRect, source.Asset.Metadata.PixelSize).IsValid)
        {
            throw new InvalidOperationException("替换面板时，裁剪区域必须位于新源图边界内。");
        }

        Guid previousAssetId = Source.Asset.Id;
        Source = source;
        SourceRect = sourceRect;
        _manualCropPixels = sourceRect;
        _fitMode = PanelFitMode.Manual;
        _frameIndex = Math.Clamp(_frameIndex, 0, FrameCount - 1);
        double physicalUnitsPerPixel = source.Asset.Metadata.PhysicalSizeX ?? 0;
        _physicalUnitsPerSourcePixel = physicalUnitsPerPixel;
        if (physicalUnitsPerPixel > 0)
        {
            _scaleBarUnit = string.IsNullOrWhiteSpace(source.Asset.Metadata.PhysicalUnit) ? "µm" : source.Asset.Metadata.PhysicalUnit;
            _scaleBarPhysicalLength = ChooseReadablePhysicalLength(sourceRect.Width * physicalUnitsPerPixel * 0.2);
        }
        else
        {
            ReplacementValidity = ScientificValidity.Invalid(
                "新源图缺少有效尺度校准；比例尺不可用于科学输出。");
        }
        if (previousAssetId != source.Asset.Id && physicalUnitsPerPixel > 0)
        {
            ReplacementValidity = ScientificValidity.ReviewRequired(
                "源图已替换；标注、测量、ROI、Inset 与色条必须复核。");
        }
        else if (previousAssetId == source.Asset.Id)
        {
            ReplacementValidity = ScientificValidity.Valid;
        }
        _preview = CreateCropPreview(source, sourceRect, _adjustments, _frameIndex);
        OnPropertyChanged(nameof(Source));
        OnPropertyChanged(nameof(SourceRect));
        OnPropertyChanged(nameof(NormalizedCrop));
        OnPropertyChanged(nameof(FitMode));
        OnPropertyChanged(nameof(FitModeText));
        OnPropertyChanged(nameof(AspectRatioText));
        OnPropertyChanged(nameof(ScalePercent));
        OnPropertyChanged(nameof(SizeStatusText));
        OnPropertyChanged(nameof(FrameIndex));
        OnPropertyChanged(nameof(FrameNumber));
        OnPropertyChanged(nameof(FrameCount));
        OnPropertyChanged(nameof(FrameSelectionVisibility));
        OnPropertyChanged(nameof(FrameStatusText));
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(EffectiveDpi));
        OnPropertyChanged(nameof(EffectiveDpiText));
        OnPropertyChanged(nameof(IsBelowMinimumDpi));
        NotifyScaleBarGeometryChanged();
    }
    public string SlotId { get; }

    public string Label
    {
        get => _label;
        set
        {
            string normalized = (value ?? string.Empty).Trim();
            if (normalized.Length > 16)
            {
                normalized = normalized[..16];
            }

            if (SetProperty(ref _label, normalized))
            {
                OnPropertyChanged(nameof(LabelVisibility));
            }
        }
    }

    public Visibility LabelVisibility => string.IsNullOrWhiteSpace(Label)
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string Role { get; }

    public string RoleDisplayName => Role switch
    {
        "sem-overview" => "低倍形貌",
        "sem-detail" => "高倍形貌",
        "tem" => "TEM",
        "hrtem" => "HRTEM",
        "elemental-map" => "元素分布",
        "inset" => "Inset 局部放大",
        "comparison" => "对照图",
        "synthesis" => "制备路线",
        "composition" => "成分/物相",
        "structure" => "结构表征",
        "mechanism" => "机理证据",
        "performance" => "性能",
        "device-schematic" => "器件/原理示意",
        "cyclic-voltammetry" => "循环伏安",
        "charge-discharge" => "充放电",
        "rate-capability" => "倍率性能",
        "cycling-stability" => "循环稳定性",
        "impedance" => "阻抗/动力学",
        "diffraction" => "衍射/晶体学",
        "spectroscopy" => "光谱",
        "surface-chemistry" => "表面化学",
        "microscopy" => "显微结构",
        "stress-strain" => "应力—应变",
        "fatigue-statistics" => "疲劳/统计",
        "fracture-overview" => "低倍断口",
        "fracture-detail" => "高倍断口",
        "deformation-mechanism" => "变形机理",
        _ => Role,
    };

    public int MinimumEffectiveDpi { get; }

    public bool RequiresScaleBar { get; }

    public string? HelpText { get; }

    public long X
    {
        get => _x;
        set
        {
            if (SetProperty(ref _x, value))
            {
                OnPropertyChanged(nameof(XMm));
                OnPropertyChanged(nameof(FrameMm));
            }
        }
    }

    public long Y
    {
        get => _y;
        set
        {
            if (SetProperty(ref _y, value))
            {
                OnPropertyChanged(nameof(YMm));
                OnPropertyChanged(nameof(FrameMm));
            }
        }
    }

    public long Width
    {
        get => _width;
        set => SetWidth(value);
    }

    public long Height
    {
        get => _height;
        set => SetHeight(value);
    }

    public double XMm
    {
        get => PixelsToMillimeters(X);
        set => X = MillimetersToPixels(value);
    }

    public double YMm
    {
        get => PixelsToMillimeters(Y);
        set => Y = MillimetersToPixels(value);
    }

    public double WidthMm
    {
        get => PixelsToMillimeters(Width);
        set => Width = Math.Max(1, MillimetersToPixels(value));
    }

    public double HeightMm
    {
        get => PixelsToMillimeters(Height);
        set => Height = Math.Max(1, MillimetersToPixels(value));
    }

    /// <summary>Locks panel resizing to the source crop aspect ratio.</summary>
    public bool IsAspectRatioLocked
    {
        get => _isAspectRatioLocked;
        set
        {
            if (!SetProperty(ref _isAspectRatioLocked, value))
            {
                return;
            }

            if (value)
            {
                SetDestinationSize(_width, CalculateHeightForWidth(_width));
            }

            OnPropertyChanged(nameof(SizeStatusText));
        }
    }

    public double ScalePercent
    {
        get => SourceRect.Width <= 0 ? 100 : Width / (double)SourceRect.Width * 100;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 1, 1000) : 100;
            long width = ScaleDimension(SourceRect.Width, normalized / 100);
            long height = ScaleDimension(SourceRect.Height, normalized / 100);
            SetDestinationSize(width, height);
        }
    }

    public string AspectRatioText => SourceRect.Width > 0 && SourceRect.Height > 0
        ? $"源图比例 {SourceRect.Width:N0} : {SourceRect.Height:N0}"
        : "源图比例未知";

    public string SizeStatusText => $"{Width:N0} × {Height:N0} px · {ScalePercent:0.#}% · {(IsAspectRatioLocked ? "等比锁定" : "自由宽高")}";

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (SetProperty(ref _isLocked, value))
            {
                OnPropertyChanged(nameof(PanelResizeHandleVisibility));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(PanelResizeHandleVisibility));
            }
        }
    }

    public Visibility PanelResizeHandleVisibility =>
        IsSelected && !IsLocked ? Visibility.Visible : Visibility.Collapsed;

    public int ZIndex
    {
        get => _zIndex;
        set => SetProperty(ref _zIndex, value);
    }

    public bool ShowScaleBar
    {
        get => _showScaleBar;
        set
        {
            if (SetProperty(ref _showScaleBar, value))
            {
                NotifyScaleBarGeometryChanged();
            }
        }
    }

    public double PhysicalUnitsPerSourcePixel
    {
        get => _physicalUnitsPerSourcePixel;
        set
        {
            if (SetProperty(ref _physicalUnitsPerSourcePixel, value))
            {
                NotifyScaleBarGeometryChanged();
            }
        }
    }

    public double ScaleBarPhysicalLength
    {
        get => _scaleBarPhysicalLength;
        set
        {
            if (SetProperty(ref _scaleBarPhysicalLength, value))
            {
                NotifyScaleBarGeometryChanged();
            }
        }
    }

    public string ScaleBarUnit
    {
        get => _scaleBarUnit;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _scaleBarUnit, normalized))
            {
                NotifyScaleBarGeometryChanged();
            }
        }
    }

    public bool ScaleBarShowLabel
    {
        get => _scaleBarShowLabel;
        set => SetProperty(ref _scaleBarShowLabel, value);
    }

    public bool IsScaleBarValid => !ShowScaleBar ||
        (double.IsFinite(PhysicalUnitsPerSourcePixel) && PhysicalUnitsPerSourcePixel > 0 &&
         double.IsFinite(ScaleBarPhysicalLength) && ScaleBarPhysicalLength > 0 &&
         !string.IsNullOrWhiteSpace(ScaleBarUnit) &&
         ScaleBarSourcePixelLength <= SourceRect.Width * 0.8);

    public double ScaleBarSourcePixelLength =>
        PhysicalUnitsPerSourcePixel > 0 && double.IsFinite(PhysicalUnitsPerSourcePixel)
            ? ScaleBarPhysicalLength / PhysicalUnitsPerSourcePixel
            : 0;

    public double ScaleBarPreviewWidth
    {
        get
        {
            if (!IsScaleBarValid || !ShowScaleBar)
            {
                return 0;
            }

            return ScaleBarSourcePixelLength * ContainedScale;
        }
    }

    public double ScaleBarPreviewX
    {
        get
        {
            (double left, _, double imageWidth, _) = ContainedImageRect;
            double margin = Math.Max(12, Math.Min(imageWidth, Height) * 0.035);
            return left + imageWidth - margin - ScaleBarPreviewWidth;
        }
    }

    public double ScaleBarPreviewY
    {
        get
        {
            (_, double top, _, double imageHeight) = ContainedImageRect;
            double margin = Math.Max(12, Math.Min(Width, imageHeight) * 0.035);
            return top + imageHeight - margin - 7;
        }
    }

    public double ScaleBarLabelPreviewY => Math.Max(2, ScaleBarPreviewY - 38);

    public bool HasRenderableScaleBar => ShowScaleBar && IsScaleBarValid;

    public string ScaleBarLabel => $"{ScaleBarPhysicalLength:0.###} {ScaleBarUnit}";

    public string ScaleBarStatusText
    {
        get
        {
            if (!ShowScaleBar)
            {
                return RequiresScaleBar
                    ? "该显微图插槽建议添加经过校准的比例尺。"
                    : "比例尺未启用。";
            }

            if (!double.IsFinite(PhysicalUnitsPerSourcePixel) || PhysicalUnitsPerSourcePixel <= 0)
            {
                return "请输入大于 0 的“每像素物理尺寸”。";
            }

            if (!double.IsFinite(ScaleBarPhysicalLength) || ScaleBarPhysicalLength <= 0)
            {
                return "请输入大于 0 的比例尺长度。";
            }

            if (string.IsNullOrWhiteSpace(ScaleBarUnit))
            {
                return "请输入物理单位，例如 nm 或 µm。";
            }

            if (ScaleBarSourcePixelLength > SourceRect.Width * 0.8)
            {
                return "比例尺超过图像宽度的 80%，请缩短长度或检查校准值。";
            }

            return $"已校准 · {ScaleBarLabel} = {ScaleBarSourcePixelLength:0.##} 个源像素";
        }
    }

    public PixelRect64 DestinationRect => new(X, Y, Width, Height);

    public double EffectiveDpi => EffectiveDpiCalculator.Calculate(
        Source.Asset.Metadata.PixelSize.Width,
        Source.Asset.Metadata.PixelSize.Height,
        NormalizedCrop,
        WidthMm,
        HeightMm);

    public string EffectiveDpiText => $"有效分辨率约 {EffectiveDpi:0} dpi";

    public bool IsBelowMinimumDpi => EffectiveDpi < MinimumEffectiveDpi;

    public FigureScaleBarExportSpec? CreateScaleBarExportSpec()
    {
        if (!ShowScaleBar)
        {
            return null;
        }

        if (!IsScaleBarValid)
        {
            throw new InvalidOperationException($"面板 {Label} 的比例尺参数无效：{ScaleBarStatusText}");
        }

        return new FigureScaleBarExportSpec(
            PhysicalUnitsPerSourcePixel,
            ScaleBarPhysicalLength,
            ScaleBarUnit,
            ScaleBarShowLabel);
    }

    public void ApplySpatialCalibration(SpatialCalibration calibration)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        if (calibration.SourceAssetId != Source.Asset.Id)
        {
            throw new InvalidOperationException("不能把其他源图像的标定应用到当前面板。");
        }

        if (!calibration.IsValid)
        {
            PhysicalUnitsPerSourcePixel = 0;
            ReplacementValidity = ScientificValidity.Invalid(
                "源图缺少有效校准；比例尺保持可见但已标记为无效。");
            return;
        }

        PhysicalUnitsPerSourcePixel = calibration.UnitsPerPixelX;
        ScaleBarUnit = calibration.Unit;
        if (!double.IsFinite(ScaleBarPhysicalLength) || ScaleBarPhysicalLength <= 0 ||
            ScaleBarSourcePixelLength > SourceRect.Width * 0.8)
        {
            ScaleBarPhysicalLength = ChooseReadablePhysicalLength(
                SourceRect.Width * calibration.UnitsPerPixelX * 0.2);
        }
    }

    public void RestoreStyleOverride(StyleOverride? styleOverride)
    {
        styleOverride?.EnsureValid();
        StyleOverride? normalized = styleOverride?.IsEmpty == true ? null : styleOverride;
        if (Equals(_styleOverride, normalized))
        {
            return;
        }

        _styleOverride = normalized;
        OnPropertyChanged(nameof(StyleOverride));
        NotifyEffectiveStyleChanged();
    }

    internal void UpdateInheritedGlobalStyle(FigureGlobalStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        if (_inheritedGlobalStyle == style)
        {
            return;
        }

        _inheritedGlobalStyle = style;
        NotifyEffectiveStyleChanged();
    }

    internal void RefreshPreview()
    {
        _preview = CreateCropPreview(Source, SourceRect, _adjustments, _frameIndex);
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(EffectiveDpi));
        OnPropertyChanged(nameof(EffectiveDpiText));
        OnPropertyChanged(nameof(IsBelowMinimumDpi));
        NotifyScaleBarGeometryChanged();
    }

    private void ApplyFitModeCrop()
    {
        SourceRect = PanelCropCalculator.ResolveSourcePixels(
            FitMode,
            Source.Asset.Metadata.PixelSize.Width,
            Source.Asset.Metadata.PixelSize.Height,
            FrameMm,
            _manualCropPixels);
        RefreshPreview();
        OnPropertyChanged(nameof(SourceRect));
        OnPropertyChanged(nameof(NormalizedCrop));
        OnPropertyChanged(nameof(AspectRatioText));
        OnPropertyChanged(nameof(ScalePercent));
    }

    private void UpdateAdjustment(Func<ImageAdjustmentParameters, ImageAdjustmentParameters> update)
    {
        Adjustments = update(_adjustments);
    }

    private void SetWidth(long value)
    {
        long normalized = NormalizeDimension(value);
        SetDestinationSize(
            normalized,
            IsAspectRatioLocked ? CalculateHeightForWidth(normalized) : Height);
    }

    private void SetHeight(long value)
    {
        long normalized = NormalizeDimension(value);
        SetDestinationSize(
            IsAspectRatioLocked ? CalculateWidthForHeight(normalized) : Width,
            normalized);
    }

    private void SetDestinationSize(long width, long height)
    {
        width = NormalizeDimension(width);
        height = NormalizeDimension(height);
        if (_isUpdatingSize || (_width == width && _height == height))
        {
            return;
        }

        _isUpdatingSize = true;
        try
        {
            _width = width;
            _height = height;
        }
        finally
        {
            _isUpdatingSize = false;
        }

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(WidthMm));
        OnPropertyChanged(nameof(HeightMm));
        OnPropertyChanged(nameof(FrameMm));
        OnPropertyChanged(nameof(ScalePercent));
        OnPropertyChanged(nameof(SizeStatusText));
        OnPropertyChanged(nameof(EffectiveDpi));
        OnPropertyChanged(nameof(EffectiveDpiText));
        OnPropertyChanged(nameof(IsBelowMinimumDpi));
        NotifyScaleBarGeometryChanged();
        if (FitMode == PanelFitMode.Fill)
        {
            ApplyFitModeCrop();
        }
    }

    internal void RestoreDestinationSize(PixelRect64 destination, bool lockAspectRatio)
    {
        _isUpdatingSize = true;
        try
        {
            _width = NormalizeDimension(destination.Width);
            _height = NormalizeDimension(destination.Height);
            _isAspectRatioLocked = lockAspectRatio;
        }
        finally
        {
            _isUpdatingSize = false;
        }

        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
        OnPropertyChanged(nameof(WidthMm));
        OnPropertyChanged(nameof(HeightMm));
        OnPropertyChanged(nameof(FrameMm));
        OnPropertyChanged(nameof(IsAspectRatioLocked));
        OnPropertyChanged(nameof(ScalePercent));
        OnPropertyChanged(nameof(SizeStatusText));
        OnPropertyChanged(nameof(EffectiveDpi));
        OnPropertyChanged(nameof(EffectiveDpiText));
        OnPropertyChanged(nameof(IsBelowMinimumDpi));
        NotifyScaleBarGeometryChanged();
    }

    public void SetMatchedFrameSize(long width, long height)
    {
        _isAspectRatioLocked = false;
        SetDestinationSize(width, height);
        OnPropertyChanged(nameof(IsAspectRatioLocked));
        OnPropertyChanged(nameof(SizeStatusText));
    }

    public void MatchAspectRatio(double aspectRatio)
    {
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));
        }

        _isAspectRatioLocked = false;
        long height = NormalizeDimension((long)Math.Round(Width / aspectRatio));
        SetDestinationSize(Width, height);
        OnPropertyChanged(nameof(IsAspectRatioLocked));
        OnPropertyChanged(nameof(SizeStatusText));
    }

    private long CalculateHeightForWidth(long width) =>
        ScaleDimension(SourceRect.Height, width / (double)Math.Max(1, SourceRect.Width));

    private long CalculateWidthForHeight(long height) =>
        ScaleDimension(SourceRect.Width, height / (double)Math.Max(1, SourceRect.Height));

    private static long ScaleDimension(long source, double scale)
    {
        double scaled = source * scale;
        return !double.IsFinite(scaled) || scaled >= long.MaxValue
            ? long.MaxValue
            : Math.Max(1, (long)Math.Round(scaled));
    }

    private static long NormalizeDimension(long value) => Math.Max(1, value);

    private double PixelsToMillimeters(long pixels) => pixels / (double)_figureDpi * 25.4;

    private long MillimetersToPixels(double millimeters)
    {
        double normalized = double.IsFinite(millimeters) ? Math.Max(0, millimeters) : 0;
        return Math.Max(0, (long)Math.Round(normalized / 25.4 * _figureDpi));
    }

    private double ContainedScale => Math.Min(
        Width / (double)SourceRect.Width,
        Height / (double)SourceRect.Height);

    private (double Left, double Top, double Width, double Height) ContainedImageRect
    {
        get
        {
            double imageWidth = SourceRect.Width * ContainedScale;
            double imageHeight = SourceRect.Height * ContainedScale;
            return (
                (Width - imageWidth) / 2.0,
                (Height - imageHeight) / 2.0,
                imageWidth,
                imageHeight);
        }
    }

    private void NotifyScaleBarGeometryChanged()
    {
        OnPropertyChanged(nameof(IsScaleBarValid));
        OnPropertyChanged(nameof(ScaleBarSourcePixelLength));
        OnPropertyChanged(nameof(ScaleBarPreviewWidth));
        OnPropertyChanged(nameof(ScaleBarPreviewX));
        OnPropertyChanged(nameof(ScaleBarPreviewY));
        OnPropertyChanged(nameof(ScaleBarLabelPreviewY));
        OnPropertyChanged(nameof(HasRenderableScaleBar));
        OnPropertyChanged(nameof(ScaleBarLabel));
        OnPropertyChanged(nameof(ScaleBarStatusText));
    }

    private void NotifyEffectiveStyleChanged()
    {
        OnPropertyChanged(nameof(EffectiveStyle));
        OnPropertyChanged(nameof(EffectivePanelLabelFontFamily));
        OnPropertyChanged(nameof(EffectivePanelLabelFontSizePixels));
        OnPropertyChanged(nameof(EffectivePanelLabelTextBrush));
        OnPropertyChanged(nameof(EffectivePanelLabelFontWeight));
        OnPropertyChanged(nameof(EffectiveScaleBarFontFamily));
        OnPropertyChanged(nameof(EffectiveScaleBarFontSizePixels));
        OnPropertyChanged(nameof(EffectiveScaleBarLabelBrush));
        OnPropertyChanged(nameof(EffectiveScaleBarLabelFontWeight));
        OnPropertyChanged(nameof(EffectiveScaleBarThicknessPixels));
        OnPropertyChanged(nameof(EffectiveScaleBarBrush));
    }

    private static Brush CreateStyleBrush(string color)
    {
        Color parsed = (Color)(ColorConverter.ConvertFromString(color) ?? Colors.Black);
        var brush = new SolidColorBrush(parsed);
        brush.Freeze();
        return brush;
    }

    private static double ChooseReadablePhysicalLength(double target)
    {
        if (!double.IsFinite(target) || target <= 0)
        {
            return 1;
        }

        double exponent = Math.Pow(10, Math.Floor(Math.Log10(target)));
        double normalized = target / exponent;
        double nice = normalized >= 5 ? 5 : normalized >= 2 ? 2 : 1;
        return nice * exponent;
    }

    private static BitmapSource CreateCropPreview(
        SourceAssetItemViewModel source,
        PixelRect64 crop,
        ImageAdjustmentParameters? adjustments = null,
        int frameIndex = 0)
    {
        BitmapSource framePreview = source.GetFramePreview(frameIndex);
        double scaleX = framePreview.PixelWidth / (double)source.Width;
        double scaleY = framePreview.PixelHeight / (double)source.Height;
        int left = Math.Clamp((int)Math.Floor(crop.X * scaleX), 0, framePreview.PixelWidth - 1);
        int top = Math.Clamp((int)Math.Floor(crop.Y * scaleY), 0, framePreview.PixelHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(crop.Right * scaleX), left + 1, framePreview.PixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(crop.Bottom * scaleY), top + 1, framePreview.PixelHeight);

        var preview = new CroppedBitmap(
            framePreview,
            new System.Windows.Int32Rect(left, top, right - left, bottom - top));
        preview.Freeze();
        return WpfImageAdjustmentProcessor.Apply(preview, adjustments);
    }
}
