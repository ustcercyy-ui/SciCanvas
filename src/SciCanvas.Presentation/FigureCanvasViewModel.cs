using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Cropping;
using SciCanvas.Core.Data;
using SciCanvas.Core.Export;
using SciCanvas.Core.Plotting;
using LinkGroup = SciCanvas.Core.Linking.LinkGroup;
using LinkSyncOptions = SciCanvas.Core.Linking.LinkSyncOptions;
using SpatialMapping = SciCanvas.Core.Linking.SpatialMapping;
using SpatialMappingKind = SciCanvas.Core.Linking.SpatialMappingKind;
using SpatialMappingOrigin = SciCanvas.Core.Linking.SpatialMappingOrigin;
using SpatialMatrix3x3 = SciCanvas.Core.Linking.SpatialMatrix3x3;
using RegistrationLandmarkPair = SciCanvas.Core.Linking.RegistrationLandmarkPair;
using SpatialMappingRevisionState = SciCanvas.Core.Linking.SpatialMappingRevisionState;
using SpatialRegistrationResult = SciCanvas.Core.Linking.SpatialRegistrationResult;
using SpatialRegistrationSolver = SciCanvas.Core.Linking.SpatialRegistrationSolver;using SciCanvas.Core.Geometry;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;
using SciCanvas.Templates;

namespace SciCanvas.Presentation;

public enum ScientificColorApplicationTarget
{
    Auto,
    AnnotationStroke,
    AnnotationFill,
    AnnotationText,
    ScaleBar,
    ScaleBarLabel,
    PanelLabel,
}

public sealed class FigureCanvasViewModel : ObservableObject
{
    private readonly TemplateCanvasLayout _layout;
    private FigurePanelViewModel? _selectedPanel;
    private FigurePlotPanelViewModel? _selectedPlotPanel;
    private FigureAnnotationViewModel? _selectedAnnotation;
    private FigureScientificObjectViewModel? _selectedScientificObject;
    private FigureGuideViewModel? _selectedGuide;
    private bool _isUpdatingPanelSelection;
    private bool _isUpdatingPlotPanelSelection;
    private bool _isUpdatingAnnotationSelection;
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
    private string _panelLabelFontFamily = FigureGlobalStyle.Default.EffectivePanelLabelFontFamily;
    private double _panelLabelFontSizePt = FigureGlobalStyle.Default.EffectivePanelLabelFontSizePt;
    private string _panelLabelTextColor = FigureGlobalStyle.Default.EffectivePanelLabelTextColor;
    private string _lastValidPanelLabelTextColor = FigureGlobalStyle.Default.EffectivePanelLabelTextColor;
    private bool _panelLabelIsBold = FigureGlobalStyle.Default.PanelLabelIsBold;
    private string _scaleBarLabelColor = FigureGlobalStyle.Default.EffectiveScaleBarLabelColor;
    private string _lastValidScaleBarLabelColor = FigureGlobalStyle.Default.EffectiveScaleBarLabelColor;
    private string _scaleBarFontFamily = FigureGlobalStyle.Default.EffectiveScaleBarFontFamily;
    private double _scaleBarFontSizePt = FigureGlobalStyle.Default.EffectiveScaleBarFontSizePt;
    private bool _scaleBarLabelIsBold = FigureGlobalStyle.Default.ScaleBarLabelIsBold;
    private double _scaleBarThicknessPt = FigureGlobalStyle.Default.EffectiveScaleBarThicknessPt;
    private bool _isUpdatingLinkGroupMembership;
    private ScientificColorEntryViewModel? _selectedScientificColor;
    private ScientificColorApplicationTarget _scientificColorApplicationTarget;
    private FigureAnnotationStyle? _copiedAnnotationStyle;
    private readonly Dictionary<Guid, RoiProjectionPanelState> _roiProjectionPanelStates = [];
    private bool _isRestoringRoiProjectionPanelState;
    private readonly List<FigureScientificPoint> _polygonAnnotationDraftPoints = [];
    private bool _isCreatingPolygonAnnotation;

    public event EventHandler? DocumentChanged;

    public event EventHandler? EditCompleted;

