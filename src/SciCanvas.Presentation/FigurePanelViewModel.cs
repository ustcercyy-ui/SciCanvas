using System.Windows.Media.Imaging;
using System.Windows;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
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
    private BitmapSource _preview;
    private string _label;

    public FigurePanelViewModel(
        SourceAssetItemViewModel source,
        PixelRect64 sourceRect,
        TemplateSlotLayout slot,
        int zIndex,
        Guid? id = null)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        Id = id ?? Guid.NewGuid();
        SourceRect = sourceRect;
        SlotId = slot.Id;
        _label = slot.Label;
        Role = slot.Role;
        MinimumEffectiveDpi = slot.MinimumEffectiveDpi;
        RequiresScaleBar = slot.RequireScaleBar;
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
        _preview = CreateCropPreview(source, sourceRect);
    }

    public SourceAssetItemViewModel Source { get; }

    public Guid Id { get; }

    public PixelRect64 SourceRect { get; }

    public BitmapSource Preview => _preview;

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
        set => SetProperty(ref _x, value);
    }

    public long Y
    {
        get => _y;
        set => SetProperty(ref _y, value);
    }

    public long Width
    {
        get => _width;
        set
        {
            if (SetProperty(ref _width, value))
            {
                OnPropertyChanged(nameof(EffectiveDpiText));
                OnPropertyChanged(nameof(IsBelowMinimumDpi));
                NotifyScaleBarGeometryChanged();
            }
        }
    }

    public long Height
    {
        get => _height;
        set
        {
            if (SetProperty(ref _height, value))
            {
                OnPropertyChanged(nameof(EffectiveDpiText));
                OnPropertyChanged(nameof(IsBelowMinimumDpi));
                NotifyScaleBarGeometryChanged();
            }
        }
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
        set => SetProperty(ref _isSelected, value);
    }

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

    public double EffectiveDpi => Math.Min(
        SourceRect.Width / (Width / 300.0),
        SourceRect.Height / (Height / 300.0));

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

    internal void RefreshPreview()
    {
        _preview = CreateCropPreview(Source, SourceRect);
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(EffectiveDpi));
        OnPropertyChanged(nameof(EffectiveDpiText));
        OnPropertyChanged(nameof(IsBelowMinimumDpi));
        NotifyScaleBarGeometryChanged();
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
        PixelRect64 crop)
    {
        double scaleX = source.Preview.PixelWidth / (double)source.Width;
        double scaleY = source.Preview.PixelHeight / (double)source.Height;
        int left = Math.Clamp((int)Math.Floor(crop.X * scaleX), 0, source.Preview.PixelWidth - 1);
        int top = Math.Clamp((int)Math.Floor(crop.Y * scaleY), 0, source.Preview.PixelHeight - 1);
        int right = Math.Clamp((int)Math.Ceiling(crop.Right * scaleX), left + 1, source.Preview.PixelWidth);
        int bottom = Math.Clamp((int)Math.Ceiling(crop.Bottom * scaleY), top + 1, source.Preview.PixelHeight);

        var preview = new CroppedBitmap(
            source.Preview,
            new System.Windows.Int32Rect(left, top, right - left, bottom - top));
        preview.Freeze();
        return preview;
    }
}
