using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Templates;

namespace SciCanvas.Presentation;

public sealed class FigureCanvasViewModel : ObservableObject
{
    private readonly TemplateCanvasLayout _layout;
    private FigurePanelViewModel? _selectedPanel;
    private FigureAnnotationViewModel? _selectedAnnotation;
    private FigureGuideViewModel? _selectedGuide;
    private bool _isUpdatingPanelSelection;
    private bool _isSnappingEnabled = true;
    private double _snapTolerancePixels = 12;
    private long _exactSpacingPixels = 24;
    private string _backgroundColor;
    private string _lastValidBackgroundColor;
    private bool _autoPanelLabelsEnabled = true;
    private bool _showPanelLabels = true;
    private string _panelLabelSequence;
    private string _globalFontFamily = FigureGlobalStyle.Default.FontFamily;
    private double _globalFontSizePt = FigureGlobalStyle.Default.FontSizePt;
    private double _globalStrokeWidthPt = FigureGlobalStyle.Default.StrokeWidthPt;
    private string _globalTextColor = FigureGlobalStyle.Default.TextColor;
    private string _globalShapeColor = FigureGlobalStyle.Default.ShapeColor;
    private string _globalScaleBarColor = FigureGlobalStyle.Default.ScaleBarColor;
    private string _lastValidGlobalTextColor = FigureGlobalStyle.Default.TextColor;
    private string _lastValidGlobalShapeColor = FigureGlobalStyle.Default.ShapeColor;
    private string _lastValidGlobalScaleBarColor = FigureGlobalStyle.Default.ScaleBarColor;
    private bool _isSynchronizingLinkedCrops;
    private ScientificColorEntryViewModel? _selectedScientificColor;

    public event EventHandler? DocumentChanged;

    public event EventHandler? EditCompleted;