    public event EventHandler? LinkGroupsChanged;

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
        RemoveSelectedPlotPanelCommand = new RelayCommand(
            RemoveSelectedPlotPanel,
            () => SelectedPlotPanel is { IsLocked: false });
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
            () => Panels.Count > 0 || PlotPanels.Count > 0);
        ApplyGlobalStyleCommand = new RelayCommand(ApplyGlobalStyleToAnnotations, () => IsGlobalStyleValid);
        ResetSelectedPanelLabelStyleCommand = new RelayCommand(
            ResetSelectedPanelLabelStyle,
            () => SelectedPanel is { IsLocked: false, StyleOverride.PanelLabel: not null });
        ResetSelectedPanelScaleBarStyleCommand = new RelayCommand(
            ResetSelectedPanelScaleBarStyle,
            () => SelectedPanel is { IsLocked: false } panel &&
                  (panel.StyleOverride?.ScaleBarText is not null || panel.StyleOverride?.ScaleBar is not null));
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
        AddAdditionalScaleBarCommand = new RelayCommand(
            () => _ = AddAdditionalScaleBar(),
            () => SelectedPanel is { IsLocked: false });
        LinkSelectedPanelCropsCommand = new RelayCommand(LinkSelectedPanelCrops, CanLinkSelectedPanelCrops);
        UnlinkSelectedPanelCropsCommand = new RelayCommand(
            UnlinkSelectedPanelCrops,
            () => SelectedPanels.Any(panel => panel.IsCropLinked));
        AddTextAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Text));
        AddArrowAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Arrow));
        AddLineAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Line));
        AddRectangleAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Rectangle));
        AddEllipseAnnotationCommand = new RelayCommand(() => AddAnnotation(FigureAnnotationKind.Ellipse));
        SelectAllAnnotationsCommand = new RelayCommand(SelectAllAnnotations, () => Annotations.Count > 0);
        ClearAnnotationSelectionCommand = new RelayCommand(
            () => SelectOnlyAnnotation(null),
            () => SelectedAnnotationCount > 0);
        AlignAnnotationLeftCommand = new RelayCommand(
            () => AlignAnnotationSelection(PanelAlignment.Left),
            CanAlignAnnotationSelection);
        AlignAnnotationHorizontalCenterCommand = new RelayCommand(
            () => AlignAnnotationSelection(PanelAlignment.HorizontalCenter),
            CanAlignAnnotationSelection);
        AlignAnnotationRightCommand = new RelayCommand(
            () => AlignAnnotationSelection(PanelAlignment.Right),
            CanAlignAnnotationSelection);
        AlignAnnotationTopCommand = new RelayCommand(
            () => AlignAnnotationSelection(PanelAlignment.Top),
            CanAlignAnnotationSelection);
        AlignAnnotationVerticalCenterCommand = new RelayCommand(
            () => AlignAnnotationSelection(PanelAlignment.VerticalCenter),
            CanAlignAnnotationSelection);
        AlignAnnotationBottomCommand = new RelayCommand(
            () => AlignAnnotationSelection(PanelAlignment.Bottom),
            CanAlignAnnotationSelection);
        SetAnnotationDirectionHorizontalCommand = new RelayCommand(
            () => SetSelectedAnnotationDirection(0),
            CanSetSelectedAnnotationDirection);
        SetAnnotationDirectionVerticalCommand = new RelayCommand(
            () => SetSelectedAnnotationDirection(90),
            CanSetSelectedAnnotationDirection);
        BeginPolygonAnnotationCommand = new RelayCommand(BeginPolygonAnnotationCreation);
        AddPolygonScientificObjectCommand = new RelayCommand(() => AddScientificObject(FigureScientificObjectKind.PolygonAnnotation));
        AddDirectionMarkerCommand = new RelayCommand(() => AddScientificObject(FigureScientificObjectKind.DirectionMarker));
        AddColorbarCommand = new RelayCommand(() => AddScientificObject(FigureScientificObjectKind.Colorbar));
        AddChannelLegendCommand = new RelayCommand(() => AddScientificObject(FigureScientificObjectKind.ChannelLegend));
        RemoveSelectedScientificObjectCommand = new RelayCommand(RemoveSelectedScientificObject, () => SelectedScientificObject is { IsLocked: false });
        RemoveSelectedAnnotationCommand = new RelayCommand(
            RemoveSelectedAnnotation,
            () => SelectedAnnotation is { IsLocked: false });
        MoveAnnotationUpCommand = new RelayCommand(
            MoveAnnotationUp,
            () => SelectedAnnotation is { IsLocked: false });
        MoveAnnotationDownCommand = new RelayCommand(
            MoveAnnotationDown,
            () => SelectedAnnotation is { IsLocked: false });
        ResetSelectedAnnotationStyleCommand = new RelayCommand(
            ResetSelectedAnnotationStyle,
            () => SelectedAnnotation is { IsLocked: false });
        CopySelectedAnnotationStyleCommand = new RelayCommand(
            CopySelectedAnnotationStyle,
            () => SelectedAnnotation is not null);
        PasteSelectedAnnotationStyleCommand = new RelayCommand(
            PasteSelectedAnnotationStyle,
            () => SelectedAnnotation is { IsLocked: false } selected &&
                  _copiedAnnotationStyle?.Kind == selected.Kind);
        ApplyAnnotationStyleToSameTypeCommand = new RelayCommand(
            ApplyAnnotationStyleToSameType,
            () => SelectedAnnotation is not null);
    }

    public FigureTemplateDefinition Template { get; }

    public FigurePanelCollectionViewModel PanelCollection { get; } = new();

    public FigureObjectCollectionViewModel ObjectCollection { get; } = new();

    public FigureLinkCoordinator LinkCoordinator { get; } = new();

    public ObservableCollection<FigurePanelViewModel> Panels => PanelCollection.Panels;

    public ObservableCollection<FigurePlotPanelViewModel> PlotPanels { get; } = [];

    public ObservableCollection<FigureAnnotationViewModel> Annotations =>
        ObjectCollection.Annotations;

    public ObservableCollection<FigureScientificObjectViewModel> ScientificObjects =>
        ObjectCollection.ScientificObjects;

    public IReadOnlyList<ChannelGroupMember> ColorbarChannels { get; private set; } = [];

    public ObservableCollection<FigureMeasurementOverlayViewModel> MeasurementOverlays =>
        PanelCollection.MeasurementOverlays;

    public ObservableCollection<FigureRoiProjectionViewModel> RoiProjections =>
        PanelCollection.RoiProjections;

    public ObservableCollection<FigureGuideViewModel> Guides => ObjectCollection.Guides;

    public ObservableCollection<ScientificColorEntryViewModel> ScientificColors =>
        ObjectCollection.ScientificColors;

    public ObservableCollection<LinkGroup> LinkGroups => LinkCoordinator.LinkGroups;

    public string LinkSynchronizationStatusText
    {
        get => LinkCoordinator.StatusText;
        private set
        {
            if (!string.Equals(LinkCoordinator.StatusText, value, StringComparison.Ordinal))
            {
                LinkCoordinator.StatusText = value;
                OnPropertyChanged();
            }
        }
    }

    public RelayCommand RemoveSelectedCommand { get; }

    public RelayCommand RemoveSelectedPlotPanelCommand { get; }

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

    public RelayCommand SelectAllAnnotationsCommand { get; }

    public RelayCommand ClearAnnotationSelectionCommand { get; }

    public RelayCommand AlignAnnotationLeftCommand { get; }

    public RelayCommand AlignAnnotationHorizontalCenterCommand { get; }

    public RelayCommand AlignAnnotationRightCommand { get; }

    public RelayCommand AlignAnnotationTopCommand { get; }

    public RelayCommand AlignAnnotationVerticalCenterCommand { get; }

    public RelayCommand AlignAnnotationBottomCommand { get; }

    public RelayCommand SetAnnotationDirectionHorizontalCommand { get; }

    public RelayCommand SetAnnotationDirectionVerticalCommand { get; }

    public RelayCommand BeginPolygonAnnotationCommand { get; }

    public RelayCommand AddPolygonScientificObjectCommand { get; }

    public RelayCommand AddDirectionMarkerCommand { get; }

    public RelayCommand AddColorbarCommand { get; }

    public RelayCommand AddChannelLegendCommand { get; }

    public RelayCommand RemoveSelectedScientificObjectCommand { get; }

    public RelayCommand RemoveSelectedAnnotationCommand { get; }

    public RelayCommand MoveAnnotationUpCommand { get; }

    public RelayCommand MoveAnnotationDownCommand { get; }

    public RelayCommand ResetSelectedAnnotationStyleCommand { get; }

    public RelayCommand CopySelectedAnnotationStyleCommand { get; }

    public RelayCommand PasteSelectedAnnotationStyleCommand { get; }

    public RelayCommand ApplyAnnotationStyleToSameTypeCommand { get; }

    public RelayCommand ResetBackgroundCommand { get; }

    public RelayCommand RenumberPanelLabelsCommand { get; }

    public RelayCommand ApplyGlobalStyleCommand { get; }

    public RelayCommand ResetSelectedPanelLabelStyleCommand { get; }

    public RelayCommand ResetSelectedPanelScaleBarStyleCommand { get; }

    public RelayCommand AddScientificColorCommand { get; }

    public RelayCommand RemoveSelectedScientificColorCommand { get; }

    public RelayCommand ApplySelectedScientificColorCommand { get; }

    public RelayCommand AddAdditionalScaleBarCommand { get; }

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
                OnPropertyChanged(nameof(LabelScheme));
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
                OnPropertyChanged(nameof(LabelScheme));
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
                OnPropertyChanged(nameof(LabelScheme));
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string PanelLabelSettingsText => !ShowPanelLabels
        ? "最终导出不显示面板编号。"
        : AutoPanelLabelsEnabled
            ? "新增、删除或切换编号序列时自动更新；可按画布位置重新编号。"
            : "自动编号已关闭，可直接编辑选中面板的编号。";

    public PanelLabelScheme LabelScheme => PanelLabelGenerator.FromLegacySettings(
        PanelLabelSequence,
        ShowPanelLabels,
        AutoPanelLabelsEnabled);

    public string GlobalFontFamily
    {
        get => _globalFontFamily;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _globalFontFamily, normalized))
            {
                OnPropertyChanged(nameof(GlobalFontChoices));
                OnPropertyChanged(nameof(IsGlobalFontMissing));
                OnPropertyChanged(nameof(GlobalFontAvailabilityMessage));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public IReadOnlyList<string> GlobalFontChoices =>
        SystemFontCatalog.Instance.CreateChoices(GlobalFontFamily);

    public bool IsGlobalFontMissing =>
        !string.IsNullOrWhiteSpace(GlobalFontFamily) &&
        !SystemFontCatalog.Instance.IsInstalled(GlobalFontFamily);

    public string GlobalFontAvailabilityMessage => IsGlobalFontMissing
        ? $"当前系统未安装字体 “{GlobalFontFamily}”；标注导出会使用回退字体，保存值不会改变。"
        : string.Empty;

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

    public string PanelLabelFontFamily
    {
        get => _panelLabelFontFamily;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _panelLabelFontFamily, normalized))
            {
                OnPropertyChanged(nameof(PanelLabelFontChoices));
                OnPropertyChanged(nameof(IsPanelLabelFontMissing));
                OnPropertyChanged(nameof(PanelLabelFontAvailabilityMessage));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public IReadOnlyList<string> PanelLabelFontChoices =>
        SystemFontCatalog.Instance.CreateChoices(PanelLabelFontFamily);

    public bool IsPanelLabelFontMissing =>
        !string.IsNullOrWhiteSpace(PanelLabelFontFamily) &&
        !SystemFontCatalog.Instance.IsInstalled(PanelLabelFontFamily);

    public string PanelLabelFontAvailabilityMessage => IsPanelLabelFontMissing
        ? $"当前系统未安装字体 “{PanelLabelFontFamily}”；Panel Label 导出会回退，但工程保存值不变。"
        : string.Empty;

    public double PanelLabelFontSizePt
    {
        get => _panelLabelFontSizePt;
        set
        {
            if (SetProperty(ref _panelLabelFontSizePt, value))
            {
                OnPropertyChanged(nameof(PanelLabelFontSizePixels));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public string PanelLabelTextColor
    {
        get => _panelLabelTextColor;
        set => SetGlobalColor(
            ref _panelLabelTextColor,
            ref _lastValidPanelLabelTextColor,
            value,
            nameof(PanelLabelTextColor),
            nameof(PanelLabelTextBrush));
    }

    public bool PanelLabelIsBold
    {
        get => _panelLabelIsBold;
        set
        {
            if (SetProperty(ref _panelLabelIsBold, value))
            {
                OnPropertyChanged(nameof(PanelLabelFontWeight));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public string ScaleBarLabelColor
    {
        get => _scaleBarLabelColor;
        set => SetGlobalColor(
            ref _scaleBarLabelColor,
            ref _lastValidScaleBarLabelColor,
            value,
            nameof(ScaleBarLabelColor),
            nameof(ScaleBarLabelBrush));
    }

    public string ScaleBarFontFamily
    {
        get => _scaleBarFontFamily;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (SetProperty(ref _scaleBarFontFamily, normalized))
            {
                OnPropertyChanged(nameof(ScaleBarFontChoices));
                OnPropertyChanged(nameof(IsScaleBarFontMissing));
                OnPropertyChanged(nameof(ScaleBarFontAvailabilityMessage));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public IReadOnlyList<string> ScaleBarFontChoices =>
        SystemFontCatalog.Instance.CreateChoices(ScaleBarFontFamily);

    public bool IsScaleBarFontMissing =>
        !string.IsNullOrWhiteSpace(ScaleBarFontFamily) &&
        !SystemFontCatalog.Instance.IsInstalled(ScaleBarFontFamily);

    public string ScaleBarFontAvailabilityMessage => IsScaleBarFontMissing
        ? $"当前系统未安装字体 “{ScaleBarFontFamily}”；Scale Bar 导出会回退，但工程保存值不变。"
        : string.Empty;

    public double ScaleBarFontSizePt
    {
        get => _scaleBarFontSizePt;
        set
        {
            if (SetProperty(ref _scaleBarFontSizePt, value))
            {
                OnPropertyChanged(nameof(ScaleBarFontSizePixels));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public bool ScaleBarLabelIsBold
    {
        get => _scaleBarLabelIsBold;
        set
        {
            if (SetProperty(ref _scaleBarLabelIsBold, value))
            {
                OnPropertyChanged(nameof(ScaleBarLabelFontWeight));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public double ScaleBarThicknessPt
    {
        get => _scaleBarThicknessPt;
        set
        {
            if (SetProperty(ref _scaleBarThicknessPt, value))
            {
                OnPropertyChanged(nameof(ScaleBarThicknessPixels));
                NotifyGlobalStyleChanged();
            }
        }
    }

    public double PanelLabelFontSizePixels => Math.Max(12, PanelLabelFontSizePt / 72.0 * Dpi);

    public double ScaleBarFontSizePixels => Math.Max(12, ScaleBarFontSizePt / 72.0 * Dpi);

    public double ScaleBarThicknessPixels => Math.Max(1, ScaleBarThicknessPt / 72.0 * Dpi);

    public Brush PanelLabelTextBrush => CreateBrush(_lastValidPanelLabelTextColor);

    public Brush ScaleBarLabelBrush => CreateBrush(_lastValidScaleBarLabelColor);

    public FontWeight PanelLabelFontWeight => PanelLabelIsBold ? FontWeights.Bold : FontWeights.Normal;

    public FontWeight ScaleBarLabelFontWeight => ScaleBarLabelIsBold ? FontWeights.Bold : FontWeights.Normal;

    public FigureGlobalStyle GlobalStyle => new(
        GlobalFontFamily,
        GlobalFontSizePt,
        GlobalStrokeWidthPt,
        GlobalTextColor,
        GlobalShapeColor,
        GlobalScaleBarColor,
        PanelLabelFontFamily,
        PanelLabelFontSizePt,
        PanelLabelTextColor,
        PanelLabelIsBold,
        ScaleBarLabelColor,
        ScaleBarFontFamily,
        ScaleBarFontSizePt,
        ScaleBarLabelIsBold,
        ScaleBarThicknessPt);

    public string SelectedPanelLabelFontFamily
    {
        get => SelectedPanel?.StyleOverride?.PanelLabel?.FontFamily ?? PanelLabelFontFamily;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                UpdateSelectedPanelLabelStyle(style => style with { FontFamily = normalized });
            }
        }
    }

    public IReadOnlyList<string> SelectedPanelLabelFontChoices =>
        SystemFontCatalog.Instance.CreateChoices(SelectedPanelLabelFontFamily);

    public string SelectedPanelLabelFontAvailabilityMessage =>
        !string.IsNullOrWhiteSpace(SelectedPanelLabelFontFamily) &&
        !SystemFontCatalog.Instance.IsInstalled(SelectedPanelLabelFontFamily)
            ? $"当前系统未安装字体 “{SelectedPanelLabelFontFamily}”；保存值不变，QC 会提示。"
            : string.Empty;

    public double SelectedPanelLabelFontSizePt
    {
        get => SelectedPanel?.StyleOverride?.PanelLabel?.FontSizePt ?? PanelLabelFontSizePt;
        set
        {
            if (double.IsFinite(value) && value is >= 4 and <= 72)
            {
                UpdateSelectedPanelLabelStyle(style => style with { FontSizePt = value });
            }
        }
    }

    public string SelectedPanelLabelTextColor
    {
        get => SelectedPanel?.StyleOverride?.PanelLabel?.Color ?? PanelLabelTextColor;
        set
        {
            if (TryNormalizeColor(value, out string normalized))
            {
                UpdateSelectedPanelLabelStyle(style => style with { Color = normalized });
            }
        }
    }

    public Brush SelectedPanelLabelTextBrush => CreateBrush(SelectedPanelLabelTextColor);

    public bool SelectedPanelLabelIsBold
    {
        get => SelectedPanel?.StyleOverride?.PanelLabel?.IsBold ?? PanelLabelIsBold;
        set => UpdateSelectedPanelLabelStyle(style => style with { IsBold = value });
    }

    public string SelectedPanelScaleBarFontFamily
    {
        get => SelectedPanel?.StyleOverride?.ScaleBarText?.FontFamily ?? ScaleBarFontFamily;
        set
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                UpdateSelectedPanelScaleBarTextStyle(style => style with { FontFamily = normalized });
            }
        }
    }

    public IReadOnlyList<string> SelectedPanelScaleBarFontChoices =>
        SystemFontCatalog.Instance.CreateChoices(SelectedPanelScaleBarFontFamily);

    public string SelectedPanelScaleBarFontAvailabilityMessage =>
        !string.IsNullOrWhiteSpace(SelectedPanelScaleBarFontFamily) &&
        !SystemFontCatalog.Instance.IsInstalled(SelectedPanelScaleBarFontFamily)
            ? $"当前系统未安装字体 “{SelectedPanelScaleBarFontFamily}”；保存值不变，QC 会提示。"
            : string.Empty;

    public double SelectedPanelScaleBarFontSizePt
    {
        get => SelectedPanel?.StyleOverride?.ScaleBarText?.FontSizePt ?? ScaleBarFontSizePt;
        set
        {
            if (double.IsFinite(value) && value is >= 4 and <= 72)
            {
                UpdateSelectedPanelScaleBarTextStyle(style => style with { FontSizePt = value });
            }
        }
    }

    public string SelectedPanelScaleBarLabelColor
    {
        get => SelectedPanel?.StyleOverride?.ScaleBarText?.Color ?? ScaleBarLabelColor;
        set
        {
            if (TryNormalizeColor(value, out string normalized))
            {
                UpdateSelectedPanelScaleBarTextStyle(style => style with { Color = normalized });
            }
        }
    }

    public Brush SelectedPanelScaleBarLabelBrush => CreateBrush(SelectedPanelScaleBarLabelColor);

    public bool SelectedPanelScaleBarLabelIsBold
    {
        get => SelectedPanel?.StyleOverride?.ScaleBarText?.IsBold ?? ScaleBarLabelIsBold;
        set => UpdateSelectedPanelScaleBarTextStyle(style => style with { IsBold = value });
    }

    public string SelectedPanelScaleBarColor
    {
        get => SelectedPanel?.StyleOverride?.ScaleBar?.Color ?? GlobalScaleBarColor;
        set
        {
            if (TryNormalizeColor(value, out string normalized))
            {
                UpdateSelectedPanelScaleBarStyle(style => style with { Color = normalized });
            }
        }
    }

    public Brush SelectedPanelScaleBarBrush => CreateBrush(SelectedPanelScaleBarColor);

    public double SelectedPanelScaleBarThicknessPt
    {
        get => SelectedPanel?.StyleOverride?.ScaleBar?.BarThicknessPt ?? ScaleBarThicknessPt;
        set
        {
            if (double.IsFinite(value) && value is >= 0.25 and <= 10)
            {
                UpdateSelectedPanelScaleBarStyle(style => style with { BarThicknessPt = value });
            }
        }
    }

    public string SelectedPanelStyleOverrideStatusText => SelectedPanel is null
        ? "请选择 Panel 后编辑局部覆盖。"
        : SelectedPanel.StyleOverride is null
            ? "当前 Panel 完全继承 Figure 样式。"
            : "当前 Panel 含局部覆盖；未覆盖字段仍随 Figure 样式变化。";

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

    public IReadOnlyList<ScientificColorApplicationTarget> ScientificColorApplicationTargets { get; } =
        Enum.GetValues<ScientificColorApplicationTarget>();

    public ScientificColorApplicationTarget ScientificColorApplicationTarget
    {
        get => _scientificColorApplicationTarget;
        set => SetProperty(ref _scientificColorApplicationTarget, value);
    }

    public int SlotCount => _layout.Slots.Count;

    public string CanvasSizeText => $"{CanvasWidth:N0} × {CanvasHeight:N0} px · {Dpi} dpi";

    public string PanelCountText
    {
        get
        {
            int insetCount = Panels.Count(panel => panel.IsInset);
            int regularCount = Panels.Count - insetCount;
            string imageText = insetCount == 0
                ? $"{regularCount} / {SlotCount} 个面板"
                : $"{regularCount} / {SlotCount} 个面板 · {insetCount} Inset";
            return PlotPanels.Count == 0 ? imageText : $"{imageText} · {PlotPanels.Count} Plot";
        }
    }

    public IReadOnlyList<FigurePanelViewModel> SelectedPanels =>
        Panels.Where(panel => panel.IsSelected).ToArray();

    public int SelectedPanelCount => Panels.Count(panel => panel.IsSelected);

    public string SelectedPanelCountText => $"已选择 {SelectedPanelCount} 个面板";

    public Visibility MultiplePanelSelectionVisibility => SelectedPanelCount >= 2
        ? Visibility.Visible
        : Visibility.Collapsed;

    public IReadOnlyList<FigureAnnotationViewModel> SelectedAnnotations =>
        Annotations.Where(annotation => annotation.IsSelected).ToArray();

    public int SelectedAnnotationCount => Annotations.Count(annotation => annotation.IsSelected);

    public string SelectedAnnotationCountText => $"已选择 {SelectedAnnotationCount} 个标注";

    public Visibility MultipleAnnotationSelectionVisibility => SelectedAnnotationCount >= 2
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string AnnotationCountText => $"{Annotations.Count} 个标注";

    public string ScientificObjectCountText => $"{ScientificObjects.Count} 个科研对象";

    public bool HasPendingPolygonAnnotation => _isCreatingPolygonAnnotation;

    public Visibility PolygonAnnotationDraftVisibility => _isCreatingPolygonAnnotation
        ? Visibility.Visible
        : Visibility.Collapsed;

    public PointCollection PolygonAnnotationDraftPoints => new(
        _polygonAnnotationDraftPoints.Select(point => new Point(point.X, point.Y)));

    public string PolygonAnnotationDraftHint => !_isCreatingPolygonAnnotation
        ? "点击“Polygon Annotation”后在 Figure Canvas 逐点创建。"
        : _polygonAnnotationDraftPoints.Count < 3
            ? $"已添加 {_polygonAnnotationDraftPoints.Count} 个点；至少需要 3 个点。"
            : $"已添加 {_polygonAnnotationDraftPoints.Count} 个点；Enter 或双击完成，Esc 取消。";

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

    public Visibility SelectedScientificObjectVisibility => SelectedScientificObject is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility EmptyVisibility => Panels.Count == 0 && PlotPanels.Count == 0 && Annotations.Count == 0 &&
        ScientificObjects.Count == 0 && RoiProjections.Count == 0
        ? Visibility.Visible
        : Visibility.Collapsed;

    public FigurePanelViewModel? SelectedPanel
    {
        get => _selectedPanel;
        set
        {
            if (value is not null)
            {
                SelectedPlotPanel = null;
                SelectedAnnotation = null;
                SelectedScientificObject = null;
                SelectedGuide = null;
            }

            SelectOnlyPanel(value);
        }
    }

    public FigurePlotPanelViewModel? SelectedPlotPanel
    {
        get => _selectedPlotPanel;
        set
        {
            if (value is not null)
            {
                SelectOnlyPanel(null);
                SelectedAnnotation = null;
                SelectedScientificObject = null;
                SelectedGuide = null;
            }

            SelectOnlyPlotPanel(value);
        }
    }

    public FigureAnnotationViewModel? SelectedAnnotation
    {
        get => _selectedAnnotation;
        set
        {
            if (value is not null)
            {
                SelectOnlyPanel(null);
                SelectedPlotPanel = null;
                SelectedScientificObject = null;
                SelectedGuide = null;
            }

            SelectOnlyAnnotation(value);
        }
    }

    public void SelectAnnotation(FigureAnnotationViewModel annotation, bool toggle)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (!Annotations.Contains(annotation))
        {
            throw new InvalidOperationException("只能选择当前拼版中的标注。");
        }

        SelectOnlyPanel(null);
        SelectedPlotPanel = null;
        SelectedScientificObject = null;
        SelectedGuide = null;

        if (!toggle)
        {
            if (annotation.IsSelected && SelectedAnnotationCount > 1)
            {
                SetPrimaryAnnotation(annotation);
                return;
            }

            SelectOnlyAnnotation(annotation);
            return;
        }

        _isUpdatingAnnotationSelection = true;
        try
        {
            annotation.IsSelected = !annotation.IsSelected;
        }
        finally
        {
            _isUpdatingAnnotationSelection = false;
        }

        SetPrimaryAnnotation(annotation.IsSelected
            ? annotation
            : SelectedAnnotations.LastOrDefault());
        NotifyAnnotationSelectionChanged();
    }
    public FigureScientificObjectViewModel? SelectedScientificObject
    {
        get => _selectedScientificObject;
        set
        {
            if (ReferenceEquals(_selectedScientificObject, value))
            {
                return;
            }

            if (_selectedScientificObject is not null)
            {
                _selectedScientificObject.IsSelected = false;
            }

            _selectedScientificObject = value;
            if (_selectedScientificObject is not null)
            {
                SelectOnlyPanel(null);
                SelectedPlotPanel = null;
                SelectedAnnotation = null;
                SelectedGuide = null;
                _selectedScientificObject.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedScientificObjectVisibility));
            RemoveSelectedScientificObjectCommand.NotifyCanExecuteChanged();
        }
    }

    public void MoveScientificObject(
        FigureScientificObjectViewModel scientificObject,
        double deltaX,
        double deltaY)
    {
        ArgumentNullException.ThrowIfNull(scientificObject);
        if (!ScientificObjects.Contains(scientificObject))
        {
            throw new InvalidOperationException("只能移动当前拼版中的科研对象。");
        }

        scientificObject.MoveBy(deltaX, deltaY);
    }

    public void BeginPolygonAnnotationCreation()
    {
        _polygonAnnotationDraftPoints.Clear();
        _isCreatingPolygonAnnotation = true;
        SelectedScientificObject = null;
        NotifyPolygonAnnotationDraftChanged();
    }

    public bool TryAddPolygonAnnotationDraftVertex(double x, double y)
    {
        if (!_isCreatingPolygonAnnotation ||
            !double.IsFinite(x) || !double.IsFinite(y) ||
            x < 0 || x > CanvasWidth || y < 0 || y > CanvasHeight)
        {
            return false;
        }

        _polygonAnnotationDraftPoints.Add(new FigureScientificPoint(x, y));
        NotifyPolygonAnnotationDraftChanged();
        return true;
    }

    public bool CompletePendingPolygonAnnotation()
    {
        if (!_isCreatingPolygonAnnotation || _polygonAnnotationDraftPoints.Count < 3)
        {
            return false;
        }

        FigureScientificObjectViewModel scientificObject = CreateScientificObject(
            FigureScientificObjectKind.PolygonAnnotation);
        if (!scientificObject.TrySetPolygonPoints(_polygonAnnotationDraftPoints))
        {
            return false;
        }

        AddScientificObject(scientificObject);
        _polygonAnnotationDraftPoints.Clear();
        _isCreatingPolygonAnnotation = false;
        NotifyPolygonAnnotationDraftChanged();
        return true;
    }

    public bool CancelPendingPolygonAnnotation()
    {
        if (!_isCreatingPolygonAnnotation)
        {
            return false;
        }

        _polygonAnnotationDraftPoints.Clear();
        _isCreatingPolygonAnnotation = false;
        NotifyPolygonAnnotationDraftChanged();
        return true;
    }

    public bool TryMoveSelectedPolygonAnnotationVertex(int index, double x, double y) =>
        SelectedScientificObject is { Kind: FigureScientificObjectKind.PolygonAnnotation } scientificObject &&
        scientificObject.TryMovePolygonVertex(index, x, y);

    public bool TryInsertSelectedPolygonAnnotationVertex(double x, double y) =>
        SelectedScientificObject is { Kind: FigureScientificObjectKind.PolygonAnnotation } scientificObject &&
        scientificObject.TryInsertPolygonVertex(x, y, out _);

    public bool TryDeleteSelectedPolygonAnnotationVertex(int index) =>
        SelectedScientificObject is { Kind: FigureScientificObjectKind.PolygonAnnotation } scientificObject &&
        scientificObject.TryDeletePolygonVertex(index);

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
                SelectedPlotPanel = null;
                SelectedAnnotation = null;
                SelectedScientificObject = null;
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

        SelectedPlotPanel = null;
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

    public void SelectPlotPanel(FigurePlotPanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (!PlotPanels.Contains(panel))
        {
            throw new InvalidOperationException("只能选择当前拼版中的 Plot panel。");
        }

        SelectedPlotPanel = panel;
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
        panel.UpdateInheritedGlobalStyle(GlobalStyle);
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
        panel.UpdateInheritedGlobalStyle(GlobalStyle);
        panel.RestoreDestinationSize(destinationRect, lockAspectRatio ?? slot.LockAspectRatio);
        panel.PropertyChanged += OnPanelPropertyChanged;
        Panels.Add(panel);
        SelectedPanel = panel;
        NotifyPanelCollectionChanged();
        return panel;
    }

    public FigurePlotPanelViewModel AddPlotPanel(PlotObject plot, TabularDataAsset dataAsset)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(dataAsset);
        plot.EnsureValid(dataAsset);
        long width = Math.Min(720, Math.Max(120, CanvasWidth * 2L / 5));
        long height = Math.Min(520, Math.Max(100, CanvasHeight * 2L / 5));
        long offset = 24L * (PlotPanels.Count + 1);
        var panel = new FigurePlotPanelViewModel(
            plot,
            dataAsset,
            new PixelRect64(
                Math.Min(offset, Math.Max(0, CanvasWidth - width)),
                Math.Min(offset, Math.Max(0, CanvasHeight - height)),
                width,
                height),
            string.Empty,
            Panels.Count + PlotPanels.Count,
            Dpi);
        AttachPlotPanel(panel);
        PlotPanels.Add(panel);
        RenumberPanelLabels(force: false);
        SelectedPlotPanel = panel;
        NotifyPlotPanelCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
        return panel;
    }

    public FigurePlotPanelViewModel RestorePlotPanel(
        PlotObject plot,
        TabularDataAsset dataAsset,
        Guid id,
        PixelRect64 destinationRect,
        string label,
        bool isVisible,
        bool isLocked,
        int zIndex,
        StyleOverride? styleOverride = null,
        FigurePlotTypographyOverride? typographyOverride = null)
    {
        if (id == Guid.Empty || PlotPanels.Any(panel => panel.Id == id))
        {
            throw new InvalidOperationException("Figure Plot panel ID 必须唯一且非空。");
        }
        if (destinationRect.Right > CanvasWidth || destinationRect.Bottom > CanvasHeight)
        {
            throw new InvalidOperationException("Figure Plot panel 超出画布范围。");
        }

        var panel = new FigurePlotPanelViewModel(
            plot,
            dataAsset,
            destinationRect,
            label,
            zIndex,
            Dpi,
            id);
        panel.RestoreState(isVisible, isLocked, styleOverride, typographyOverride);
        AttachPlotPanel(panel);
        PlotPanels.Add(panel);
        SelectedPlotPanel = panel;
        NotifyPlotPanelCollectionChanged();
        return panel;
    }

    public bool IsPlotReferenced(Guid plotId) =>
        PlotPanels.Any(panel => panel.PlotId == plotId);

    public void SynchronizePlotReferences(
        IReadOnlyCollection<PlotObject> plots,
        IReadOnlyCollection<TabularDataAsset> dataAssets)
    {
        ArgumentNullException.ThrowIfNull(plots);
        ArgumentNullException.ThrowIfNull(dataAssets);
        foreach (FigurePlotPanelViewModel panel in PlotPanels)
        {
            PlotObject plot = plots.SingleOrDefault(candidate => candidate.Id == panel.PlotId)
                ?? throw new InvalidOperationException($"Figure Plot panel 引用的 Plot {panel.PlotId} 不存在。");
            TabularDataAsset dataAsset = dataAssets.SingleOrDefault(candidate => candidate.Id == plot.Data.DataAssetId)
                ?? throw new InvalidOperationException($"Plot {plot.Name} 引用的 DataAsset 不存在。");
            panel.UpdatePlot(plot, dataAsset);
        }
    }

    public void Clear()
    {
        foreach (FigurePanelViewModel panel in Panels)
        {
            panel.PropertyChanged -= OnPanelPropertyChanged;
        }

        Panels.Clear();
        SelectedPanel = null;
        foreach (FigurePlotPanelViewModel panel in PlotPanels)
        {
            panel.PropertyChanged -= OnPlotPanelPropertyChanged;
        }
        PlotPanels.Clear();
        SelectedPlotPanel = null;
        LinkGroups.Clear();
        LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
        LinkSynchronizationStatusText = "当前没有跨素材联动组。";
        foreach (FigureAnnotationViewModel annotation in Annotations)
        {
            annotation.PropertyChanged -= OnAnnotationPropertyChanged;
        }

        Annotations.Clear();
        SelectedAnnotation = null;
        foreach (FigureScientificObjectViewModel scientificObject in ScientificObjects)
        {
            scientificObject.PropertyChanged -= OnScientificObjectPropertyChanged;
        }
        ScientificObjects.Clear();
        SelectedScientificObject = null;
        CancelPendingPolygonAnnotation();
        MeasurementOverlays.Clear();
        RoiProjections.Clear();
        _roiProjectionPanelStates.Clear();
        foreach (FigureGuideViewModel guide in Guides)
        {
            guide.PropertyChanged -= OnGuidePropertyChanged;
        }

        Guides.Clear();
        SelectedGuide = null;
        NotifyPanelCollectionChanged();
        NotifyAnnotationCollectionChanged();
        NotifyScientificObjectCollectionChanged();
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

    public void MovePlotPanel(FigurePlotPanelViewModel panel, long x, long y)
    {
        ArgumentNullException.ThrowIfNull(panel);
        if (!PlotPanels.Contains(panel) || panel.IsLocked)
        {
            return;
        }

        panel.X = Math.Clamp(x, 0, Math.Max(0, CanvasWidth - panel.Width));
        panel.Y = Math.Clamp(y, 0, Math.Max(0, CanvasHeight - panel.Height));
    }

    public FigureExportDocument CreateExportDocument(
        IReadOnlyList<MultiChannelAssetGroup>? multiChannelGroups = null,
        IReadOnlyCollection<SourceAssetItemViewModel>? sources = null)
    {
        if (multiChannelGroups is not null)
        {
            SynchronizeScientificObjectChannels(multiChannelGroups);
        }

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
                panel.IsInset,
                panel.StyleOverride,
                panel.Id,
                panel.CreateScaleBarExportSpecs(),
                CreateChannelLayers(panel, multiChannelGroups, sources),
                panel.Source.SourceRevision))
            .ToArray();
        FigureAnnotationExportItem[] annotations = Annotations
            .OrderBy(annotation => annotation.ZIndex)
            .Select(annotation => annotation.CreateExportItem())
            .ToArray();
        FigureScientificObjectExportItem[] scientificObjects = ScientificObjects
            .OrderBy(scientificObject => scientificObject.ZIndex)
            .Select(scientificObject => scientificObject.CreateExportItem())
            .ToArray();
        FigurePlotPanelExportItem[] plotPanels = PlotPanels
            .OrderBy(panel => panel.ZIndex)
            .Select(panel => panel.CreateExportItem(ShowPanelLabels))
            .ToArray();
        return new FigureExportDocument(
            CanvasWidth,
            CanvasHeight,
            Dpi,
            panels,
            annotations,
            NormalizedBackgroundColor,
            globalStyle: GlobalStyle,
            measurementOverlays: MeasurementOverlays.Select(overlay => overlay.CreateExportItem()).ToArray(),
            scientificObjects: scientificObjects,
            roiProjections: RoiProjections.Select(projection => projection.CreateExportItem()).ToArray(),
            plotPanels: plotPanels);
    }

    private IReadOnlyList<FigureChannelLayerExportItem> CreateChannelLayers(
        FigurePanelViewModel panel,
        IReadOnlyList<MultiChannelAssetGroup>? multiChannelGroups,
        IReadOnlyCollection<SourceAssetItemViewModel>? sources)
    {
        if (panel.CompositeGroupId is not Guid groupId)
        {
            return [];
        }

        MultiChannelAssetGroup group = multiChannelGroups?.SingleOrDefault(item => item.Id == groupId)
            ?? throw new InvalidOperationException(
                $"Composite panel {panel.Label} references missing multi-channel group {groupId}.");
        SourceAssetItemViewModel[] availableSources = sources?.ToArray()
            ?? throw new InvalidOperationException("Composite export requires the project source collection.");
        group.EnsureValid(availableSources.Select(item => item.Asset.Id).ToHashSet());
        if (!group.Members.Any(member => member.AssetId == panel.Source.Asset.Id))
        {
            throw new InvalidOperationException(
                $"Composite panel {panel.Label} source does not belong to group {group.Name}.");
        }
        if (panel.Source.Asset.Id != group.ReferenceAssetId)
        {
            throw new InvalidOperationException(
                $"Composite panel {panel.Label} must use the group reference channel as its output grid.");
        }

        LinkGroup? linkGroup = LinkGroups.FirstOrDefault(link =>
            link.ContainsAsset(panel.Source.Asset.Id) &&
            group.Members.All(member => link.ContainsAsset(member.AssetId)));
        if (!group.SameFieldOfViewConfirmed && linkGroup is null)
        {
            throw new InvalidOperationException(
                $"Composite group {group.Name} requires a LinkGroup with current SpatialMappings before export.");
        }

        if (linkGroup is not null)
        {
            if (linkGroup.ReferenceAssetId != group.ReferenceAssetId)
            {
                throw new InvalidOperationException(
                    $"Composite group {group.Name} and LinkGroup must share the same reference asset.");
            }

            IReadOnlyDictionary<Guid, long> revisions = availableSources
                .Where(item => linkGroup.ContainsAsset(item.Asset.Id))
                .ToDictionary(item => item.Asset.Id, item => item.SourceRevision);
            if (!linkGroup.AreMappingsCurrent(revisions))
            {
                throw new InvalidOperationException(
                    $"Composite group {group.Name} has stale SpatialMappings; review registration before export.");
            }
        }

        ChannelGroupMember referenceMember = group.Members.Single(
            member => member.AssetId == group.ReferenceAssetId);
        var referenceGrid = new RegisteredReferenceGrid(
            new ScientificPlaneRef(
                group.ReferenceAssetId,
                panel.Source.SourceRevision,
                referenceMember.PlaneSelector),
            panel.SourceRect).EnsureValid();
        return group.Members.Select(member =>
        {
            SourceAssetItemViewModel source = availableSources.Single(item => item.Asset.Id == member.AssetId);
            RegisteredPlaneResamplingSpec? resampling = null;
            PixelRect64 sourceRect = panel.SourceRect;
            if (member.AssetId != group.ReferenceAssetId && linkGroup is not null)
            {
                SpatialMapping mapping = linkGroup.Mappings.Single(
                    candidate => candidate.TargetAssetId == member.AssetId);
                resampling = new RegisteredPlaneResamplingSpec(
                    mapping,
                    referenceGrid,
                    source.Asset.Metadata.PixelSize,
                    RegisteredInterpolation.Bilinear,
                    RegisteredBorderPolicy.Transparent,
                    RegisteredPlaneSemantic.ContinuousDisplay).EnsureValid();
                sourceRect = RegisteredPlaneResampler.CalculateSourceReadRegion(resampling);
            }

            ScientificSampleType sampleType = source.Asset.Metadata.BitsPerChannel <= 8
                ? ScientificSampleType.UInt8
                : ScientificSampleType.UInt16;
            ScientificChannelDescriptor selector = member.PlaneSelector.CreateChannelDescriptor(
                member.ChannelId,
                member.Name,
                sampleType,
                source.Asset.Metadata.BitsPerChannel,
                member.Role,
                member.Color);
            return new FigureChannelLayerExportItem(
                group.Id,
                source.Asset,
                source.SourceRevision,
                sourceRect,
                member.FrameIndex,
                selector,
                member.DisplaySettings,
                RegistrationResampling: resampling).EnsureValid();
        }).ToArray();
    }

    public FigureAdditionalScaleBarViewModel AddAdditionalScaleBar()
    {
        if (SelectedPanel is not { IsLocked: false } panel)
        {
            throw new InvalidOperationException("请先选择一个未锁定的 Panel，再新增比例尺。");
        }

        return panel.AddAdditionalScaleBar();
    }
    public FigureMeasurementOverlayViewModel PinMeasurement(
        ScientificMeasurementViewModel measurement,
        FigurePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentNullException.ThrowIfNull(panel);
        if (!measurement.IsValid || measurement.SourceAssetId != panel.Source.Asset.Id ||
            measurement.SourceRevision != panel.Source.SourceRevision)
        {
            throw new InvalidOperationException("只有当前源修订上的有效测量才能 Pin 到引用同一源图的 Figure Panel。");
        }

        FigureMeasurementOverlayViewModel? existing = MeasurementOverlays.FirstOrDefault(overlay =>
            overlay.MeasurementId == measurement.Id && overlay.PanelId == panel.Id);
        if (existing is not null)
        {
            return existing;
        }

        ScientificMeasurementVisualStyle style = measurement.VisualStyle;
        SpatialCalibration? calibration = measurement.Calibration is { IsValid: true } currentCalibration
            ? currentCalibration
            : null;
        var scientificObject = new MeasurementOverlayObject
        {
            Id = Guid.NewGuid(),
            AssetId = measurement.SourceAssetId,
            PanelId = panel.Id,
            SourceRevision = measurement.SourceRevision,
            MeasurementId = measurement.Id,
            SourceGeometry = measurement.Measurement,
            CalibrationRelationship = calibration is null
                ? null
                : new FigureMeasurementCalibrationRelationship(
                    measurement.SourceAssetId,
                    measurement.SourceRevision,
                    calibration.UnitsPerPixelX,
                    calibration.UnitsPerPixelY,
                    calibration.Unit),
            Style = new FigureMeasurementOverlayStyle(
                style.StrokeColor,
                style.StrokeWidthPixels,
                style.LineStyle,
                style.FillColor,
                style.FillOpacityPercent,
                style.MarkerStrokeColor,
                style.MarkerFillColor,
                style.MarkerSizePixels,
                style.ShowMarkers,
                style.LabelColor,
                style.LabelFontFamily,
                style.LabelFontSizePt,
                style.LabelIsBold,
                style.ShowLabel),
            StyleOverride = new StyleOverride(
                Measurement: new MeasurementStyle(
                    new ShapeStyle(
                        style.StrokeColor,
                        style.FillColor,
                        style.FillOpacityPercent,
                        Math.Clamp(style.StrokeWidthPixels * 72.0 / 96.0, 0.25, 10)),
                    new MarkerStyle(style.MarkerStrokeColor, style.MarkerFillColor, style.MarkerSizePixels),
                    new TextStyle(style.LabelFontFamily, style.LabelFontSizePt, style.LabelIsBold, style.LabelColor),
                    style.LineStyle,
                    style.ShowMarkers,
                    style.ShowLabel)),
            IsVisible = style.IsVisible,
            ZIndex = MeasurementOverlays.Count,
        };
        var overlay = new FigureMeasurementOverlayViewModel(scientificObject, panel);
        MeasurementOverlays.Add(overlay);
        NotifyMeasurementOverlayCollectionChanged();
        return overlay;
    }

    public FigureMeasurementOverlayViewModel RestoreMeasurementOverlay(
        MeasurementOverlayObject scientificObject)
    {
        ArgumentNullException.ThrowIfNull(scientificObject);
        FigurePanelViewModel panel = Panels.SingleOrDefault(candidate => candidate.Id == scientificObject.PanelId)
            ?? throw new InvalidOperationException("Measurement Overlay 引用了不存在的 Figure Panel。");
        var overlay = new FigureMeasurementOverlayViewModel(scientificObject, panel);
        MeasurementOverlays.Add(overlay);
        NotifyMeasurementOverlayCollectionChanged();
        return overlay;
    }

    public FigureRoiProjectionViewModel AddRoiProjection(RoiObject roi) =>
        AddRoiProjection(
            roi,
            SelectedPanel ?? throw new InvalidOperationException(
                "请先在 Figure 中选择一个与 ROI 同源的 Panel。"));

    public FigureRoiProjectionViewModel AddRoiProjection(
        RoiObject roi,
        FigurePanelViewModel panel)
    {
        ArgumentNullException.ThrowIfNull(roi);
        ArgumentNullException.ThrowIfNull(panel);
        roi.EnsureValid();
        if (roi.Validity.State == ScientificValidityState.Invalid ||
            roi.AssetId != panel.Source.Asset.Id ||
            roi.SourceRevision != panel.Source.SourceRevision ||
            roi.FrameIndex != panel.FrameIndex)
        {
            throw new InvalidOperationException(
                "只有当前 source revision/frame 上的可用 canonical ROI 才能投影到同源 Figure Panel。");
        }

        if (!FigureRoiProjectionMapper.FitsPanelSourceRect(roi, panel.SourceRect))
        {
            throw new InvalidOperationException(
                "Canonical ROI 未完全位于所选 Panel crop 内；请调整 crop 后再创建 Figure projection。");
        }

        FigureRoiProjectionViewModel? existing = RoiProjections.FirstOrDefault(projection =>
            projection.RoiId == roi.Id && projection.PanelId == panel.Id);
        if (existing is not null)
        {
            return existing;
        }

        var projectionObject = new RoiFigureProjectionObject
        {
            Id = Guid.NewGuid(),
            RoiId = roi.Id,
            PanelId = panel.Id,
            AssetId = roi.AssetId,
            SourceRevision = roi.SourceRevision,
            IsVisible = true,
            ZIndex = RoiProjections.Count,
        };
        var projection = new FigureRoiProjectionViewModel(projectionObject, roi, panel);
        RoiProjections.Add(projection);
        RememberRoiProjectionPanelState(panel);
        NotifyRoiProjectionCollectionChanged();
        return projection;
    }

    public FigureRoiProjectionViewModel RestoreRoiProjection(
        RoiFigureProjectionObject projectionObject,
        RoiObject roi)
    {
        ArgumentNullException.ThrowIfNull(projectionObject);
        ArgumentNullException.ThrowIfNull(roi);
        FigurePanelViewModel panel = Panels.SingleOrDefault(candidate => candidate.Id == projectionObject.PanelId)
            ?? throw new InvalidOperationException("ROI Figure Projection 引用了不存在的 Figure Panel。");
        if (projectionObject.RoiId != roi.Id)
        {
            throw new InvalidOperationException("ROI Figure Projection 引用了错误的 canonical ROI。");
        }

        var projection = new FigureRoiProjectionViewModel(projectionObject, roi, panel);
        RoiProjections.Add(projection);
        RememberRoiProjectionPanelState(panel);
        NotifyRoiProjectionCollectionChanged();
        return projection;
    }

    public bool RemoveRoiProjection(Guid projectionId)
    {
        FigureRoiProjectionViewModel? projection =
            RoiProjections.FirstOrDefault(item => item.Id == projectionId);
        if (projection is null)
        {
            return false;
        }

        RoiProjections.Remove(projection);
        if (!RoiProjections.Any(item => item.PanelId == projection.PanelId))
        {
            _roiProjectionPanelStates.Remove(projection.PanelId);
        }
        NotifyRoiProjectionCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void RefreshRoiProjectionSource(RoiObject canonicalRoi)
    {
        ArgumentNullException.ThrowIfNull(canonicalRoi);
        foreach (FigureRoiProjectionViewModel projection in RoiProjections
                     .Where(item => item.RoiId == canonicalRoi.Id))
        {
            projection.UpdateCanonicalRoi(canonicalRoi);
        }

        if (RoiProjections.Any(item => item.RoiId == canonicalRoi.Id))
        {
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public void ValidateRoiProjectionSource(RoiObject canonicalRoi)
    {
        ArgumentNullException.ThrowIfNull(canonicalRoi);
        foreach (FigureRoiProjectionViewModel projection in RoiProjections
                     .Where(item => item.RoiId == canonicalRoi.Id))
        {
            projection.ValidateCanonicalRoi(canonicalRoi);
        }
    }

    public bool HasRoiProjections(Guid panelId) =>
        RoiProjections.Any(item => item.PanelId == panelId);

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
        PanelLabelFontFamily = style.EffectivePanelLabelFontFamily;
        PanelLabelFontSizePt = style.EffectivePanelLabelFontSizePt;
        PanelLabelTextColor = style.EffectivePanelLabelTextColor;
        PanelLabelIsBold = style.PanelLabelIsBold;
        ScaleBarLabelColor = style.EffectiveScaleBarLabelColor;
        ScaleBarFontFamily = style.EffectiveScaleBarFontFamily;
        ScaleBarFontSizePt = style.EffectiveScaleBarFontSizePt;
        ScaleBarLabelIsBold = style.ScaleBarLabelIsBold;
        ScaleBarThicknessPt = style.EffectiveScaleBarThicknessPt;
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
        return RestoreAnnotation(
            id,
            kind,
            x,
            y,
            endX,
            endY,
            text,
            kind == FigureAnnotationKind.Text ? GlobalShapeColor : color,
            color,
            0,
            kind == FigureAnnotationKind.Text ? color : GlobalTextColor,
            GlobalFontFamily,
            fontSizePt,
            strokeWidthPt,
            isBold,
            isVisible,
            isLocked,
            zIndex);
    }

    public FigureAnnotationViewModel RestoreAnnotation(
        Guid id,
        FigureAnnotationKind kind,
        double x,
        double y,
        double endX,
        double endY,
        string text,
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
            StrokeColor = strokeColor,
            FillColor = fillColor,
            FillOpacityPercent = fillOpacityPercent,
            TextColor = textColor,
            FontFamily = fontFamily,
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
    public FigureScientificObjectViewModel RestoreScientificObject(
        Guid id,
        FigureScientificObjectKind kind,
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
        int zIndex,
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
        var scientificObject = new FigureScientificObjectViewModel(kind, CanvasWidth, CanvasHeight, Dpi, zIndex, id);
        scientificObject.Restore(pointsText, label, strokeColor, fillColor, fillOpacityPercent, textColor,
            fontFamily, fontSizePt, strokeWidthPt, isBold, isVisible, isLocked, minimum, maximum, unit,
            colormap, channelEntriesText, channelId, colorbarBindingState, colorbarOrientation,
            colorbarTicksText, channelLegendPadding);
        scientificObject.SetAvailableChannels(ColorbarChannels);
        scientificObject.PropertyChanged += OnScientificObjectPropertyChanged;
        ScientificObjects.Add(scientificObject);
        SelectedScientificObject = scientificObject;
        NotifyScientificObjectCollectionChanged();
        return scientificObject;
    }

    private void AddScientificObject(FigureScientificObjectKind kind)
    {
        AddScientificObject(CreateScientificObject(kind));
    }

    private FigureScientificObjectViewModel CreateScientificObject(FigureScientificObjectKind kind)
    {
        FigureScientificObjectViewModel? previous = ScientificObjects.LastOrDefault();
        var scientificObject = new FigureScientificObjectViewModel(
            kind,
            CanvasWidth,
            CanvasHeight,
            Dpi,
            ScientificObjects.Count)
        {
            StrokeColor = previous?.StrokeColor ?? GlobalShapeColor,
            FillColor = previous?.FillColor ?? GlobalShapeColor,
            TextColor = previous?.TextColor ?? GlobalTextColor,
            FontFamily = previous?.FontFamily ?? GlobalFontFamily,
            FontSizePt = previous?.FontSizePt ?? GlobalFontSizePt,
            StrokeWidthPt = previous?.StrokeWidthPt ?? GlobalStrokeWidthPt,
        };
        scientificObject.SetAvailableChannels(ColorbarChannels);
        if (kind == FigureScientificObjectKind.Colorbar && ColorbarChannels.FirstOrDefault() is { } channel)
        {
            scientificObject.LinkColorbarToChannel(channel);
        }
        return scientificObject;
    }

    public void SynchronizeScientificObjectChannels(IReadOnlyList<MultiChannelAssetGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ColorbarChannels = groups
            .SelectMany(group => group.Members)
            .GroupBy(member => member.ChannelId)
            .Select(group => group.Last())
            .OrderBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        OnPropertyChanged(nameof(ColorbarChannels));
        foreach (FigureScientificObjectViewModel scientificObject in ScientificObjects)
        {
            scientificObject.SetAvailableChannels(ColorbarChannels);
        }
    }

    private void AddScientificObject(FigureScientificObjectViewModel scientificObject)
    {
        scientificObject.PropertyChanged += OnScientificObjectPropertyChanged;
        ScientificObjects.Add(scientificObject);
        SelectedScientificObject = scientificObject;
        NotifyScientificObjectCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyPolygonAnnotationDraftChanged()
    {
        OnPropertyChanged(nameof(HasPendingPolygonAnnotation));
        OnPropertyChanged(nameof(PolygonAnnotationDraftVisibility));
        OnPropertyChanged(nameof(PolygonAnnotationDraftPoints));
        OnPropertyChanged(nameof(PolygonAnnotationDraftHint));
    }

    private void RemoveSelectedScientificObject()
    {
        if (SelectedScientificObject is not { IsLocked: false } scientificObject)
        {
            return;
        }

        int index = ScientificObjects.IndexOf(scientificObject);
        scientificObject.PropertyChanged -= OnScientificObjectPropertyChanged;
        ScientificObjects.Remove(scientificObject);
        SelectedScientificObject = ScientificObjects.Count == 0
            ? null
            : ScientificObjects[Math.Clamp(index, 0, ScientificObjects.Count - 1)];
        for (int objectIndex = 0; objectIndex < ScientificObjects.Count; objectIndex++)
        {
            ScientificObjects[objectIndex].ZIndex = objectIndex;
        }
        NotifyScientificObjectCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    public void MoveAnnotation(FigureAnnotationViewModel annotation, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(annotation);
        if (annotation.IsSelected)
        {
            MoveSelectedAnnotationsBy(deltaX, deltaY);
            return;
        }

        annotation.MoveBy(deltaX, deltaY);
    }

    public (double DeltaX, double DeltaY) MoveSelectedAnnotationsBy(double deltaX, double deltaY)
    {
        FigureAnnotationViewModel[] movable = SelectedAnnotations
            .Where(annotation => !annotation.IsLocked)
            .ToArray();
        if (movable.Length == 0 || !double.IsFinite(deltaX) || !double.IsFinite(deltaY))
        {
            return (0, 0);
        }

        double minimumX = movable.Min(annotation => annotation.Bounds.Left);
        double maximumX = movable.Max(annotation => annotation.Bounds.Right);
        double minimumY = movable.Min(annotation => annotation.Bounds.Top);
        double maximumY = movable.Max(annotation => annotation.Bounds.Bottom);
        double adjustedX = Math.Clamp(deltaX, -minimumX, CanvasWidth - maximumX);
        double adjustedY = Math.Clamp(deltaY, -minimumY, CanvasHeight - maximumY);
        foreach (FigureAnnotationViewModel annotation in movable)
        {
            annotation.MoveBy(adjustedX, adjustedY);
        }

        return (adjustedX, adjustedY);
    }

    private void AlignAnnotationSelection(PanelAlignment alignment)
    {
        if (SelectedAnnotation is not { } reference || SelectedAnnotationCount < 2)
        {
            return;
        }

        Rect referenceBounds = reference.Bounds;
        foreach (FigureAnnotationViewModel annotation in SelectedAnnotations.Where(
                     annotation => !annotation.IsLocked && !ReferenceEquals(annotation, reference)))
        {
            Rect bounds = annotation.Bounds;
            (double deltaX, double deltaY) = alignment switch
            {
                PanelAlignment.Left => (referenceBounds.Left - bounds.Left, 0d),
                PanelAlignment.HorizontalCenter => (referenceBounds.Left + referenceBounds.Width / 2 -
                                                     (bounds.Left + bounds.Width / 2), 0d),
                PanelAlignment.Right => (referenceBounds.Right - bounds.Right, 0d),
                PanelAlignment.Top => (0d, referenceBounds.Top - bounds.Top),
                PanelAlignment.VerticalCenter => (0d, referenceBounds.Top + referenceBounds.Height / 2 -
                                                   (bounds.Top + bounds.Height / 2)),
                PanelAlignment.Bottom => (0d, referenceBounds.Bottom - bounds.Bottom),
                _ => (0d, 0d),
            };
            annotation.MoveBy(deltaX, deltaY);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanAlignAnnotationSelection() =>
        SelectedAnnotationCount >= 2 &&
        SelectedAnnotation is not null &&
        SelectedAnnotations.Any(annotation => !annotation.IsLocked);

    private void SetSelectedAnnotationDirection(double angleDegrees)
    {
        foreach (FigureAnnotationViewModel annotation in SelectedAnnotations.Where(
                     annotation => !annotation.IsLocked &&
                                   annotation.Kind is FigureAnnotationKind.Arrow or FigureAnnotationKind.Line))
        {
            annotation.SetDirectionAngle(angleDegrees);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanSetSelectedAnnotationDirection() =>
        SelectedAnnotations.Any(annotation => !annotation.IsLocked &&
            annotation.Kind is FigureAnnotationKind.Arrow or FigureAnnotationKind.Line);

    private void SelectAllAnnotations()
    {
        SelectOnlyPanel(null);
        SelectedScientificObject = null;
        SelectedGuide = null;
        _isUpdatingAnnotationSelection = true;
        try
        {
            foreach (FigureAnnotationViewModel annotation in Annotations)
            {
                annotation.IsSelected = true;
            }
        }
        finally
        {
            _isUpdatingAnnotationSelection = false;
        }

        SetPrimaryAnnotation(Annotations.LastOrDefault());
        NotifyAnnotationSelectionChanged();
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
            foreach (FigureRoiProjectionViewModel projection in RoiProjections
                         .Where(item => item.PanelId == panel.Id)
                         .ToArray())
            {
                RoiProjections.Remove(projection);
            }
            _roiProjectionPanelStates.Remove(panel.Id);
            panel.PropertyChanged -= OnPanelPropertyChanged;
            Panels.Remove(panel);
        }

        SelectedPanel = Panels.Count == 0
            ? null
            : Panels[Math.Clamp(index, 0, Panels.Count - 1)];
        NormalizeZIndexes();
        RenumberPanelLabels(force: false);
        NotifyRoiProjectionCollectionChanged();
        NotifyPanelCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void RemoveSelectedPlotPanel()
    {
        if (SelectedPlotPanel is not { IsLocked: false } panel)
        {
            return;
        }

        int index = PlotPanels.IndexOf(panel);
        panel.PropertyChanged -= OnPlotPanelPropertyChanged;
        PlotPanels.Remove(panel);
        SelectedPlotPanel = PlotPanels.Count == 0
            ? null
            : PlotPanels[Math.Clamp(index, 0, PlotPanels.Count - 1)];
        for (int plotIndex = 0; plotIndex < PlotPanels.Count; plotIndex++)
        {
            PlotPanels[plotIndex].ZIndex = Panels.Count + plotIndex;
        }
        RenumberPanelLabels(force: false);
        NotifyPlotPanelCollectionChanged();
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
        FigureAnnotationViewModel? previous = Annotations.LastOrDefault();
        var annotation = new FigureAnnotationViewModel(
            kind,
            CanvasWidth,
            CanvasHeight,
            Dpi,
            Annotations.Count)
        {
            StrokeColor = previous?.StrokeColor ?? GlobalShapeColor,
            FillColor = previous?.FillColor ?? GlobalShapeColor,
            FillOpacityPercent = previous?.FillOpacityPercent ?? 0,
            TextColor = previous?.TextColor ?? GlobalTextColor,
            FontFamily = previous?.FontFamily ?? GlobalFontFamily,
            FontSizePt = previous?.FontSizePt ?? GlobalFontSizePt,
            StrokeWidthPt = previous?.StrokeWidthPt ?? GlobalStrokeWidthPt,
            IsBold = previous?.IsBold ?? false,
        };
        annotation.PropertyChanged += OnAnnotationPropertyChanged;
        Annotations.Add(annotation);
        SelectedAnnotation = annotation;
        NotifyAnnotationCollectionChanged();
    }

    private void ResetSelectedAnnotationStyle()
    {
        if (SelectedAnnotation is not { IsLocked: false } annotation)
        {
            return;
        }

        annotation.ApplyStyle(new FigureAnnotationStyle(
            annotation.Kind,
            GlobalShapeColor,
            GlobalShapeColor,
            0,
            GlobalTextColor,
            GlobalFontFamily,
            GlobalFontSizePt,
            GlobalStrokeWidthPt,
            IsBold: false));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void CopySelectedAnnotationStyle()
    {
        _copiedAnnotationStyle = SelectedAnnotation?.CaptureStyle();
        PasteSelectedAnnotationStyleCommand.NotifyCanExecuteChanged();
    }

    private void PasteSelectedAnnotationStyle()
    {
        if (SelectedAnnotation is not { IsLocked: false } annotation ||
            _copiedAnnotationStyle is not { } style ||
            style.Kind != annotation.Kind)
        {
            return;
        }

        annotation.ApplyStyle(style);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyAnnotationStyleToSameType()
    {
        if (SelectedAnnotation is not { } selected)
        {
            return;
        }

        FigureAnnotationStyle style = selected.CaptureStyle();
        foreach (FigureAnnotationViewModel annotation in Annotations
                     .Where(annotation => annotation.Kind == selected.Kind && !annotation.IsLocked))
        {
            annotation.ApplyStyle(style);
        }

        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
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
                annotation.FontFamily = GlobalFontFamily;
                annotation.FontSizePt = GlobalFontSizePt;
                annotation.TextColor = GlobalTextColor;
            }
            else
            {
                annotation.StrokeWidthPt = GlobalStrokeWidthPt;
                annotation.StrokeColor = GlobalShapeColor;
            }
        }

        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
        OnPropertyChanged(nameof(GlobalStyleStatusText));
    }

    private void UpdateSelectedPanelLabelStyle(Func<TextStyle, TextStyle> update)
    {
        if (SelectedPanel is not { IsLocked: false } panel)
        {
            return;
        }

        TextStyle inherited = new(
            PanelLabelFontFamily,
            PanelLabelFontSizePt,
            PanelLabelIsBold,
            PanelLabelTextColor);
        StyleOverride current = panel.StyleOverride ?? new StyleOverride();
        panel.RestoreStyleOverride(current with
        {
            PanelLabel = update(current.PanelLabel ?? inherited),
        });
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectedPanelScaleBarTextStyle(Func<TextStyle, TextStyle> update)
    {
        if (SelectedPanel is not { IsLocked: false } panel)
        {
            return;
        }

        TextStyle inherited = new(
            ScaleBarFontFamily,
            ScaleBarFontSizePt,
            ScaleBarLabelIsBold,
            ScaleBarLabelColor);
        StyleOverride current = panel.StyleOverride ?? new StyleOverride();
        panel.RestoreStyleOverride(current with
        {
            ScaleBarText = update(current.ScaleBarText ?? inherited),
        });
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSelectedPanelScaleBarStyle(Func<ScaleBarStyle, ScaleBarStyle> update)
    {
        if (SelectedPanel is not { IsLocked: false } panel)
        {
            return;
        }

        ScaleBarStyle inherited = new(
            ScaleBarAnchor.BottomRight,
            ScaleBarThicknessPt,
            GlobalScaleBarColor);
        StyleOverride current = panel.StyleOverride ?? new StyleOverride();
        panel.RestoreStyleOverride(current with
        {
            ScaleBar = update(current.ScaleBar ?? inherited),
        });
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ResetSelectedPanelLabelStyle()
    {
        if (SelectedPanel is not { IsLocked: false } panel || panel.StyleOverride is not { } current)
        {
            return;
        }

        panel.RestoreStyleOverride(current with { PanelLabel = null });
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void ResetSelectedPanelScaleBarStyle()
    {
        if (SelectedPanel is not { IsLocked: false } panel || panel.StyleOverride is not { } current)
        {
            return;
        }

        panel.RestoreStyleOverride(current with { ScaleBarText = null, ScaleBar = null });
        EditCompleted?.Invoke(this, EventArgs.Empty);
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

        switch (ScientificColorApplicationTarget)
        {
            case ScientificColorApplicationTarget.AnnotationStroke when SelectedAnnotation is not null:
                SelectedAnnotation.StrokeColor = selected.Color;
                break;
            case ScientificColorApplicationTarget.AnnotationFill when SelectedAnnotation is not null:
                SelectedAnnotation.FillColor = selected.Color;
                break;
            case ScientificColorApplicationTarget.AnnotationText when SelectedAnnotation is not null:
                SelectedAnnotation.TextColor = selected.Color;
                break;
            case ScientificColorApplicationTarget.ScaleBar:
                GlobalScaleBarColor = selected.Color;
                break;
            case ScientificColorApplicationTarget.ScaleBarLabel:
                ScaleBarLabelColor = selected.Color;
                break;
            case ScientificColorApplicationTarget.PanelLabel:
                PanelLabelTextColor = selected.Color;
                break;
            default:
                if (SelectedAnnotation is not null)
                {
                    if (SelectedAnnotation.Kind == FigureAnnotationKind.Text)
                    {
                        SelectedAnnotation.TextColor = selected.Color;
                    }
                    else
                    {
                        SelectedAnnotation.StrokeColor = selected.Color;
                    }
                }
                else
                {
                    GlobalShapeColor = selected.Color;
                }
                break;
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
        inset.UpdateInheritedGlobalStyle(GlobalStyle);
        inset.ApplySpatialCalibration(reference.Source.Calibration.Calibration);
        inset.PropertyChanged += OnPanelPropertyChanged;
        Panels.Add(inset);
        SelectedPanel = inset;
        NotifyPanelCollectionChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private bool CanLinkSelectedPanelCrops() =>
        SelectedPanelCount >= 2 &&
        SelectedPanels.All(panel => !panel.IsLocked && !panel.IsCropLinked);

    private void LinkSelectedPanelCrops()
    {
        if (!CanLinkSelectedPanelCrops() || SelectedPanel is not FigurePanelViewModel reference)
        {
            return;
        }

        FigurePanelViewModel[] selected = SelectedPanels.ToArray();
        Guid[] assetIds = selected
            .Select(panel => panel.Source.Asset.Id)
            .Distinct()
            .ToArray();
        Guid groupId = Guid.NewGuid();
        _isUpdatingLinkGroupMembership = true;
        try
        {
            foreach (FigurePanelViewModel panel in selected)
            {
                panel.CropLinkGroupId = groupId;
            }
        }
        finally
        {
            _isUpdatingLinkGroupMembership = false;
        }

        if (assetIds.Length > 1)
        {
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            SpatialMapping[] mappings = assetIds
                .Where(assetId => assetId != reference.Source.Asset.Id)
                .Select(targetAssetId =>
                {
                    FigurePanelViewModel target = selected.First(panel => panel.Source.Asset.Id == targetAssetId);
                    return SpatialMapping.CreateIdentity(
                        reference.Source.Asset.Id,
                        targetAssetId,
                        reference.Source.SourceRevision,
                        target.Source.SourceRevision,
                        createdAt);
                })
                .ToArray();
            var group = new LinkGroup(
                groupId,
                $"联动组 {LinkGroups.Count + 1}",
                reference.Source.Asset.Id,
                Array.AsReadOnly(assetIds),
                LinkSyncOptions.Crop | LinkSyncOptions.Roi | LinkSyncOptions.ColorScale,
                Array.AsReadOnly(mappings)).EnsureValid();
            LinkGroups.Add(group);
            LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            LinkSynchronizationStatusText =
                $"已创建跨素材联动组；参考素材 {reference.Source.DisplayName}，映射来源为用户声明的 Identity。";
            SynchronizeLinkedCrop(reference);
            SynchronizeLinkedColorScale(reference);
        }
        else
        {
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            LinkSynchronizationStatusText = "已创建同素材裁剪联动；各面板继续引用原素材。";
        }

        LinkSelectedPanelCropsCommand.NotifyCanExecuteChanged();
        UnlinkSelectedPanelCropsCommand.NotifyCanExecuteChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private void UnlinkSelectedPanelCrops()
    {
        Guid[] affectedGroupIds = SelectedPanels
            .Select(panel => panel.CropLinkGroupId)
            .OfType<Guid>()
            .Distinct()
            .ToArray();
        _isUpdatingLinkGroupMembership = true;
        try
        {
            foreach (FigurePanelViewModel panel in SelectedPanels)
            {
                panel.CropLinkGroupId = null;
            }

            foreach (Guid groupId in affectedGroupIds)
            {
                int groupIndex = FindLinkGroupIndex(groupId);
                if (groupIndex < 0)
                {
                    continue;
                }

                LinkGroup group = LinkGroups[groupIndex];
                Guid[] remainingAssetIds = Panels
                    .Where(panel => panel.CropLinkGroupId == groupId)
                    .Select(panel => panel.Source.Asset.Id)
                    .Distinct()
                    .ToArray();
                if (remainingAssetIds.Length < 2 || !remainingAssetIds.Contains(group.ReferenceAssetId))
                {
                    foreach (FigurePanelViewModel remaining in Panels.Where(panel => panel.CropLinkGroupId == groupId))
                    {
                        remaining.CropLinkGroupId = null;
                    }

                    LinkGroups.RemoveAt(groupIndex);
                    continue;
                }

                LinkGroups[groupIndex] = (group with
                {
                    AssetIds = Array.AsReadOnly(remainingAssetIds),
                    Mappings = Array.AsReadOnly(group.Mappings
                        .Where(mapping => remainingAssetIds.Contains(mapping.TargetAssetId))
                        .ToArray()),
                }).EnsureValid();
            }
        }
        finally
        {
            _isUpdatingLinkGroupMembership = false;
        }

        if (affectedGroupIds.Length > 0)
        {
            LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
            DocumentChanged?.Invoke(this, EventArgs.Empty);
            LinkSynchronizationStatusText = LinkGroups.Count == 0
                ? "当前没有跨素材联动组。"
                : "已更新联动组成员。";
        }

        LinkSelectedPanelCropsCommand.NotifyCanExecuteChanged();
        UnlinkSelectedPanelCropsCommand.NotifyCanExecuteChanged();
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<LinkGroup> CreateLinkGroupModels() =>
        LinkGroups
            .Select(group => group with
            {
                AssetIds = Array.AsReadOnly(group.AssetIds.ToArray()),
                Mappings = Array.AsReadOnly(group.Mappings.ToArray()),
            })
            .ToArray();

    public void RestoreLinkGroups(IEnumerable<LinkGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        LinkGroups.Clear();
        HashSet<Guid> availableAssetIds = Panels
            .Select(panel => panel.Source.Asset.Id)
            .ToHashSet();
        foreach (LinkGroup group in groups)
        {
            LinkGroups.Add(group.EnsureValid(availableAssetIds));
        }

        LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
        LinkSynchronizationStatusText = LinkGroups.Count == 0
            ? "当前没有跨素材联动组。"
            : $"已恢复 {LinkGroups.Count} 个跨素材联动组。";
    }

    public void UpdateLinkIdentity(Guid groupId, Guid targetAssetId)
    {
        int groupIndex = FindLinkGroupIndex(groupId);
        if (groupIndex < 0)
        {
            throw new InvalidOperationException("找不到待更新的联动组。");
        }

        LinkGroup group = LinkGroups[groupIndex];
        SpatialMapping current = group.Mappings.Single(mapping => mapping.TargetAssetId == targetAssetId);
        (long sourceRevision, long targetRevision) = GetCurrentMappingRevisions(current);
        SpatialMapping replacement = SpatialMapping.CreateIdentity(
            current.SourceAssetId,
            current.TargetAssetId,
            sourceRevision,
            targetRevision,
            DateTimeOffset.UtcNow,
            current.Id);
        LinkGroups[groupIndex] = group.ReplaceMapping(replacement).EnsureValid();
        LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        LinkSynchronizationStatusText = "已重置为用户声明的 Identity 映射。";

        FigurePanelViewModel? reference = Panels.FirstOrDefault(panel =>
            panel.CropLinkGroupId == groupId &&
            panel.Source.Asset.Id == group.ReferenceAssetId);
        if (reference is not null)
        {
            SynchronizeLinkedCrop(reference);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }
    public void UpdateLinkTranslation(Guid groupId, Guid targetAssetId, double offsetX, double offsetY)
    {
        int groupIndex = FindLinkGroupIndex(groupId);
        if (groupIndex < 0)
        {
            throw new InvalidOperationException("找不到待更新的联动组。");
        }

        LinkGroup group = LinkGroups[groupIndex];
        SpatialMapping current = group.Mappings.Single(mapping => mapping.TargetAssetId == targetAssetId);
        (long sourceRevision, long targetRevision) = GetCurrentMappingRevisions(current);
        SpatialMapping replacement = SpatialMapping.CreateTranslation(
            current.SourceAssetId,
            current.TargetAssetId,
            sourceRevision,
            targetRevision,
            offsetX,
            offsetY,
            DateTimeOffset.UtcNow,
            current.Id);
        LinkGroups[groupIndex] = group.ReplaceMapping(replacement).EnsureValid();
        LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        LinkSynchronizationStatusText =
            $"已更新平移映射：X={offsetX.ToString("0.###", CultureInfo.InvariantCulture)} px，Y={offsetY.ToString("0.###", CultureInfo.InvariantCulture)} px。";

        FigurePanelViewModel? reference = Panels.FirstOrDefault(panel =>
            panel.CropLinkGroupId == groupId &&
            panel.Source.Asset.Id == group.ReferenceAssetId);
        if (reference is not null)
        {
            SynchronizeLinkedCrop(reference);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    public SpatialRegistrationResult UpdateLinkRegistration(
        Guid groupId,
        Guid targetAssetId,
        SpatialMappingKind kind,
        IReadOnlyList<RegistrationLandmarkPair> landmarkPairs)
    {
        int groupIndex = FindLinkGroupIndex(groupId);
        if (groupIndex < 0)
        {
            throw new InvalidOperationException("找不到待配准的联动组。");
        }

        LinkGroup group = LinkGroups[groupIndex];
        SpatialMapping current = group.Mappings.Single(mapping => mapping.TargetAssetId == targetAssetId);
        (long sourceRevision, long targetRevision) = GetCurrentMappingRevisions(current);
        SourceAssetItemViewModel targetSource = Panels
            .Select(panel => panel.Source)
            .First(source => source.Asset.Id == current.TargetAssetId);
        SpatialRegistrationResult result = SpatialRegistrationSolver.Solve(
            current.SourceAssetId,
            current.TargetAssetId,
            sourceRevision,
            targetRevision,
            kind,
            landmarkPairs,
            DateTimeOffset.UtcNow,
            targetSource.Calibration.Calibration,
            current.Id);
        LinkGroups[groupIndex] = group.ReplaceMapping(result.Mapping).EnsureValid();
        LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        string physical = result.RmsPhysical is double value
            ? $" · RMS {value.ToString("0.###", CultureInfo.InvariantCulture)} {result.PhysicalUnit}"
            : string.Empty;
        LinkSynchronizationStatusText =
            $"{kind} registration 已更新 · {result.PointResiduals.Count} pairs · RMS {result.RmsPixels.ToString("0.###", CultureInfo.InvariantCulture)} px{physical}";

        FigurePanelViewModel? reference = Panels.FirstOrDefault(panel =>
            panel.CropLinkGroupId == groupId &&
            panel.Source.Asset.Id == group.ReferenceAssetId);
        if (reference is not null)
        {
            SynchronizeLinkedCrop(reference);
        }

        EditCompleted?.Invoke(this, EventArgs.Empty);
        return result;
    }

    public SpatialMappingRevisionState GetLinkMappingRevisionState(Guid groupId, Guid targetAssetId)
    {
        LinkGroup group = LinkGroups.Single(group => group.Id == groupId);
        SpatialMapping mapping = group.Mappings.Single(item => item.TargetAssetId == targetAssetId);
        (long sourceRevision, long targetRevision) = GetCurrentMappingRevisions(mapping);
        return mapping.GetRevisionState(sourceRevision, targetRevision);
    }

    private (long SourceRevision, long TargetRevision) GetCurrentMappingRevisions(SpatialMapping mapping)
    {
        long sourceRevision = Panels
            .Select(panel => panel.Source)
            .First(source => source.Asset.Id == mapping.SourceAssetId)
            .SourceRevision;
        long targetRevision = Panels
            .Select(panel => panel.Source)
            .First(source => source.Asset.Id == mapping.TargetAssetId)
            .SourceRevision;
        return (sourceRevision, targetRevision);
    }
    public void UpdateLinkSyncOptions(Guid groupId, LinkSyncOptions syncOptions)
    {
        int groupIndex = FindLinkGroupIndex(groupId);
        if (groupIndex < 0)
        {
            throw new InvalidOperationException("找不到待更新的联动组。");
        }

        LinkGroups[groupIndex] = (LinkGroups[groupIndex] with { SyncOptions = syncOptions }).EnsureValid();
        LinkGroupsChanged?.Invoke(this, EventArgs.Empty);
        DocumentChanged?.Invoke(this, EventArgs.Empty);
        EditCompleted?.Invoke(this, EventArgs.Empty);
    }

    private int FindLinkGroupIndex(Guid groupId)
    {
        for (int index = 0; index < LinkGroups.Count; index++)
        {
            if (LinkGroups[index].Id == groupId)
            {
                return index;
            }
        }

        return -1;
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
        foreach (FigurePanelViewModel panel in Panels)
        {
            panel.UpdateInheritedGlobalStyle(GlobalStyle);
        }
        foreach (FigurePlotPanelViewModel panel in PlotPanels)
        {
            panel.UpdateInheritedStyle(GlobalStyle);
        }
        NotifySelectedPanelStyleChanged();
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

    private void SelectOnlyPlotPanel(FigurePlotPanelViewModel? panel)
    {
        if (panel is not null && !PlotPanels.Contains(panel))
        {
            throw new InvalidOperationException("只能选择当前拼版中的 Plot panel。");
        }

        _isUpdatingPlotPanelSelection = true;
        try
        {
            foreach (FigurePlotPanelViewModel candidate in PlotPanels)
            {
                candidate.IsSelected = ReferenceEquals(candidate, panel);
            }
        }
        finally
        {
            _isUpdatingPlotPanelSelection = false;
        }

        if (SetProperty(ref _selectedPlotPanel, panel, nameof(SelectedPlotPanel)))
        {
            RemoveSelectedPlotPanelCommand.NotifyCanExecuteChanged();
        }
    }

    private void AttachPlotPanel(FigurePlotPanelViewModel panel)
    {
        panel.UpdateInheritedStyle(GlobalStyle);
        panel.PropertyChanged += OnPlotPanelPropertyChanged;
    }

    private void SelectOnlyAnnotation(FigureAnnotationViewModel? annotation)
    {
        if (annotation is not null && !Annotations.Contains(annotation))
        {
            throw new InvalidOperationException("只能选择当前拼版中的标注。");
        }

        _isUpdatingAnnotationSelection = true;
        try
        {
            foreach (FigureAnnotationViewModel candidate in Annotations)
            {
                candidate.IsSelected = ReferenceEquals(candidate, annotation);
            }
        }
        finally
        {
            _isUpdatingAnnotationSelection = false;
        }

        SetPrimaryAnnotation(annotation);
        NotifyAnnotationSelectionChanged();
    }

    private void SetPrimaryAnnotation(FigureAnnotationViewModel? annotation)
    {
        if (!SetProperty(ref _selectedAnnotation, annotation, nameof(SelectedAnnotation)))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedAnnotationVisibility));
        RemoveSelectedAnnotationCommand.NotifyCanExecuteChanged();
        MoveAnnotationUpCommand.NotifyCanExecuteChanged();
        MoveAnnotationDownCommand.NotifyCanExecuteChanged();
        ResetSelectedAnnotationStyleCommand.NotifyCanExecuteChanged();
        CopySelectedAnnotationStyleCommand.NotifyCanExecuteChanged();
        PasteSelectedAnnotationStyleCommand.NotifyCanExecuteChanged();
        ApplyAnnotationStyleToSameTypeCommand.NotifyCanExecuteChanged();
    }

    private void NotifyAnnotationSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedAnnotations));
        OnPropertyChanged(nameof(SelectedAnnotationCount));
        OnPropertyChanged(nameof(SelectedAnnotationCountText));
        OnPropertyChanged(nameof(MultipleAnnotationSelectionVisibility));
        SelectAllAnnotationsCommand.NotifyCanExecuteChanged();
        ClearAnnotationSelectionCommand.NotifyCanExecuteChanged();
        AlignAnnotationLeftCommand.NotifyCanExecuteChanged();
        AlignAnnotationHorizontalCenterCommand.NotifyCanExecuteChanged();
        AlignAnnotationRightCommand.NotifyCanExecuteChanged();
        AlignAnnotationTopCommand.NotifyCanExecuteChanged();
        AlignAnnotationVerticalCenterCommand.NotifyCanExecuteChanged();
        AlignAnnotationBottomCommand.NotifyCanExecuteChanged();
        SetAnnotationDirectionHorizontalCommand.NotifyCanExecuteChanged();
        SetAnnotationDirectionVerticalCommand.NotifyCanExecuteChanged();
    }

    private void SetPrimaryPanel(FigurePanelViewModel? panel)
    {
        if (SetProperty(ref _selectedPanel, panel, nameof(SelectedPanel)))
        {
            RemoveSelectedCommand.NotifyCanExecuteChanged();
            MoveLayerUpCommand.NotifyCanExecuteChanged();
            MoveLayerDownCommand.NotifyCanExecuteChanged();
            NotifyPanelAlignmentCanExecuteChanged();
            NotifySelectedPanelStyleChanged();
        }
    }

    private void NotifySelectedPanelStyleChanged()
    {
        OnPropertyChanged(nameof(SelectedPanelLabelFontFamily));
        OnPropertyChanged(nameof(SelectedPanelLabelFontChoices));
        OnPropertyChanged(nameof(SelectedPanelLabelFontAvailabilityMessage));
        OnPropertyChanged(nameof(SelectedPanelLabelFontSizePt));
        OnPropertyChanged(nameof(SelectedPanelLabelTextColor));
        OnPropertyChanged(nameof(SelectedPanelLabelTextBrush));
        OnPropertyChanged(nameof(SelectedPanelLabelIsBold));
        OnPropertyChanged(nameof(SelectedPanelScaleBarFontFamily));
        OnPropertyChanged(nameof(SelectedPanelScaleBarFontChoices));
        OnPropertyChanged(nameof(SelectedPanelScaleBarFontAvailabilityMessage));
        OnPropertyChanged(nameof(SelectedPanelScaleBarFontSizePt));
        OnPropertyChanged(nameof(SelectedPanelScaleBarLabelColor));
        OnPropertyChanged(nameof(SelectedPanelScaleBarLabelBrush));
        OnPropertyChanged(nameof(SelectedPanelScaleBarLabelIsBold));
        OnPropertyChanged(nameof(SelectedPanelScaleBarColor));
        OnPropertyChanged(nameof(SelectedPanelScaleBarBrush));
        OnPropertyChanged(nameof(SelectedPanelScaleBarThicknessPt));
        OnPropertyChanged(nameof(SelectedPanelStyleOverrideStatusText));
        ResetSelectedPanelLabelStyleCommand.NotifyCanExecuteChanged();
        ResetSelectedPanelScaleBarStyleCommand.NotifyCanExecuteChanged();
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

    private void NotifyPlotPanelCollectionChanged()
    {
        OnPropertyChanged(nameof(PanelCountText));
        OnPropertyChanged(nameof(EmptyVisibility));
        RemoveSelectedPlotPanelCommand.NotifyCanExecuteChanged();
        RenumberPanelLabelsCommand.NotifyCanExecuteChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyMeasurementOverlayCollectionChanged()
    {
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyRoiProjectionCollectionChanged()
    {
        OnPropertyChanged(nameof(EmptyVisibility));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
    private void NotifyAnnotationCollectionChanged()
    {
        OnPropertyChanged(nameof(AnnotationCountText));
        OnPropertyChanged(nameof(GlobalStyleStatusText));
        OnPropertyChanged(nameof(EmptyVisibility));
        NotifyAnnotationSelectionChanged();
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }
    private void NotifyScientificObjectCollectionChanged()
    {
        OnPropertyChanged(nameof(ScientificObjectCountText));
        OnPropertyChanged(nameof(EmptyVisibility));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyGuideCollectionChanged()
    {
        OnPropertyChanged(nameof(GuideCountText));
        DocumentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SynchronizeLinkedCrop(FigurePanelViewModel changedPanel)
    {
        LinkCoordinator.SynchronizeCrop(changedPanel, Panels);
        OnPropertyChanged(nameof(LinkSynchronizationStatusText));
    }

    private void SynchronizeLinkedColorScale(FigurePanelViewModel changedPanel)
    {
        LinkCoordinator.SynchronizeColorScale(changedPanel, Panels);
        OnPropertyChanged(nameof(LinkSynchronizationStatusText));
    }

    private void OnPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_isRestoringRoiProjectionPanelState)
        {
            return;
        }

        if (sender is FigurePanelViewModel relationshipPanel &&
            e.PropertyName is nameof(FigurePanelViewModel.Source) or
                nameof(FigurePanelViewModel.SourceRect) or
                nameof(FigurePanelViewModel.FrameIndex) &&
            HasRoiProjections(relationshipPanel.Id) &&
            !TryAcceptRoiProjectionPanelState(relationshipPanel))
        {
            return;
        }

        if (e.PropertyName is nameof(FigurePanelViewModel.Source) or
            nameof(FigurePanelViewModel.SourceRect) or
            nameof(FigurePanelViewModel.FrameIndex) or
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
            nameof(FigurePanelViewModel.CalibrationUnit) or
            nameof(FigurePanelViewModel.PrimaryScaleBarAnchor) or
            nameof(FigurePanelViewModel.AdditionalScaleBars) or
            nameof(FigurePanelViewModel.HasScaleBars) or
            nameof(FigurePanelViewModel.ScaleBarShowLabel) or
            nameof(FigurePanelViewModel.StyleOverride) or
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
            if (e.PropertyName != nameof(FigurePanelViewModel.CropLinkGroupId) ||
                !_isUpdatingLinkGroupMembership)
            {
                DocumentChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        if (sender is FigurePanelViewModel overlayPanel &&
            e.PropertyName is nameof(FigurePanelViewModel.SourceRect) or
                nameof(FigurePanelViewModel.FrameIndex) or
                nameof(FigurePanelViewModel.X) or
                nameof(FigurePanelViewModel.Y) or
                nameof(FigurePanelViewModel.Width) or
                nameof(FigurePanelViewModel.Height) or
                nameof(FigurePanelViewModel.IsVisible))
        {
            foreach (FigureMeasurementOverlayViewModel overlay in MeasurementOverlays)
            {
                overlay.RefreshLayout(overlayPanel);
            }
            foreach (FigureRoiProjectionViewModel projection in RoiProjections)
            {
                projection.RefreshLayout(overlayPanel);
            }
        }
        if (e.PropertyName == nameof(FigurePanelViewModel.StyleOverride) &&
            ReferenceEquals(sender, SelectedPanel))
        {
            NotifySelectedPanelStyleChanged();
        }

        if (sender is FigurePanelViewModel linkedPanel &&
            linkedPanel.CropLinkGroupId.HasValue &&
            !LinkCoordinator.IsSynchronizing)
        {
            if (e.PropertyName == nameof(FigurePanelViewModel.SourceRect))
            {
                SynchronizeLinkedCrop(linkedPanel);
            }
            else if (e.PropertyName is nameof(FigurePanelViewModel.BlackPoint) or
                     nameof(FigurePanelViewModel.WhitePoint))
            {
                SynchronizeLinkedColorScale(linkedPanel);
            }
        }

        if (e.PropertyName is nameof(FigurePanelViewModel.Source) or
            nameof(FigurePanelViewModel.SourceRect) or
            nameof(FigurePanelViewModel.FrameIndex) or
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
                SelectedScientificObject = null;
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

    private void OnPlotPanelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is not FigurePlotPanelViewModel panel)
        {
            return;
        }

        if (e.PropertyName == nameof(FigurePlotPanelViewModel.IsSelected) &&
            !_isUpdatingPlotPanelSelection)
        {
            SelectOnlyPlotPanel(panel.IsSelected ? panel : null);
            return;
        }

        if (e.PropertyName is nameof(FigurePlotPanelViewModel.X) or
            nameof(FigurePlotPanelViewModel.Y) or
            nameof(FigurePlotPanelViewModel.Width) or
            nameof(FigurePlotPanelViewModel.Height) or
            nameof(FigurePlotPanelViewModel.Label) or
            nameof(FigurePlotPanelViewModel.IsVisible) or
            nameof(FigurePlotPanelViewModel.IsLocked) or
            nameof(FigurePlotPanelViewModel.ZIndex) or
            nameof(FigurePlotPanelViewModel.StyleOverride) or
            nameof(FigurePlotPanelViewModel.TypographyOverride) or
            nameof(FigurePlotPanelViewModel.Plot))
        {
            RemoveSelectedPlotPanelCommand.NotifyCanExecuteChanged();
            DocumentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void RememberRoiProjectionPanelState(FigurePanelViewModel panel)
    {
        _roiProjectionPanelStates[panel.Id] = new RoiProjectionPanelState(
            panel.Source,
            panel.SourceRect,
            panel.FrameIndex);
    }

    private bool TryAcceptRoiProjectionPanelState(FigurePanelViewModel panel)
    {
        FigureRoiProjectionViewModel[] projections = RoiProjections
            .Where(item => item.PanelId == panel.Id)
            .ToArray();
        try
        {
            foreach (FigureRoiProjectionViewModel projection in projections)
            {
                projection.ValidatePanel(panel);
            }

            foreach (FigureRoiProjectionViewModel projection in projections)
            {
                projection.RefreshLayout(panel);
            }
            RememberRoiProjectionPanelState(panel);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            if (!_roiProjectionPanelStates.TryGetValue(panel.Id, out RoiProjectionPanelState? previous))
            {
                throw;
            }

            _isRestoringRoiProjectionPanelState = true;
            try
            {
                if (!ReferenceEquals(panel.Source, previous.Source))
                {
                    panel.ReplaceSource(previous.Source, previous.SourceRect);
                }
                else if (panel.SourceRect != previous.SourceRect)
                {
                    panel.ApplyLinkedCrop(previous.SourceRect);
                }

                panel.FrameIndex = previous.FrameIndex;
            }
            finally
            {
                _isRestoringRoiProjectionPanelState = false;
            }

            foreach (FigureRoiProjectionViewModel projection in projections)
            {
                projection.RefreshLayout(panel);
            }
            LinkSynchronizationStatusText =
                $"Panel 修改已回滚：会破坏 ROI Projection 引用关系。{exception.Message}";
            return false;
        }
    }

    private sealed record RoiProjectionPanelState(
        SourceAssetItemViewModel Source,
        PixelRect64 SourceRect,
        int FrameIndex);

    private void OnAnnotationPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FigureAnnotationViewModel.X) or
            nameof(FigureAnnotationViewModel.Y) or
            nameof(FigureAnnotationViewModel.EndX) or
            nameof(FigureAnnotationViewModel.EndY) or
            nameof(FigureAnnotationViewModel.Text) or
            nameof(FigureAnnotationViewModel.StrokeColor) or
            nameof(FigureAnnotationViewModel.FillColor) or
            nameof(FigureAnnotationViewModel.FillOpacityPercent) or
            nameof(FigureAnnotationViewModel.TextColor) or
            nameof(FigureAnnotationViewModel.FontFamily) or
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
            ResetSelectedAnnotationStyleCommand.NotifyCanExecuteChanged();
            PasteSelectedAnnotationStyleCommand.NotifyCanExecuteChanged();
            NotifyAnnotationSelectionChanged();
        }

        if (e.PropertyName == nameof(FigureAnnotationViewModel.IsSelected) &&
            !_isUpdatingAnnotationSelection &&
            sender is FigureAnnotationViewModel annotation)
        {
            SetPrimaryAnnotation(annotation.IsSelected
                ? annotation
                : ReferenceEquals(annotation, SelectedAnnotation)
                    ? SelectedAnnotations.LastOrDefault()
                    : SelectedAnnotation);
            NotifyAnnotationSelectionChanged();
        }
    }
    private void OnScientificObjectPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FigureScientificObjectViewModel.IsLocked))
        {
            RemoveSelectedScientificObjectCommand.NotifyCanExecuteChanged();
        }

        DocumentChanged?.Invoke(this, EventArgs.Empty);
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
        AddAdditionalScaleBarCommand.NotifyCanExecuteChanged();
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

        var readingOrder = Panels
            .Select(panel => new PanelLabelTarget(panel.X, panel.Y, panel.ZIndex, value => panel.Label = value))
            .Concat(PlotPanels.Select(panel =>
                new PanelLabelTarget(panel.X, panel.Y, panel.ZIndex, value => panel.Label = value)))
            .OrderBy(panel => panel.Y)
            .ThenBy(panel => panel.X)
            .ThenBy(panel => panel.ZIndex)
            .ToArray();
        for (int index = 0; index < readingOrder.Length; index++)
        {
            readingOrder[index].SetLabel(PanelLabelGenerator.Generate(
                index,
                PanelLabelGenerator.FromLegacySettings(PanelLabelSequence)));
        }

        OnPropertyChanged(nameof(PanelLabelSettingsText));
        if (force && readingOrder.Length > 0)
        {
            EditCompleted?.Invoke(this, EventArgs.Empty);
        }
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

    private sealed record PanelLabelTarget(
        long X,
        long Y,
        int ZIndex,
        Action<string> SetLabel);
}
