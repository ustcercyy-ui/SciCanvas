using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SciCanvas.Core.Cropping;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
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
    private readonly EditorHistoryManager _history = new(100);
    private readonly List<ProjectAuditEntrySnapshot> _auditTrail = [];
    private readonly DispatcherTimer _autosaveTimer;
    private FigureCanvasViewModel _figure;
    private FigureTemplateDefinition _selectedFigureTemplate;
    private SourceAssetItemViewModel? _selectedSource;
    private BatchCropQueueItemViewModel? _selectedBatchCrop;
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
        IBatchExportFolderPicker? batchExportFolderPicker = null)
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
        ArgumentNullException.ThrowIfNull(figureTemplates);
        if (figureTemplates.Count == 0)
        {
            throw new ArgumentException("至少需要一个拼版模板。", nameof(figureTemplates));
        }

        _figureTemplates = figureTemplates.ToArray();
        AvailableTemplates = new ObservableCollection<FigureTemplateDefinition>(_figureTemplates);
        _selectedFigureTemplate = _figureTemplates[0];
        _figure = new FigureCanvasViewModel(_selectedFigureTemplate);
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
        ShowCropWorkspaceCommand = new RelayCommand(() => WorkspaceMode = WorkspaceMode.Crop);
        ShowFigureWorkspaceCommand = new RelayCommand(() => WorkspaceMode = WorkspaceMode.Figure);
        ShowInspectorTabCommand = new RelayCommand(() => IsLayersTabActive = false);
        ShowLayersTabCommand = new RelayCommand(() => IsLayersTabActive = true);
        ShowHelpCommand = new RelayCommand(() => StatusMessage = "SciCanvas v1.0.0-alpha · 快捷键：Ctrl+N/O/S/I/Enter，Ctrl+Z/Y");
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

    public AsyncRelayCommand AcceptSourceRevisionCommand { get; }

    public RelayCommand ShowCropWorkspaceCommand { get; }

    public RelayCommand ShowFigureWorkspaceCommand { get; }

    public RelayCommand ShowInspectorTabCommand { get; }

    public RelayCommand ShowLayersTabCommand { get; }
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

                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(EmptyStateVisibility));
                ExportCropCommand.NotifyCanExecuteChanged();
                AddCurrentCropToBatchQueueCommand.NotifyCanExecuteChanged();
                AcceptSourceRevisionCommand.NotifyCanExecuteChanged();
                AddCurrentCropToFigureCommand.NotifyCanExecuteChanged();
                ReplaceSelectedPanelSourceCommand.NotifyCanExecuteChanged();
                MarkDirty();
            }
        }
    }

    public bool HasSelection => SelectedSource is not null;

    public Visibility EmptyStateVisibility => HasSelection ? Visibility.Collapsed : Visibility.Visible;

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
                AcceptSourceRevisionCommand.NotifyCanExecuteChanged();
                ReplaceSelectedPanelSourceCommand.NotifyCanExecuteChanged();
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
            FigurePreflightResult preflight = FigurePreflight.Check(
                exportDocument,
                Sources.Select(item => item.Asset).ToArray(),
                IsDirty);
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
                typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "0.9.0",
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
        StatusMessage = $"正在准备 {FigureExportProfile.BuiltIns.Count} 个投稿版本…";

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

            foreach (FigureExportProfile profile in FigureExportProfile.BuiltIns)
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
                            typeof(MainWindowViewModel).Assembly.GetName().Version?.ToString() ?? "0.9.0",
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
                ? $"投稿版本导出完成 · {completed}/{FigureExportProfile.BuiltIns.Count} 项 · 主图/补充图/缩略图均未覆盖原文件"
                : $"投稿版本导出完成 · {completed}/{FigureExportProfile.BuiltIns.Count} 项成功 · 失败项已保留，原图未修改";
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
                _auditTrail);

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
            foreach (SourceAssetItemViewModel source in restoredSources)
            {
                Sources.Add(source);
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
            if (slotId is null ||
                projectTemplate.Slots.All(slot => slot.Id != slotId) ||
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
        MarkDirty();
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
                    panel.IsAspectRatioLocked))
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

            Figure.IsSnappingEnabled = snapshot.SnappingEnabled;
            Figure.SnapTolerancePixels = snapshot.SnapTolerancePixels;
            Figure.ExactSpacingPixels = snapshot.ExactSpacingPixels;
            Figure.BackgroundColor = snapshot.BackgroundColor;
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
            .SequenceEqual(after.Guides.Select(guide => guide.Id));

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
                _auditTrail);

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