    public FigureCanvasViewModel(FigureTemplateDefinition template)
    {
        Template = template ?? throw new ArgumentNullException(nameof(template));
        _layout = TemplateLayoutEngine.CreateLayout(template);
        _backgroundColor = NormalizeTemplateBackground(template.Canvas.Background);
        _lastValidBackgroundColor = _backgroundColor;
        _panelLabelSequence = NormalizeLabelSequence(template.LabelStyle.Sequence);
        RemoveSelectedCommand = new RelayCommand(
            RemoveSelected,
            () => SelectedPanels.Any(panel => !panel.IsLocked));
        MoveLayerUpCommand = new RelayCommand(MoveLayerUp, () => SelectedPanel is { IsLocked: false });
        MoveLayerDownCommand = new RelayCommand(MoveLayerDown, () => SelectedPanel is { IsLocked: false });
        SelectAllPanelsCommand = new RelayCommand(SelectAllPanels, () => Panels.Count > 0);
        ClearPanelSelectionCommand = new RelayCommand(
            () => SelectOnlyPanel(null),
            () => SelectedPanelCount > 0);
        AlignPanelLeftCommand = new RelayCommand(
            () => AlignSelectedPanel(PanelAlignment.Left),
            CanAlignSelectedPanel);
        AlignPanelHorizontalCenterCommand = new RelayCommand(
            () => AlignSelectedPanel(PanelAlignment.HorizontalCenter),
            CanAlignSelectedPanel);
        AlignPanelRightCommand = new RelayCommand(
            () => AlignSelectedPanel(PanelAlignment.Right),
            CanAlignSelectedPanel);
        AlignPanelTopCommand = new RelayCommand(
            () => AlignSelectedPanel(PanelAlignment.Top),
            CanAlignSelectedPanel);
        AlignPanelVerticalCenterCommand = new RelayCommand(
            () => AlignSelectedPanel(PanelAlignment.VerticalCenter),
            CanAlignSelectedPanel);
        AlignPanelBottomCommand = new RelayCommand(
            () => AlignSelectedPanel(PanelAlignment.Bottom),
            CanAlignSelectedPanel);
        AlignSelectionLeftCommand = new RelayCommand(
            () => AlignPanelSelection(PanelAlignment.Left),
            CanAlignPanelSelection);
        AlignSelectionHorizontalCenterCommand = new RelayCommand(
            () => AlignPanelSelection(PanelAlignment.HorizontalCenter),
            CanAlignPanelSelection);
        AlignSelectionRightCommand = new RelayCommand(
            () => AlignPanelSelection(PanelAlignment.Right),
            CanAlignPanelSelection);
        AlignSelectionTopCommand = new RelayCommand(
            () => AlignPanelSelection(PanelAlignment.Top),
            CanAlignPanelSelection);
        AlignSelectionVerticalCenterCommand = new RelayCommand(
            () => AlignPanelSelection(PanelAlignment.VerticalCenter),
            CanAlignPanelSelection);
        AlignSelectionBottomCommand = new RelayCommand(
            () => AlignPanelSelection(PanelAlignment.Bottom),
            CanAlignPanelSelection);
        DistributeSelectionHorizontallyCommand = new RelayCommand(
            () => DistributePanelSelection(horizontal: true),
            CanDistributePanelSelection);
        DistributeSelectionVerticallyCommand = new RelayCommand(
            () => DistributePanelSelection(horizontal: false),
            CanDistributePanelSelection);
        SetHorizontalSpacingCommand = new RelayCommand(
            () => SetExactPanelSpacing(horizontal: true),
            () => CanSetExactPanelSpacing(horizontal: true));
        SetVerticalSpacingCommand = new RelayCommand(
            () => SetExactPanelSpacing(horizontal: false),
            () => CanSetExactPanelSpacing(horizontal: false));
        MatchSelectionWidthCommand = new RelayCommand(
            () => MatchPanelSelection(PanelMatchMode.Width),
            CanMatchPanelSelection);
        MatchSelectionHeightCommand = new RelayCommand(
            () => MatchPanelSelection(PanelMatchMode.Height),
            CanMatchPanelSelection);
        MatchSelectionFrameCommand = new RelayCommand(
            () => MatchPanelSelection(PanelMatchMode.Frame),
            CanMatchPanelSelection);
        MatchSelectionAspectRatioCommand = new RelayCommand(
            () => MatchPanelSelection(PanelMatchMode.AspectRatio),
            CanMatchPanelSelection);
        AddVerticalGuideCommand = new RelayCommand(
            () => AddGuide(FigureGuideOrientation.Vertical));
        AddHorizontalGuideCommand = new RelayCommand(
            () => AddGuide(FigureGuideOrientation.Horizontal));
        RemoveSelectedGuideCommand = new RelayCommand(
            RemoveSelectedGuide,
            () => SelectedGuide is { IsLocked: false });
        ResetBackgroundCommand = new RelayCommand(() => BackgroundColor = "#FFFFFFFF");
        RenumberPanelLabelsCommand = new RelayCommand(
            () => RenumberPanelLabels(force: true),
            () => Panels.Count > 0);
        ApplyGlobalStyleCommand = new RelayCommand(ApplyGlobalStyleToAnnotations, () => IsGlobalStyleValid);
        foreach (ScientificColorDefinition definition in ScientificColorPalette.Default)
        {
            AddScientificColorEntry(definition);
        }
        SelectedScientificColor = ScientificColors.FirstOrDefault();
        AddScientificColorCommand = new RelayCommand(AddScientificColor);
        RemoveSelectedScientificColorCommand = new RelayCommand(
            RemoveSelectedScientificColor,
            () => SelectedScientificColor is not null && ScientificColors.Count > 1);
        ApplySelectedScientificColorCommand = new RelayCommand(
            ApplySelectedScientificColor,
            () => SelectedScientificColor?.Definition.IsValid == true);
        CreateInsetCommand = new RelayCommand(CreateInsetFromSelectedPanel, () => SelectedPanel is not null);
        LinkSelectedPanelCropsCommand = new RelayCommand(LinkSelectedPanelCrops, CanLinkSelectedPanelCrops);
        UnlinkSelectedPanelCropsCommand = new RelayCommand(
            UnlinkSelectedPanelCrops,
            () => SelectedPanels.Any(panel => panel.IsCropLinked));
        AddTextAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Text));
        AddArrowAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Arrow));
        AddLineAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Line));
        AddRectangleAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Rectangle));
        AddEllipseAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Ellipse));
        RemoveSelectedAnnotationCommand = new RelayCommand(
            RemoveSelectedAnnotation,
            () => SelectedAnnotation is { IsLocked: false });
        MoveAnnotationUpCommand = new RelayCommand(
            MoveAnnotationUp,
            () => SelectedAnnotation is { IsLocked: false });
        MoveAnnotationDownCommand = new RelayCommand(
            MoveAnnotationDown,
            () => SelectedAnnotation is { IsLocked: false });
    }

    public FigureTemplateDefinition Template { get; }

    public ObservableCollection<FigurePanelViewModel> Panels { get; } = [];

    public ObservableCollection<FigureAnnotationViewModel> Annotations { get; } = [];

    public ObservableCollection<FigureGuideViewModel> Guides { get; } = [];

    public ObservableCollection<ScientificColorEntryViewModel> ScientificColors { get; } = [];

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand MoveLayerUpCommand { get; }

    public RelayCommand MoveLayerDownCommand { get; }

    public RelayCommand SelectAllPanelsCommand { get; }

    public RelayCommand ClearPanelSelectionCommand { get; }

    public RelayCommand AlignPanelLeftCommand { get; }

    public RelayCommand AlignPanelHorizontalCenterCommand { get; }

    public RelayCommand AlignPanelRightCommand { get; }

    public RelayCommand AlignPanelTopCommand { get; }

    public RelayCommand AlignPanelVerticalCenterCommand { get; }

    public RelayCommand AlignPanelBottomCommand { get; }

    public RelayCommand AlignSelectionLeftCommand { get; }

    public RelayCommand AlignSelectionHorizontalCenterCommand { get; }

    public RelayCommand AlignSelectionRightCommand { get; }

    public RelayCommand AlignSelectionTopCommand { get; }

    public RelayCommand AlignSelectionVerticalCenterCommand { get; }

    public RelayCommand AlignSelectionBottomCommand { get; }

    public RelayCommand DistributeSelectionHorizontallyCommand { get; }

    public RelayCommand DistributeSelectionVerticallyCommand { get; }

    public RelayCommand SetHorizontalSpacingCommand { get; }

    public RelayCommand SetVerticalSpacingCommand { get; }

    public RelayCommand MatchSelectionWidthCommand { get; }

    public RelayCommand MatchSelectionHeightCommand { get; }

    public RelayCommand MatchSelectionFrameCommand { get; }

    public RelayCommand MatchSelectionAspectRatioCommand { get; }

    public RelayCommand AddVerticalGuideCommand { get; }

    public RelayCommand AddHorizontalGuideCommand { get; }

    public RelayCommand RemoveSelectedGuideCommand { get; }

    public RelayCommand AddTextAnnotationCommand { get; }

    public RelayCommand AddArrowAnnotationCommand { get; }

    public RelayCommand AddLineAnnotationCommand { get; }

    public RelayCommand AddRectangleAnnotationCommand { get; }

    public RelayCommand AddEllipseAnnotationCommand { get; }

    public RelayCommand RemoveSelectedAnnotationCommand { get; }

    public RelayCommand MoveAnnotationUpCommand { get; }

    public RelayCommand MoveAnnotationDownCommand { get; }

    public RelayCommand ResetBackgroundCommand { get; }

    public RelayCommand RenumberPanelLabelsCommand { get; }

    public RelayCommand ApplyGlobalStyleCommand { get; }

    public RelayCommand AddScientificColorCommand { get; }

    public RelayCommand RemoveSelectedScientificColorCommand { get; }

    public RelayCommand ApplySelectedScientificColorCommand { get; }

    public RelayCommand CreateInsetCommand { get; }

    public RelayCommand LinkSelectedPanelCropsCommand { get; }

    public RelayCommand UnlinkSelectedPanelCropsCommand { get; }

    public string TemplateName => _layout.TemplateName;

    public string TemplateDescription => Template.Description;

    public int CanvasWidth => _layout.WidthPixels;

    public int CanvasHeight => _layout.HeightPixels;

    public int Dpi => _layout.Dpi;

    public string BackgroundColor
    {
        get => _backgroundColor;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!SetProperty(ref _backgroundColor, normalized))
            {
                return;
            }

            if (TryNormalizeColor(normalized, out string validColor))
            {
                _lastValidBackgroundColor = validColor;
            }

            OnPropertyChanged(nameof(BackgroundBrush));
            OnPropertyChanged(nameof(IsBackgroundColorValid));
            OnPropertyChanged(nameof(BackgroundValidationMessage));
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public Brush BackgroundBrush
    {
        get
        {
            Color color = ParseColor(_lastValidBackgroundColor);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public bool IsBackgroundColorValid => TryNormalizeColor(BackgroundColor, out _);

    public string BackgroundValidationMessage => IsBackgroundColorValid
        ? $"导出背景：{NormalizedBackgroundColor}"
        : "请输入 #RRGGBB 或 #AARRGGBB；修正前导出会被阻止。";

    public string NormalizedBackgroundColor =>
        TryNormalizeColor(BackgroundColor, out string normalized)
            ? normalized
            : throw new InvalidOperationException("画布背景颜色无效，请使用 #RRGGBB 或 #AARRGGBB。");

    public bool AutoPanelLabelsEnabled
    {
        get => _autoPanelLabelsEnabled;
        set
        {
            if (SetProperty(ref _autoPanelLabelsEnabled, value))
            {
                if (value)
                {
                    RenumberPanelLabels(force: true);
                }

                OnPropertyChanged(nameof(PanelLabelSettingsText));
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool ShowPanelLabels
    {
        get => _showPanelLabels;
        set
        {
            if (SetProperty(ref _showPanelLabels, value))
            {
                OnPropertyChanged(nameof(PanelLabelSettingsText));
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string PanelLabelSequence
    {
        get => _panelLabelSequence;
        set
        {
            string normalized = NormalizeLabelSequence(value);
            if (SetProperty(ref _panelLabelSequence, normalized))
            {
                if (AutoPanelLabelsEnabled)
                {
                    RenumberPanelLabels(force: true);
                }

                OnPropertyChanged(nameof(PanelLabelSettingsText));
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string PanelLabelSettingsText => !ShowPanelLabels
        ? "最终导出不显示面板编号。"
        : AutoPanelLabelsEnabled
            ? "新增、删除或切换编号序列时自动更新；可按画布位置重新编号。"
            : "自动编号已关闭，可直接编辑选中面板的编号。";

    public string GlobalFontFamily
    {
        get => _globalFontFamily;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _globalFontFamily, normalized))
            {
                NotifyGlobalStyleChanged();
            }
        }
    }

    public double GlobalFontSizePt
    {
        get => _globalFontSizePt;
        set
        {
            if (SetProperty(ref _globalFontSizePt, value))
            {
                OnPropertyChanged(nameof(GlobalFontSizePixels));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public double GlobalStrokeWidthPt
    {
        get => _globalStrokeWidthPt;
        set
        {
            if (SetProperty(ref _globalStrokeWidthPt, value))
            {
                OnPropertyChanged(nameof(GlobalStrokeWidthPixels));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public string GlobalTextColor
    {
        get => _globalTextColor;
        set => SetGlobalColor(ref _globalTextColor, ref _lastValidGlobalTextColor, value, nameof(GlobalTextColor), nameof(GlobalTextBrush));
    }

    public string GlobalShapeColor
    {
        get => _globalShapeColor;
        set => SetGlobalColor(ref _globalShapeColor, ref _lastValidGlobalShapeColor, value, nameof(GlobalShapeColor), nameof(GlobalShapeBrush));
    }

    public string GlobalScaleBarColor
    {
        get => _globalScaleBarColor;
        set => SetGlobalColor(ref _globalScaleBarColor, ref _lastValidGlobalScaleBarColor, value, nameof(GlobalScaleBarColor), nameof(GlobalScaleBarBrush));
    }

    public double GlobalFontSizePixels => Math.Max(12, GlobalFontSizePt / 72.0 * Dpi);

    public double GlobalStrokeWidthPixels => Math.Max(1, GlobalStrokeWidthPt / 72.0 * Dpi);

    public Brush GlobalTextBrush => CreateBrush(_lastValidGlobalTextColor);

    public Brush GlobalShapeBrush => CreateBrush(_lastValidGlobalShapeColor);

    public Brush GlobalScaleBarBrush => CreateBrush(_lastValidGlobalScaleBarColor);

    public FigureGlobalStyle GlobalStyle => new(
        GlobalFontFamily,
        GlobalFontSizePt,
        GlobalStrokeWidthPt,
        GlobalTextColor,
        GlobalShapeColor,
        GlobalScaleBarColor);

    public bool IsGlobalStyleValid => GlobalStyle.IsValid;

    public string GlobalStyleStatusText => IsGlobalStyleValid
        ? $"{GlobalFontFamily} · {GlobalFontSizePt:0.##} pt · {GlobalStrokeWidthPt:0.##} pt · 可一键统一 {Annotations.Count} 个标注"
        : "样式无效：字体 4–72 pt、线宽 0.25–10 pt，颜色使用 #RRGGBB 或 #AARRGGBB。";

    public ScientificColorEntryViewModel? SelectedScientificColor
    {
        get => _selectedScientificColor;
        set
        {
            if (SetProperty(ref _selectedScientificColor, value))
            {
                RemoveSelectedScientificColorCommand?.NotifyCanExecuteChanged();
                ApplySelectedScientificColorCommand?.NotifyCanExecuteChanged();
            }
        }
    }

    public string ScientificColorStatusText
    {
        get
        {
            ScientificColorPaletteReview review = ScientificColorPalette.Review(
                ScientificColors.Select(entry => entry.Definition));
            return review.IsValid
                ? $"{ScientificColors.Count} 个项目颜色 · 名称唯一 · 红绿色觉缺陷模拟未发现近似色"
                : string.Join(" ", review.Warnings);
        }
    }

    public int SlotCount => _layout.Slots.Count;

    public string CanvasSizeText => $"{CanvasWidth:N0} × {CanvasHeight:N0} px · {Dpi} dpi";

    public string PanelCountText
    {
        get
        {
            int insetCount = Panels.Count(panel => panel.IsInset);
            int regularCount = Panels.Count - insetCount;
            return insetCount == 0
                ? $"{regularCount} / {SlotCount} 个面板"
                : $"{regularCount} / {SlotCount} 个面板 · {insetCount} Inset";
        }
    }

    public IReadOnlyList<FigurePanelViewModel> SelectedPanels =>
        Panels.Where(panel => panel.IsSelected).ToArray();

    public int SelectedPanelCount => Panels.Count(panel => panel.IsSelected);

    public string SelectedPanelCountText => $"已选择 {SelectedPanelCount} 个面板";

    public Visibility MultiplePanelSelectionVisibility => SelectedPanelCount >= 2
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string AnnotationCountText => $"{Annotations.Count} 个标注";

    public string GuideCountText => $"{Guides.Count} 条";

    public bool IsSnappingEnabled
    {
        get => _isSnappingEnabled;
        set
        {
            if (SetProperty(ref _isSnappingEnabled, value))
            {
                OnPropertyChanged(nameof(SnapSettingsText));
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public double SnapTolerancePixels
    {
        get => _snapTolerancePixels;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 1, 100) : 12;
            if (SetProperty(ref _snapTolerancePixels, normalized))
            {
                OnPropertyChanged(nameof(SnapSettingsText));
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public long ExactSpacingPixels
    {
        get => _exactSpacingPixels;
        set
        {
            long normalized = Math.Clamp(value, 0, Math.Max(CanvasWidth, CanvasHeight));
            if (SetProperty(ref _exactSpacingPixels, normalized))
            {
                OnPropertyChanged(nameof(ExactSpacingStatusText));
                NotifyPanelAlignmentCanExecuteChanged();
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string SnapSettingsText => IsSnappingEnabled
        ? $"吸附已开启 · 阈值 {SnapTolerancePixels:0.##} px"
        : "吸附已关闭";

    public string ExactSpacingStatusText => SelectedPanelCount < 2
        ? "至少选择 2 个未锁定面板。"
        : SelectedPanels.Any(panel => panel.IsLocked)
            ? "精确间距不移动锁定面板，请取消选择或解除锁定。"
            : $"将相邻面板边界间距设为 {ExactSpacingPixels} px；超出画布的方向会禁用。";

    public Visibility SelectedAnnotationVisibility => SelectedAnnotation is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility SelectedGuideVisibility => SelectedGuide is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility EmptyVisibility => Panels.Count == 0 && Annotations.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public FigurePanelViewModel? SelectedPanel
    {
        get => _selectedPanel;
        set
        {
            if (value is not null)
            {
                SelectedAnnotation = null;
                SelectedGuide = null;
            }

            SelectOnlyPanel(value);
        }
    }

    public FigureAnnotationViewModel? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            if (ReferenceEquals(_selectedAnnotation, value))
            {
                return;
            }

            if (_selectedAnnotation is not null)
            {
                _selectedAnnotation.IsSelected = false;
            }

            _selectedAnnotation = value;
            if (_selectedAnnotation is not null)
            {
                SelectOnlyPanel(null);
                SelectedGuide = null;
                _selectedAnnotation.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedAnnotationVisibility));
            RemoveSelectedAnnotationCommand.NotifyCanExecuteChanged();
            MoveAnnotationUpCommand.NotifyCanExecuteChanged();
            MoveAnnotationDownCommand.NotifyCanExecuteChanged();
        }
    }

    public FigureGuideViewModel? SelectedGuide
    {
        get => _selectedGuide;
        set
        {
            if (ReferenceEquals(_selectedGuide, value))
            {
                return;
            }

            if (_selectedGuide is not null)
            {
                _selectedGuide.IsSelected = false;
            }

            _selectedGuide = value;
            if (_selectedGuide is not null)
            {
                SelectOnlyPanel(null);
                SelectedAnnotation = null;
                _selectedGuide.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedGuideVisibility));
            RemoveSelectedGuideCommand.NotifyCanExecuteChanged();
        }
    }

    public void SelectPanel(FigurePanelViewModel panel, bool toggle)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (!Panels.Contains(panel))
        {
            throw new InvalidOperationException("只能选择当前拼版中的面板。");
        }

        SelectedAnnotation = null;
        SelectedGuide = null;

        if (!toggle)
        {
            if (panel.IsSelected && SelectedPanelCount > 1)
            {
                SetPrimaryPanel(panel);
                return;
            }

            SelectOnlyPanel(panel);
            return;
        }

        _isUpdatingPanelSelection = true;
        try
        {
            panel.IsSelected = !panel.IsSelected;
        }
        finally
        {
            _isUpdatingPanelSelection = false;
        }

        SetPrimaryPanel(panel.IsSelected
            ? panel
            : SelectedPanels.LastOrDefault());
        NotifyPanelSelectionChanged();
    }

    public void RestorePanelSelection(IEnumerable<Guid> selectedPanelIds, Guid? primaryPanelId)
    {
        ArgumentNullException.ThrowIfNull(selectedPanelIds);
        HashSet<Guid> selectedIds = selectedPanelIds.ToHashSet();
        _isUpdatingPanelSelection = true;
        try
        {
            foreach (FigurePanelViewModel panel in Panels)
            {
                panel.IsSelected = selectedIds.Contains(panel.Id);
            }
        }
        finally
        {
            _isUpdatingPanelSelection = false;
        }

        FigurePanelViewModel? primary = primaryPanelId is Guid primaryId
            ? Panels.FirstOrDefault(panel => panel.Id == primaryId && panel.IsSelected)
            : null;
        SetPrimaryPanel(primary ?? SelectedPanels.LastOrDefault());
        NotifyPanelSelectionChanged();
    }

    private void SelectAllPanels()
    {
        _isUpdatingPanelSelection = true;
        try
        {
            foreach (FigurePanelViewModel panel in Panels)
            {
                panel.IsSelected = true;
            }
        }
        finally
        {
            _isUpdatingPanelSelection = false;
        }

        SetPrimaryPanel(Panels.LastOrDefault());
        NotifyPanelSelectionChanged();
    }

    public (long DeltaX, long DeltaY) MoveSelectedPanelsBy(long deltaX, long deltaY)
    {
        FigurePanelViewModel[] movable = SelectedPanels
            .Where(panel => !panel.IsLocked)
            .ToArray();
        if (movable.Length == 0 || (deltaX == 0 && deltaY == 0))
        {
            return (0, 0);
        }

        long minX = movable.Min(panel => panel.X);
        long minY = movable.Min(panel => panel.Y);
        long maxRight = movable.Max(panel => panel.X + panel.Width);
        long maxBottom = movable.Max(panel => panel.Y + panel.Height);
        long clampedX = Math.Clamp(deltaX, -minX, CanvasWidth - maxRight);
        long clampedY = Math.Clamp(deltaY, -minY, CanvasHeight - maxBottom);
        (long snapX, long snapY) = CalculateSnapAdjustment(
            movable,
            clampedX,
            clampedY);
        long finalX = Math.Clamp(
            clampedX + snapX,
            -minX,
            CanvasWidth - maxRight);
        long finalY = Math.Clamp(
            clampedY + snapY,
            -minY,
            CanvasHeight - maxBottom);
        foreach (FigurePanelViewModel panel in movable)
        {
            panel.X += finalX;
            panel.Y += finalY;
        }

        return (finalX, finalY);
    }

    private (long DeltaX, long DeltaY) CalculateSnapAdjustment(
        IReadOnlyList<FigurePanelViewModel> movable,
        long proposedDeltaX,
        long proposedDeltaY)
    {
        if (!IsSnappingEnabled || movable.Count == 0)
        {
            return (0, 0);
        }

        double left = movable.Min(panel => panel.X) + proposedDeltaX;
        double right = movable.Max(panel => panel.X + panel.Width) + proposedDeltaX;
        double top = movable.Min(panel => panel.Y) + proposedDeltaY;
        double bottom = movable.Max(panel => panel.Y + panel.Height) + proposedDeltaY;
        double[] sourceX = [left, (left + right) / 2.0, right];
        double[] sourceY = [top, (top + bottom) / 2.0, bottom];
        List<double> targetX = [0, CanvasWidth / 2.0, CanvasWidth];
        List<double> targetY = [0, CanvasHeight / 2.0, CanvasHeight];
        targetX.AddRange(Guides
            .Where(guide => guide.Orientation == FigureGuideOrientation.Vertical)
            .Select(guide => guide.Position));
        targetY.AddRange(Guides
            .Where(guide => guide.Orientation == FigureGuideOrientation.Horizontal)
            .Select(guide => guide.Position));

        foreach (FigurePanelViewModel panel in Panels.Where(
                     panel => !panel.IsSelected && panel.IsVisible))
        {
            targetX.Add(panel.X);
            targetX.Add(panel.X + panel.Width / 2.0);
            targetX.Add(panel.X + panel.Width);
            targetY.Add(panel.Y);
            targetY.Add(panel.Y + panel.Height / 2.0);
            targetY.Add(panel.Y + panel.Height);
        }

        return (
            FindNearestSnapDelta(sourceX, targetX),
            FindNearestSnapDelta(sourceY, targetY));
    }

    private long FindNearestSnapDelta(
        IReadOnlyList<double> sourcePositions,
        IReadOnlyList<double> targetPositions)
    {
        double bestDelta = 0;
        double bestDistance = SnapTolerancePixels + double.Epsilon;
        foreach (double source in sourcePositions)
        {
            foreach (double target in targetPositions)
            {
                double delta = target - source;
                double distance = Math.Abs(delta);
                if (distance <= SnapTolerancePixels && distance < bestDistance)
                {
                    bestDelta = delta;
                    bestDistance = distance;
                }
            }
        }

        return (long)Math.Round(bestDelta);
    }

    public FigureGuideViewModel RestoreGuide(
        Guid id,
        FigureGuideOrientation orientation,
        double position,
        bool isLocked)
    {
        var guide = new FigureGuideViewModel(
            orientation,
            CanvasWidth,
            CanvasHeight,
            position,
            isLocked,
            id);
        guide.PropertyChanged += OnGuidePropertyChanged;
        Guides.Add(guide);
        SelectedGuide = guide;
        NotifyGuideCollectionChanged();
        return guide;
    }

    public FigurePanelViewModel? AddPanel(
        SourceAssetItemViewModel source,
        PixelRect64 sourceRect)
    {
        TemplateSlotLayout? slot = _layout.Slots.FirstOrDefault(
            candidate => Panels.All(panel => panel.SlotId != candidate.Id));
        if (slot is null)
        {
            return null;
        }

        var panel = new FigurePanelViewModel(source, sourceRect, slot, Panels.Count, _layout.Dpi);
        panel.PropertyChanged += OnPanelPropertyChanged;
        Panels.Add(panel);
        RenumberPanelLabels(force: false);
        SelectedPanel = panel;
        NotifyPanelCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
        return panel;
    }

    public FigurePanelViewModel? RestorePanel(
        SourceAssetItemViewModel source,
        PixelRect64 sourceRect,
        string slotId,
        Guid layerId,
        PixelRect64 destinationRect,
        bool isVisible,
        bool isLocked,
        int zIndex,
        SciCanvas.Core.Images.ImageAdjustmentParameters? adjustments = null,
        int frameIndex = 0,
        bool? lockAspectRatio = null)
    {
        TemplateSlotLayout? slot = _layout.Slots.FirstOrDefault(
            candidate => string.Equals(candidate.Id, slotId, StringComparison.Ordinal));
        if (slot is null && IsInsetSlot(slotId))
        {
            slot = CreateInsetSlot(slotId, destinationRect);
        }

        if (slot is null || Panels.Any(panel => panel.SlotId == slot.Id))
        {
            return null;
        }

        var panel = new FigurePanelViewModel(source, sourceRect, slot, zIndex, _layout.Dpi, layerId)
        {
            X = destinationRect.X,
            Y = destinationRect.Y,
            IsVisible = isVisible,
            IsLocked = isLocked,
            Adjustments = adjustments ?? new(),
            FrameIndex = frameIndex,
        };
        panel.RestoreDestinationSize(destinationRect, lockAspectRatio ?? slot.LockAspectRatio);
        panel.PropertyChanged += OnPanelPropertyChanged;
        Panels.Add(panel);
        SelectedPanel = panel;
        NotifyPanelCollectionChanged();
        return panel;
    }

    public void Clear()
    {
        foreach (FigurePanelViewModel panel in Panels)
        {
            panel.PropertyChanged -= OnPanelPropertyChanged;
        }

        Panels.Clear();
        SelectedPanel = null;
        foreach (FigureAnnotationViewModel annotation in Annotations)
        {
            annotation.PropertyChanged -= OnAnnotationPropertyChanged;
        }

        Annotations.Clear();
        SelectedAnnotation = null;
        foreach (FigureGuideViewModel guide in Guides)
        {
            guide.PropertyChanged -= OnGuidePropertyChanged;
        }

        Guides.Clear();
        SelectedGuide = null;
        NotifyPanelCollectionChanged();
        NotifyAnnotationCollectionChanged();
        NotifyGuideCollectionChanged();
    }

    public void MovePanel(FigurePanelViewModel panel, long x, long y)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (panel.IsLocked)
        {
            return;
        }

        panel.X = Math.Clamp(x, 0, Math.Max(0, CanvasWidth - panel.Width));
        panel.Y = Math.Clamp(y, 0, Math.Max(0, CanvasHeight - panel.Height));
    }

    public FigureExportDocument CreateExportDocument()
    {
        FigurePanelExportItem[] panels = Panels
            .OrderBy(panel => panel.ZIndex)
            .Select(panel => new FigurePanelExportItem(
                panel.Source.Asset,
                panel.SourceRect,
                panel.DestinationRect,
                ShowPanelLabels ? panel.Label : string.Empty,
                panel.IsVisible,
                panel.CreateScaleBarExportSpec(),
                panel.Adjustments,
                panel.FrameIndex,
                panel.IsInset))
            .ToArray();
        FigureAnnotationExportItem[] annotations = Annotations
            .OrderBy(annotation => annotation.ZIndex)
            .Select(annotation => annotation.CreateExportItem())
            .ToArray();
        return new FigureExportDocument(
            CanvasWidth,
            CanvasHeight,
            Dpi,
            panels,
            annotations,
            NormalizedBackgroundColor,
            globalStyle: GlobalStyle);
    }

    public int ResetRegularPanelsToTemplateLayout()
    {
        int updated = 0;
        foreach (FigurePanelViewModel panel in Panels.Where(panel => !panel.IsInset))
        {
            TemplateSlotLayout? slot = _layout.Slots.FirstOrDefault(
                candidate => string.Equals(candidate.Id, panel.SlotId, StringComparison.Ordinal));
            if (slot is null)
            {
                continue;
            }

            panel.X = slot.PixelRect.X;
            panel.Y = slot.PixelRect.Y;
            panel.RestoreDestinationSize(slot.PixelRect, slot.LockAspectRatio);
            updated++;
        }

        if (updated > 0)
        {
            RenumberPanelLabels(force: true);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }

        return updated;
    }

    public void RestoreGlobalStyle(FigureGlobalStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        style.EnsureValid();
        GlobalFontFamily = style.FontFamily;
        GlobalFontSizePt = style.FontSizePt;
        GlobalStrokeWidthPt = style.StrokeWidthPt;
        GlobalTextColor = style.TextColor;
        GlobalShapeColor = style.ShapeColor;
        GlobalScaleBarColor = style.ScaleBarColor;
    }

    public void RestoreScientificColors(IEnumerable<ScientificColorDefinition>? definitions)
    {
        foreach (ScientificColorEntryViewModel entry in ScientificColors)
        {
            entry.Changed -= OnScientificColorChanged;
        }

        ScientificColors.Clear();
        ScientificColorDefinition[] restored = definitions?.Where(definition => definition.IsValid).ToArray() ?? [];
        foreach (ScientificColorDefinition definition in restored.Length == 0
                     ? ScientificColorPalette.Default
                     : restored)
        {
            AddScientificColorEntry(definition);
        }

        SelectedScientificColor = ScientificColors.FirstOrDefault();
        NotifyScientificColorStateChanged();
    }

    public FigureAnnotationViewModel RestoreAnnotation(
        Guid id,
        FigureAnnotationKind kind,
        double x,
        double y,
        double endX,
        double endY,
        string text,
        string color,
        double fontSizePt,
        double strokeWidthPt,
        bool isBold,
        bool isVisible,
        bool isLocked,
        int zIndex)
    {
        var annotation = new FigureAnnotationViewModel(
            kind,
            CanvasWidth,
            CanvasHeight,
            Dpi,
            zIndex,
            id)
        {
            X = x,
            Y = y,
            EndX = endX,
            EndY = endY,
            Text = text,
            Color = color,
            FontSizePt = fontSizePt,
            StrokeWidthPt = strokeWidthPt,
            IsBold = isBold,
            IsVisible = isVisible,
            IsLocked = isLocked,
        };
        annotation.PropertyChanged += OnAnnotationPropertyChanged;
        Annotations.Add(annotation);
        SelectedAnnotation = annotation;
        NotifyAnnotationCollectionChanged();
        return annotation;
    }

    public void MoveAnnotation(FigureAnnotationViewModel annotation, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        annotation.MoveBy(deltaX, deltaY);
    }

    private void RemoveSelected()
    {
        FigurePanelViewModel[] selected = SelectedPanels
            .Where(panel => !panel.IsLocked)
            .ToArray();
        if (selected.Length == 0)
        {
            return;
        }

        int index = selected.Min(Panels.IndexOf);
        foreach (FigurePanelViewModel panel in selected)
        {
            panel.PropertyChanged -= OnPanelPropertyChanged;
            Panels.Remove(panel);
        }

        SelectedPanel = Panels.Count == 0
            ? null
            : Panels[Math.Clamp(index, 0, Panels.Count - 1)];
        NormalizeZIndexes();
        RenumberPanelLabels(force: false);
        NotifyPanelCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void MoveLayerUp()
    {
        HashSet<FigurePanelViewModel> selected = SelectedPanels.ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }

        bool moved = false;
        for (int index = Panels.Count - 2; index >= 0; index--)
        {
            if (selected.Contains(Panels[index]) && !selected.Contains(Panels[index + 1]))
            {
                Panels.Move(index, index + 1);
                moved = true;
            }
        }

        if (moved)
        {
            NormalizeZIndexes();
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MoveLayerDown()
    {
        HashSet<FigurePanelViewModel> selected = SelectedPanels.ToHashSet();
        if (selected.Count == 0)
        {
            return;
        }

        bool moved = false;
        for (int index = 1; index < Panels.Count; index++)
        {
            if (selected.Contains(Panels[index]) && !selected.Contains(Panels[index - 1]))
            {
                Panels.Move(index, index - 1);
                moved = true;
            }
        }

        if (moved)
        {
            NormalizeZIndexes();
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AddAnnotation(FigureAnnotationKind kind)
    {
        FigureAnnotationViewModel? previous = SelectedAnnotation;
        var annotation = new FigureAnnotationViewModel(
            kind,
            CanvasWidth,
            CanvasHeight,
            Dpi,
            Annotations.Count)
        {
            Color = previous?.Color ??
                    (kind == FigureAnnotationKind.Text ? GlobalTextColor : GlobalShapeColor),
            FontSizePt = previous?.FontSizePt ?? GlobalFontSizePt,
            StrokeWidthPt = previous?.StrokeWidthPt ?? GlobalStrokeWidthPt,
            IsBold = previous?.IsBold ?? false,
        };
        annotation.PropertyChanged += OnAnnotationPropertyChanged;
        Annotations.Add(annotation);
        SelectedAnnotation = annotation;
        NotifyAnnotationCollectionChanged();
    }

    private void AddGuide(FigureGuideOrientation orientation)
    {
        double position = orientation == FigureGuideOrientation.Vertical
            ? CanvasWidth / 2.0
            : CanvasHeight / 2.0;
        var guide = new FigureGuideViewModel(
            orientation,
            CanvasWidth,
            CanvasHeight,
            position);
        guide.PropertyChanged += OnGuidePropertyChanged;
        Guides.Add(guide);
        SelectedGuide = guide;
        NotifyGuideCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveSelectedGuide()
    {
        if (SelectedGuide is null || SelectedGuide.IsLocked)
        {
            return;
        }

        int index = Guides.IndexOf(SelectedGuide);
        SelectedGuide.PropertyChanged -= OnGuidePropertyChanged;
        Guides.Remove(SelectedGuide);
        SelectedGuide = Guides.Count == 0
            ? null
            : Guides[Math.Clamp(index, 0, Guides.Count - 1)];
        NotifyGuideCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveSelectedAnnotation()
    {
        if (SelectedAnnotation is null || SelectedAnnotation.IsLocked)
        {
            return;
        }

        int index = Annotations.IndexOf(SelectedAnnotation);
        SelectedAnnotation.PropertyChanged -= OnAnnotationPropertyChanged;
        Annotations.Remove(SelectedAnnotation);
        SelectedAnnotation = Annotations.Count == 0
            ? null
            : Annotations[Math.Clamp(index, 0, Annotations.Count - 1)];
        NormalizeAnnotationZIndexes();
        NotifyAnnotationCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void MoveAnnotationUp()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        int index = Annotations.IndexOf(SelectedAnnotation);
        if (index < Annotations.Count - 1)
        {
            Annotations.Move(index, index + 1);
            NormalizeAnnotationZIndexes();
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void MoveAnnotationDown()
    {
        if (SelectedAnnotation is null)
        {
            return;
        }

        int index = Annotations.IndexOf(SelectedAnnotation);
        if (index > 0)
        {
            Annotations.Move(index, index - 1);
            NormalizeAnnotationZIndexes();
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private void AlignSelectedPanel(PanelAlignment alignment)
    {
        if (SelectedPanel is null || SelectedPanel.IsLocked)
        {
            return;
        }

        long x = SelectedPanel.X;
        long y = SelectedPanel.Y;
        switch (alignment)
        {
            case PanelAlignment.Left:
                x = 0;
                break;
            case PanelAlignment.HorizontalCenter:
                x = Math.Max(0, (CanvasWidth - SelectedPanel.Width) / 2);
                break;
            case PanelAlignment.Right:
                x = Math.Max(0, CanvasWidth - SelectedPanel.Width);
                break;
            case PanelAlignment.Top:
                y = 0;
                break;
            case PanelAlignment.VerticalCenter:
                y = Math.Max(0, (CanvasHeight - SelectedPanel.Height) / 2);
                break;
            case PanelAlignment.Bottom:
                y = Math.Max(0, CanvasHeight - SelectedPanel.Height);
                break;
        }

        MovePanel(SelectedPanel, x, y);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAlignSelectedPanel() => SelectedPanel is { IsLocked: false };

    private void AlignPanelSelection(PanelAlignment alignment)
    {
        FigurePanelViewModel[] selected = SelectedPanels.ToArray();
        if (selected.Length < 2)
        {
            return;
        }

        long left = selected.Min(panel => panel.X);
        long top = selected.Min(panel => panel.Y);
        long right = selected.Max(panel => panel.X + panel.Width);
        long bottom = selected.Max(panel => panel.Y + panel.Height);
        double horizontalCenter = (left + right) / 2.0;
        double verticalCenter = (top + bottom) / 2.0;

        foreach (FigurePanelViewModel panel in selected.Where(panel => !panel.IsLocked))
        {
            long x = panel.X;
            long y = panel.Y;
            switch (alignment)
            {
                case PanelAlignment.Left:
                    x = left;
                    break;
                case PanelAlignment.HorizontalCenter:
                    x = (long)Math.Round(horizontalCenter - panel.Width / 2.0);
                    break;
                case PanelAlignment.Right:
                    x = right - panel.Width;
                    break;
                case PanelAlignment.Top:
                    y = top;
                    break;
                case PanelAlignment.VerticalCenter:
                    y = (long)Math.Round(verticalCenter - panel.Height / 2.0);
                    break;
                case PanelAlignment.Bottom:
                    y = bottom - panel.Height;
                    break;
            }

            MovePanel(panel, x, y);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void DistributePanelSelection(bool horizontal)
    {
        FigurePanelViewModel[] selected = horizontal
            ? SelectedPanels.OrderBy(panel => panel.X).ToArray()
            : SelectedPanels.OrderBy(panel => panel.Y).ToArray();
        if (selected.Length < 3 || selected.Any(panel => panel.IsLocked))
        {
            return;
        }

        if (horizontal)
        {
            double span = selected[^1].X + selected[^1].Width - selected[0].X;
            double content = selected.Sum(panel => (double)panel.Width);
            double gap = (span - content) / (selected.Length - 1);
            double cursor = selected[0].X + selected[0].Width + gap;
            for (int index = 1; index < selected.Length - 1; index++)
            {
                MovePanel(selected[index], (long)Math.Round(cursor), selected[index].Y);
                cursor += selected[index].Width + gap;
            }
        }
        else
        {
            double span = selected[^1].Y + selected[^1].Height - selected[0].Y;
            double content = selected.Sum(panel => (double)panel.Height);
            double gap = (span - content) / (selected.Length - 1);
            double cursor = selected[0].Y + selected[0].Height + gap;
            for (int index = 1; index < selected.Length - 1; index++)
            {
                MovePanel(selected[index], selected[index].X, (long)Math.Round(cursor));
                cursor += selected[index].Height + gap;
            }
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAlignPanelSelection() =>
        SelectedPanelCount >= 2 && SelectedPanels.Any(panel => !panel.IsLocked);

    private bool CanDistributePanelSelection() =>
        SelectedPanelCount >= 3 && SelectedPanels.All(panel => !panel.IsLocked);

    private void SetExactPanelSpacing(bool horizontal)
    {
        FigurePanelViewModel[] selected = horizontal
            ? SelectedPanels.OrderBy(panel => panel.X).ToArray()
            : SelectedPanels.OrderBy(panel => panel.Y).ToArray();
        if (!CanSetExactPanelSpacing(horizontal))
        {
            return;
        }

        long cursor = horizontal
            ? selected[0].X + selected[0].Width + ExactSpacingPixels
            : selected[0].Y + selected[0].Height + ExactSpacingPixels;
        for (int index = 1; index < selected.Length; index++)
        {
            if (horizontal)
            {
                MovePanel(selected[index], cursor, selected[index].Y);
                cursor += selected[index].Width + ExactSpacingPixels;
            }
            else
            {
                MovePanel(selected[index], selected[index].X, cursor);
                cursor += selected[index].Height + ExactSpacingPixels;
            }
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSetExactPanelSpacing(bool horizontal)
    {
        FigurePanelViewModel[] selected = horizontal
            ? SelectedPanels.OrderBy(panel => panel.X).ToArray()
            : SelectedPanels.OrderBy(panel => panel.Y).ToArray();
        if (selected.Length < 2 || selected.Any(panel => panel.IsLocked))
        {
            return false;
        }

        long totalSize = horizontal
            ? selected.Sum(panel => panel.Width)
            : selected.Sum(panel => panel.Height);
        long requiredSpan = totalSize + ExactSpacingPixels * (selected.Length - 1L);
        long start = horizontal ? selected[0].X : selected[0].Y;
        long canvasSize = horizontal ? CanvasWidth : CanvasHeight;
        return start + requiredSpan <= canvasSize;
    }

    private bool CanMatchPanelSelection() =>
        SelectedPanelCount >= 2 && SelectedPanel is not null &&
        SelectedPanels.Any(panel => !ReferenceEquals(panel, SelectedPanel) && !panel.IsLocked);

    private void MatchPanelSelection(PanelMatchMode mode)
    {
        FigurePanelViewModel? reference = SelectedPanel;
        if (reference is null || !CanMatchPanelSelection())
        {
            return;
        }

        foreach (FigurePanelViewModel panel in SelectedPanels.Where(
                     panel => !ReferenceEquals(panel, reference) && !panel.IsLocked))
        {
            switch (mode)
            {
                case PanelMatchMode.Width:
                    panel.Width = Math.Min(reference.Width, CanvasWidth);
                    break;
                case PanelMatchMode.Height:
                    panel.Height = Math.Min(reference.Height, CanvasHeight);
                    break;
                case PanelMatchMode.Frame:
                    panel.SetMatchedFrameSize(
                        Math.Min(reference.Width, CanvasWidth),
                        Math.Min(reference.Height, CanvasHeight));
                    break;
                case PanelMatchMode.AspectRatio:
                    double aspectRatio = reference.Width / (double)Math.Max(1, reference.Height);
                    long width = panel.Width;
                    long height = Math.Max(1, (long)Math.Round(width / aspectRatio));
                    if (height > CanvasHeight)
                    {
                        height = CanvasHeight;
                        width = Math.Max(1, (long)Math.Round(height * aspectRatio));
                    }

                    panel.SetMatchedFrameSize(width, height);
                    break;
            }

            MovePanel(
                panel,
                Math.Clamp(panel.X, 0, Math.Max(0, CanvasWidth - panel.Width)),
                Math.Clamp(panel.Y, 0, Math.Max(0, CanvasHeight - panel.Height)));
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyGlobalStyleToAnnotations()
    {
        if (!IsGlobalStyleValid)
        {
            return;
        }

        foreach (FigureAnnotationViewModel annotation in Annotations)
        {
            if (annotation.Kind == FigureAnnotationKind.Text)
            {
                annotation.FontSizePt = GlobalFontSizePt;
                annotation.Color = GlobalTextColor;
            }
            else
            {
                annotation.StrokeWidthPt = GlobalStrokeWidthPt;
                annotation.Color = GlobalShapeColor;
            }
        }

        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(GlobalStyleStatusText));
    }

    private void AddScientificColor()
    {
        string[] palette = ["#FF56B4E9", "#FF009E73", "#FFF0E442", "#FFCC79A7", "#FF332288"];
        var definition = new ScientificColorDefinition(
            Guid.NewGuid(),
            $"Object {ScientificColors.Count + 1}",
            palette[ScientificColors.Count % palette.Length]);
        ScientificColorEntryViewModel entry = AddScientificColorEntry(definition);
        SelectedScientificColor = entry;
        NotifyScientificColorStateChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveSelectedScientificColor()
    {
        ScientificColorEntryViewModel? selected = SelectedScientificColor;
        if (selected is null || ScientificColors.Count <= 1)
        {
            return;
        }

        int index = ScientificColors.IndexOf(selected);
        selected.Changed -= OnScientificColorChanged;
        ScientificColors.Remove(selected);
        SelectedScientificColor = ScientificColors[Math.Clamp(index, 0, ScientificColors.Count - 1)];
        NotifyScientificColorStateChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplySelectedScientificColor()
    {
        ScientificColorDefinition? selected = SelectedScientificColor?.Definition;
        if (selected?.IsValid != true)
        {
            return;
        }

        if (SelectedAnnotation is not null)
        {
            SelectedAnnotation.Color = selected.Color;
        }
        else
        {
            GlobalShapeColor = selected.Color;
        }

        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private ScientificColorEntryViewModel AddScientificColorEntry(ScientificColorDefinition definition)
    {
        var entry = new ScientificColorEntryViewModel(definition);
        entry.Changed += OnScientificColorChanged;
        ScientificColors.Add(entry);
        return entry;
    }

    private void OnScientificColorChanged(object? sender, EventArgs e)
    {
        NotifyScientificColorStateChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyScientificColorStateChanged()
    {
        OnPropertyChanged(nameof(ScientificColorStatusText));
        RemoveSelectedScientificColorCommand.NotifyCanExecuteChanged();
        ApplySelectedScientificColorCommand.NotifyCanExecuteChanged();
    }

    private void CreateInsetFromSelectedPanel()
    {
        FigurePanelViewModel? reference = SelectedPanel;
        if (reference is null)
        {
            return;
        }

        PixelRect64 source = reference.SourceRect;
        long cropWidth = Math.Max(1, source.Width / 2);
        long cropHeight = Math.Max(1, source.Height / 2);
        var insetCrop = new PixelRect64(
            source.X + (source.Width - cropWidth) / 2,
            source.Y + (source.Height - cropHeight) / 2,
            cropWidth,
            cropHeight);
        long width = Math.Clamp((long)Math.Round(reference.Width * 0.36), 80, Math.Max(80, CanvasWidth));
        long height = Math.Max(1, (long)Math.Round(width * cropHeight / (double)cropWidth));
        if (height > CanvasHeight)
        {
            height = CanvasHeight;
            width = Math.Max(1, (long)Math.Round(height * cropWidth / (double)cropHeight));
        }

        long x = Math.Clamp(
            reference.X + reference.Width - width - 24,
            0,
            Math.Max(0, CanvasWidth - width));
        long y = Math.Clamp(
            reference.Y + reference.Height - height - 24,
            0,
            Math.Max(0, CanvasHeight - height));
        string slotId = $"inset:{Guid.NewGuid():N}";
        TemplateSlotLayout slot = CreateInsetSlot(slotId, new PixelRect64(x, y, width, height));
        var inset = new FigurePanelViewModel(reference.Source, insetCrop, slot, Panels.Count, _layout.Dpi)
        {
            Adjustments = reference.Adjustments,
            FrameIndex = reference.FrameIndex,
        };
        inset.ApplySpatialCalibration(reference.Source.Calibration.Calibration);
        inset.PropertyChanged += OnPanelPropertyChanged;
        Panels.Add(inset);
        SelectedPanel = inset;
        NotifyPanelCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanLinkSelectedPanelCrops() =>
        SelectedPanelCount >= 2 &&
        SelectedPanels.All(panel => !panel.IsLocked) &&
        SelectedPanels.Select(panel => panel.Source.Asset.Id).Distinct().Count() == 1;

    private void LinkSelectedPanelCrops()
    {
        if (!CanLinkSelectedPanelCrops())
        {
            return;
        }

        Guid groupId = Guid.NewGuid();
        foreach (FigurePanelViewModel panel in SelectedPanels)
        {
            panel.CropLinkGroupId = groupId;
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UnlinkSelectedPanelCrops()
    {
        foreach (FigurePanelViewModel panel in SelectedPanels)
        {
            panel.CropLinkGroupId = null;
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private static bool IsInsetSlot(string slotId) =>
        slotId.StartsWith("inset:", StringComparison.Ordinal) && slotId.Length > "inset:".Length;

    private static TemplateSlotLayout CreateInsetSlot(string slotId, PixelRect64 destination) => new(
        slotId,
        string.Empty,
        "inset",
        destination,
        300,
        false,
        true,
        "Inset 局部放大面板；导出时保留 0.5 pt 边框。");

    private void SetGlobalColor(
        ref string field,
        ref string lastValid,
        string? value,
        string propertyName,
        string brushPropertyName)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (!SetProperty(ref field, normalized, propertyName))
        {
            return;
        }

        if (TryNormalizeColor(normalized, out string valid))
        {
            lastValid = valid;
        }

        OnPropertyChanged(brushPropertyName);
        NotifyGlobalStyleChanged();
    }

    private void NotifyGlobalStyleChanged()
    {
        OnPropertyChanged(nameof(GlobalStyle));
        OnPropertyChanged(nameof(IsGlobalStyleValid));
        OnPropertyChanged(nameof(GlobalStyleStatusText));
        ApplyGlobalStyleCommand.NotifyCanExecuteChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private static Brush CreateBrush(string color)
    {
        var brush = new SolidColorBrush(ParseColor(color));
        brush.Freeze();
        return brush;
    }

    private void SelectOnlyPanel(FigurePanelViewModel? panel)
    {
        if (panel is not null && !Panels.Contains(panel))
        {
            throw new InvalidOperationException("只能选择当前拼版中的面板。");
        }

        _isUpdatingPanelSelection = true;
        try
        {
            foreach (FigurePanelViewModel candidate in Panels)
            {
                candidate.IsSelected = ReferenceEquals(candidate, panel);
            }
        }
        finally
        {
            _isUpdatingPanelSelection = false;
        }

        SetPrimaryPanel(panel);
        NotifyPanelSelectionChanged();
    }

    private void SetPrimaryPanel(FigurePanelViewModel? panel)
    {
        if (SetProperty(ref _selectedPanel, panel, nameof(SelectedPanel)))
        {
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            MoveLayerUpCommand.NotifyCanExecuteChanged();
            MoveLayerDownCommand.NotifyCanExecuteChanged();
            NotifyPanelAlignmentCanExecuteChanged();
        }
    }

    private void NotifyPanelSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedPanels));
        OnPropertyChanged(nameof(SelectedPanelCount));
        OnPropertyChanged(nameof(SelectedPanelCountText));
        OnPropertyChanged(nameof(MultiplePanelSelectionVisibility));
        OnPropertyChanged(nameof(ExactSpacingStatusText));
        RemoveSelectedCommand.NotifyCanExecuteChanged();
        MoveLayerUpCommand.NotifyCanExecuteChanged();
        MoveLayerDownCommand.NotifyCanExecuteChanged();
        SelectAllPanelsCommand.NotifyCanExecuteChanged();
        ClearPanelSelectionCommand.NotifyCanExecuteChanged();
        NotifyPanelAlignmentCanExecuteChanged();
    }

    private void NormalizeZIndexes()
    {
        for (int index = 0; index < Panels.Count; index++)
        {
            Panels[index].ZIndex = index;
        }
    }

    private void NormalizeAnnotationZIndexes()
    {
        for (int index = 0; index < Annotations.Count; index++)
        {
            Annotations[index].ZIndex = index;
        }

        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyPanelCollectionChanged()
    {
        OnPropertyChanged(nameof(PanelCountText));
        OnPropertyChanged(nameof(EmptyVisibility));
        NotifyPanelSelectionChanged();
        RenumberPanelLabelsCommand.NotifyCanExecuteChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyAnnotationCollectionChanged()
    {
        OnPropertyChanged(nameof(AnnotationCountText));
        OnPropertyChanged(nameof(GlobalStyleStatusText));
        OnPropertyChanged(nameof(EmptyVisibility));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyGuideCollectionChanged()
    {
        OnPropertyChanged(nameof(GuideCountText));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FigurePanelViewModel.Source) or
            nameof(FigurePanelViewModel.SourceRect) or
            nameof(FigurePanelViewModel.X) or
            nameof(FigurePanelViewModel.Y) or
            nameof(FigurePanelViewModel.Width) or
            nameof(FigurePanelViewModel.Height) or
            nameof(FigurePanelViewModel.IsAspectRatioLocked) or
            nameof(FigurePanelViewModel.IsVisible) or
            nameof(FigurePanelViewModel.IsLocked) or
            nameof(FigurePanelViewModel.ZIndex) or
            nameof(FigurePanelViewModel.ShowScaleBar) or
            nameof(FigurePanelViewModel.PhysicalUnitsPerSourcePixel) or
            nameof(FigurePanelViewModel.ScaleBarPhysicalLength) or
            nameof(FigurePanelViewModel.ScaleBarUnit) or
            nameof(FigurePanelViewModel.ScaleBarShowLabel) or
            nameof(FigurePanelViewModel.Adjustments) or
            nameof(FigurePanelViewModel.Brightness) or
            nameof(FigurePanelViewModel.Contrast) or
            nameof(FigurePanelViewModel.Gamma) or
            nameof(FigurePanelViewModel.BlackPoint) or
            nameof(FigurePanelViewModel.WhitePoint) or
            nameof(FigurePanelViewModel.Invert) or
            nameof(FigurePanelViewModel.Grayscale) or
            nameof(FigurePanelViewModel.Channel) or
            nameof(FigurePanelViewModel.CropLinkGroupId) or
            nameof(FigurePanelViewModel.Label))
        {
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (e.PropertyName == nameof(FigurePanelViewModel.SourceRect) &&
            sender is FigurePanelViewModel changedPanel &&
            changedPanel.CropLinkGroupId is Guid linkGroup &&
            !_isSynchronizingLinkedCrops)
        {
            _isSynchronizingLinkedCrops = true;
            try
            {
                foreach (FigurePanelViewModel linked in Panels.Where(panel =>
                             !ReferenceEquals(panel, changedPanel) &&
                             panel.CropLinkGroupId == linkGroup &&
                             !panel.IsLocked))
                {
                    linked.ReplaceSource(changedPanel.Source, changedPanel.SourceRect);
                    linked.ApplySpatialCalibration(changedPanel.Source.Calibration.Calibration);
                }
            }
            finally
            {
                _isSynchronizingLinkedCrops = false;
            }
        }

        if (e.PropertyName is nameof(FigurePanelViewModel.Source) or
            nameof(FigurePanelViewModel.SourceRect) or
            nameof(FigurePanelViewModel.X) or
            nameof(FigurePanelViewModel.Y) or
            nameof(FigurePanelViewModel.Width) or
            nameof(FigurePanelViewModel.Height))
        {
            OnPropertyChanged(nameof(ExactSpacingStatusText));
            SetHorizontalSpacingCommand.NotifyCanExecuteChanged();
            SetVerticalSpacingCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(FigurePanelViewModel.IsLocked))
        {
            OnPropertyChanged(nameof(ExactSpacingStatusText));
            NotifyPanelAlignmentCanExecuteChanged();
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            MoveLayerUpCommand.NotifyCanExecuteChanged();
            MoveLayerDownCommand.NotifyCanExecuteChanged();
        }

        if (e.PropertyName == nameof(FigurePanelViewModel.IsSelected) &&
            !_isUpdatingPanelSelection && sender is FigurePanelViewModel panel)
        {
            if (panel.IsSelected)
            {
                SelectedAnnotation = null;
                SelectedGuide = null;
            }

            SetPrimaryPanel(panel.IsSelected
                ? panel
                : ReferenceEquals(panel, SelectedPanel)
                    ? SelectedPanels.LastOrDefault()
                    : SelectedPanel);
            NotifyPanelSelectionChanged();
        }
    }

    private void OnAnnotationPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FigureAnnotationViewModel.X) or
            nameof(FigureAnnotationViewModel.Y) or
            nameof(FigureAnnotationViewModel.EndX) or
            nameof(FigureAnnotationViewModel.EndY) or
            nameof(FigureAnnotationViewModel.Text) or
            nameof(FigureAnnotationViewModel.Color) or
            nameof(FigureAnnotationViewModel.FontSizePt) or
            nameof(FigureAnnotationViewModel.StrokeWidthPt) or
            nameof(FigureAnnotationViewModel.IsBold) or
            nameof(FigureAnnotationViewModel.IsVisible) or
            nameof(FigureAnnotationViewModel.IsLocked) or
            nameof(FigureAnnotationViewModel.ZIndex))
        {
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (e.PropertyName == nameof(FigureAnnotationViewModel.IsLocked))
        {
            RemoveSelectedAnnotationCommand.NotifyCanExecuteChanged();
            MoveAnnotationUpCommand.NotifyCanExecuteChanged();
            MoveAnnotationDownCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnGuidePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FigureGuideViewModel.Position) or
            nameof(FigureGuideViewModel.IsLocked))
        {
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }

        if (e.PropertyName == nameof(FigureGuideViewModel.IsLocked))
        {
            RemoveSelectedGuideCommand.NotifyCanExecuteChanged();
        }
    }

    private void NotifyPanelAlignmentCanExecuteChanged()
    {
        AlignPanelLeftCommand.NotifyCanExecuteChanged();
        AlignPanelHorizontalCenterCommand.NotifyCanExecuteChanged();
        AlignPanelRightCommand.NotifyCanExecuteChanged();
        AlignPanelTopCommand.NotifyCanExecuteChanged();
        AlignPanelVerticalCenterCommand.NotifyCanExecuteChanged();
        AlignPanelBottomCommand.NotifyCanExecuteChanged();
        AlignSelectionLeftCommand.NotifyCanExecuteChanged();
        AlignSelectionHorizontalCenterCommand.NotifyCanExecuteChanged();
        AlignSelectionRightCommand.NotifyCanExecuteChanged();
        AlignSelectionTopCommand.NotifyCanExecuteChanged();
        AlignSelectionVerticalCenterCommand.NotifyCanExecuteChanged();
        AlignSelectionBottomCommand.NotifyCanExecuteChanged();
        DistributeSelectionHorizontallyCommand.NotifyCanExecuteChanged();
        DistributeSelectionVerticallyCommand.NotifyCanExecuteChanged();
        SetHorizontalSpacingCommand.NotifyCanExecuteChanged();
        SetVerticalSpacingCommand.NotifyCanExecuteChanged();
        MatchSelectionWidthCommand.NotifyCanExecuteChanged();
        MatchSelectionHeightCommand.NotifyCanExecuteChanged();
        MatchSelectionFrameCommand.NotifyCanExecuteChanged();
        MatchSelectionAspectRatioCommand.NotifyCanExecuteChanged();
        CreateInsetCommand.NotifyCanExecuteChanged();
        LinkSelectedPanelCropsCommand.NotifyCanExecuteChanged();
        UnlinkSelectedPanelCropsCommand.NotifyCanExecuteChanged();
    }

    public void RenumberPanelLabels(bool force)
    {
        if (!force && !AutoPanelLabelsEnabled)
        {
            return;
        }

        FigurePanelViewModel[] readingOrder = Panels
            .OrderBy(panel => panel.Y)
            .ThenBy(panel => panel.X)
            .ThenBy(panel => panel.ZIndex)
            .ToArray();
        for (int index = 0; index < readingOrder.Length; index++)
        {
            readingOrder[index].Label = CreatePanelLabel(index, PanelLabelSequence);
        }

        OnPropertyChanged(nameof(PanelLabelSettingsText));
        if (force && readingOrder.Length > 0)
        {
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string CreatePanelLabel(int index, string sequence)
    {
        if (sequence == "numeric")
        {
            return (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        int value = index;
        Span<char> buffer = stackalloc char[16];
        int position = buffer.Length;
        do
        {
            int digit = value % 26;
            buffer[--position] = (char)('a' + digit);
            value = value / 26 - 1;
        }
        while (value >= 0);

        string label = new(buffer[position..]);
        return sequence == "uppercase" ? label.ToUpperInvariant() : label;
    }

    private static string NormalizeLabelSequence(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "uppercase" => "uppercase",
        "numeric" => "numeric",
        _ => "lowercase",
    };

    private static string NormalizeTemplateBackground(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "black" => "#FF000000",
        "transparent" => "#00FFFFFF",
        _ => "#FFFFFFFF",
    };

    private static bool TryNormalizeColor(string? value, out string normalized)
    {
        string hex = value?.Trim().TrimStart('#') ?? string.Empty;
        if (hex.Length == 6 && hex.All(Uri.IsHexDigit))
        {
            normalized = $"#FF{hex.ToUpperInvariant()}";
            return true;
        }

        if (hex.Length == 8 && hex.All(Uri.IsHexDigit))
        {
            normalized = $"#{hex.ToUpperInvariant()}";
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static Color ParseColor(string value)
    {
        _ = TryNormalizeColor(value, out string normalized);
        return Color.FromArgb(
            byte.Parse(normalized.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(normalized.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(normalized.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(normalized.AsSpan(7, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    private enum PanelAlignment
    {
        Left,
        HorizontalCenter,
        Right,
        Top,
        VerticalCenter,
        Bottom,
    }

    private enum PanelMatchMode
    {
        Width,
        Height,
        Frame,
        AspectRatio,
    }
}
