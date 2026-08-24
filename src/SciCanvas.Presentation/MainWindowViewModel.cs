using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SciCanvas.Core.Cropping;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Templates;

namespace SciCanvas.Presentation;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly IImageFilePicker _filePicker;
    private readonly IExportFilePicker _exportFilePicker;
    private readonly ISourceAssetReader _sourceReader;
    private readonly IImagePreviewLoader _previewLoader;
    private readonly IPathSafetyPolicy _pathSafetyPolicy;
    private readonly IImageCropExporter _cropExporter;
    private readonly IFigureExporter _figureExporter;
    private readonly IProjectFilePicker _projectFilePicker;
    private readonly IProjectStore _projectStore;
    private readonly IProjectRecoveryStore _projectRecoveryStore;
    private readonly IProjectRecoveryPrompt _projectRecoveryPrompt;
    private readonly ISourceRelinkFilePicker _sourceRelinkFilePicker;
    private readonly ISourceRevisionAcceptancePrompt _sourceRevisionAcceptancePrompt;
    private readonly ITemplateFilePicker? _templateFilePicker;
    private readonly IUserTemplateCatalog? _userTemplateCatalog;
    private readonly IBatchExportFolderPicker? _batchExportFolderPicker;
    private readonly IReadOnlyList<FigureTemplateDefinition> _figureTemplates;
    private readonly IIntensityProfileAnalyzer _intensityProfileAnalyzer;
    private readonly IAssistedRegionAnalyzer _assistedRegionAnalyzer;
    private readonly EditorHistoryManager _history = new(100);
    private readonly List<ProjectAuditEntrySnapshot> _auditTrail = [];
    private readonly DispatcherTimer _autosaveTimer;
    private FigureCanvasViewModel _figure;
    private FigureTemplateDefinition _selectedFigureTemplate;
    private SourceAssetItemViewModel? _selectedSource;
    private BatchCropQueueItemViewModel? _selectedBatchCrop;
    private ExportProfileEditorViewModel? _selectedExportProfile;
    private bool _isBusy;
    private string _statusMessage = "就绪";
    private string? _lastError;
    private bool _isCropOverlayVisible = true;
    private bool _lockCropSizeAcrossSources = true;
    private WorkspaceMode _workspaceMode = WorkspaceMode.Crop;
    private bool _isLayersTabActive;
    private Guid _projectId = Guid.NewGuid();
    private DateTimeOffset _projectCreatedAt = DateTimeOffset.UtcNow;
    private string? _projectPath;
    private bool _isDirty;
    private bool _isRestoringProject;
    private bool _historyReady;
    private bool _autosaveInProgress;
    private bool _autosavePending;
    private string _autosaveStatusText = "自动保存待命";
    private ScientificToolMode _activeScienceTool = ScientificToolMode.Crop;
    private ScientificMeasurementViewModel? _pendingMeasurement;
    private int _pendingAngleStep;
    private FigureQcIssueViewModel? _selectedFigureQcIssue;
    private string _figureQcStatusText = "尚未运行 Figure QC";
    private bool _isFigureQcStale = true;
    private IntensityProfileResult? _intensityProfile;
    private PointCollection _intensityProfilePoints = [];
    private string _intensityProfileStatusText = "请选择长度测量后运行强度剖面";
    private AssistedRegionAnalysisResult? _assistedRegionResult;
    private AssistedRegionCandidateViewModel? _selectedAssistedRegion;
    private AssistedRegionMode _assistedRegionMode = AssistedRegionMode.BrightParticles;
    private bool _useAutomaticRegionThreshold = true;
    private double _regionThresholdPercent = 50;
    private int _minimumRegionAreaPixels = 16;
    private string _assistedRegionStatusText = "在当前裁剪 ROI 中生成可人工复核的候选区域";
    private string _smartAssistStatusText = "可解释规则会给出布局、样式、QC 与科研诚信建议；建议默认不自动成为事实。";

    public MainWindowViewModel(
        IImageFilePicker filePicker,
        ISourceAssetReader sourceReader,
        IImagePreviewLoader previewLoader,
        IExportFilePicker exportFilePicker,
        IPathSafetyPolicy pathSafetyPolicy,
        IImageCropExporter cropExporter,
        IFigureExporter figureExporter,
        IReadOnlyList<FigureTemplateDefinition> figureTemplates,
        IProjectFilePicker projectFilePicker,
        IProjectStore projectStore,
        IProjectRecoveryStore? projectRecoveryStore = null,
        IProjectRecoveryPrompt? projectRecoveryPrompt = null,
        ISourceRelinkFilePicker? sourceRelinkFilePicker = null,
        ISourceRevisionAcceptancePrompt? sourceRevisionAcceptancePrompt = null,
        ITemplateFilePicker? templateFilePicker = null,
        IUserTemplateCatalog? userTemplateCatalog = null,
        IBatchExportFolderPicker? batchExportFolderPicker = null,
        IIntensityProfileAnalyzer? intensityProfileAnalyzer = null,
        IAssistedRegionAnalyzer? assistedRegionAnalyzer = null)
    {
        _filePicker = filePicker ?? throw new ArgumentNullException(nameof(filePicker));
        _sourceReader = sourceReader ?? throw new ArgumentNullException(nameof(sourceReader));
        _previewLoader = previewLoader ?? throw new ArgumentNullException(nameof(previewLoader));
        _exportFilePicker = exportFilePicker ?? throw new ArgumentNullException(nameof(exportFilePicker));
        _pathSafetyPolicy = pathSafetyPolicy ?? throw new ArgumentNullException(nameof(pathSafetyPolicy));
        _cropExporter = cropExporter ?? throw new ArgumentNullException(nameof(cropExporter));
        _figureExporter = figureExporter ?? throw new ArgumentNullException(nameof(figureExporter));
        _projectFilePicker = projectFilePicker ?? throw new ArgumentNullException(nameof(projectFilePicker));
        _projectStore = projectStore ?? throw new ArgumentNullException(nameof(projectStore));
        _projectRecoveryStore = projectRecoveryStore ?? new NullProjectRecoveryStore();
        _projectRecoveryPrompt = projectRecoveryPrompt ?? new DeclineProjectRecoveryPrompt();
        _templateFilePicker = templateFilePicker;
        _userTemplateCatalog = userTemplateCatalog;
        _batchExportFolderPicker = batchExportFolderPicker;
        _sourceRelinkFilePicker = sourceRelinkFilePicker ?? new NullSourceRelinkFilePicker();
        _sourceRevisionAcceptancePrompt = sourceRevisionAcceptancePrompt ??
            new DeclineSourceRevisionAcceptancePrompt();
        _intensityProfileAnalyzer = intensityProfileAnalyzer ?? new WpfIntensityProfileAnalyzer();
        _assistedRegionAnalyzer = assistedRegionAnalyzer ?? new WpfAssistedRegionAnalyzer();
        ArgumentNullException.ThrowIfNull(figureTemplates);
        if (figureTemplates.Count == 0)
        {
            throw new ArgumentException("至少需要一个拼版模板。", nameof(figureTemplates));
        }

        _figureTemplates = figureTemplates.ToArray();
        AvailableTemplates = new ObservableCollection<FigureTemplateDefinition>(_figureTemplates);
        _selectedFigureTemplate = _figureTemplates[0];
        _figure = new FigureCanvasViewModel(_selectedFigureTemplate);
        foreach (FigureExportProfile profile in FigureExportProfile.BuiltIns)
        {
            var editor = new ExportProfileEditorViewModel(profile);
            editor.PropertyChanged += OnExportProfilePropertyChanged;
            ExportProfiles.Add(editor);
        }
        _selectedExportProfile = ExportProfiles[0];
        _autosaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(10),
        };
        _autosaveTimer.Tick += OnAutosaveTimerTick;
        Crop.PropertyChanged += OnCropPropertyChanged;
        Figure.DocumentChanged += OnFigureDocumentChanged;
        Figure.EditCompleted += OnFigureEditCompleted;
        OpenSourcesCommand = new AsyncRelayCommand(
            ImportSourcesAsync,
            () => !IsBusy,
            HandleUnexpectedCommandError);
        ExportCropCommand = new AsyncRelayCommand(
            ExportCropAsync,
            () => HasSelection && !IsBusy,
            HandleUnexpectedCommandError);
        ExportBatchCropsCommand = new AsyncRelayCommand(
            ExportBatchCropsAsync,
            () => BatchCropQueue.Count > 0 && !IsBusy && _batchExportFolderPicker is not null,
            HandleUnexpectedCommandError);
        AddCurrentCropToBatchQueueCommand = new RelayCommand(
            AddCurrentCropToBatchQueue,
            () => HasSelection && Crop.TryGetCrop(out _) && !IsBusy);
        RemoveSelectedBatchCropCommand = new RelayCommand(
            RemoveSelectedBatchCrop,
            () => SelectedBatchCrop is not null && !IsBusy);
        ClearBatchCropQueueCommand = new RelayCommand(
            ClearBatchCropQueue,
            () => BatchCropQueue.Count > 0 && !IsBusy);
        ImportTemplateCommand = new RelayCommand(
            ImportTemplate,
            () => _templateFilePicker is not null && _userTemplateCatalog is not null && !IsBusy);
        AcceptSourceRevisionCommand = new AsyncRelayCommand(
            AcceptSelectedSourceRevisionAsync,
            () => HasSelection && !IsBusy,
            HandleUnexpectedCommandError);
        ExportFigureCommand = new AsyncRelayCommand(
            ExportFigureAsync,
            () => Figure.Panels.Count > 0 && !IsBusy,
            HandleUnexpectedCommandError);
        ExportFigureVariantsCommand = new AsyncRelayCommand(
            ExportFigureVariantsAsync,
            () => Figure.Panels.Count > 0 && !IsBusy && _batchExportFolderPicker is not null,
            HandleUnexpectedCommandError);
        RunFigureQcCommand = new RelayCommand(RunFigureQc, () => !IsBusy);
        ApplySmartLayoutCommand = new RelayCommand(ApplySmartLayout, () => !IsBusy);
        HarmonizeFigureStyleCommand = new RelayCommand(HarmonizeFigureStyle, () => !IsBusy);
        RunAssistedFigureReviewCommand = new RelayCommand(RunAssistedFigureReview, () => !IsBusy);
        NavigateToSelectedQcIssueCommand = new RelayCommand(
            NavigateToSelectedQcIssue,
            () => SelectedFigureQcIssue?.CanNavigate == true && !IsBusy);
        AddExportProfileCommand = new RelayCommand(AddExportProfile, () => !IsBusy);
        RemoveSelectedExportProfileCommand = new RelayCommand(
            RemoveSelectedExportProfile,
            () => SelectedExportProfile is not null && ExportProfiles.Count > 1 && !IsBusy);
        ResetExportProfilesCommand = new RelayCommand(
            () => ResetExportProfilesToBuiltIns(markDirty: true), () => !IsBusy);
        ShowCropWorkspaceCommand = new RelayCommand(() => WorkspaceMode = WorkspaceMode.Crop);
        ShowFigureWorkspaceCommand = new RelayCommand(() => WorkspaceMode = WorkspaceMode.Figure);
        ShowInspectorTabCommand = new RelayCommand(() => IsLayersTabActive = false);
        ShowLayersTabCommand = new RelayCommand(() => IsLayersTabActive = true);
        SelectCropToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.Crop);
        SelectCalibrationToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.Calibration);
        SelectLengthToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.Length);
        SelectAngleToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.Angle);
        SelectRectangleRoiToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.RectangleRoi);
        SelectCircleRoiToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.CircleRoi);
        SelectPolylineToolCommand = new RelayCommand(() => ActiveScienceTool = ScientificToolMode.Polyline);
        DeleteSelectedMeasurementCommand = new RelayCommand(
            DeleteSelectedMeasurement,
            () => SelectedSource?.SelectedMeasurement is { IsLocked: false } && !IsBusy);
        DeleteSelectionCommand = new RelayCommand(DeleteCurrentSelection, () => !IsBusy);
        CopyMeasurementsCommand = new RelayCommand(
            CopyMeasurements,
            () => SelectedSource?.Measurements.Count > 0 && !IsBusy);
        ExportMeasurementsCommand = new AsyncRelayCommand(
            ExportMeasurementsAsync,
            () => SelectedSource?.Measurements.Count > 0 && !IsBusy,
            HandleUnexpectedCommandError);
        AnalyzeIntensityProfileCommand = new AsyncRelayCommand(
            AnalyzeIntensityProfileAsync,
            () => SelectedSource?.Measurements.Count > 0 && !IsBusy,
            HandleUnexpectedCommandError);
        AnalyzeAssistedRegionsCommand = new AsyncRelayCommand(
            AnalyzeAssistedRegionsAsync,
            () => SelectedSource is not null && Crop.TryGetCrop(out _) && !IsBusy,
            HandleUnexpectedCommandError);
        AcceptAllAssistedRegionsCommand = new RelayCommand(
            AcceptAllAssistedRegions,
            () => AssistedRegions.Any(candidate => !candidate.IsCommitted) && !IsBusy);
        RejectSelectedAssistedRegionCommand = new RelayCommand(
            RejectSelectedAssistedRegion,
            () => SelectedAssistedRegion is { IsCommitted: false } && !IsBusy);
        CommitAcceptedAssistedRegionsCommand = new RelayCommand(
            CommitAcceptedAssistedRegions,
            () => AssistedRegions.Any(candidate => candidate.IsAccepted && !candidate.IsCommitted) && !IsBusy);
        ClearAssistedRegionsCommand = new RelayCommand(
            ClearAssistedRegionAnalysis,
            () => AssistedRegions.Count > 0 && !IsBusy);
        ApplyCalibrationToFigurePanelsCommand = new RelayCommand(
            ApplyCalibrationToFigurePanels,
            () => SelectedSource?.Calibration.IsCalibrated == true && !IsBusy);
        ShowHelpCommand = new RelayCommand(() => StatusMessage = "快捷键 · V 裁剪，K 标定，L 长度，A 角度，R 矩形，E 圆形，P 折线；F 适合窗口，1 原始大小，Ctrl +/- 缩放，Space/中键平移，Delete 删除，方向键微调");
        AddCurrentCropToFigureCommand = new RelayCommand(
            AddCurrentCropToFigure,
            () => HasSelection && Figure.Panels.Count < Figure.SlotCount &&
                  Crop.TryGetCrop(out _));
        ReplaceSelectedPanelSourceCommand = new RelayCommand(
            ReplaceSelectedPanelSource,
            () => HasSelection && Figure.SelectedPanel is not null && Crop.TryGetCrop(out _) && !IsBusy);
        OpenProjectCommand = new AsyncRelayCommand(
            OpenProjectAsync,
            () => !IsBusy,
            HandleUnexpectedCommandError);
        SaveProjectCommand = new AsyncRelayCommand(
            SaveProjectAsync,
            () => !IsBusy,
            HandleUnexpectedCommandError);
        SaveProjectAsCommand = new AsyncRelayCommand(
            SaveProjectAsAsync,
            () => !IsBusy,
            HandleUnexpectedCommandError);
        NewProjectCommand = new RelayCommand(NewProject);
        UndoCommand = new RelayCommand(Undo, () => _history.CanUndo && !IsBusy);
        RedoCommand = new RelayCommand(Redo, () => _history.CanRedo && !IsBusy);
        _history.Reset(CaptureHistorySnapshot(), markSaved: true);
        _historyReady = true;
    }

    public BatchCropQueueItemViewModel? SelectedBatchCrop
    {
        get => _selectedBatchCrop;
        set
        {
            if (SetProperty(ref _selectedBatchCrop, value))
            {
                RemoveSelectedBatchCropCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string BatchCropQueueSummary => $"批量队列 · {BatchCropQueue.Count} 项";

    public AsyncRelayCommand ExportBatchCropsCommand { get; }

    public RelayCommand AddCurrentCropToBatchQueueCommand { get; }

    public RelayCommand RemoveSelectedBatchCropCommand { get; }

    public RelayCommand ClearBatchCropQueueCommand { get; }
    public RelayCommand ImportTemplateCommand { get; }


    public ObservableCollection<SourceAssetItemViewModel> Sources { get; } = [];

    public ObservableCollection<BatchCropQueueItemViewModel> BatchCropQueue { get; } = [];

    public ObservableCollection<ExportProfileEditorViewModel> ExportProfiles { get; } = [];

    public ObservableCollection<FigureQcIssueViewModel> FigureQcIssues { get; } = [];

    public ObservableCollection<AssistedRegionCandidateViewModel> AssistedRegions { get; } = [];

    public IReadOnlyList<AssistedRegionModeOption> AssistedRegionModes { get; } =
    [
        new(AssistedRegionMode.BrightParticles, "亮颗粒", "识别亮于阈值的颗粒候选"),
        new(AssistedRegionMode.DarkParticles, "暗颗粒", "识别暗于阈值的颗粒候选"),
        new(AssistedRegionMode.DarkPores, "孔隙 / 暗区", "以暗区面积分数辅助孔隙率复核"),
        new(AssistedRegionMode.BrightPhase, "亮相区", "以亮区面积分数辅助相分数复核"),
        new(AssistedRegionMode.GrainRegions, "晶粒区域候选", "阈值分割晶粒内部并统计等效圆直径"),
        new(AssistedRegionMode.DarkCracks, "裂纹候选", "仅保留长宽比 ≥ 3 的暗区候选"),
        new(AssistedRegionMode.BrightLamellae, "片层候选", "仅保留长宽比 ≥ 3 的亮区候选"),
    ];

    public FigureQcIssueViewModel? SelectedFigureQcIssue
    {
        get => _selectedFigureQcIssue;
        set
        {
            if (SetProperty(ref _selectedFigureQcIssue, value))
            {
                NavigateToSelectedQcIssueCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string FigureQcStatusText
    {
        get => _figureQcStatusText;
        private set => SetProperty(ref _figureQcStatusText, value);
    }

    public string FigureQcCountText =>
        $"{FigureQcIssues.Count(issue => issue.Severity == FigurePreflightSeverity.Error)} 错误 · " +
        $"{FigureQcIssues.Count(issue => issue.Severity == FigurePreflightSeverity.Warning)} 提醒 · " +
        $"{FigureQcIssues.Count(issue => issue.Severity == FigurePreflightSeverity.Info)} 信息";

    public IntensityProfileResult? IntensityProfile
    {
        get => _intensityProfile;
        private set
        {
            if (SetProperty(ref _intensityProfile, value))
            {
                OnPropertyChanged(nameof(IntensityProfileVisibility));
            }
        }
    }

    public PointCollection IntensityProfilePoints
    {
        get => _intensityProfilePoints;
        private set => SetProperty(ref _intensityProfilePoints, value);
    }

    public string IntensityProfileStatusText
    {
        get => _intensityProfileStatusText;
        private set => SetProperty(ref _intensityProfileStatusText, value);
    }

    public Visibility IntensityProfileVisibility => IntensityProfile is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public AssistedRegionMode AssistedRegionMode
    {
        get => _assistedRegionMode;
        set
        {
            if (SetProperty(ref _assistedRegionMode, value))
            {
                MarkAssistedRegionAnalysisStale();
            }
        }
    }

    public bool UseAutomaticRegionThreshold
    {
        get => _useAutomaticRegionThreshold;
        set
        {
            if (SetProperty(ref _useAutomaticRegionThreshold, value))
            {
                MarkAssistedRegionAnalysisStale();
            }
        }
    }

    public double RegionThresholdPercent
    {
        get => _regionThresholdPercent;
        set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 50;
            if (SetProperty(ref _regionThresholdPercent, normalized))
            {
                MarkAssistedRegionAnalysisStale();
            }
        }
    }

    public int MinimumRegionAreaPixels
    {
        get => _minimumRegionAreaPixels;
        set
        {
            int normalized = Math.Clamp(value, 1, 10_000_000);
            if (SetProperty(ref _minimumRegionAreaPixels, normalized))
            {
                MarkAssistedRegionAnalysisStale();
            }
        }
    }

    public AssistedRegionCandidateViewModel? SelectedAssistedRegion
    {
        get => _selectedAssistedRegion;
        set
        {
            if (SetProperty(ref _selectedAssistedRegion, value))
            {
                RejectSelectedAssistedRegionCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string AssistedRegionStatusText
    {
        get => _assistedRegionStatusText;
        private set => SetProperty(ref _assistedRegionStatusText, value);
    }

    public string AssistedRegionDecisionText =>
        $"接受 {AssistedRegions.Count(candidate => candidate.IsAccepted)} · " +
        $"拒绝 {AssistedRegions.Count(candidate => !candidate.IsAccepted)} · " +
        $"已写入 {AssistedRegions.Count(candidate => candidate.IsCommitted)}";

    public Visibility AssistedRegionResultsVisibility => AssistedRegions.Count == 0
        ? Visibility.Collapsed
        : Visibility.Visible;

    public string SmartAssistStatusText
    {
        get => _smartAssistStatusText;
        private set => SetProperty(ref _smartAssistStatusText, value);
    }

    public ExportProfileEditorViewModel? SelectedExportProfile
    {
        get => _selectedExportProfile;
        set
        {
            if (SetProperty(ref _selectedExportProfile, value))
            {
                RemoveSelectedExportProfileCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ExportProfileSummary => $"投稿预设 · {ExportProfiles.Count} 项";

    public CropEditorViewModel Crop { get; } = new();

    public ObservableCollection<FigureTemplateDefinition> AvailableTemplates { get; }

    public FigureCanvasViewModel Figure
    {
        get => _figure;
        private set => SetProperty(ref _figure, value);
    }

    public FigureTemplateDefinition SelectedFigureTemplate
    {
        get => _selectedFigureTemplate;
        set
        {
            if (value is null || ReferenceEquals(value, _selectedFigureTemplate))
            {
                return;
            }

            if (!IsTemplateSelectionEnabled)
            {
                OnPropertyChanged();
                return;
            }

            ReplaceFigure(value);
        }
    }

    public bool IsTemplateSelectionEnabled =>
        Figure.Panels.Count == 0 && Figure.Annotations.Count == 0 &&
        Figure.Guides.Count == 0 && !IsBusy;

    public string TemplateLibraryLabel => $"模板库 · {AvailableTemplates.Count}";

    public AsyncRelayCommand OpenSourcesCommand { get; }

    public AsyncRelayCommand ExportCropCommand { get; }

    public AsyncRelayCommand ExportFigureCommand { get; }

    public AsyncRelayCommand ExportFigureVariantsCommand { get; }

    public RelayCommand RunFigureQcCommand { get; }
    public RelayCommand ApplySmartLayoutCommand { get; }
    public RelayCommand HarmonizeFigureStyleCommand { get; }
    public RelayCommand RunAssistedFigureReviewCommand { get; }

    public RelayCommand NavigateToSelectedQcIssueCommand { get; }

    public RelayCommand AddExportProfileCommand { get; }

    public RelayCommand RemoveSelectedExportProfileCommand { get; }

    public RelayCommand ResetExportProfilesCommand { get; }

    public AsyncRelayCommand AcceptSourceRevisionCommand { get; }

    public RelayCommand ShowCropWorkspaceCommand { get; }

    public RelayCommand ShowFigureWorkspaceCommand { get; }

    public RelayCommand ShowInspectorTabCommand { get; }

    public RelayCommand ShowLayersTabCommand { get; }
    public RelayCommand SelectCropToolCommand { get; }
    public RelayCommand SelectCalibrationToolCommand { get; }
    public RelayCommand SelectLengthToolCommand { get; }
    public RelayCommand SelectAngleToolCommand { get; }
    public RelayCommand SelectRectangleRoiToolCommand { get; }
    public RelayCommand SelectCircleRoiToolCommand { get; }
    public RelayCommand SelectPolylineToolCommand { get; }
    public RelayCommand DeleteSelectedMeasurementCommand { get; }

    public RelayCommand DeleteSelectionCommand { get; }
    public RelayCommand CopyMeasurementsCommand { get; }
    public AsyncRelayCommand ExportMeasurementsCommand { get; }
    public AsyncRelayCommand AnalyzeIntensityProfileCommand { get; }
    public AsyncRelayCommand AnalyzeAssistedRegionsCommand { get; }
    public RelayCommand AcceptAllAssistedRegionsCommand { get; }
    public RelayCommand RejectSelectedAssistedRegionCommand { get; }
    public RelayCommand CommitAcceptedAssistedRegionsCommand { get; }
    public RelayCommand ClearAssistedRegionsCommand { get; }
    public RelayCommand ApplyCalibrationToFigurePanelsCommand { get; }
    public RelayCommand ShowHelpCommand { get; }

    public RelayCommand AddCurrentCropToFigureCommand { get; }

    public RelayCommand ReplaceSelectedPanelSourceCommand { get; }

    public AsyncRelayCommand OpenProjectCommand { get; }

    public AsyncRelayCommand SaveProjectCommand { get; }

    public AsyncRelayCommand SaveProjectAsCommand { get; }

    public RelayCommand NewProjectCommand { get; }

    public RelayCommand UndoCommand { get; }

    public RelayCommand RedoCommand { get; }

    public string HistoryStatusText =>
        $"撤销 {_history.UndoCount} / 100 · 重做 {_history.RedoCount}";

    public string AutosaveStatusText
    {
        get => _autosaveStatusText;
        private set => SetProperty(ref _autosaveStatusText, value);
    }

    public SourceAssetItemViewModel? SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (SetProperty(ref _selectedSource, value))
            {
                if (value is not null)
                {
                    Crop.ConfigureForSource(
                        value.Asset.Metadata.PixelSize,
                        preserveSize: LockCropSizeAcrossSources);
                }

                CancelPendingScientificMeasurement();

                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(EmptyStateVisibility));
                OnPropertyChanged(nameof(MeasurementDockVisibility));
                OnPropertyChanged(nameof(ActiveScienceToolHint));
                OnPropertyChanged(nameof(CropOverlayVisibility));
                ExportCropCommand.NotifyCanExecuteChanged();
                AddCurrentCropToBatchQueueCommand.NotifyCanExecuteChanged();
                AcceptSourceRevisionCommand.NotifyCanExecuteChanged();
                AddCurrentCropToFigureCommand.NotifyCanExecuteChanged();
                ReplaceSelectedPanelSourceCommand.NotifyCanExecuteChanged();
                DeleteSelectedMeasurementCommand.NotifyCanExecuteChanged();
                DeleteSelectionCommand.NotifyCanExecuteChanged();
                CopyMeasurementsCommand.NotifyCanExecuteChanged();
                ExportMeasurementsCommand.NotifyCanExecuteChanged();
                AnalyzeIntensityProfileCommand.NotifyCanExecuteChanged();
                AnalyzeAssistedRegionsCommand.NotifyCanExecuteChanged();
                ApplyCalibrationToFigurePanelsCommand.NotifyCanExecuteChanged();
                ClearIntensityProfile();
                ClearAssistedRegionAnalysis();
                MarkDirty();
            }
        }
    }

    public bool HasSelection => SelectedSource is not null;

    public Visibility EmptyStateVisibility => HasSelection ? Visibility.Collapsed : Visibility.Visible;

    public Visibility MeasurementDockVisibility =>
        WorkspaceMode == WorkspaceMode.Crop && HasSelection
            ? Visibility.Visible
            : Visibility.Collapsed;

    public ScientificToolMode ActiveScienceTool
    {
        get => _activeScienceTool;
        set
        {
            if (SetProperty(ref _activeScienceTool, value))
            {
                CancelPendingScientificMeasurement();
                WorkspaceMode = WorkspaceMode.Crop;
                OnPropertyChanged(nameof(IsCropToolActive));
                OnPropertyChanged(nameof(IsCalibrationToolActive));
                OnPropertyChanged(nameof(IsLengthToolActive));
                OnPropertyChanged(nameof(IsAngleToolActive));
                OnPropertyChanged(nameof(IsRectangleRoiToolActive));
                OnPropertyChanged(nameof(IsCircleRoiToolActive));
                OnPropertyChanged(nameof(IsPolylineToolActive));
                OnPropertyChanged(nameof(ActiveScienceToolHint));
                OnPropertyChanged(nameof(CropOverlayVisibility));
            }
        }
    }

    public bool IsCropToolActive => ActiveScienceTool == ScientificToolMode.Crop;
    public bool IsCalibrationToolActive => ActiveScienceTool == ScientificToolMode.Calibration;
    public bool IsLengthToolActive => ActiveScienceTool == ScientificToolMode.Length;
    public bool IsAngleToolActive => ActiveScienceTool == ScientificToolMode.Angle;
    public bool IsRectangleRoiToolActive => ActiveScienceTool == ScientificToolMode.RectangleRoi;
    public bool IsCircleRoiToolActive => ActiveScienceTool == ScientificToolMode.CircleRoi;
    public bool IsPolylineToolActive => ActiveScienceTool == ScientificToolMode.Polyline;

    public Visibility CropOverlayVisibility =>
        IsCropOverlayVisible && IsCropToolActive
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string ActiveScienceToolHint => ActiveScienceTool switch
    {
        ScientificToolMode.Calibration => "标定工具 · 拖动参考线，然后输入已知真实距离",
        ScientificToolMode.Length => "长度工具 · 拖动测量，结果优先显示真实尺寸",
        ScientificToolMode.Angle => "角度工具 · 依次点击第一端点、顶点、第二端点",
        ScientificToolMode.RectangleRoi => "矩形 ROI · 拖动建立研究区域",
        ScientificToolMode.CircleRoi => "圆形测量 · 拖动建立等宽高 ROI，自动计算等效直径、面积与周长",
        ScientificToolMode.Polyline => "折线工具 · 逐点单击添加路径，双击最后一点完成",
        _ => "裁剪工具 · 拖动空白处新建，拖动框体移动，拖动八个手柄调整大小",
    };

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ExportBatchCropsCommand.NotifyCanExecuteChanged();
                AddCurrentCropToBatchQueueCommand.NotifyCanExecuteChanged();
                RemoveSelectedBatchCropCommand.NotifyCanExecuteChanged();
                ClearBatchCropQueueCommand.NotifyCanExecuteChanged();
                ImportTemplateCommand.NotifyCanExecuteChanged();
                OpenSourcesCommand.NotifyCanExecuteChanged();
                ExportCropCommand.NotifyCanExecuteChanged();
                ExportFigureCommand.NotifyCanExecuteChanged();
                ExportFigureVariantsCommand.NotifyCanExecuteChanged();
                RunFigureQcCommand.NotifyCanExecuteChanged();
                ApplySmartLayoutCommand.NotifyCanExecuteChanged();
                HarmonizeFigureStyleCommand.NotifyCanExecuteChanged();
                RunAssistedFigureReviewCommand.NotifyCanExecuteChanged();
                NavigateToSelectedQcIssueCommand.NotifyCanExecuteChanged();
                AddExportProfileCommand.NotifyCanExecuteChanged();
                RemoveSelectedExportProfileCommand.NotifyCanExecuteChanged();
                ResetExportProfilesCommand.NotifyCanExecuteChanged();
                AcceptSourceRevisionCommand.NotifyCanExecuteChanged();
                ReplaceSelectedPanelSourceCommand.NotifyCanExecuteChanged();
                DeleteSelectedMeasurementCommand.NotifyCanExecuteChanged();
                CopyMeasurementsCommand.NotifyCanExecuteChanged();
                ExportMeasurementsCommand.NotifyCanExecuteChanged();
                AnalyzeIntensityProfileCommand.NotifyCanExecuteChanged();
                AnalyzeAssistedRegionsCommand.NotifyCanExecuteChanged();
                AcceptAllAssistedRegionsCommand.NotifyCanExecuteChanged();
                RejectSelectedAssistedRegionCommand.NotifyCanExecuteChanged();
                CommitAcceptedAssistedRegionsCommand.NotifyCanExecuteChanged();
                ClearAssistedRegionsCommand.NotifyCanExecuteChanged();
                ApplyCalibrationToFigurePanelsCommand.NotifyCanExecuteChanged();
                OpenProjectCommand.NotifyCanExecuteChanged();
                SaveProjectCommand.NotifyCanExecuteChanged();
                SaveProjectAsCommand.NotifyCanExecuteChanged();
                UndoCommand.NotifyCanExecuteChanged();
                RedoCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsTemplateSelectionEnabled));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    public bool IsCropOverlayVisible
    {
        get => _isCropOverlayVisible;
        set
        {
            if (SetProperty(ref _isCropOverlayVisible, value))
            {
                OnPropertyChanged(nameof(CropOverlayVisibility));
                MarkDirty();
            }
        }
    }

    public bool LockCropSizeAcrossSources
    {
        get => _lockCropSizeAcrossSources;
        set
        {
            if (SetProperty(ref _lockCropSizeAcrossSources, value))
            {
                MarkDirty();
            }
        }
    }

    public WorkspaceMode WorkspaceMode
    {
        get => _workspaceMode;
        set
        {
            if (SetProperty(ref _workspaceMode, value))
            {
                OnPropertyChanged(nameof(CropWorkspaceVisibility));
                OnPropertyChanged(nameof(FigureWorkspaceVisibility));
                OnPropertyChanged(nameof(WorkspaceModeText));
                OnPropertyChanged(nameof(MeasurementDockVisibility));
                MarkDirty();
            }
        }
    }

    public Visibility CropWorkspaceVisibility =>
        WorkspaceMode == WorkspaceMode.Crop ? Visibility.Visible : Visibility.Collapsed;

    public Visibility FigureWorkspaceVisibility =>
        WorkspaceMode == WorkspaceMode.Figure ? Visibility.Visible : Visibility.Collapsed;

    public bool IsLayersTabActive
    {
        get => _isLayersTabActive;
        set
        {
            if (SetProperty(ref _isLayersTabActive, value))
            {
                OnPropertyChanged(nameof(InspectorTabVisibility));
                OnPropertyChanged(nameof(LayersTabVisibility));
            }
        }
    }

    public Visibility InspectorTabVisibility =>
        IsLayersTabActive ? Visibility.Collapsed : Visibility.Visible;

    public Visibility LayersTabVisibility =>
        IsLayersTabActive ? Visibility.Visible : Visibility.Collapsed;

    public string WorkspaceModeText => WorkspaceMode == WorkspaceMode.Crop ? "裁剪视图" : "拼版视图";

    public string? ProjectPath => _projectPath;

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (SetProperty(ref _isDirty, value))
            {
                OnPropertyChanged(nameof(ProjectDisplayName));
            }
        }
    }

    public string ProjectDisplayName
    {
        get
        {
            string name = _projectPath is null
                ? "未命名工程"
                : Path.GetFileNameWithoutExtension(_projectPath);
            return IsDirty ? $"{name} *" : name;
        }
    }

    public bool BeginScientificGesture(double x, double y, bool finishMultiPoint = false)
    {
        SourceAssetItemViewModel? source = SelectedSource;
        if (source is null || ActiveScienceTool == ScientificToolMode.Crop)
        {
            return false;
        }

        var point = new MeasurementPoint(x, y);
        switch (ActiveScienceTool)
        {
            case ScientificToolMode.Calibration:
                source.Calibration.BeginReferenceLine(x, y);
                StatusMessage = "拖动参考线 · 松开后输入已知真实距离";
                return true;
            case ScientificToolMode.Length:
                _pendingMeasurement = source.AddMeasurement(
                    ScientificMeasurementKind.Length,
                    point,
                    point);
                StatusMessage = "正在测量长度…";
                return true;
            case ScientificToolMode.RectangleRoi:
                _pendingMeasurement = source.AddMeasurement(
                    ScientificMeasurementKind.RectangleRoi,
                    point,
                    point);
                StatusMessage = "正在创建矩形 ROI…";
                return true;
            case ScientificToolMode.CircleRoi:
                _pendingMeasurement = source.AddMeasurement(
                    ScientificMeasurementKind.CircleRoi,
                    point,
                    point);
                StatusMessage = "正在创建圆形测量…";
                return true;
            case ScientificToolMode.Polyline:
                HandlePolylinePoint(source, point, finishMultiPoint);
                return false;
            case ScientificToolMode.Angle:
                HandleAnglePoint(source, point);
                return false;
            default:
                return false;
        }
    }

    public void UpdateScientificGesture(double x, double y)
    {
        if (SelectedSource is null)
        {
            return;
        }

        if (ActiveScienceTool == ScientificToolMode.Calibration)
        {
            SelectedSource.Calibration.UpdateReferenceLine(x, y);
            return;
        }

        if (_pendingMeasurement is not null && ActiveScienceTool == ScientificToolMode.CircleRoi)
        {
            double deltaX = x - _pendingMeasurement.X1;
            double deltaY = y - _pendingMeasurement.Y1;
            double directionX = deltaX < 0 ? -1 : 1;
            double directionY = deltaY < 0 ? -1 : 1;
            double maximumX = directionX > 0
                ? SelectedSource.Width - 1 - _pendingMeasurement.X1
                : _pendingMeasurement.X1;
            double maximumY = directionY > 0
                ? SelectedSource.Height - 1 - _pendingMeasurement.Y1
                : _pendingMeasurement.Y1;
            double diameter = Math.Min(
                Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)),
                Math.Min(maximumX, maximumY));
            _pendingMeasurement.UpdatePointB(
                _pendingMeasurement.X1 + directionX * diameter,
                _pendingMeasurement.Y1 + directionY * diameter);
            return;
        }

        if (_pendingMeasurement is not null &&
            ActiveScienceTool is ScientificToolMode.Length or ScientificToolMode.RectangleRoi)
        {
            _pendingMeasurement.UpdatePointB(x, y);
        }
    }

    public void CompleteScientificGesture()
    {
        SourceAssetItemViewModel? source = SelectedSource;
        if (source is null)
        {
            _pendingMeasurement = null;
            return;
        }

        if (ActiveScienceTool == ScientificToolMode.Calibration)
        {
            source.Calibration.CompleteReferenceLine();
            StatusMessage = source.Calibration.CanApplyReference
                ? "参考线已建立 · 输入真实距离并点击“应用参考标定”"
                : "参考线过短 · 标定未改变";
            return;
        }

        if (_pendingMeasurement is not null &&
            ActiveScienceTool is ScientificToolMode.Length or ScientificToolMode.RectangleRoi or ScientificToolMode.CircleRoi)
        {
            ScientificMeasurementViewModel completed = _pendingMeasurement;
            _pendingMeasurement = null;
            if (!completed.IsValid)
            {
                source.RemoveMeasurement(completed);
                StatusMessage = "测量已取消 · 几何尺寸过小";
                return;
            }

            StatusMessage = $"已添加{completed.TypeText} · {completed.ValueText}";
            CompleteHistoryGesture();
        }
    }

    private void HandleAnglePoint(SourceAssetItemViewModel source, MeasurementPoint point)
    {
        if (_pendingMeasurement is null || _pendingAngleStep == 0)
        {
            _pendingMeasurement = source.AddMeasurement(
                ScientificMeasurementKind.Angle,
                point,
                point,
                point);
            _pendingAngleStep = 1;
            StatusMessage = "角度测量 1/3 · 已设置第一端点，请点击顶点";
            return;
        }

        if (_pendingAngleStep == 1)
        {
            _pendingMeasurement.UpdatePointB(point.X, point.Y);
            _pendingAngleStep = 2;
            StatusMessage = "角度测量 2/3 · 已设置顶点，请点击第二端点";
            return;
        }

        _pendingMeasurement.UpdatePointC(point.X, point.Y);
        ScientificMeasurementViewModel completed = _pendingMeasurement;
        _pendingMeasurement = null;
        _pendingAngleStep = 0;
        if (!completed.IsValid)
        {
            source.RemoveMeasurement(completed);
            StatusMessage = "角度测量已取消 · 两条边必须具有有效长度";
            return;
        }

        StatusMessage = $"已添加角度测量 · {completed.ValueText}";
        CompleteHistoryGesture();
    }

    private void CancelPendingScientificMeasurement()
    {
        ScientificMeasurementViewModel? pending = _pendingMeasurement;
        _pendingMeasurement = null;
        _pendingAngleStep = 0;
        if (pending is null)
        {
            return;
        }

        SourceAssetItemViewModel? owner = Sources.FirstOrDefault(
            source => source.Asset.Id == pending.SourceAssetId);
        owner?.CancelMeasurement(pending);
    }

    private void HandlePolylinePoint(
        SourceAssetItemViewModel source,
        MeasurementPoint point,
        bool finish)
    {
        if (_pendingMeasurement is null || _pendingMeasurement.Kind != ScientificMeasurementKind.Polyline)
        {
            _pendingMeasurement = source.AddMeasurement(
                ScientificMeasurementKind.Polyline,
                point,
                point,
                pathPoints: [point, point]);
            StatusMessage = "折线测量 · 已设置起点，继续单击添加节点，双击完成";
            return;
        }

        if (!finish)
        {
            _pendingMeasurement.CommitPolylinePoint(point.X, point.Y);
            StatusMessage = $"折线测量 · {_pendingMeasurement.PathPoints.Count - 1} 段 · 双击完成";
            return;
        }

        _pendingMeasurement.CompletePolyline(point.X, point.Y);
        ScientificMeasurementViewModel completed = _pendingMeasurement;
        _pendingMeasurement = null;
        if (!completed.IsValid)
        {
            source.RemoveMeasurement(completed);
            StatusMessage = "折线测量已取消 · 至少需要两个不同节点";
            return;
        }

        StatusMessage = $"已添加折线测量 · {completed.ValueText} · {completed.PathPoints.Count} points";
        CompleteHistoryGesture();
    }

    private void DeleteSelectedMeasurement()
    {
        if (SelectedSource?.SelectedMeasurement is not ScientificMeasurementViewModel measurement)
        {
            return;
        }

        if (measurement.IsLocked)
        {
            StatusMessage = "测量图层已锁定 · 请先解锁后删除";
            return;
        }

        SelectedSource.RemoveMeasurement(measurement);
        StatusMessage = "已删除测量对象 · 原图未修改";
    }

    private void DeleteCurrentSelection()
    {
        if (WorkspaceMode == WorkspaceMode.Crop)
        {
            DeleteSelectedMeasurement();
            return;
        }

        if (Figure.SelectedAnnotation is { } annotation)
        {
            if (annotation.IsLocked)
            {
                StatusMessage = "标注图层已锁定 · 请先解锁后删除";
                return;
            }

            Figure.RemoveSelectedAnnotationCommand.Execute(null);
            StatusMessage = "已删除选中标注";
            return;
        }

        if (Figure.SelectedGuide is { } guide)
        {
            if (guide.IsLocked)
            {
                StatusMessage = "参考线已锁定 · 请先解锁后删除";
                return;
            }

            Figure.RemoveSelectedGuideCommand.Execute(null);
            StatusMessage = "已删除选中参考线";
            return;
        }

        if (Figure.SelectedPanels.Any(panel => !panel.IsLocked))
        {
            Figure.RemoveSelectedCommand.Execute(null);
            StatusMessage = "已删除选中拼版面板";
            return;
        }

        StatusMessage = "没有可删除的选中对象";
    }

    private void CopyMeasurements()
    {
        if (SelectedSource?.Measurements.Count is not > 0)
        {
            return;
        }

        Clipboard.SetText(SelectedSource.CreateMeasurementCsv());
        StatusMessage = $"已复制 {SelectedSource.Measurements.Count} 条测量记录";
    }

    private async Task ExportMeasurementsAsync()
    {
        SourceAssetItemViewModel? source = SelectedSource;
        if (source?.Measurements.Count is not > 0)
        {
            return;
        }

        string suggestedName = $"{Path.GetFileNameWithoutExtension(source.DisplayName)}_measurements.csv";
        string? requestedPath = _exportFilePicker.PickNewMeasurementExportPath(suggestedName);
        if (requestedPath is null)
        {
            return;
        }

        ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
            requestedPath,
            Sources.Select(item => item.Asset).ToArray());
        if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
        {
            LastError = decision.Message;
            StatusMessage = "测量表导出已阻止 · 路径不安全";
            return;
        }

        if (File.Exists(decision.NormalizedTargetPath))
        {
            LastError = "测量表只能导出到全新 CSV 或 XLSX 文件，不覆盖任何已有文件。";
            StatusMessage = "测量表导出已阻止 · 目标文件已存在";
            return;
        }

        string extension = Path.GetExtension(decision.NormalizedTargetPath).ToLowerInvariant();
        if (extension == ".xlsx")
        {
            MeasurementTableXlsxWriter.WriteNew(decision.NormalizedTargetPath, source);
        }
        else if (extension == ".csv")
        {
            await using var output = new FileStream(
                decision.NormalizedTargetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                4096,
                useAsync: true);
            await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            await writer.WriteAsync(source.CreateMeasurementCsv());
            await writer.FlushAsync();
        }
        else
        {
            LastError = "测量表格式只支持 .csv 或 .xlsx。";
            StatusMessage = "测量表导出已阻止 · 格式不支持";
            return;
        }

        _auditTrail.Add(new ProjectAuditEntrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Command = extension == ".xlsx" ? "ExportMeasurementsXlsx" : "ExportMeasurementsCsv",
            Parameters = new Dictionary<string, object?>
            {
                ["sourceAssetId"] = source.Asset.Id,
                ["measurementCount"] = source.Measurements.Count,
                ["calibrated"] = source.Calibration.IsCalibrated,
            },
        });
        LastError = null;
        StatusMessage = $"测量表已导出 · {Path.GetFileName(decision.NormalizedTargetPath)} · 原图未修改";
    }

    private void ApplyCalibrationToFigurePanels()
    {
        SourceAssetItemViewModel? source = SelectedSource;
        if (source?.Calibration.IsCalibrated != true)
        {
            return;
        }

        int updated = SynchronizeScaleBarsForSource(source);
        StatusMessage = updated == 0
            ? "当前源图尚未加入拼版；后续面板会自动继承标定"
            : $"已将标定同步到 {updated} 个拼版面板的比例尺";
        CompleteHistoryGesture();
    }

    private void AttachSourceScience(SourceAssetItemViewModel source)
    {
        source.ScienceChanged -= OnSourceScienceChanged;
        source.ScienceEditCompleted -= OnSourceScienceEditCompleted;
        source.MeasurementSelectionChanged -= OnMeasurementSelectionChanged;
        source.ScienceChanged += OnSourceScienceChanged;
        source.ScienceEditCompleted += OnSourceScienceEditCompleted;
        source.MeasurementSelectionChanged += OnMeasurementSelectionChanged;
    }

    private void OnMeasurementSelectionChanged(object? sender, EventArgs e)
    {
        DeleteSelectedMeasurementCommand.NotifyCanExecuteChanged();
    }

    private void OnSourceScienceChanged(object? sender, EventArgs e)
    {
        if (sender is SourceAssetItemViewModel source)
        {
            SynchronizeScaleBarsForSource(source);
        }

        OnPropertyChanged(nameof(MeasurementDockVisibility));
        DeleteSelectedMeasurementCommand.NotifyCanExecuteChanged();
        CopyMeasurementsCommand.NotifyCanExecuteChanged();
        ExportMeasurementsCommand.NotifyCanExecuteChanged();
        AnalyzeIntensityProfileCommand.NotifyCanExecuteChanged();
        ApplyCalibrationToFigurePanelsCommand.NotifyCanExecuteChanged();
        if (IntensityProfile is not null)
        {
            IntensityProfileStatusText = "测量或标定已变化 · 请重新运行强度剖面";
        }
        MarkDirty();
    }

    private async Task AnalyzeIntensityProfileAsync()
    {
        SourceAssetItemViewModel? source = SelectedSource;
        ScientificMeasurementViewModel? measurement = source?.SelectedMeasurement;
        if (source is null || measurement?.Kind != ScientificMeasurementKind.Length)
        {
            LastError = "强度剖面需要先在测量表中选择一条长度测量。";
            StatusMessage = "强度剖面未运行 · 未选择长度测量";
            return;
        }

        IsBusy = true;
        LastError = null;
        StatusMessage = $"正在从原始 {source.Asset.Metadata.BitsPerChannel}-bit 文件采样强度…";
        try
        {
            ScientificMeasurement model = measurement.Measurement;
            IntensityProfileResult profile = await _intensityProfileAnalyzer.AnalyzeAsync(
                source.Asset,
                model.PointA,
                model.PointB,
                source.Calibration.Calibration);
            if (!profile.IsValid)
            {
                throw new InvalidDataException("强度剖面结果无效或采样点不足。");
            }

            IntensityProfile = profile;
            IntensityProfilePoints = CreateIntensityProfilePoints(profile);
            IntensityProfileStatusText =
                $"N {profile.Samples.Count} · Min {profile.Minimum:0.000} · Mean {profile.Mean:0.000} · " +
                $"Max {profile.Maximum:0.000} · 原始 {profile.SourceBitDepth}-bit 归一化";
            StatusMessage = $"强度剖面完成 · {profile.Samples.Count} 个采样点 · 原图未修改";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentOutOfRangeException)
        {
            LastError = exception.Message;
            StatusMessage = "强度剖面失败 · 原图未修改";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearIntensityProfile()
    {
        IntensityProfile = null;
        IntensityProfilePoints = [];
        IntensityProfileStatusText = "请选择长度测量后运行强度剖面";
    }

    private static PointCollection CreateIntensityProfilePoints(IntensityProfileResult profile)
    {
        const double chartWidth = 300;
        const double chartHeight = 100;
        int lastIndex = Math.Max(1, profile.Samples.Count - 1);
        var points = new PointCollection(profile.Samples.Count);
        for (int index = 0; index < profile.Samples.Count; index++)
        {
            points.Add(new Point(
                index / (double)lastIndex * chartWidth,
                (1 - profile.Samples[index].NormalizedIntensity) * chartHeight));
        }

        points.Freeze();
        return points;
    }

    private async Task AnalyzeAssistedRegionsAsync()
    {
        SourceAssetItemViewModel? source = SelectedSource;
        if (source is null || !Crop.TryGetCrop(out PixelRect64 roi))
        {
            LastError = "请先选择源图并建立有效裁剪 ROI。";
            StatusMessage = "候选区域分析未运行 · ROI 无效";
            return;
        }

        var options = new AssistedRegionAnalysisOptions(
            AssistedRegionMode,
            roi,
            UseAutomaticRegionThreshold,
            RegionThresholdPercent / 100,
            MinimumRegionAreaPixels);
        IsBusy = true;
        LastError = null;
        StatusMessage = "正在从原始像素生成可复核候选；不会修改源文件…";
        try
        {
            AssistedRegionAnalysisResult result = await _assistedRegionAnalyzer.AnalyzeAsync(
                source.Asset,
                options);
            if (!result.IsValid)
            {
                throw new InvalidDataException("候选区域分析结果无效。请调整阈值或最小面积。");
            }

            ClearAssistedRegionAnalysis();
            _assistedRegionResult = result;
            foreach (AssistedRegionCandidate candidate in result.Candidates)
            {
                var item = new AssistedRegionCandidateViewModel(
                    candidate,
                    source.Calibration.Calibration,
                    result.Options.Mode);
                item.Changed += OnAssistedRegionDecisionChanged;
                AssistedRegions.Add(item);
            }

            SelectedAssistedRegion = AssistedRegions.FirstOrDefault();
            AssistedRegionStatusText =
                $"{GetAssistedRegionModeLabel(result.Options.Mode)} · {result.Candidates.Count} 候选 · " +
                $"面积分数 {result.AreaFraction:P2} · 阈值 {result.AppliedThresholdNormalized:P1} · " +
                $"{result.AnalyzerId}";
            _auditTrail.Add(new ProjectAuditEntrySnapshot
            {
                Timestamp = result.AnalyzedAt,
                Command = "AnalyzeAssistedRegions",
                Parameters = new Dictionary<string, object?>
                {
                    ["sourceAssetId"] = source.Asset.Id,
                    ["mode"] = result.Options.Mode.ToString(),
                    ["roi"] = $"{roi.X},{roi.Y},{roi.Width},{roi.Height}",
                    ["automaticThreshold"] = result.Options.UseAutomaticThreshold,
                    ["appliedThreshold"] = result.AppliedThresholdNormalized,
                    ["minimumAreaPixels"] = result.Options.MinimumAreaPixels,
                    ["candidateCount"] = result.Candidates.Count,
                    ["areaFraction"] = result.AreaFraction,
                    ["analyzerId"] = result.AnalyzerId,
                },
            });
            NotifyAssistedRegionStateChanged();
            MarkDirty();
            StatusMessage = $"候选区域分析完成 · {result.Candidates.Count} 项等待人工接受/拒绝 · 原图未修改";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentException or OverflowException)
        {
            LastError = exception.Message;
            StatusMessage = "候选区域分析失败 · 原图未修改";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AcceptAllAssistedRegions()
    {
        foreach (AssistedRegionCandidateViewModel candidate in AssistedRegions.Where(
                     candidate => !candidate.IsCommitted))
        {
            candidate.IsAccepted = true;
        }

        NotifyAssistedRegionStateChanged();
        StatusMessage = "所有未写入候选已标记为接受；仍需点击“写入测量表”确认";
    }

    private void RejectSelectedAssistedRegion()
    {
        if (SelectedAssistedRegion is not { IsCommitted: false } selected)
        {
            return;
        }

        selected.IsAccepted = false;
        NotifyAssistedRegionStateChanged();
        StatusMessage = $"候选 {selected.Id} 已拒绝；分析结果未写入科研测量";
    }

    private void CommitAcceptedAssistedRegions()
    {
        SourceAssetItemViewModel? source = SelectedSource;
        AssistedRegionCandidateViewModel[] accepted = AssistedRegions
            .Where(candidate => candidate.IsAccepted && !candidate.IsCommitted)
            .ToArray();
        if (source is null || accepted.Length == 0)
        {
            return;
        }

        int committed = 0;
        AssistedRegionMode mode = _assistedRegionResult?.Options.Mode ?? AssistedRegionMode.BrightParticles;
        foreach (AssistedRegionCandidateViewModel candidate in accepted)
        {
            double maximumX = Math.Max(0, source.Width - 1);
            double maximumY = Math.Max(0, source.Height - 1);
            if (mode is AssistedRegionMode.DarkCracks or AssistedRegionMode.BrightLamellae)
            {
                bool majorIsHorizontal = candidate.Candidate.Bounds.Width >= candidate.Candidate.Bounds.Height;
                bool measureHorizontal = mode == AssistedRegionMode.DarkCracks
                    ? majorIsHorizontal
                    : !majorIsHorizontal;
                double length = mode == AssistedRegionMode.DarkCracks
                    ? Math.Max(candidate.Candidate.Bounds.Width, candidate.Candidate.Bounds.Height)
                    : Math.Min(candidate.Candidate.Bounds.Width, candidate.Candidate.Bounds.Height);
                double half = length / 2;
                MeasurementPoint start = measureHorizontal
                    ? new MeasurementPoint(
                        Math.Clamp(candidate.Candidate.CentroidX - half, 0, maximumX),
                        Math.Clamp(candidate.Candidate.CentroidY, 0, maximumY))
                    : new MeasurementPoint(
                        Math.Clamp(candidate.Candidate.CentroidX, 0, maximumX),
                        Math.Clamp(candidate.Candidate.CentroidY - half, 0, maximumY));
                MeasurementPoint end = measureHorizontal
                    ? new MeasurementPoint(
                        Math.Clamp(candidate.Candidate.CentroidX + half, 0, maximumX),
                        Math.Clamp(candidate.Candidate.CentroidY, 0, maximumY))
                    : new MeasurementPoint(
                        Math.Clamp(candidate.Candidate.CentroidX, 0, maximumX),
                        Math.Clamp(candidate.Candidate.CentroidY + half, 0, maximumY));
                if (Math.Abs(end.X - start.X) + Math.Abs(end.Y - start.Y) < 1)
                {
                    continue;
                }

                source.AddMeasurement(
                    ScientificMeasurementKind.Length,
                    start,
                    end,
                    strokeColor: "#FF75D9AA");
                candidate.MarkCommitted();
                committed++;
                continue;
            }

            double diameter = Math.Min(
                candidate.Candidate.EquivalentDiameterPixels,
                Math.Min(maximumX, maximumY));
            if (diameter < 1)
            {
                continue;
            }

            double x = Math.Clamp(
                candidate.Candidate.CentroidX - diameter / 2,
                0,
                Math.Max(0, maximumX - diameter));
            double y = Math.Clamp(
                candidate.Candidate.CentroidY - diameter / 2,
                0,
                Math.Max(0, maximumY - diameter));
            source.AddMeasurement(
                ScientificMeasurementKind.CircleRoi,
                new MeasurementPoint(x, y),
                new MeasurementPoint(x + diameter, y + diameter),
                strokeColor: "#FF75D9AA");
            candidate.MarkCommitted();
            committed++;
        }

        _auditTrail.Add(new ProjectAuditEntrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Command = "AcceptAssistedRegions",
            Parameters = new Dictionary<string, object?>
            {
                ["sourceAssetId"] = source.Asset.Id,
                ["mode"] = _assistedRegionResult?.Options.Mode.ToString(),
                ["acceptedCandidateIds"] = string.Join(",", accepted.Select(candidate => candidate.Id)),
                ["committedMeasurementCount"] = committed,
                ["rejectedCandidateCount"] = AssistedRegions.Count(candidate => !candidate.IsAccepted),
                ["analyzerId"] = _assistedRegionResult?.AnalyzerId,
            },
        });
        NotifyAssistedRegionStateChanged();
        CompleteHistoryGesture();
        string metric = mode switch
        {
            AssistedRegionMode.DarkCracks => "裂纹长度",
            AssistedRegionMode.BrightLamellae => "片层宽度",
            _ => "等效圆直径",
        };
        StatusMessage = $"已人工确认并写入 {committed} 条{metric}测量 · 可在测量表继续删除校正";
    }

    private void OnAssistedRegionDecisionChanged(object? sender, EventArgs e) =>
        NotifyAssistedRegionStateChanged();

    private void MarkAssistedRegionAnalysisStale()
    {
        if (_assistedRegionResult is not null)
        {
            AssistedRegionStatusText = "参数已变化 · 当前候选仅作旧结果参考，请重新分析";
        }
    }

    private void ClearAssistedRegionAnalysis()
    {
        foreach (AssistedRegionCandidateViewModel candidate in AssistedRegions)
        {
            candidate.Changed -= OnAssistedRegionDecisionChanged;
        }

        AssistedRegions.Clear();
        SelectedAssistedRegion = null;
        _assistedRegionResult = null;
        AssistedRegionStatusText = "在当前裁剪 ROI 中生成可人工复核的候选区域";
        NotifyAssistedRegionStateChanged();
    }

    private void NotifyAssistedRegionStateChanged()
    {
        OnPropertyChanged(nameof(AssistedRegionDecisionText));
        OnPropertyChanged(nameof(AssistedRegionResultsVisibility));
        AcceptAllAssistedRegionsCommand.NotifyCanExecuteChanged();
        RejectSelectedAssistedRegionCommand.NotifyCanExecuteChanged();
        CommitAcceptedAssistedRegionsCommand.NotifyCanExecuteChanged();
        ClearAssistedRegionsCommand.NotifyCanExecuteChanged();
    }

    private string GetAssistedRegionModeLabel(AssistedRegionMode mode) =>
        AssistedRegionModes.FirstOrDefault(option => option.Mode == mode)?.Label ?? mode.ToString();

    private void OnSourceScienceEditCompleted(object? sender, EventArgs e) =>
        CompleteHistoryGesture();

    private int SynchronizeScaleBarsForSource(SourceAssetItemViewModel source)
    {
        SpatialCalibration calibration = source.Calibration.Calibration;
        FigurePanelViewModel[] panels = Figure.Panels
            .Where(panel => ReferenceEquals(panel.Source, source))
            .ToArray();
        foreach (FigurePanelViewModel panel in panels)
        {
            panel.ApplySpatialCalibration(calibration);
        }

        return panels.Length;
    }

    private static bool MeasurementFitsSource(
        ScientificMeasurement measurement,
        SourceAssetItemViewModel source)
    {
        bool Fits(MeasurementPoint point) =>
            point.IsFinite && point.X >= 0 && point.Y >= 0 &&
            point.X < source.Width && point.Y < source.Height;
        return Fits(measurement.PointA) && Fits(measurement.PointB) &&
               (!measurement.PointC.HasValue || Fits(measurement.PointC.Value)) &&
               measurement.EffectivePathPoints.All(Fits);
    }

    private static bool MeasurementFitsDimensions(
        ScientificMeasurement measurement,
        long width,
        long height)
    {
        bool Fits(MeasurementPoint point) =>
            point.IsFinite && point.X >= 0 && point.Y >= 0 &&
            point.X < width && point.Y < height;
        return Fits(measurement.PointA) && Fits(measurement.PointB) &&
               (!measurement.PointC.HasValue || Fits(measurement.PointC.Value)) &&
               measurement.EffectivePathPoints.All(Fits);
    }

    private async Task ImportSourcesAsync()
    {
        IReadOnlyList<string> paths = _filePicker.PickImageFiles();
        if (paths.Count == 0)
        {
            return;
        }

        int sourceCountBeforeImport = Sources.Count;
        IsBusy = true;
        LastError = null;
        List<string> errors = [];

        try
        {
            foreach (string path in paths)
            {
                try
                {
                    StatusMessage = $"正在只读导入 {Path.GetFileName(path)}…";
                    SourceAsset asset = await _sourceReader.ImportAsync(path);
                    var preview = await _previewLoader.LoadAsync(path, 1400);
                    SourceAssetItemViewModel item = new(asset, preview);
                    AttachSourceScience(item);
                    Sources.Add(item);
                    MarkDirty();
                    SelectedSource ??= item;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    errors.Add($"{Path.GetFileName(path)}：{exception.Message}");
                }
            }

            StatusMessage = $"已导入 {Sources.Count:N0} 个源图像 · 原图未修改";
            LastError = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
            if (Sources.Count != sourceCountBeforeImport)
            {
                _history.ResetPreservingSavedState(CaptureHistorySnapshot());
                RefreshHistoryState();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportCropAsync()
    {
        SourceAssetItemViewModel? selected = SelectedSource;
        if (selected is null || !Crop.TryGetCrop(out var crop))
        {
            LastError = "裁剪区域无效，无法导出。";
            return;
        }

        string suggestedName = $"{Path.GetFileNameWithoutExtension(selected.DisplayName)}_crop_{crop.Width}x{crop.Height}.tif";
        string? requestedPath = _exportFilePicker.PickNewExportPath(suggestedName);
        if (requestedPath is null)
        {
            return;
        }

        IsBusy = true;
        LastError = null;
        StatusMessage = "正在验证源文件与导出路径…";

        try
        {
            SourceVerification verification = await _sourceReader.VerifyAsync(selected.Asset);
            if (verification.State != SourceLinkState.Verified)
            {
                LastError = verification.Message ?? "源文件自导入后已变化，导出已停止。";
                StatusMessage = "导出已停止 · 源文件验证失败";
                return;
            }

            ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
                requestedPath,
                Sources.Select(item => item.Asset).ToArray());
            if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
            {
                LastError = decision.Message;
                StatusMessage = "导出已阻止 · 路径不安全";
                return;
            }

            if (File.Exists(decision.NormalizedTargetPath))
            {
                LastError = "为保护科研数据，当前版本只导出到全新文件，不覆盖任何已有文件。";
                StatusMessage = "导出已阻止 · 目标文件已存在";
                return;
            }

            StatusMessage = $"正在导出 {Path.GetFileName(decision.NormalizedTargetPath)}…";
            await _cropExporter.ExportAsync(
                selected.OriginalPath,
                decision.NormalizedTargetPath,
                crop);

            StatusMessage = $"导出完成 · {Path.GetFileName(decision.NormalizedTargetPath)} · 原图未修改";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            LastError = exception.Message;
            StatusMessage = "导出失败 · 未修改源文件";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddCurrentCropToBatchQueue()
    {
        SourceAssetItemViewModel? selected = SelectedSource;
        if (selected is null || !Crop.TryGetCrop(out PixelRect64 crop))
        {
            LastError = "请先选择有效裁剪区域。";
            return;
        }

        if (BatchCropQueue.Any(item => ReferenceEquals(item.Source, selected) && item.Crop == crop))
        {
            LastError = "该源图像的相同裁剪已在批量队列中。";
            return;
        }

        var item = new BatchCropQueueItemViewModel(selected, crop);
        BatchCropQueue.Add(item);
        SelectedBatchCrop = item;
        OnPropertyChanged(nameof(BatchCropQueueSummary));
        ExportBatchCropsCommand.NotifyCanExecuteChanged();
        ClearBatchCropQueueCommand.NotifyCanExecuteChanged();
        RemoveSelectedBatchCropCommand.NotifyCanExecuteChanged();
        LastError = null;
        StatusMessage = $"已加入批量裁剪队列 · {BatchCropQueue.Count} 项";
    }

    private void RemoveSelectedBatchCrop()
    {
        if (SelectedBatchCrop is not { } selected)
        {
            return;
        }

        int index = BatchCropQueue.IndexOf(selected);
        BatchCropQueue.Remove(selected);
        SelectedBatchCrop = BatchCropQueue.Count == 0
            ? null
            : BatchCropQueue[Math.Clamp(index, 0, BatchCropQueue.Count - 1)];
        OnPropertyChanged(nameof(BatchCropQueueSummary));
        ExportBatchCropsCommand.NotifyCanExecuteChanged();
        ClearBatchCropQueueCommand.NotifyCanExecuteChanged();
        StatusMessage = $"已从批量队列移除 · {BatchCropQueue.Count} 项";
    }

    private void ClearBatchCropQueue()
    {
        BatchCropQueue.Clear();
        SelectedBatchCrop = null;
        OnPropertyChanged(nameof(BatchCropQueueSummary));
        ExportBatchCropsCommand.NotifyCanExecuteChanged();
        ClearBatchCropQueueCommand.NotifyCanExecuteChanged();
        StatusMessage = "批量裁剪队列已清空";
    }

    private void ImportTemplate()
    {
        if (_templateFilePicker is null || _userTemplateCatalog is null)
        {
            return;
        }

        string? sourcePath = _templateFilePicker.PickTemplatePath();
        if (sourcePath is null)
        {
            return;
        }

        try
        {
            FigureTemplateDefinition template = _userTemplateCatalog.ImportFromFile(sourcePath);
            if (AvailableTemplates.Any(item => string.Equals(item.Id, template.Id, StringComparison.Ordinal)))
            {
                LastError = $"模板 ID {template.Id} 已在当前模板库中。";
                StatusMessage = "模板导入已停止 · ID 重复";
                return;
            }

            AvailableTemplates.Add(template);
            OnPropertyChanged(nameof(TemplateLibraryLabel));
            if (IsTemplateSelectionEnabled)
            {
                ReplaceFigure(template);
                StatusMessage = $"已导入并应用用户模板 · {template.Name}";
            }
            else
            {
                StatusMessage = $"已导入用户模板 · {template.Name} · 当前拼版未重排";
            }

            LastError = null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            System.Text.Json.JsonException or NotSupportedException)
        {
            LastError = exception.Message;
            StatusMessage = "模板导入失败 · 当前拼版未改变";
        }
    }

    private async Task ExportBatchCropsAsync()
    {
        if (_batchExportFolderPicker is null || BatchCropQueue.Count == 0)
        {
            return;
        }

        string? folder = _batchExportFolderPicker.PickExportFolder();
        if (folder is null)
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            LastError = "批量输出文件夹不存在。";
            StatusMessage = "批量导出已停止";
            return;
        }

        BatchCropQueueItemViewModel[] items = BatchCropQueue.ToArray();
        HashSet<string> plannedPaths = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        int completed = 0;
        IsBusy = true;
        LastError = null;
        StatusMessage = $"正在准备批量裁剪 · {items.Length} 项…";

        try
        {
            for (int index = 0; index < items.Length; index++)
            {
                BatchCropQueueItemViewModel item = items[index];
                try
                {
                    item.MarkValidating();
                    SourceVerification verification = await _sourceReader.VerifyAsync(item.Source.Asset);
                    if (verification.State != SourceLinkState.Verified)
                    {
                        throw new InvalidDataException(
                            verification.Message ?? "源文件自导入后已变化，已跳过该队列项。");
                    }

                    if (!CropBoundsValidator.Validate(
                            item.Crop,
                            item.Source.Asset.Metadata.PixelSize).IsValid)
                    {
                        throw new InvalidDataException("裁剪区域已超出当前源图像边界。");
                    }

                    string targetPath = CreateBatchTargetPath(folder, item, index + 1, plannedPaths);
                    ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
                        targetPath,
                        Sources.Select(source => source.Asset).ToArray());
                    if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
                    {
                        throw new InvalidOperationException(decision.Message);
                    }

                    if (File.Exists(decision.NormalizedTargetPath))
                    {
                        throw new IOException("目标文件已存在，为保护科研数据未覆盖它。");
                    }

                    item.MarkExporting(decision.NormalizedTargetPath);
                    await _cropExporter.ExportAsync(
                        item.Source.OriginalPath,
                        decision.NormalizedTargetPath,
                        item.Crop);
                    item.MarkCompleted(decision.NormalizedTargetPath);
                    completed++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    NotSupportedException or InvalidOperationException)
                {
                    item.MarkFailed(exception.Message);
                    errors.Add($"{item.DisplayName}：{exception.Message}");
                }

                StatusMessage = $"批量裁剪 {index + 1}/{items.Length} · 已完成 {completed} 项";
            }

            LastError = errors.Count == 0 ? null : string.Join(Environment.NewLine, errors);
            StatusMessage = errors.Count == 0
                ? $"批量裁剪完成 · {completed}/{items.Length} 项 · 原图未修改"
                : $"批量裁剪完成 · {completed}/{items.Length} 项成功 · 失败项已保留在队列";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string CreateBatchTargetPath(
        string folder,
        BatchCropQueueItemViewModel item,
        int sequence,
        ISet<string> plannedPaths)
    {
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        string stem = string.Concat(
                Path.GetFileNameWithoutExtension(item.DisplayName).Select(
                    character => invalidCharacters.Contains(character) ? '_' : character))
            .Trim();
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "image";
        }

        stem = stem.Length > 80 ? stem[..80] : stem;
        string baseName = $"{stem}_crop_{item.Crop.Width}x{item.Crop.Height}_{sequence:000}";
        string candidate = Path.Combine(folder, $"{baseName}.tif");
        int suffix = 2;
        while (File.Exists(candidate) || !plannedPaths.Add(candidate))
        {
            candidate = Path.Combine(folder, $"{baseName}_{suffix++}.tif");
        }

        return candidate;
    }
    internal async Task AcceptSelectedSourceRevisionAsync()
    {
        SourceAssetItemViewModel? selected = SelectedSource;
        if (selected is null)
        {
            return;
        }

        IsBusy = true;
        LastError = null;
        StatusMessage = "正在只读核对源图当前版本…";
        try
        {
            SourceAsset previous = selected.Asset;
            SourceVerification verification = await _sourceReader.VerifyAsync(previous);
            if (verification.State == SourceLinkState.Verified)
            {
                StatusMessage = "源图与工程记录一致，无需接受新版本";
                return;
            }

            if (verification.State != SourceLinkState.Modified || verification.CurrentFingerprint is null)
            {
                LastError = verification.Message ?? "当前源图无法作为新版本接受。";
                StatusMessage = "未接受源图新版本";
                return;
            }

            SourceAsset proposed = await _sourceReader.ImportAsync(previous.OriginalPath);
            if (!string.Equals(
                    proposed.Fingerprint.Sha256,
                    verification.CurrentFingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("源文件在核对过程中再次变化，请等待写入完成后重试。");
            }

            ValidateAcceptedRevisionBounds(selected, proposed);
            var request = new SourceRevisionAcceptanceRequest(
                previous.DisplayName,
                previous.OriginalPath,
                previous.Fingerprint,
                proposed.Fingerprint,
                previous.Metadata.PixelSize.Width,
                previous.Metadata.PixelSize.Height,
                proposed.Metadata.PixelSize.Width,
                proposed.Metadata.PixelSize.Height);
            if (!_sourceRevisionAcceptancePrompt.ConfirmAcceptance(request))
            {
                StatusMessage = "已取消接受源图新版本 · 工程指纹未改变";
                return;
            }

            SourceAsset confirmed = await _sourceReader.ImportAsync(previous.OriginalPath);
            if (!string.Equals(
                    confirmed.Fingerprint.Sha256,
                    proposed.Fingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("确认后源文件内容再次变化，本次接受已取消。");
            }

            BitmapSource preview = await _previewLoader.LoadAsync(previous.OriginalPath, 1400);
            SourceAsset accepted = confirmed with
            {
                Id = previous.Id,
                DisplayName = previous.DisplayName,
                OriginalPath = previous.OriginalPath,
                LinkState = SourceLinkState.Verified,
            };
            SourceVerification finalVerification = await _sourceReader.VerifyAsync(accepted);
            if (finalVerification.State != SourceLinkState.Verified)
            {
                throw new IOException("读取新预览期间源文件再次变化，本次接受已取消。");
            }

            selected.AcceptRevision(accepted, preview);
            foreach (FigurePanelViewModel panel in Figure.Panels.Where(
                         panel => ReferenceEquals(panel.Source, selected)))
            {
                panel.RefreshPreview();
            }

            if (ReferenceEquals(SelectedSource, selected))
            {
                Crop.ConfigureForSource(accepted.Metadata.PixelSize, preserveSize: true);
            }

            _auditTrail.Add(new ProjectAuditEntrySnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                Command = "AcceptSourceRevision",
                Parameters = new Dictionary<string, object?>
                {
                    ["sourceId"] = accepted.Id,
                    ["path"] = accepted.OriginalPath,
                    ["previousSha256"] = previous.Fingerprint.Sha256,
                    ["acceptedSha256"] = accepted.Fingerprint.Sha256,
                    ["previousWidth"] = previous.Metadata.PixelSize.Width,
                    ["previousHeight"] = previous.Metadata.PixelSize.Height,
                    ["acceptedWidth"] = accepted.Metadata.PixelSize.Width,
                    ["acceptedHeight"] = accepted.Metadata.PixelSize.Height,
                },
            });

            _history.Reset(CaptureHistorySnapshot(), markSaved: false);
            RefreshHistoryState();
            CompleteHistoryGesture();
            StatusMessage = $"已明确接受 {accepted.DisplayName} 的新版本 · 请保存工程 · 源文件未修改";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or InvalidOperationException)
        {
            LastError = exception.Message;
            StatusMessage = "接受源图新版本失败 · 工程记录未更新";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ValidateAcceptedRevisionBounds(
        SourceAssetItemViewModel selected,
        SourceAsset proposed)
    {
        foreach (FigurePanelViewModel panel in Figure.Panels.Where(
                     panel => ReferenceEquals(panel.Source, selected)))
        {
            if (!CropBoundsValidator.Validate(panel.SourceRect, proposed.Metadata.PixelSize).IsValid)
            {
                throw new InvalidDataException(
                    $"新版本尺寸不足以覆盖面板 {panel.Label} 的源图裁剪区域，不能接受。" );
            }
        }

        if (ReferenceEquals(SelectedSource, selected) &&
            Crop.TryGetCrop(out PixelRect64 activeCrop) &&
            !CropBoundsValidator.Validate(activeCrop, proposed.Metadata.PixelSize).IsValid)
        {
            throw new InvalidDataException("新版本尺寸不足以覆盖当前活动裁剪区域，不能接受。");
        }

        if (selected.Measurements.Any(measurement =>
                !MeasurementFitsDimensions(
                    measurement.Measurement,
                    proposed.Metadata.PixelSize.Width,
                    proposed.Metadata.PixelSize.Height)))
        {
            throw new InvalidDataException("新版本尺寸不足以覆盖现有科学测量坐标，不能接受。");
        }
    }

    private void AddCurrentCropToFigure()
    {
        SourceAssetItemViewModel? selected = SelectedSource;
        if (selected is null || !Crop.TryGetCrop(out var crop))
        {
            LastError = "请先选择有效裁剪区域。";
            return;
        }

        FigurePanelViewModel? panel = Figure.AddPanel(selected, crop);
        if (panel is null)
        {
            LastError = $"当前模板只有 {Figure.SlotCount} 个插槽，已经全部使用。";
            StatusMessage = "未加入拼版 · 模板插槽已满";
            return;
        }

        panel.ApplySpatialCalibration(selected.Calibration.Calibration);

        LastError = panel.IsBelowMinimumDpi
            ? $"面板 {panel.Label} 的{panel.EffectiveDpiText}，低于模板建议的 {panel.MinimumEffectiveDpi} dpi。"
            : null;
        WorkspaceMode = WorkspaceMode.Figure;
        StatusMessage = $"已加入拼版面板 {panel.Label} · {panel.RoleDisplayName} · 原图未修改";
        ExportFigureCommand.NotifyCanExecuteChanged();
    }

    private void ReplaceSelectedPanelSource()
    {
        FigurePanelViewModel? panel = Figure.SelectedPanel;
        SourceAssetItemViewModel? source = SelectedSource;
        if (panel is null || source is null || !Crop.TryGetCrop(out PixelRect64 crop))
        {
            LastError = "请选择源图和有效裁剪区域，再替换选中面板。";
            return;
        }

        try
        {
            panel.ReplaceSource(source, crop);
            panel.ApplySpatialCalibration(source.Calibration.Calibration);
            LastError = null;
            StatusMessage = $"已将面板 {panel.Label} 替换为 {source.DisplayName} · 原图未修改";
            CompleteHistoryGesture();
        }
        catch (InvalidOperationException exception)
        {
            LastError = exception.Message;
            StatusMessage = "面板替换已阻止";
        }
    }
    private async Task ExportFigureAsync()
    {
        if (Figure.Panels.Count == 0)
        {
            LastError = "拼版中还没有图像面板。";
            return;
        }

        string suggestedName = $"figure_{Figure.Template.Id.Split('.').Last()}_{DateTime.Now:yyyyMMdd_HHmm}";
        string? requestedPath = _exportFilePicker.PickNewFigureExportPath(suggestedName);
        if (requestedPath is null)
        {
            return;
        }

        IsBusy = true;
        LastError = null;
        StatusMessage = "正在验证拼版中的所有源文件…";

        try
        {
            SourceAsset[] figureSources = Figure.Panels
                .Select(panel => panel.Source.Asset)
                .DistinctBy(source => source.Id)
                .ToArray();
            foreach (SourceAsset source in figureSources)
            {
                SourceVerification verification = await _sourceReader.VerifyAsync(source);
                if (verification.State != SourceLinkState.Verified)
                {
                    LastError = $"{source.DisplayName}：{verification.Message ?? "源文件验证失败。"}";
                    StatusMessage = "拼版导出已停止 · 源文件验证失败";
                    return;
                }
            }

            ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
                requestedPath,
                Sources.Select(item => item.Asset).ToArray());
            if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
            {
                LastError = decision.Message;
                StatusMessage = "拼版导出已阻止 · 路径不安全";
                return;
            }

            if (File.Exists(decision.NormalizedTargetPath))
            {
                LastError = "拼版只能导出到全新文件，不覆盖任何已有文件。";
                StatusMessage = "拼版导出已阻止 · 目标文件已存在";
                return;
            }

            FigureExportDocument exportDocument = Figure.CreateExportDocument();
            FigurePreflightResult preflight = UpdateFigureQc(exportDocument);
            if (preflight.HasErrors)
            {
                LastError = string.Join(Environment.NewLine, preflight.Issues
                    .Where(issue => issue.Severity == FigurePreflightSeverity.Error)
                    .Select(issue => issue.Message));
                StatusMessage = $"拼版导出已阻止 · {preflight.Summary}";
                return;
            }

            StatusMessage = $"正在以原始像素渲染 {Figure.Panels.Count} 个面板…";
            await _figureExporter.ExportAsync(exportDocument, decision.NormalizedTargetPath);
            string provenancePath = Path.ChangeExtension(decision.NormalizedTargetPath, ".provenance.json");
            string reportPath = Path.ChangeExtension(decision.NormalizedTargetPath, ".export-report.html");
            FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
                exportDocument,
                decision.NormalizedTargetPath,
                typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "1.2.0-alpha",
                Sources.Select(item => item.Asset).ToArray(),
                preflight);
            try
            {
                FigureProvenanceWriter.WriteJson(provenance, provenancePath);
                FigureProvenanceWriter.WriteHtml(provenance, reportPath);
            }
            catch (IOException exception)
            {
                LastError = $"主图已导出，但溯源报告写入失败：{exception.Message}";
            }
            StatusMessage = $"拼版导出完成 · {Path.GetFileName(decision.NormalizedTargetPath)} · 已生成溯源报告 · 原图未修改";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException)
        {
            LastError = exception.Message;
            StatusMessage = "拼版导出失败 · 未修改源文件";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddExportProfile()
    {
        var editor = new ExportProfileEditorViewModel(new FigureExportProfile(
            Guid.NewGuid().ToString("D"),
            $"自定义预设 {ExportProfiles.Count + 1}",
            "tiff",
            300,
            bitDepth: 16));
        editor.PropertyChanged += OnExportProfilePropertyChanged;
        ExportProfiles.Add(editor);
        SelectedExportProfile = editor;
        OnPropertyChanged(nameof(ExportProfileSummary));
        RemoveSelectedExportProfileCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    private void RemoveSelectedExportProfile()
    {
        if (SelectedExportProfile is not { } selected || ExportProfiles.Count <= 1)
        {
            return;
        }

        int index = ExportProfiles.IndexOf(selected);
        selected.PropertyChanged -= OnExportProfilePropertyChanged;
        ExportProfiles.Remove(selected);
        SelectedExportProfile = ExportProfiles[Math.Clamp(index, 0, ExportProfiles.Count - 1)];
        OnPropertyChanged(nameof(ExportProfileSummary));
        RemoveSelectedExportProfileCommand.NotifyCanExecuteChanged();
        MarkDirty();
    }

    private void ResetExportProfilesToBuiltIns(bool markDirty) => ReplaceExportProfiles(
        FigureExportProfile.BuiltIns.Select(profile => new ExportProfileEditorViewModel(profile)),
        markDirty);

    private void RestoreExportProfiles(IReadOnlyList<ProjectExportProfileSnapshot> snapshots)
    {
        IEnumerable<ExportProfileEditorViewModel> profiles = snapshots.Count == 0
            ? FigureExportProfile.BuiltIns.Select(profile => new ExportProfileEditorViewModel(profile))
            : snapshots.Select(ExportProfileEditorViewModel.FromSnapshot);
        ReplaceExportProfiles(profiles, markDirty: false);
    }

    private void ReplaceExportProfiles(
        IEnumerable<ExportProfileEditorViewModel> profiles,
        bool markDirty)
    {
        foreach (ExportProfileEditorViewModel existing in ExportProfiles)
        {
            existing.PropertyChanged -= OnExportProfilePropertyChanged;
        }
        ExportProfiles.Clear();
        foreach (ExportProfileEditorViewModel profile in profiles)
        {
            profile.PropertyChanged += OnExportProfilePropertyChanged;
            ExportProfiles.Add(profile);
        }
        if (ExportProfiles.Count == 0)
        {
            throw new InvalidDataException("工程至少需要一个导出预设。");
        }

        SelectedExportProfile = ExportProfiles[0];
        OnPropertyChanged(nameof(ExportProfileSummary));
        RemoveSelectedExportProfileCommand.NotifyCanExecuteChanged();
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private FigureExportProfile[] CreateValidatedExportProfiles() =>
        ExportProfiles.Select(profile => profile.ToModel()).ToArray();

    private void OnExportProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(ExportProfileSummary));
        MarkDirty();
    }

    private async Task ExportFigureVariantsAsync()
    {
        if (_batchExportFolderPicker is null || Figure.Panels.Count == 0)
        {
            return;
        }

        string? folder = _batchExportFolderPicker.PickExportFolder();
        if (folder is null)
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            LastError = "批量输出文件夹不存在。";
            StatusMessage = "投稿版本导出已停止";
            return;
        }

        FigureExportProfile[] profiles;
        try
        {
            profiles = CreateValidatedExportProfiles();
        }
        catch (InvalidDataException exception)
        {
            LastError = exception.Message;
            StatusMessage = "投稿版本导出已停止 · 请修正预设";
            return;
        }
        if (profiles.Length == 0)
        {
            LastError = "至少需要一个投稿导出预设。";
            return;
        }

        SourceAsset[] figureSources = Figure.Panels
            .Select(panel => panel.Source.Asset)
            .DistinctBy(source => source.Id)
            .ToArray();
        FigureExportDocument baseDocument = Figure.CreateExportDocument();
        HashSet<string> plannedPaths = new(StringComparer.OrdinalIgnoreCase);
        List<string> errors = [];
        List<string> warnings = [];
        int completed = 0;
        IsBusy = true;
        LastError = null;
        StatusMessage = $"正在准备 {profiles.Length} 个投稿版本…";

        try
        {
            foreach (SourceAsset source in figureSources)
            {
                SourceVerification verification = await _sourceReader.VerifyAsync(source);
                if (verification.State != SourceLinkState.Verified)
                {
                    LastError = $"{source.DisplayName}：{verification.Message ?? "源文件验证失败。"}";
                    StatusMessage = "投稿版本导出已停止 · 源文件验证失败";
                    return;
                }
            }

            foreach (FigureExportProfile profile in profiles)
            {
                try
                {
                    FigureExportDocument variant = profile.Apply(baseDocument);
                    FigurePreflightResult preflight = FigurePreflight.Check(
                        variant,
                        figureSources,
                        IsDirty);
                    if (preflight.HasErrors)
                    {
                        throw new InvalidDataException(string.Join(Environment.NewLine, preflight.Issues
                            .Where(issue => issue.Severity == FigurePreflightSeverity.Error)
                            .Select(issue => issue.Message)));
                    }

                    string requestedPath = CreateFigureVariantTargetPath(
                        folder,
                        Figure.Template.Id,
                        profile,
                        plannedPaths);
                    ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
                        requestedPath,
                        Sources.Select(item => item.Asset).ToArray());
                    if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
                    {
                        throw new InvalidOperationException(decision.Message);
                    }

                    if (File.Exists(decision.NormalizedTargetPath))
                    {
                        throw new IOException("目标文件已存在，为保护科研数据未覆盖它。");
                    }

                    StatusMessage = $"正在导出 {profile.Name} · {variant.WidthPixels:N0}×{variant.HeightPixels:N0} px…";
                    await _figureExporter.ExportAsync(variant, decision.NormalizedTargetPath);
                    if (profile.WriteProvenance)
                    {
                        FigureProvenanceDocument provenance = FigureProvenanceWriter.Create(
                            variant,
                            decision.NormalizedTargetPath,
                            typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "1.2.0-alpha",
                            figureSources,
                            preflight,
                            profile.Id,
                            profile.Name);
                        try
                        {
                            FigureProvenanceWriter.WriteJson(
                                provenance,
                                Path.ChangeExtension(decision.NormalizedTargetPath, ".provenance.json"));
                            FigureProvenanceWriter.WriteHtml(
                                provenance,
                                Path.ChangeExtension(decision.NormalizedTargetPath, ".export-report.html"));
                        }
                        catch (IOException exception)
                        {
                            warnings.Add($"{profile.Name} 溯源报告写入失败：{exception.Message}");
                        }
                    }

                    completed++;
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException or InvalidDataException or
                    NotSupportedException or InvalidOperationException)
                {
                    errors.Add($"{profile.Name}：{exception.Message}");
                }
            }

            LastError = errors.Count == 0
                ? warnings.Count == 0 ? null : string.Join(Environment.NewLine, warnings)
                : string.Join(Environment.NewLine, errors.Concat(warnings));
            StatusMessage = errors.Count == 0
                ? $"投稿版本导出完成 · {completed}/{profiles.Length} 项 · 所有输出均为全新文件"
                : $"投稿版本导出完成 · {completed}/{profiles.Length} 项成功 · 失败项未覆盖任何文件";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string CreateFigureVariantTargetPath(
        string folder,
        string templateId,
        FigureExportProfile profile,
        ISet<string> plannedPaths)
    {
        HashSet<char> invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        string templateStem = string.Concat(templateId.Select(
                character => invalidCharacters.Contains(character) ? '_' : character))
            .Trim();
        if (string.IsNullOrWhiteSpace(templateStem))
        {
            templateStem = "figure";
        }

        string suffix = string.Concat(profile.Id.Select(
            character => invalidCharacters.Contains(character) ? '_' : character));
        string baseName = $"figure_{templateStem}_{suffix}";
        string candidate = Path.GetFullPath(Path.Combine(folder, baseName + profile.Extension));
        int attempt = 2;
        while (!plannedPaths.Add(candidate))
        {
            candidate = Path.GetFullPath(Path.Combine(folder, $"{baseName}_{attempt++}{profile.Extension}"));
        }

        return candidate;
    }
    private async Task SaveProjectAsync()
    {
        if (_projectPath is null)
        {
            await SaveProjectAsAsync();
            return;
        }

        await SaveProjectToPathAsync(_projectPath);
    }

    private void NewProject()
    {
        if (IsDirty)
        {
            LastError = "当前工程有未保存更改。请先保存，再新建工程。";
            StatusMessage = "未新建工程 · 需要先保存当前更改";
            return;
        }

        _isRestoringProject = true;
        try
        {
            Sources.Clear();
            BatchCropQueue.Clear();
            SelectedBatchCrop = null;
            OnPropertyChanged(nameof(BatchCropQueueSummary));
            ReplaceFigure(_selectedFigureTemplate, markDirty: false);
            SelectedSource = null;
            Crop.Reset();
            WorkspaceMode = WorkspaceMode.Crop;
            LockCropSizeAcrossSources = true;
            IsCropOverlayVisible = true;
            _projectId = Guid.NewGuid();
            _projectCreatedAt = DateTimeOffset.UtcNow;
            _projectPath = null;
            _auditTrail.Clear();
            ResetExportProfilesToBuiltIns(markDirty: false);
            LastError = null;
            StatusMessage = "已新建空白工程";
            OnPropertyChanged(nameof(ProjectPath));
            OnPropertyChanged(nameof(ProjectDisplayName));
            ExportFigureCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            _isRestoringProject = false;
        }

        IsDirty = false;
        _history.Reset(CaptureHistorySnapshot(), markSaved: true);
        RefreshHistoryState();
    }

    private async Task SaveProjectAsAsync()
    {
        string suggestedName = _projectPath is null
            ? $"SciCanvas_{DateTime.Now:yyyyMMdd_HHmm}.scicanvas"
            : Path.GetFileName(_projectPath);
        string? path = _projectFilePicker.PickProjectToSave(suggestedName, _projectPath);
        if (path is null)
        {
            return;
        }

        await SaveProjectToPathAsync(path);
    }

    internal async Task SaveProjectToPathAsync(string path)
    {
        IsBusy = true;
        LastError = null;
        StatusMessage = "正在安全保存工程…";

        try
        {
            ExportPathDecision decision = await _pathSafetyPolicy.ValidateExportTargetAsync(
                path,
                Sources.Select(item => item.Asset).ToArray());
            if (!decision.IsAllowed || decision.NormalizedTargetPath is null)
            {
                LastError = decision.Message;
                StatusMessage = "工程保存已阻止 · 路径不安全";
                return;
            }

            string normalizedPath = Path.ChangeExtension(decision.NormalizedTargetPath, ".scicanvas");
            string title = Path.GetFileNameWithoutExtension(normalizedPath);
            SciCanvasProjectDocument document = ProjectDocumentMapper.Create(
                _projectId,
                _projectCreatedAt,
                title,
                Sources,
                SelectedSource,
                Crop,
                Figure,
                WorkspaceMode,
                LockCropSizeAcrossSources,
                IsCropOverlayVisible,
                _auditTrail,
                CreateValidatedExportProfiles());

            string? previousProjectPath = _projectPath;
            await _projectStore.SaveAsync(normalizedPath, document);
            _auditTrail.Clear();
            _auditTrail.AddRange(document.AuditTrail);
            await _projectRecoveryStore.DeleteAsync(_projectId, previousProjectPath);
            if (!string.Equals(previousProjectPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
            {
                await _projectRecoveryStore.DeleteAsync(_projectId, normalizedPath);
            }

            _projectPath = normalizedPath;
            OnPropertyChanged(nameof(ProjectPath));
            _history.MarkSaved(CaptureHistorySnapshot());
            RefreshHistoryState();
            OnPropertyChanged(nameof(ProjectDisplayName));
            AutosaveStatusText = "已手动保存";
            StatusMessage = $"工程已保存 · {Path.GetFileName(normalizedPath)} · 源图未修改";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or InvalidOperationException)
        {
            LastError = exception.Message;
            StatusMessage = "工程保存失败 · 当前内容仍保留在内存中";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task OpenProjectAsync()
    {
        if (IsDirty)
        {
            LastError = "当前工程有未保存更改。请先保存，再打开其他工程。";
            StatusMessage = "未打开工程 · 需要先保存当前更改";
            return;
        }

        string? path = _projectFilePicker.PickProjectToOpen();
        if (path is null)
        {
            return;
        }

        await OpenProjectFromPathAsync(path);
    }

    internal async Task OpenProjectFromPathAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            ProjectRecoveryCandidate? candidate =
                await _projectRecoveryStore.FindForProjectAsync(path);
            if (candidate is not null)
            {
                if (_projectRecoveryPrompt.ShouldRestore(candidate))
                {
                    await RestoreProjectFromPathAsync(
                        candidate.RecoveryPath,
                        Path.GetFullPath(path),
                        markSaved: false,
                        isRecovery: true);
                    return;
                }

                await _projectRecoveryStore.DeleteCandidateAsync(candidate);
            }

            await RestoreProjectFromPathAsync(
                path,
                Path.GetFullPath(path),
                markSaved: true,
                isRecovery: false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LastError = exception.Message;
            StatusMessage = "工程打开失败 · 当前工程未被替换";
        }
    }

    public async Task TryRestoreLatestAutosaveAsync()
    {
        if (IsBusy || IsDirty || Sources.Count > 0)
        {
            return;
        }

        try
        {
            ProjectRecoveryCandidate? candidate =
                await _projectRecoveryStore.FindLatestUnsavedAsync();
            if (candidate is null)
            {
                return;
            }

            if (!_projectRecoveryPrompt.ShouldRestore(candidate))
            {
                await _projectRecoveryStore.DeleteCandidateAsync(candidate);
                AutosaveStatusText = "已放弃旧恢复副本";
                return;
            }

            await RestoreProjectFromPathAsync(
                candidate.RecoveryPath,
                projectPath: null,
                markSaved: false,
                isRecovery: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LastError = $"自动恢复失败：{exception.Message}";
            StatusMessage = "自动恢复失败 · 恢复副本仍保留";
        }
    }

    private async Task RestoreProjectFromPathAsync(
        string loadPath,
        string? projectPath,
        bool markSaved,
        bool isRecovery)
    {

        IsBusy = true;
        LastError = null;
        StatusMessage = isRecovery
            ? "正在读取自动保存并验证全部源图…"
            : "正在读取工程并验证全部源图…";

        try
        {
            SciCanvasProjectDocument document = await _projectStore.LoadAsync(loadPath);
            FigureTemplateDefinition projectTemplate = ResolveProjectTemplate(document);

            List<SourceAssetItemViewModel> restoredSources = [];
            Dictionary<Guid, SourceAssetItemViewModel> sourceMap = [];
            List<string> sourceErrors = [];
            int relinkedSourceCount = 0;

            foreach (ProjectSourceSnapshot snapshot in document.Sources)
            {
                try
                {
                    (SourceAssetItemViewModel item, bool relinked) =
                        await ResolveProjectSourceAsync(snapshot);
                    restoredSources.Add(item);
                    sourceMap.Add(snapshot.Id, item);
                    if (relinked)
                    {
                        relinkedSourceCount++;
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    sourceErrors.Add($"{snapshot.DisplayName}：{exception.Message}");
                }
            }

            if (sourceErrors.Count > 0)
            {
                LastError = "工程未打开，因为以下源文件未通过验证：" +
                            Environment.NewLine + string.Join(Environment.NewLine, sourceErrors);
                StatusMessage = "工程打开已停止 · 源文件需要恢复或重新链接";
                return;
            }

            ValidateRestorableProject(document, sourceMap, projectTemplate);
            CommitRestoredProject(
                projectPath,
                document,
                restoredSources,
                sourceMap,
                projectTemplate,
                markSaved && relinkedSourceCount == 0);
            AutosaveStatusText = isRecovery || relinkedSourceCount > 0
                ? "已恢复或重新链接 · 等待手动保存"
                : "自动保存待命";
            string displayName = projectPath is null
                ? "未命名恢复工程"
                : Path.GetFileName(projectPath);
            StatusMessage = relinkedSourceCount > 0
                ? $"工程已打开并安全重新链接 {relinkedSourceCount} 个源图 · 请保存工程 · 源图未修改"
                : isRecovery
                    ? $"已恢复自动保存 · {displayName} · 请手动保存 · 源图未修改"
                    : $"工程已打开 · {displayName} · {Sources.Count} 个源图 · {Figure.Panels.Count} 个面板";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            LastError = exception.Message;
            StatusMessage = "工程打开失败 · 当前工程未被替换";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task<(SourceAssetItemViewModel Item, bool Relinked)> ResolveProjectSourceAsync(
        ProjectSourceSnapshot snapshot)
    {
        string originalFailure;
        try
        {
            SourceAsset original = await _sourceReader.ImportAsync(snapshot.OriginalPath);
            if (string.Equals(
                    original.Fingerprint.Sha256,
                    snapshot.Fingerprint.Sha256,
                    StringComparison.OrdinalIgnoreCase))
            {
                var preview = await _previewLoader.LoadAsync(snapshot.OriginalPath, 1400);
                SourceAsset restored = original with
                {
                    Id = snapshot.Id,
                    DisplayName = snapshot.DisplayName,
                    OriginalPath = Path.GetFullPath(snapshot.OriginalPath),
                    LinkState = SourceLinkState.Verified,
                };
                return (new SourceAssetItemViewModel(restored, preview), false);
            }

            originalFailure = "原路径文件内容与保存工程时不同";
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            originalFailure = exception.Message;
        }

        string? replacementPath = _sourceRelinkFilePicker.PickReplacement(
            snapshot.DisplayName,
            snapshot.OriginalPath,
            snapshot.Fingerprint.Sha256);
        if (replacementPath is null)
        {
            throw new InvalidDataException($"{originalFailure}；未选择重新链接文件。");
        }

        SourceAsset replacement = await _sourceReader.ImportAsync(replacementPath);
        if (!string.Equals(
                replacement.Fingerprint.Sha256,
                snapshot.Fingerprint.Sha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"所选替代文件 SHA-256 不匹配；需要 {snapshot.Fingerprint.Sha256[..12]}，" +
                $"实际为 {replacement.Fingerprint.Sha256[..12]}。");
        }

        var replacementPreview = await _previewLoader.LoadAsync(replacementPath, 1400);
        SourceAsset relinked = replacement with
        {
            Id = snapshot.Id,
            DisplayName = snapshot.DisplayName,
            OriginalPath = Path.GetFullPath(replacementPath),
            LinkState = SourceLinkState.Relocated,
        };
        return (new SourceAssetItemViewModel(relinked, replacementPreview), true);
    }

    private void CommitRestoredProject(
        string? path,
        SciCanvasProjectDocument document,
        IReadOnlyList<SourceAssetItemViewModel> restoredSources,
        IReadOnlyDictionary<Guid, SourceAssetItemViewModel> sourceMap,
        FigureTemplateDefinition projectTemplate,
        bool markSaved)
    {
        _isRestoringProject = true;
        try
        {
            ReplaceFigure(projectTemplate, markDirty: false);
            Sources.Clear();
            BatchCropQueue.Clear();
            SelectedBatchCrop = null;
            OnPropertyChanged(nameof(BatchCropQueueSummary));
            RestoreExportProfiles(document.ExportProfiles);
            foreach (SourceAssetItemViewModel source in restoredSources)
            {
                AttachSourceScience(source);
                Sources.Add(source);
            }

            foreach (SourceAssetItemViewModel source in restoredSources)
            {
                ProjectCalibrationSnapshot? calibrationSnapshot = document.Calibrations
                    .SingleOrDefault(item => item.SourceAssetId == source.Asset.Id);
                SpatialCalibration calibration = calibrationSnapshot is null
                    ? source.Calibration.Calibration
                    : ProjectDocumentMapper.ToCalibration(calibrationSnapshot);
                ProjectMeasurementSnapshot[] measurementSnapshots = document.Measurements
                    .Where(item => item.SourceAssetId == source.Asset.Id)
                    .ToArray();
                ScientificMeasurement[] measurements = measurementSnapshots
                    .Select(ProjectDocumentMapper.ToMeasurement)
                    .ToArray();
                if (measurements.Any(measurement => !MeasurementFitsSource(measurement, source)))
                {
                    throw new InvalidDataException($"源图 {source.DisplayName} 包含越界的测量坐标。");
                }

                Dictionary<Guid, ScientificMeasurementVisualStyle> styles =
                    measurementSnapshots.ToDictionary(
                        item => item.Id,
                        item => new ScientificMeasurementVisualStyle
                        {
                            StrokeColor = item.StrokeColor,
                            StrokeWidthPixels = item.StrokeWidthPixels,
                            LineStyle = item.LineStyle,
                            MarkerSizePixels = item.MarkerSizePixels,
                            ShowMarkers = item.ShowMarkers,
                            ShowLabel = item.ShowLabel,
                            FillOpacityPercent = item.FillOpacityPercent,
                            IsVisible = item.IsVisible,
                            IsLocked = item.IsLocked,
                        });
                source.RestoreScience(
                    calibration,
                    calibrationSnapshot?.ReferenceStartX ?? 0,
                    calibrationSnapshot?.ReferenceStartY ?? 0,
                    calibrationSnapshot?.ReferenceEndX ?? 0,
                    calibrationSnapshot?.ReferenceEndY ?? 0,
                    measurements,
                    styles);
            }

            Figure.Clear();
            IReadOnlyDictionary<Guid, string> layerSlots =
                document.TemplateSnapshot?.LayerSlots ?? new Dictionary<Guid, string>();
            IReadOnlyDictionary<Guid, ProjectScaleBarSnapshot> scaleBars =
                document.TemplateSnapshot?.ScaleBars ?? new Dictionary<Guid, ProjectScaleBarSnapshot>();
            int layerIndex = 0;
            foreach (ProjectImageLayerSnapshot layer in document.Layers.OrderBy(item => item.ZIndex))
            {
                SourceAssetItemViewModel source = sourceMap[layer.SourceAssetId];
                PixelRect64 sourceRect = ProjectDocumentMapper.ToPixelRect(layer.SourceRect);
                CropValidationResult validation = CropBoundsValidator.Validate(
                    sourceRect,
                    source.Asset.Metadata.PixelSize);
                if (!validation.IsValid)
                {
                    throw new InvalidDataException($"图层 {layer.Name} 的裁剪区域超出源图边界。");
                }

                if (layer.FrameIndex < 0 || layer.FrameIndex >= source.FrameCount)
                {
                    throw new InvalidDataException($"图层 {layer.Name} 引用了不存在的图像帧 {layer.FrameIndex + 1}。");
                }

                string? slotId = layerSlots.GetValueOrDefault(layer.Id) ??
                                 Figure.Template.Slots.ElementAtOrDefault(layerIndex)?.Id;
                if (slotId is null)
                {
                    throw new InvalidDataException($"图层 {layer.Name} 缺少模板插槽信息。");
                }

                PixelRect64 destination = ProjectDocumentMapper.ToDestinationRect(layer);
                if (destination.Right > Figure.CanvasWidth || destination.Bottom > Figure.CanvasHeight)
                {
                    throw new InvalidDataException($"图层 {layer.Name} 超出拼版画布边界。");
                }

                FigurePanelViewModel? restored = Figure.RestorePanel(
                    source,
                    sourceRect,
                    slotId,
                    layer.Id,
                    destination,
                    layer.Visible,
                    layer.Locked,
                    layer.ZIndex,
                    layer.Adjustments.Count == 0 ? null : ToAdjustment(layer.Adjustments[0]),
                    layer.FrameIndex,
                    layer.LockAspectRatio);
                if (restored is null)
                {
                    throw new InvalidDataException($"无法恢复图层 {layer.Name} 的模板插槽。");
                }

                restored.Label = layer.PanelLabel ?? restored.Label;
                restored.CropLinkGroupId = layer.CropLinkGroupId;

                if (scaleBars.TryGetValue(layer.Id, out ProjectScaleBarSnapshot? scaleBar))
                {
                    restored.PhysicalUnitsPerSourcePixel = scaleBar.PhysicalUnitsPerSourcePixel;
                    restored.ScaleBarPhysicalLength = scaleBar.PhysicalLength;
                    restored.ScaleBarUnit = scaleBar.Unit;
                    restored.ScaleBarShowLabel = scaleBar.ShowLabel;
                    restored.ShowScaleBar = scaleBar.Enabled;
                }

                layerIndex++;
            }

            ProjectTemplateSnapshot? editor = document.TemplateSnapshot;
            ProjectGlobalStyleSnapshot? globalStyle = editor?.GlobalStyle;
            Figure.RestoreGlobalStyle(globalStyle is null
                ? FigureGlobalStyle.Default
                : new FigureGlobalStyle(
                    globalStyle.FontFamily,
                    globalStyle.FontSizePt,
                    globalStyle.StrokeWidthPt,
                    globalStyle.TextColor,
                    globalStyle.ShapeColor,
                    globalStyle.ScaleBarColor));
            Figure.RestoreScientificColors((editor?.ScientificColors ?? [])
                .Select(color => new ScientificColorDefinition(color.Id, color.Name, color.Color)));
            foreach (ProjectAnnotationSnapshot annotation in
                     (editor?.Annotations ?? []).OrderBy(item => item.ZIndex))
            {
                FigureAnnotationKind kind = ParseAnnotationKind(annotation.Kind);
                Figure.RestoreAnnotation(
                    annotation.Id,
                    kind,
                    annotation.X,
                    annotation.Y,
                    annotation.EndX,
                    annotation.EndY,
                    annotation.Text,
                    annotation.Color,
                    annotation.FontSizePt,
                    annotation.StrokeWidthPt,
                    annotation.IsBold,
                    annotation.Visible,
                    annotation.Locked,
                    annotation.ZIndex);
            }

            foreach (ProjectGuideSnapshot guide in document.Guides)
            {
                Figure.RestoreGuide(
                    Guid.NewGuid(),
                    ParseGuideOrientation(guide.Orientation),
                    guide.Position,
                    guide.Locked);
            }

            Figure.IsSnappingEnabled = editor?.SnappingEnabled ?? true;
            Figure.SnapTolerancePixels = editor?.SnapTolerancePixels ?? 12;
            Figure.ExactSpacingPixels = editor?.ExactSpacingPixels ?? 24;
            Figure.BackgroundColor = document.Canvas.BackgroundColor ??
                                     document.Canvas.Background switch
                                     {
                                         "black" => "#FF000000",
                                         "transparent" => "#00FFFFFF",
                                         _ => "#FFFFFFFF",
                                     };
            if (!Figure.IsBackgroundColorValid)
            {
                throw new InvalidDataException("工程包含无效的画布背景颜色。");
            }

            Figure.AutoPanelLabelsEnabled = false;
            Figure.PanelLabelSequence = editor?.PanelLabelSequence ?? "lowercase";
            Figure.ShowPanelLabels = editor?.ShowPanelLabels ?? true;
            Figure.AutoPanelLabelsEnabled = editor?.AutoPanelLabelsEnabled ?? true;

            SelectedSource = editor?.SelectedSourceId is Guid selectedId && sourceMap.TryGetValue(selectedId, out var selected)
                ? selected
                : Sources.FirstOrDefault();

            if (SelectedSource is not null && editor?.ActiveCrop is not null)
            {
                PixelRect64 activeCrop = ProjectDocumentMapper.ToPixelRect(editor.ActiveCrop);
                if (!Crop.RestoreForSource(SelectedSource.Asset.Metadata.PixelSize, activeCrop))
                {
                    throw new InvalidDataException("工程中的活动裁剪区域超出源图边界。");
                }
            }

            LockCropSizeAcrossSources = editor?.LockCropSizeAcrossSources ?? true;
            IsCropOverlayVisible = editor?.CropOverlayVisible ?? true;
            WorkspaceMode = string.Equals(editor?.WorkspaceMode, "figure", StringComparison.OrdinalIgnoreCase)
                ? WorkspaceMode.Figure
                : WorkspaceMode.Crop;

            _projectId = document.ProjectId;
            _projectCreatedAt = document.CreatedAt;
            _projectPath = path is null ? null : Path.GetFullPath(path);
            _auditTrail.Clear();
            _auditTrail.AddRange(document.AuditTrail);
            OnPropertyChanged(nameof(ProjectPath));
            OnPropertyChanged(nameof(ProjectDisplayName));
            ExportFigureCommand.NotifyCanExecuteChanged();
        }
        finally
        {
            _isRestoringProject = false;
        }

        _history.Reset(CaptureHistorySnapshot(), markSaved);
        RefreshHistoryState();
    }

    private void ValidateRestorableProject(
        SciCanvasProjectDocument document,
        IReadOnlyDictionary<Guid, SourceAssetItemViewModel> sourceMap,
        FigureTemplateDefinition projectTemplate)
    {
        TemplateCanvasLayout layout = TemplateLayoutEngine.CreateLayout(projectTemplate);
        IReadOnlyDictionary<Guid, string> layerSlots =
            document.TemplateSnapshot?.LayerSlots ?? new Dictionary<Guid, string>();
        HashSet<string> usedSlots = new(StringComparer.Ordinal);
        int layerIndex = 0;

        foreach (ProjectImageLayerSnapshot layer in document.Layers.OrderBy(item => item.ZIndex))
        {
            if (!sourceMap.TryGetValue(layer.SourceAssetId, out SourceAssetItemViewModel? source))
            {
                throw new InvalidDataException($"图层 {layer.Name} 引用了不存在的源图像。");
            }

            PixelRect64 sourceRect = ProjectDocumentMapper.ToPixelRect(layer.SourceRect);
            if (!CropBoundsValidator.Validate(sourceRect, source.Asset.Metadata.PixelSize).IsValid)
            {
                throw new InvalidDataException($"图层 {layer.Name} 的裁剪区域超出源图边界。");
            }

            PixelRect64 destination = ProjectDocumentMapper.ToDestinationRect(layer);
            if (destination.Right > layout.WidthPixels || destination.Bottom > layout.HeightPixels)
            {
                throw new InvalidDataException($"图层 {layer.Name} 超出拼版画布边界。");
            }

            string? slotId = layerSlots.GetValueOrDefault(layer.Id) ??
                             projectTemplate.Slots.ElementAtOrDefault(layerIndex)?.Id;
            bool insetSlot = slotId?.StartsWith("inset:", StringComparison.Ordinal) == true;
            if (slotId is null ||
                (!insetSlot && projectTemplate.Slots.All(slot => slot.Id != slotId)) ||
                !usedSlots.Add(slotId))
            {
                throw new InvalidDataException($"图层 {layer.Name} 的模板插槽无效或重复。");
            }

            layerIndex++;
        }

        IReadOnlyDictionary<Guid, ProjectScaleBarSnapshot> scaleBars =
            document.TemplateSnapshot?.ScaleBars ?? new Dictionary<Guid, ProjectScaleBarSnapshot>();
        foreach ((Guid layerId, ProjectScaleBarSnapshot scaleBar) in scaleBars)
        {
            ProjectImageLayerSnapshot? layer = document.Layers.FirstOrDefault(item => item.Id == layerId);
            if (layer is null)
            {
                throw new InvalidDataException("工程包含没有对应图层的比例尺参数。");
            }

        }

        HashSet<Guid> annotationIds = [];
        foreach (ProjectAnnotationSnapshot annotation in document.TemplateSnapshot?.Annotations ?? [])
        {
            if (!annotationIds.Add(annotation.Id))
            {
                throw new InvalidDataException("工程包含重复的标注 ID。");
            }

            _ = ParseAnnotationKind(annotation.Kind);
        }

        foreach (ProjectGuideSnapshot guide in document.Guides)
        {
            FigureGuideOrientation orientation = ParseGuideOrientation(guide.Orientation);
            double maximum = orientation == FigureGuideOrientation.Vertical
                ? layout.WidthPixels
                : layout.HeightPixels;
            if (!double.IsFinite(guide.Position) || guide.Position < 0 || guide.Position > maximum)
            {
                throw new InvalidDataException("工程包含超出拼版画布的参考线。");
            }
        }

        ProjectTemplateSnapshot? templateSettings = document.TemplateSnapshot;
        if (templateSettings is not null &&
            (!double.IsFinite(templateSettings.SnapTolerancePixels) ||
             templateSettings.SnapTolerancePixels is < 1 or > 100 ||
             templateSettings.ExactSpacingPixels < 0 ||
             templateSettings.ExactSpacingPixels > Math.Max(layout.WidthPixels, layout.HeightPixels)))
        {
            throw new InvalidDataException("工程包含无效的吸附或精确间距设置。");
        }

        ProjectTemplateSnapshot? editor = document.TemplateSnapshot;
        if (editor?.SelectedSourceId is Guid selectedId && !sourceMap.ContainsKey(selectedId))
        {
            throw new InvalidDataException("工程选择了不存在的源图像。");
        }

        if (editor?.ActiveCrop is not null && editor.SelectedSourceId is Guid cropSourceId)
        {
            PixelRect64 crop = ProjectDocumentMapper.ToPixelRect(editor.ActiveCrop);
            if (!CropBoundsValidator.Validate(crop, sourceMap[cropSourceId].Asset.Metadata.PixelSize).IsValid)
            {
                throw new InvalidDataException("工程中的活动裁剪区域超出源图边界。");
            }
        }
    }

    public async Task<bool> SaveBeforeCloseAsync()
    {
        if (IsBusy)
        {
            return false;
        }

        await SaveProjectAsync();
        return !IsDirty;
    }

    public async Task DiscardRecoveryBeforeCloseAsync()
    {
        _autosaveTimer.Stop();
        await DeleteCurrentRecoveryBestEffortAsync();
        AutosaveStatusText = "已放弃未保存更改";
    }

    private void OnCropPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CropEditorViewModel.X) or nameof(CropEditorViewModel.Y) or
            nameof(CropEditorViewModel.Width) or nameof(CropEditorViewModel.Height))
        {
            AddCurrentCropToFigureCommand.NotifyCanExecuteChanged();
            AddCurrentCropToBatchQueueCommand.NotifyCanExecuteChanged();
            ReplaceSelectedPanelSourceCommand.NotifyCanExecuteChanged();
            AnalyzeAssistedRegionsCommand.NotifyCanExecuteChanged();
            MarkAssistedRegionAnalysisStale();
            MarkDirty();
        }
    }

    private void OnFigureDocumentChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsTemplateSelectionEnabled));
        ExportFigureCommand.NotifyCanExecuteChanged();
        ExportFigureVariantsCommand.NotifyCanExecuteChanged();
        AddCurrentCropToFigureCommand.NotifyCanExecuteChanged();
        ReplaceSelectedPanelSourceCommand.NotifyCanExecuteChanged();
        MarkFigureQcStale();
        MarkDirty();
    }

    private void ApplySmartLayout()
    {
        if (Figure.Panels.Count > 0)
        {
            int reset = Figure.ResetRegularPanelsToTemplateLayout();
            SmartAssistStatusText = reset == 0
                ? "当前只有 Inset 或没有可重排面板；布局未改变。"
                : $"已按 {Figure.TemplateName} 的确定性槽位重排 {reset} 个面板；Inset 保持原位。";
            StatusMessage = $"辅助布局完成 · {reset} 个面板 · 可撤销";
            return;
        }

        if (Sources.Count == 0)
        {
            SmartAssistStatusText = "尚无源图；导入图像后才能按数量推荐模板。";
            StatusMessage = "辅助布局未运行 · 没有源图";
            return;
        }

        int requestedCount = Sources.Count;
        FigureTemplateDefinition recommended = AvailableTemplates
            .Where(template => template.Slots.Count >= requestedCount)
            .OrderBy(template => template.Slots.Count - requestedCount)
            .ThenBy(template => template.Id, StringComparer.Ordinal)
            .FirstOrDefault() ?? AvailableTemplates
            .OrderByDescending(template => template.Slots.Count)
            .ThenBy(template => template.Id, StringComparer.Ordinal)
            .First();
        if (!ReferenceEquals(recommended, SelectedFigureTemplate))
        {
            ReplaceFigure(recommended);
        }

        int placed = 0;
        foreach (SourceAssetItemViewModel source in Sources.Take(Figure.SlotCount))
        {
            if (Figure.AddPanel(
                    source,
                    new PixelRect64(0, 0, source.Width, source.Height)) is not null)
            {
                placed++;
            }
        }

        _auditTrail.Add(new ProjectAuditEntrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Command = "ApplyExplainableLayoutSuggestion",
            Parameters = new Dictionary<string, object?>
            {
                ["inputSourceCount"] = requestedCount,
                ["templateId"] = recommended.Id,
                ["templateSlotCount"] = recommended.Slots.Count,
                ["placedPanelCount"] = placed,
                ["rule"] = "smallest-template-capacity-greater-than-or-equal-to-source-count",
            },
        });
        SmartAssistStatusText =
            $"按源图数量 {requestedCount} 推荐 {recommended.Name}（{recommended.Slots.Count} 槽），已放置 {placed} 张；" +
            "规则与结果已记录，可人工移动或撤销。";
        CompleteHistoryGesture();
        StatusMessage = $"辅助布局完成 · {placed}/{requestedCount} 张源图已放置";
    }

    private void HarmonizeFigureStyle()
    {
        FigureExportDocument before = Figure.CreateExportDocument();
        FigureGlobalStyle style = before.GlobalStyle;
        int changed = before.Annotations.Count(annotation => annotation.IsVisible &&
            (annotation.Kind == "text"
                ? Math.Abs(annotation.FontSizePt - style.FontSizePt) > 0.01 ||
                  !SameHexColor(annotation.Color, style.TextColor)
                : Math.Abs(annotation.StrokeWidthPt - style.StrokeWidthPt) > 0.01 ||
                  !SameHexColor(annotation.Color, style.ShapeColor)));
        Figure.ApplyGlobalStyleCommand.Execute(null);
        _auditTrail.Add(new ProjectAuditEntrySnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            Command = "ApplyStyleHarmonizationSuggestion",
            Parameters = new Dictionary<string, object?>
            {
                ["changedAnnotationCount"] = changed,
                ["fontFamily"] = style.FontFamily,
                ["fontSizePt"] = style.FontSizePt,
                ["strokeWidthPt"] = style.StrokeWidthPt,
                ["rule"] = "project-global-style-is-canonical",
            },
        });
        SmartAssistStatusText = changed == 0
            ? "样式检查未发现偏离项目全局样式的标注。"
            : $"已将 {changed} 个标注统一到项目全局样式；修改可撤销，规则已记录。";
        CompleteHistoryGesture();
        StatusMessage = $"样式协调完成 · {changed} 个标注已统一";
    }

    private void RunAssistedFigureReview()
    {
        try
        {
            FigureExportDocument document = Figure.CreateExportDocument();
            FigurePreflightResult result = FigureAssistance.Review(
                document,
                Sources.Select(source => source.Asset).ToArray(),
                IsDirty);
            UpdateFigureQcIssues(result);
            int integrityCount = result.Issues.Count(issue =>
                issue.Code.StartsWith("INTEGRITY_", StringComparison.Ordinal));
            int styleCount = result.Issues.Count(issue =>
                issue.Code is "STYLE_HARMONIZATION" or "LOW_COLOR_CONTRAST");
            _auditTrail.Add(new ProjectAuditEntrySnapshot
            {
                Timestamp = DateTimeOffset.UtcNow,
                Command = "RunExplainableFigureReview",
                Parameters = new Dictionary<string, object?>
                {
                    ["issueCount"] = result.Issues.Count,
                    ["integrityFindingCount"] = integrityCount,
                    ["styleFindingCount"] = styleCount,
                    ["engine"] = "scicanvas.explainable-figure-review.v1",
                    ["generativeModificationEnabled"] = false,
                },
            });
            SmartAssistStatusText =
                $"辅助审查完成 · {result.Issues.Count} 项（科研诚信 {integrityCount}、样式/颜色 {styleCount}）；" +
                "均为可解释规则，需人工判断。";
            MarkDirty();
            StatusMessage = $"可解释 Figure 审查 · {result.Summary}";
        }
        catch (InvalidOperationException exception)
        {
            LastError = exception.Message;
            SmartAssistStatusText = "辅助审查未运行：当前拼版包含无效编辑状态。";
            StatusMessage = "可解释 Figure 审查失败";
        }
    }

    private static bool SameHexColor(string first, string second)
    {
        static string Normalize(string value)
        {
            string hex = value.Trim().TrimStart('#');
            return hex.Length == 8 ? hex[2..] : hex;
        }

        return string.Equals(Normalize(first), Normalize(second), StringComparison.OrdinalIgnoreCase);
    }

    private void RunFigureQc()
    {
        try
        {
            FigureExportDocument document = Figure.CreateExportDocument();
            FigurePreflightResult result = UpdateFigureQc(document);
            LastError = result.HasErrors
                ? string.Join(Environment.NewLine, result.Issues
                    .Where(issue => issue.Severity == FigurePreflightSeverity.Error)
                    .Select(issue => issue.Message))
                : null;
            StatusMessage = $"Figure QC · {result.Summary}";
        }
        catch (InvalidOperationException exception)
        {
            var result = new FigurePreflightResult(
            [
                new FigurePreflightIssue(
                    FigurePreflightSeverity.Error,
                    "QC_ENGINE_ERROR",
                    exception.Message),
            ]);
            UpdateFigureQcIssues(result);
            LastError = exception.Message;
            StatusMessage = "Figure QC 未通过 · 编辑状态无效";
        }
    }

    private FigurePreflightResult UpdateFigureQc(FigureExportDocument document)
    {
        FigurePreflightResult result = FigurePreflight.Check(
            document,
            Sources.Select(item => item.Asset).ToArray(),
            IsDirty);
        UpdateFigureQcIssues(result);
        return result;
    }

    private void UpdateFigureQcIssues(FigurePreflightResult result)
    {
        FigureQcIssues.Clear();
        foreach (FigurePreflightIssue issue in result.Issues
                     .OrderByDescending(issue => issue.Severity)
                     .ThenBy(issue => issue.PanelLabel, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(issue => issue.Code, StringComparer.Ordinal))
        {
            FigureQcIssues.Add(new FigureQcIssueViewModel(issue));
        }

        SelectedFigureQcIssue = FigureQcIssues.FirstOrDefault();
        _isFigureQcStale = false;
        FigureQcStatusText = result.Summary;
        OnPropertyChanged(nameof(FigureQcCountText));
    }

    private void MarkFigureQcStale()
    {
        if (!_isFigureQcStale)
        {
            _isFigureQcStale = true;
            FigureQcStatusText = "拼版已更改 · 请重新运行 Figure QC";
        }
    }

    private void NavigateToSelectedQcIssue()
    {
        string? panelLabel = SelectedFigureQcIssue?.PanelLabel;
        if (string.IsNullOrWhiteSpace(panelLabel))
        {
            return;
        }

        FigurePanelViewModel? panel = Figure.Panels.FirstOrDefault(
            item => string.Equals(item.Label, panelLabel, StringComparison.OrdinalIgnoreCase));
        if (panel is null)
        {
            LastError = $"QC 目标面板 {panelLabel} 已不存在，请重新运行检查。";
            MarkFigureQcStale();
            return;
        }

        WorkspaceMode = WorkspaceMode.Figure;
        IsLayersTabActive = false;
        Figure.SelectPanel(panel, toggle: false);
        LastError = null;
        StatusMessage = $"已定位 Figure QC 问题 · 面板 {panel.Label}";
    }

    private void OnFigureEditCompleted(object? sender, EventArgs e) => CompleteHistoryGesture();

    private FigureTemplateDefinition ResolveProjectTemplate(SciCanvasProjectDocument document)
    {
        string? templateId = document.TemplateSnapshot?.TemplateId;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return AvailableTemplates[0];
        }

        return AvailableTemplates.FirstOrDefault(
                   template => string.Equals(template.Id, templateId, StringComparison.Ordinal))
               ?? throw new NotSupportedException($"工程使用模板 {templateId}，当前版本尚未安装该模板。");
    }

    private static ImageAdjustmentParameters ToAdjustment(ProjectImageAdjustmentSnapshot snapshot) => new()
    {
        Brightness = snapshot.Brightness,
        Contrast = snapshot.Contrast,
        Gamma = snapshot.Gamma,
        BlackPoint = snapshot.BlackPoint,
        WhitePoint = snapshot.WhitePoint,
        Invert = snapshot.Invert,
        Grayscale = snapshot.Grayscale,
        Channel = snapshot.Channel,
    };
    private static FigureAnnotationKind ParseAnnotationKind(string? kind) =>
        kind?.ToLowerInvariant() switch
        {
            "text" => FigureAnnotationKind.Text,
            "arrow" => FigureAnnotationKind.Arrow,
            "line" => FigureAnnotationKind.Line,
            "rectangle" => FigureAnnotationKind.Rectangle,
            "ellipse" => FigureAnnotationKind.Ellipse,
            _ => throw new InvalidDataException($"不支持的标注类型：{kind ?? "<空>"}"),
        };

    private static FigureGuideOrientation ParseGuideOrientation(string? orientation) =>
        orientation?.ToLowerInvariant() switch
        {
            "vertical" => FigureGuideOrientation.Vertical,
            "horizontal" => FigureGuideOrientation.Horizontal,
            _ => throw new InvalidDataException($"不支持的参考线方向：{orientation ?? "<空>"}"),
        };

    public void CompleteHistoryGesture() => _history.BreakCoalescing();

    private void Undo()
    {
        EditorHistorySnapshot? snapshot = _history.Undo();
        if (snapshot is null)
        {
            return;
        }

        try
        {
            RestoreHistorySnapshot(snapshot);
            StatusMessage = "已撤销上一步编辑 · 原图未修改";
            LastError = null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidDataException)
        {
            LastError = $"无法撤销：{exception.Message}";
            StatusMessage = "撤销失败 · 当前内容仍保留";
        }
    }

    private void Redo()
    {
        EditorHistorySnapshot? snapshot = _history.Redo();
        if (snapshot is null)
        {
            return;
        }

        try
        {
            RestoreHistorySnapshot(snapshot);
            StatusMessage = "已重做上一步编辑 · 原图未修改";
            LastError = null;
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or InvalidDataException)
        {
            LastError = $"无法重做：{exception.Message}";
            StatusMessage = "重做失败 · 当前内容仍保留";
        }
    }

    private EditorHistorySnapshot CaptureHistorySnapshot()
    {
        PixelRect64? activeCrop = Crop.TryGetCrop(out PixelRect64 crop) ? crop : null;
        return new EditorHistorySnapshot(
            Figure.Template.Id,
            Sources.Select(source => source.Asset.Id).ToArray(),
            SelectedSource?.Asset.Id,
            activeCrop,
            LockCropSizeAcrossSources,
            IsCropOverlayVisible,
            WorkspaceMode,
            Figure.BackgroundColor,
            Figure.AutoPanelLabelsEnabled,
            Figure.ShowPanelLabels,
            Figure.PanelLabelSequence,
            Figure.GlobalStyle,
            Figure.ScientificColors.Select(entry => entry.Definition).ToArray(),
            Figure.SelectedPanel?.Id,
            Figure.SelectedPanels.Select(panel => panel.Id).ToArray(),
            Figure.SelectedAnnotation?.Id,
            Figure.SelectedGuide?.Id,
            Figure.IsSnappingEnabled,
            Figure.SnapTolerancePixels,
            Figure.ExactSpacingPixels,
            Figure.Panels
                .OrderBy(panel => panel.ZIndex)
                .Select(panel => new PanelHistorySnapshot(
                    panel.Id,
                    panel.Source.Asset.Id,
                    panel.SourceRect,
                    panel.SlotId,
                    panel.DestinationRect,
                    panel.Label,
                    panel.IsVisible,
                    panel.IsLocked,
                    panel.ZIndex,
                    panel.ShowScaleBar,
                    panel.PhysicalUnitsPerSourcePixel,
                    panel.ScaleBarPhysicalLength,
                    panel.ScaleBarUnit,
                    panel.ScaleBarShowLabel,
                    panel.FrameIndex,
                    panel.Adjustments,
                    panel.IsAspectRatioLocked,
                    panel.CropLinkGroupId))
                .ToArray(),
            Figure.Annotations
                .OrderBy(annotation => annotation.ZIndex)
                .Select(annotation => new AnnotationHistorySnapshot(
                    annotation.Id,
                    annotation.Kind,
                    annotation.X,
                    annotation.Y,
                    annotation.EndX,
                    annotation.EndY,
                    annotation.Text,
                    annotation.Color,
                    annotation.FontSizePt,
                    annotation.StrokeWidthPt,
                    annotation.IsBold,
                    annotation.IsVisible,
                    annotation.IsLocked,
                    annotation.ZIndex))
                .ToArray(),
            Figure.Guides
                .Select(guide => new GuideHistorySnapshot(
                    guide.Id,
                    guide.Orientation,
                    guide.Position,
                    guide.IsLocked))
                .ToArray(),
            Sources.Select(source => new CalibrationHistorySnapshot(
                    source.Asset.Id,
                    source.Calibration.Calibration,
                    source.Calibration.ReferenceStartX,
                    source.Calibration.ReferenceStartY,
                    source.Calibration.ReferenceEndX,
                    source.Calibration.ReferenceEndY))
                .ToArray(),
            Sources.SelectMany(source => source.Measurements)
                .Select(measurement => new MeasurementHistorySnapshot(
                    measurement.Id,
                    measurement.SourceAssetId,
                    measurement.Kind,
                    measurement.Measurement.PointA,
                    measurement.Measurement.PointB,
                    measurement.Measurement.PointC,
                    measurement.PathPoints,
                    measurement.StrokeColor,
                    measurement.StrokeWidthPixels,
                    measurement.LineStyle,
                    measurement.MarkerSizePixels,
                    measurement.ShowMarkers,
                    measurement.ShowLabel,
                    measurement.FillOpacityPercent,
                    measurement.IsVisible,
                    measurement.IsLocked))
                .ToArray());
    }

    private void RestoreHistorySnapshot(EditorHistorySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.SourceIds.SequenceEqual(Sources.Select(source => source.Asset.Id)))
        {
            throw new InvalidOperationException("源图像集合已变化，不能应用这一步历史记录。");
        }

        FigureTemplateDefinition template = AvailableTemplates.FirstOrDefault(
                item => string.Equals(item.Id, snapshot.TemplateId, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"历史记录引用了未安装的模板 {snapshot.TemplateId}。");
        Dictionary<Guid, SourceAssetItemViewModel> sourceMap = Sources.ToDictionary(
            source => source.Asset.Id);

        _isRestoringProject = true;
        try
        {
            ReplaceFigure(template, markDirty: false);
            foreach (PanelHistorySnapshot panelSnapshot in snapshot.Panels.OrderBy(panel => panel.ZIndex))
            {
                if (!sourceMap.TryGetValue(panelSnapshot.SourceId, out SourceAssetItemViewModel? source))
                {
                    throw new InvalidOperationException("历史记录引用的源图像已经不在工作区中。");
                }

                FigurePanelViewModel restored = Figure.RestorePanel(
                    source,
                    panelSnapshot.SourceRect,
                    panelSnapshot.SlotId,
                    panelSnapshot.Id,
                    panelSnapshot.DestinationRect,
                    panelSnapshot.IsVisible,
                    panelSnapshot.IsLocked,
                    panelSnapshot.ZIndex,
                    panelSnapshot.Adjustments,
                    panelSnapshot.FrameIndex,
                    panelSnapshot.IsAspectRatioLocked)
                    ?? throw new InvalidOperationException("无法恢复历史记录中的拼版面板。");
                restored.PhysicalUnitsPerSourcePixel = panelSnapshot.PhysicalUnitsPerSourcePixel;
                restored.Label = panelSnapshot.Label;
                restored.ScaleBarPhysicalLength = panelSnapshot.ScaleBarPhysicalLength;
                restored.ScaleBarUnit = panelSnapshot.ScaleBarUnit;
                restored.ScaleBarShowLabel = panelSnapshot.ScaleBarShowLabel;
                restored.ShowScaleBar = panelSnapshot.ShowScaleBar;
                restored.CropLinkGroupId = panelSnapshot.CropLinkGroupId;
            }

            foreach (AnnotationHistorySnapshot annotation in
                     snapshot.Annotations.OrderBy(item => item.ZIndex))
            {
                Figure.RestoreAnnotation(
                    annotation.Id,
                    annotation.Kind,
                    annotation.X,
                    annotation.Y,
                    annotation.EndX,
                    annotation.EndY,
                    annotation.Text,
                    annotation.Color,
                    annotation.FontSizePt,
                    annotation.StrokeWidthPt,
                    annotation.IsBold,
                    annotation.IsVisible,
                    annotation.IsLocked,
                    annotation.ZIndex);
            }

            foreach (GuideHistorySnapshot guide in snapshot.Guides)
            {
                Figure.RestoreGuide(
                    guide.Id,
                    guide.Orientation,
                    guide.Position,
                    guide.IsLocked);
            }

            foreach (SourceAssetItemViewModel source in Sources)
            {
                CalibrationHistorySnapshot calibration = snapshot.Calibrations.Single(
                    item => item.SourceId == source.Asset.Id);
                MeasurementHistorySnapshot[] measurementSnapshots = snapshot.Measurements
                    .Where(item => item.SourceId == source.Asset.Id)
                    .ToArray();
                source.RestoreScience(
                    calibration.Calibration,
                    calibration.ReferenceStartX,
                    calibration.ReferenceStartY,
                    calibration.ReferenceEndX,
                    calibration.ReferenceEndY,
                    measurementSnapshots.Select(item => new ScientificMeasurement(
                        item.Id,
                        item.SourceId,
                        item.Kind,
                        item.PointA,
                        item.PointB,
                        item.PointC,
                        Name: null,
                        PathPoints: item.PathPoints)),
                    measurementSnapshots.ToDictionary(
                        item => item.Id,
                        item => new ScientificMeasurementVisualStyle
                        {
                            StrokeColor = item.StrokeColor,
                            StrokeWidthPixels = item.StrokeWidthPixels,
                            LineStyle = item.LineStyle,
                            MarkerSizePixels = item.MarkerSizePixels,
                            ShowMarkers = item.ShowMarkers,
                            ShowLabel = item.ShowLabel,
                            FillOpacityPercent = item.FillOpacityPercent,
                            IsVisible = item.IsVisible,
                            IsLocked = item.IsLocked,
                        }));
                SynchronizeScaleBarsForSource(source);
            }

            Figure.IsSnappingEnabled = snapshot.SnappingEnabled;
            Figure.SnapTolerancePixels = snapshot.SnapTolerancePixels;
            Figure.ExactSpacingPixels = snapshot.ExactSpacingPixels;
            Figure.BackgroundColor = snapshot.BackgroundColor;
            Figure.RestoreGlobalStyle(snapshot.GlobalStyle);
            Figure.RestoreScientificColors(snapshot.ScientificColors);
            Figure.AutoPanelLabelsEnabled = false;
            Figure.PanelLabelSequence = snapshot.PanelLabelSequence;
            Figure.ShowPanelLabels = snapshot.ShowPanelLabels;
            Figure.AutoPanelLabelsEnabled = snapshot.AutoPanelLabelsEnabled;

            SelectedSource = snapshot.SelectedSourceId is Guid selectedId &&
                             sourceMap.TryGetValue(selectedId, out SourceAssetItemViewModel? selected)
                ? selected
                : null;
            if (SelectedSource is not null && snapshot.ActiveCrop is PixelRect64 restoredCrop)
            {
                if (!Crop.RestoreForSource(SelectedSource.Asset.Metadata.PixelSize, restoredCrop))
                {
                    throw new InvalidOperationException("历史记录中的裁剪区域超出源图边界。");
                }
            }
            else if (SelectedSource is null)
            {
                Crop.Reset();
            }

            LockCropSizeAcrossSources = snapshot.LockCropSizeAcrossSources;
            IsCropOverlayVisible = snapshot.CropOverlayVisible;
            WorkspaceMode = snapshot.WorkspaceMode;
            Figure.RestorePanelSelection(snapshot.SelectedPanelIds, snapshot.SelectedPanelId);
            Figure.SelectedAnnotation = snapshot.SelectedAnnotationId is Guid annotationId
                ? Figure.Annotations.FirstOrDefault(annotation => annotation.Id == annotationId)
                : null;
            Figure.SelectedGuide = snapshot.SelectedGuideId is Guid guideId
                ? Figure.Guides.FirstOrDefault(guide => guide.Id == guideId)
                : null;
        }
        finally
        {
            _isRestoringProject = false;
        }

        ExportFigureCommand.NotifyCanExecuteChanged();
        AddCurrentCropToFigureCommand.NotifyCanExecuteChanged();
        RefreshHistoryState();
    }

    private static bool CanCoalesceHistoryChange(
        EditorHistorySnapshot before,
        EditorHistorySnapshot after) =>
        string.Equals(before.TemplateId, after.TemplateId, StringComparison.Ordinal) &&
        before.SourceIds.SequenceEqual(after.SourceIds) &&
        before.Panels.Select(panel => panel.Id).SequenceEqual(after.Panels.Select(panel => panel.Id)) &&
        before.Annotations.Select(annotation => annotation.Id)
            .SequenceEqual(after.Annotations.Select(annotation => annotation.Id)) &&
        before.Guides.Select(guide => guide.Id)
            .SequenceEqual(after.Guides.Select(guide => guide.Id)) &&
        before.Measurements.Select(measurement => measurement.Id)
            .SequenceEqual(after.Measurements.Select(measurement => measurement.Id));

    private void RefreshHistoryState()
    {
        IsDirty = _history.IsDirty;
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HistoryStatusText));
        if (_historyReady)
        {
            UpdateAutosaveSchedule();
        }
    }

    internal async Task FlushAutosaveAsync()
    {
        _autosaveTimer.Stop();
        if (_autosaveInProgress)
        {
            _autosavePending = true;
            return;
        }

        if (!IsDirty || Sources.Count == 0)
        {
            return;
        }

        if (IsBusy)
        {
            _autosavePending = true;
            UpdateAutosaveSchedule();
            return;
        }

        _autosaveInProgress = true;
        try
        {
            string title = _projectPath is null
                ? "未命名工程"
                : Path.GetFileNameWithoutExtension(_projectPath);
            SciCanvasProjectDocument document = ProjectDocumentMapper.Create(
                _projectId,
                _projectCreatedAt,
                title,
                Sources,
                SelectedSource,
                Crop,
                Figure,
                WorkspaceMode,
                LockCropSizeAcrossSources,
                IsCropOverlayVisible,
                _auditTrail,
                CreateValidatedExportProfiles());

            await _projectRecoveryStore.SaveAsync(_projectId, _projectPath, document);
            AutosaveStatusText = $"自动保存 {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or InvalidOperationException)
        {
            AutosaveStatusText = $"自动保存失败：{exception.Message}";
        }
        finally
        {
            _autosaveInProgress = false;
            if (_autosavePending)
            {
                _autosavePending = false;
                UpdateAutosaveSchedule();
            }
        }
    }

    private void UpdateAutosaveSchedule()
    {
        _autosaveTimer.Stop();
        if (IsDirty && Sources.Count > 0)
        {
            AutosaveStatusText = "等待自动保存";
            _autosaveTimer.Start();
            return;
        }

        _ = DeleteCurrentRecoveryBestEffortAsync();
    }

    private async Task DeleteCurrentRecoveryBestEffortAsync()
    {
        try
        {
            await _projectRecoveryStore.DeleteAsync(_projectId, _projectPath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            AutosaveStatusText = $"恢复副本清理失败：{exception.Message}";
        }
    }

    private async void OnAutosaveTimerTick(object? sender, EventArgs e) =>
        await FlushAutosaveAsync();

    private void ReplaceFigure(FigureTemplateDefinition template, bool markDirty = true)
    {
        ArgumentNullException.ThrowIfNull(template);
        Figure.DocumentChanged -= OnFigureDocumentChanged;
        Figure.EditCompleted -= OnFigureEditCompleted;
        Figure = new FigureCanvasViewModel(template);
        Figure.DocumentChanged += OnFigureDocumentChanged;
        Figure.EditCompleted += OnFigureEditCompleted;
        FigureQcIssues.Clear();
        SelectedFigureQcIssue = null;
        _isFigureQcStale = true;
        FigureQcStatusText = "拼版已重建 · 请运行 Figure QC";
        OnPropertyChanged(nameof(FigureQcCountText));
        _selectedFigureTemplate = template;
        OnPropertyChanged(nameof(SelectedFigureTemplate));
        OnPropertyChanged(nameof(IsTemplateSelectionEnabled));
        if (markDirty)
        {
            MarkDirty();
        }
    }

    private void MarkDirty()
    {
        if (!_isRestoringProject && _historyReady)
        {
            EditorHistorySnapshot current = CaptureHistorySnapshot();
            EditorHistorySnapshot before = _history.CurrentSnapshot ?? current;
            bool canCoalesce = CanCoalesceHistoryChange(before, current);
            _history.Record(current, canCoalesce);
            RefreshHistoryState();
        }
    }

    private void HandleUnexpectedCommandError(Exception exception)
    {
        LastError = $"发生未预期错误：{exception.Message}";
        StatusMessage = "操作失败 · 原图未修改";
        IsBusy = false;
    }
}
