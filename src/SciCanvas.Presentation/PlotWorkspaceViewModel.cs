using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using SciCanvas.Core.Data;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public sealed class PlotWorkspaceViewModel : ObservableObject, IDisposable
{
    private TabularDataAsset? _selectedDataAsset;
    private PlotObject? _selectedPlot;
    private PlotKind _selectedPlotKind = PlotKind.Line;
    private DataColumn? _selectedXColumn;
    private DataColumn? _selectedYColumn;
    private DataColumn? _selectedValueColumn;
    private DataColumn? _selectedSymmetricErrorColumn;
    private DataColumn? _selectedLowerErrorColumn;
    private DataColumn? _selectedUpperErrorColumn;
    private PlotErrorBarMode _selectedErrorBarMode = PlotErrorBarMode.Symmetric;
    private string _plotName = string.Empty;
    private string _statusText = "选择已导入 DataAsset，设置列绑定与科学绘图样式。";
    private bool _isFilterEnabled;
    private DataColumn? _selectedFilterColumn;
    private PlotFilterOperator _selectedFilterOperator = PlotFilterOperator.GreaterThanOrEqual;
    private string _filterOperand = string.Empty;
    private string _filterExpression = "未启用 filter";
    private int _excludedRowCount;
    private PlotTransformEditorViewModel? _selectedTransform;
    private PlotTransformKind _newTransformKind = PlotTransformKind.NormalizeMinMax;
    private DataColumn? _newTransformColumn;
    private double _newTransformParameter;
    private int _newTransformWindowSize = 3;
    private PlotMovingAverageAlignment _newTransformAlignment =
        PlotMovingAverageAlignment.Centered;
    private bool _isLoadingPlot;
    private bool _disposed;
    private readonly Action<PlotObject, TabularDataAsset>? _addToFigure;
    private readonly Func<PlotObject, bool>? _canRemovePlot;

    public PlotWorkspaceViewModel(
        ObservableCollection<TabularDataAsset> dataAssets,
        ObservableCollection<PlotObject>? plots = null,
        Action<PlotObject, TabularDataAsset>? addToFigure = null,
        Func<PlotObject, bool>? canRemovePlot = null)
    {
        DataAssets = dataAssets ?? throw new ArgumentNullException(nameof(dataAssets));
        Plots = plots ?? [];
        _addToFigure = addToFigure;
        _canRemovePlot = canRemovePlot;
        XAxis = new PlotAxisEditorViewModel(PlotAxisDefinition.DefaultX);
        YAxis = new PlotAxisEditorViewModel(PlotAxisDefinition.DefaultY);
        AxisFont = new PlotTextStyleEditorViewModel(PlotTypography.Default.Axis);
        TickFont = new PlotTextStyleEditorViewModel(PlotTypography.Default.Tick);
        LegendFont = new PlotTextStyleEditorViewModel(PlotTypography.Default.Legend);
        AnnotationFont = new PlotTextStyleEditorViewModel(PlotTypography.Default.Annotation);
        SeriesStyle = new PlotSeriesStyleEditorViewModel(PlotSeriesStyle.Default);
        NewPlotCommand = new RelayCommand(BeginNewPlot);
        SavePlotCommand = new RelayCommand(() => _ = SavePlot());
        PreviewFilterCommand = new RelayCommand(PreviewFilter);
        AddTransformCommand = new RelayCommand(AddTransform);
        RemoveSelectedTransformCommand = new RelayCommand(
            RemoveSelectedTransform,
            () => SelectedTransform is not null);
        MoveTransformUpCommand = new RelayCommand(
            MoveTransformUp,
            () => SelectedTransform is not null &&
                Transforms.IndexOf(SelectedTransform) > 0);
        MoveTransformDownCommand = new RelayCommand(
            MoveTransformDown,
            () => SelectedTransform is not null &&
                Transforms.IndexOf(SelectedTransform) >= 0 &&
                Transforms.IndexOf(SelectedTransform) < Transforms.Count - 1);
        RemoveSelectedPlotCommand = new RelayCommand(
            RemoveSelectedPlot,
            () => SelectedPlot is { } selected && (_canRemovePlot?.Invoke(selected) ?? true));
        AddSelectedPlotToFigureCommand = new RelayCommand(
            AddSelectedPlotToFigure,
            () => _addToFigure is not null && SelectedPlot is not null);
        DataAssets.CollectionChanged += OnDataAssetsChanged;
        SelectedDataAsset = DataAssets.FirstOrDefault();
    }

    public event EventHandler? Changed;

    public ObservableCollection<TabularDataAsset> DataAssets { get; }

    public ObservableCollection<PlotObject> Plots { get; }

    public ObservableCollection<DataColumn> AvailableColumns { get; } = [];

    public ObservableCollection<DataColumn> NumericColumns { get; } = [];

    public ObservableCollection<PlotTransformEditorViewModel> Transforms { get; } = [];

    public IReadOnlyList<PlotKind> PlotKindChoices { get; } =
        Enum.GetValues<PlotKind>();

    public IReadOnlyList<PlotErrorBarMode> ErrorBarModeChoices { get; } =
        Enum.GetValues<PlotErrorBarMode>();

    public IReadOnlyList<PlotAxisScale> AxisScaleChoices { get; } =
        Enum.GetValues<PlotAxisScale>();

    public IReadOnlyList<PlotLineStyle> LineStyleChoices { get; } =
        Enum.GetValues<PlotLineStyle>();

    public IReadOnlyList<PlotMarkerShape> MarkerShapeChoices { get; } =
        Enum.GetValues<PlotMarkerShape>();

    public IReadOnlyList<PlotFilterOperator> FilterOperatorChoices { get; } =
        Enum.GetValues<PlotFilterOperator>();

    public IReadOnlyList<PlotTransformKind> TransformKindChoices { get; } =
        Enum.GetValues<PlotTransformKind>();

    public IReadOnlyList<PlotMovingAverageAlignment> MovingAverageAlignmentChoices { get; } =
        Enum.GetValues<PlotMovingAverageAlignment>();

    public RelayCommand NewPlotCommand { get; }

    public RelayCommand SavePlotCommand { get; }

    public RelayCommand RemoveSelectedPlotCommand { get; }

    public RelayCommand AddSelectedPlotToFigureCommand { get; }

    public RelayCommand PreviewFilterCommand { get; }

    public RelayCommand AddTransformCommand { get; }

    public RelayCommand RemoveSelectedTransformCommand { get; }

    public RelayCommand MoveTransformUpCommand { get; }

    public RelayCommand MoveTransformDownCommand { get; }

    public void RefreshFigureReferenceState() =>
        RemoveSelectedPlotCommand.NotifyCanExecuteChanged();

    public PlotAxisEditorViewModel XAxis { get; }

    public PlotAxisEditorViewModel YAxis { get; }

    public PlotTextStyleEditorViewModel AxisFont { get; }

    public PlotTextStyleEditorViewModel TickFont { get; }

    public PlotTextStyleEditorViewModel LegendFont { get; }

    public PlotTextStyleEditorViewModel AnnotationFont { get; }

    public PlotSeriesStyleEditorViewModel SeriesStyle { get; }

    public TabularDataAsset? SelectedDataAsset
    {
        get => _selectedDataAsset;
        set
        {
            if (SetProperty(ref _selectedDataAsset, value))
            {
                RefreshColumns();
                if (value is not null && SelectedPlot is null &&
                    string.IsNullOrWhiteSpace(PlotName))
                {
                    PlotName = $"{SelectedPlotKind} · {value.Name}";
                }

                OnPropertyChanged(nameof(SelectedDataAssetSummary));
            }
        }
    }

    public PlotObject? SelectedPlot
    {
        get => _selectedPlot;
        set
        {
            if (SetProperty(ref _selectedPlot, value))
            {
                RemoveSelectedPlotCommand.NotifyCanExecuteChanged();
                AddSelectedPlotToFigureCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(IsEditingExistingPlot));
                OnPropertyChanged(nameof(SaveActionLabel));
                if (value is not null && !_isLoadingPlot)
                {
                    LoadPlot(value);
                }
            }
        }
    }

    public PlotKind SelectedPlotKind
    {
        get => _selectedPlotKind;
        set
        {
            if (SetProperty(ref _selectedPlotKind, value))
            {
                ApplyPlotKindDefaults(value);
                NotifyPlotKindPropertiesChanged();
            }
        }
    }

    public DataColumn? SelectedXColumn
    {
        get => _selectedXColumn;
        set => SetProperty(ref _selectedXColumn, value);
    }

    public DataColumn? SelectedYColumn
    {
        get => _selectedYColumn;
        set => SetProperty(ref _selectedYColumn, value);
    }

    public DataColumn? SelectedValueColumn
    {
        get => _selectedValueColumn;
        set => SetProperty(ref _selectedValueColumn, value);
    }

    public DataColumn? SelectedSymmetricErrorColumn
    {
        get => _selectedSymmetricErrorColumn;
        set => SetProperty(ref _selectedSymmetricErrorColumn, value);
    }

    public DataColumn? SelectedLowerErrorColumn
    {
        get => _selectedLowerErrorColumn;
        set => SetProperty(ref _selectedLowerErrorColumn, value);
    }

    public DataColumn? SelectedUpperErrorColumn
    {
        get => _selectedUpperErrorColumn;
        set => SetProperty(ref _selectedUpperErrorColumn, value);
    }

    public PlotErrorBarMode SelectedErrorBarMode
    {
        get => _selectedErrorBarMode;
        set
        {
            if (SetProperty(ref _selectedErrorBarMode, value))
            {
                OnPropertyChanged(nameof(UsesSymmetricErrorColumn));
                OnPropertyChanged(nameof(UsesAsymmetricErrorColumns));
            }
        }
    }

    public string PlotName
    {
        get => _plotName;
        set => SetProperty(ref _plotName, value ?? string.Empty);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsFilterEnabled
    {
        get => _isFilterEnabled;
        set
        {
            if (SetProperty(ref _isFilterEnabled, value))
            {
                OnPropertyChanged(nameof(FilterSummary));
                if (!value)
                {
                    FilterExpression = "未启用 filter";
                    ExcludedRowCount = 0;
                }
            }
        }
    }

    public DataColumn? SelectedFilterColumn
    {
        get => _selectedFilterColumn;
        set => SetProperty(ref _selectedFilterColumn, value);
    }

    public PlotFilterOperator SelectedFilterOperator
    {
        get => _selectedFilterOperator;
        set
        {
            if (SetProperty(ref _selectedFilterOperator, value))
            {
                OnPropertyChanged(nameof(FilterNeedsOperand));
            }
        }
    }

    public string FilterOperand
    {
        get => _filterOperand;
        set => SetProperty(ref _filterOperand, value ?? string.Empty);
    }

    public string FilterExpression
    {
        get => _filterExpression;
        private set
        {
            if (SetProperty(ref _filterExpression, value))
            {
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public int ExcludedRowCount
    {
        get => _excludedRowCount;
        private set
        {
            if (SetProperty(ref _excludedRowCount, value))
            {
                OnPropertyChanged(nameof(FilterSummary));
            }
        }
    }

    public bool FilterNeedsOperand =>
        SelectedFilterOperator is not (
            PlotFilterOperator.IsMissing or PlotFilterOperator.IsNotMissing);

    public string FilterSummary => IsFilterEnabled
        ? $"{FilterExpression} · excluded {ExcludedRowCount:N0}"
        : "未启用 filter · excluded 0";

    public PlotTransformEditorViewModel? SelectedTransform
    {
        get => _selectedTransform;
        set
        {
            if (SetProperty(ref _selectedTransform, value))
            {
                RemoveSelectedTransformCommand.NotifyCanExecuteChanged();
                MoveTransformUpCommand.NotifyCanExecuteChanged();
                MoveTransformDownCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public PlotTransformKind NewTransformKind
    {
        get => _newTransformKind;
        set
        {
            if (SetProperty(ref _newTransformKind, value))
            {
                OnPropertyChanged(nameof(NewTransformNeedsParameter));
                OnPropertyChanged(nameof(NewTransformNeedsWindow));
            }
        }
    }

    public DataColumn? NewTransformColumn
    {
        get => _newTransformColumn;
        set => SetProperty(ref _newTransformColumn, value);
    }

    public double NewTransformParameter
    {
        get => _newTransformParameter;
        set => SetProperty(ref _newTransformParameter, value);
    }

    public int NewTransformWindowSize
    {
        get => _newTransformWindowSize;
        set => SetProperty(ref _newTransformWindowSize, value);
    }

    public PlotMovingAverageAlignment NewTransformAlignment
    {
        get => _newTransformAlignment;
        set => SetProperty(ref _newTransformAlignment, value);
    }

    public bool NewTransformNeedsParameter =>
        NewTransformKind == PlotTransformKind.Offset;

    public bool NewTransformNeedsWindow =>
        NewTransformKind == PlotTransformKind.MovingAverage;

    public bool IsEditingExistingPlot => SelectedPlot is not null;

    public string SaveActionLabel => IsEditingExistingPlot ? "更新 Plot" : "创建 Plot";

    public bool UsesXColumn =>
        SelectedPlotKind is PlotKind.Line or PlotKind.Scatter or
        PlotKind.LineAndSymbol or PlotKind.ErrorBar or PlotKind.Heatmap;

    public bool UsesCategoryColumn =>
        SelectedPlotKind == PlotKind.BoxPlot;

    public bool UsesErrorBars =>
        SelectedPlotKind == PlotKind.ErrorBar;

    public bool UsesValueColumn =>
        SelectedPlotKind == PlotKind.Heatmap;

    public bool UsesSymmetricErrorColumn =>
        UsesErrorBars && SelectedErrorBarMode == PlotErrorBarMode.Symmetric;

    public bool UsesAsymmetricErrorColumns =>
        UsesErrorBars && SelectedErrorBarMode == PlotErrorBarMode.Asymmetric;

    public string SelectedDataAssetSummary => SelectedDataAsset is null
        ? "尚无可绘制 DataAsset"
        : $"{SelectedDataAsset.Name} · revision {SelectedDataAsset.SourceRevision} · " +
          $"{SelectedDataAsset.Columns.Count} 列 × {SelectedDataAsset.Rows.Count:N0} 行";

    public void BeginNewPlot()
    {
        _isLoadingPlot = true;
        try
        {
            SelectedPlot = null;
            SelectedPlotKind = PlotKind.Line;
            PlotName = SelectedDataAsset is null
                ? "New plot"
                : $"Line · {SelectedDataAsset.Name}";
            XAxis.Load(PlotAxisDefinition.DefaultX);
            YAxis.Load(PlotAxisDefinition.DefaultY);
            AxisFont.Load(PlotTypography.Default.Axis);
            TickFont.Load(PlotTypography.Default.Tick);
            LegendFont.Load(PlotTypography.Default.Legend);
            AnnotationFont.Load(PlotTypography.Default.Annotation);
            SeriesStyle.Load(PlotSeriesStyle.Default);
            IsFilterEnabled = false;
            SelectedFilterOperator = PlotFilterOperator.GreaterThanOrEqual;
            FilterOperand = string.Empty;
            FilterExpression = "未启用 filter";
            ExcludedRowCount = 0;
            Transforms.Clear();
            SelectedTransform = null;
            RefreshColumns();
        }
        finally
        {
            _isLoadingPlot = false;
        }

        OnPropertyChanged(nameof(IsEditingExistingPlot));
        OnPropertyChanged(nameof(SaveActionLabel));
        StatusText = "新 Plot 草稿已就绪；保存前不会写入工程。";
    }

    public PlotObject? SavePlot()
    {
        if (SelectedDataAsset is not { } asset)
        {
            StatusText = "无法创建 Plot：请先导入并选择 DataAsset。";
            return null;
        }

        try
        {
            PlotObject plot = CreateModel(asset);
            int existingIndex = SelectedPlot is null
                ? -1
                : Plots.IndexOf(SelectedPlot);
            if (existingIndex >= 0)
            {
                Plots[existingIndex] = plot;
                StatusText = $"已更新 {plot.Name}；数据仍绑定 revision {plot.Data.SourceRevision}。";
            }
            else
            {
                Plots.Add(plot);
                StatusText = $"已创建 {plot.Name}；原始 DataAsset 与列未修改。";
            }

            _isLoadingPlot = true;
            SelectedPlot = plot;
            _isLoadingPlot = false;
            Changed?.Invoke(this, EventArgs.Empty);
            return plot;
        }
        catch (Exception exception) when (exception is
            InvalidDataException or InvalidOperationException or ArgumentException)
        {
            _isLoadingPlot = false;
            StatusText = $"Plot 校验失败：{exception.Message}";
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        DataAssets.CollectionChanged -= OnDataAssetsChanged;
        _disposed = true;
    }

    private PlotObject CreateModel(TabularDataAsset asset)
    {
        Guid? xColumnId = SelectedPlotKind switch
        {
            PlotKind.Histogram => null,
            _ => SelectedXColumn?.Id,
        };
        PlotErrorBarBinding? errorBars = SelectedPlotKind == PlotKind.ErrorBar
            ? CreateErrorBarBinding()
            : null;
        Guid? valueColumnId = SelectedPlotKind == PlotKind.Heatmap
            ? SelectedValueColumn?.Id
            : null;
        var plot = new PlotObject
        {
            Id = SelectedPlot?.Id ?? Guid.NewGuid(),
            Name = PlotName.Trim(),
            PlotType = SelectedPlotKind,
            Data = new PlotDataBinding(
                asset.Id,
                asset.SourceRevision,
                xColumnId,
                SelectedYColumn?.Id ?? Guid.Empty,
                errorBars,
                valueColumnId),
            XAxis = XAxis.CreateModel(),
            YAxis = YAxis.CreateModel(),
            Typography = new PlotTypography(
                AxisFont.CreateModel(),
                TickFont.CreateModel(),
                LegendFont.CreateModel(),
                AnnotationFont.CreateModel()),
            Style = SeriesStyle.CreateModel(),
            Filter = CreateFilter(asset),
            Transforms = Transforms
                .Select(transform => transform.CreateModel())
                .ToArray(),
        };
        PlotObject valid = plot.EnsureValid(asset);
        if (valid.Filter is { } filter)
        {
            FilterExpression = filter.Expression;
            ExcludedRowCount = filter.ExcludedRowCount;
        }

        return valid;
    }

    private PlotErrorBarBinding CreateErrorBarBinding() =>
        SelectedErrorBarMode switch
        {
            PlotErrorBarMode.Symmetric => new PlotErrorBarBinding(
                PlotErrorBarMode.Symmetric,
                SymmetricColumnId: SelectedSymmetricErrorColumn?.Id),
            PlotErrorBarMode.Asymmetric => new PlotErrorBarBinding(
                PlotErrorBarMode.Asymmetric,
                LowerColumnId: SelectedLowerErrorColumn?.Id,
                UpperColumnId: SelectedUpperErrorColumn?.Id),
            _ => throw new InvalidDataException("未知误差列模式。"),
        };

    private void LoadPlot(PlotObject plot)
    {
        TabularDataAsset? asset = DataAssets.FirstOrDefault(
            candidate => candidate.Id == plot.Data.DataAssetId);
        if (asset is null)
        {
            StatusText = $"Plot {plot.Name} 引用的 DataAsset 当前不可用。";
            return;
        }

        _isLoadingPlot = true;
        try
        {
            SelectedDataAsset = asset;
            SelectedPlotKind = plot.PlotType;
            PlotName = plot.Name;
            SelectedXColumn = FindColumn(plot.Data.XColumnId);
            SelectedYColumn = FindColumn(plot.Data.YColumnId);
            SelectedValueColumn = FindColumn(plot.Data.ValueColumnId);
            if (plot.Data.ErrorBars is { } errors)
            {
                SelectedErrorBarMode = errors.Mode;
                SelectedSymmetricErrorColumn = FindColumn(errors.SymmetricColumnId);
                SelectedLowerErrorColumn = FindColumn(errors.LowerColumnId);
                SelectedUpperErrorColumn = FindColumn(errors.UpperColumnId);
            }

            XAxis.Load(plot.XAxis);
            YAxis.Load(plot.YAxis);
            AxisFont.Load(plot.Typography.Axis);
            TickFont.Load(plot.Typography.Tick);
            LegendFont.Load(plot.Typography.Legend);
            AnnotationFont.Load(plot.Typography.Annotation);
            SeriesStyle.Load(plot.Style);
            if (plot.Filter is { } filter)
            {
                IsFilterEnabled = true;
                SelectedFilterColumn = FindColumn(filter.ColumnId);
                SelectedFilterOperator = filter.Operator;
                FilterOperand = filter.Operand ?? string.Empty;
                FilterExpression = filter.Expression;
                ExcludedRowCount = filter.ExcludedRowCount;
            }
            else
            {
                IsFilterEnabled = false;
            }

            Transforms.Clear();
            foreach (PlotDataTransform transform in plot.Transforms)
            {
                DataColumn? column = FindColumn(transform.ColumnId);
                if (column is null)
                {
                    throw new InvalidDataException("Plot transform column 当前不可用。");
                }

                Transforms.Add(new PlotTransformEditorViewModel(column, transform));
            }
            SelectedTransform = Transforms.FirstOrDefault();
            StatusText = $"正在编辑 {plot.Name} · revision {plot.Data.SourceRevision}";
        }
        finally
        {
            _isLoadingPlot = false;
        }
    }

    private DataColumn? FindColumn(Guid? id) =>
        id is { } columnId
            ? AvailableColumns.FirstOrDefault(column => column.Id == columnId)
            : null;

    private void RefreshColumns()
    {
        AvailableColumns.Clear();
        NumericColumns.Clear();
        if (SelectedDataAsset is not { } asset)
        {
            SelectedXColumn = null;
            SelectedYColumn = null;
            SelectedValueColumn = null;
            SelectedSymmetricErrorColumn = null;
            SelectedLowerErrorColumn = null;
            SelectedUpperErrorColumn = null;
            return;
        }

        foreach (DataColumn column in asset.Columns)
        {
            AvailableColumns.Add(column);
            if (column.DataType == TabularDataType.Numeric)
            {
                NumericColumns.Add(column);
            }
        }

        SelectedXColumn = KeepOrDefault(
            SelectedXColumn,
            asset.Columns.FirstOrDefault(column => column.Role == DataColumnRole.X) ??
            NumericColumns.FirstOrDefault());
        SelectedYColumn = KeepOrDefault(
            SelectedYColumn,
            asset.Columns.FirstOrDefault(column => column.Role == DataColumnRole.Y) ??
            NumericColumns.FirstOrDefault(column => column.Id != SelectedXColumn?.Id) ??
            NumericColumns.FirstOrDefault());
        SelectedValueColumn = KeepOrDefault(
            SelectedValueColumn,
            NumericColumns.FirstOrDefault(column =>
                column.Id != SelectedXColumn?.Id && column.Id != SelectedYColumn?.Id));
        DataColumn? defaultError = asset.Columns.FirstOrDefault(
            column => column.Role == DataColumnRole.YError);
        SelectedSymmetricErrorColumn = KeepOrDefault(
            SelectedSymmetricErrorColumn,
            defaultError);
        SelectedLowerErrorColumn = KeepOrDefault(
            SelectedLowerErrorColumn,
            defaultError);
        SelectedUpperErrorColumn = KeepOrDefault(
            SelectedUpperErrorColumn,
            asset.Columns.FirstOrDefault(column =>
                column.Role == DataColumnRole.YError && column.Id != defaultError?.Id) ??
            defaultError);
        SelectedFilterColumn = KeepOrDefault(
            SelectedFilterColumn,
            AvailableColumns.FirstOrDefault());
        NewTransformColumn = KeepOrDefault(
            NewTransformColumn,
            SelectedYColumn ?? NumericColumns.FirstOrDefault());
        ApplyAxisMetadata();
    }

    private DataColumn? KeepOrDefault(DataColumn? current, DataColumn? fallback) =>
        current is not null && AvailableColumns.Any(column => column.Id == current.Id)
            ? AvailableColumns.First(column => column.Id == current.Id)
            : fallback;

    private void ApplyAxisMetadata()
    {
        if (_isLoadingPlot)
        {
            return;
        }

        if (SelectedXColumn is { } x)
        {
            XAxis.Title = x.Name;
            XAxis.Unit = x.Unit ?? string.Empty;
        }

        if (SelectedYColumn is { } y)
        {
            YAxis.Title = y.Name;
            YAxis.Unit = y.Unit ?? string.Empty;
        }
    }

    private void ApplyPlotKindDefaults(PlotKind kind)
    {
        if (_isLoadingPlot)
        {
            return;
        }

        SeriesStyle.MarkerShape = kind switch
        {
            PlotKind.Line or PlotKind.Histogram or PlotKind.BoxPlot or PlotKind.Heatmap =>
                PlotMarkerShape.None,
            _ => PlotMarkerShape.Circle,
        };
        if (SelectedDataAsset is { } asset &&
            (string.IsNullOrWhiteSpace(PlotName) ||
             PlotName.StartsWith("Line · ", StringComparison.Ordinal)))
        {
            PlotName = $"{kind} · {asset.Name}";
        }
    }

    private void NotifyPlotKindPropertiesChanged()
    {
        OnPropertyChanged(nameof(UsesXColumn));
        OnPropertyChanged(nameof(UsesCategoryColumn));
        OnPropertyChanged(nameof(UsesErrorBars));
        OnPropertyChanged(nameof(UsesValueColumn));
        OnPropertyChanged(nameof(UsesSymmetricErrorColumn));
        OnPropertyChanged(nameof(UsesAsymmetricErrorColumns));
    }

    private void RemoveSelectedPlot()
    {
        if (SelectedPlot is not { } selected || !Plots.Remove(selected))
        {
            return;
        }

        SelectedPlot = Plots.FirstOrDefault();
        Changed?.Invoke(this, EventArgs.Empty);
        StatusText = $"已移除 {selected.Name}；原始 DataAsset 未修改。";
    }

    private void AddSelectedPlotToFigure()
    {
        if (_addToFigure is null || SelectedPlot is not { } plot)
        {
            return;
        }

        TabularDataAsset dataAsset = DataAssets.SingleOrDefault(asset => asset.Id == plot.Data.DataAssetId)
            ?? throw new InvalidOperationException($"Plot {plot.Name} 引用的 DataAsset 不存在。");
        plot.EnsureValid(dataAsset);
        _addToFigure(plot, dataAsset);
        StatusText = $"已将 Plot “{plot.Name}”作为矢量面板添加到 Figure。";
        RemoveSelectedPlotCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private PlotDataFilter? CreateFilter(TabularDataAsset asset)
    {
        if (!IsFilterEnabled)
        {
            return null;
        }

        if (SelectedFilterColumn is null)
        {
            throw new InvalidDataException("启用 filter 时必须选择来源列。");
        }

        return PlotDataFilter.Create(
            asset,
            SelectedFilterColumn.Id,
            SelectedFilterOperator,
            FilterNeedsOperand ? FilterOperand : null);
    }

    private void PreviewFilter()
    {
        if (SelectedDataAsset is not { } asset)
        {
            StatusText = "无法预览 filter：请先选择 DataAsset。";
            return;
        }

        try
        {
            PlotDataFilter? filter = CreateFilter(asset);
            FilterExpression = filter?.Expression ?? "未启用 filter";
            ExcludedRowCount = filter?.ExcludedRowCount ?? 0;
            StatusText = filter is null
                ? "Filter 已关闭 · excluded 0"
                : $"Filter 预览完成 · excluded {filter.ExcludedRowCount:N0}/{asset.Rows.Count:N0}";
        }
        catch (Exception exception) when (exception is
            InvalidDataException or ArgumentException)
        {
            StatusText = $"Filter 校验失败：{exception.Message}";
        }
    }

    private void AddTransform()
    {
        if (NewTransformColumn is null)
        {
            StatusText = "无法添加 transform：请选择 Plot 绑定的数值列。";
            return;
        }

        PlotDataTransform model = NewTransformKind switch
        {
            PlotTransformKind.Offset => new PlotDataTransform(
                NewTransformColumn.Id,
                NewTransformKind,
                Parameter: NewTransformParameter),
            PlotTransformKind.MovingAverage => new PlotDataTransform(
                NewTransformColumn.Id,
                NewTransformKind,
                WindowSize: NewTransformWindowSize,
                Alignment: NewTransformAlignment),
            _ => new PlotDataTransform(NewTransformColumn.Id, NewTransformKind),
        };
        var editor = new PlotTransformEditorViewModel(NewTransformColumn, model);
        Transforms.Add(editor);
        SelectedTransform = editor;
        NotifyTransformCommands();
        StatusText = $"已添加 transform #{Transforms.Count} · 保存 Plot 时按列表顺序执行。";
    }

    private void RemoveSelectedTransform()
    {
        if (SelectedTransform is not { } selected || !Transforms.Remove(selected))
        {
            return;
        }

        SelectedTransform = Transforms.FirstOrDefault();
        NotifyTransformCommands();
        StatusText = "已移除 transform；原始 DataAsset 未修改。";
    }

    private void MoveTransformUp()
    {
        int index = SelectedTransform is null ? -1 : Transforms.IndexOf(SelectedTransform);
        if (index <= 0)
        {
            return;
        }

        Transforms.Move(index, index - 1);
        NotifyTransformCommands();
        StatusText = "Transform 顺序已更新；provenance 将保存该执行顺序。";
    }

    private void MoveTransformDown()
    {
        int index = SelectedTransform is null ? -1 : Transforms.IndexOf(SelectedTransform);
        if (index < 0 || index >= Transforms.Count - 1)
        {
            return;
        }

        Transforms.Move(index, index + 1);
        NotifyTransformCommands();
        StatusText = "Transform 顺序已更新；provenance 将保存该执行顺序。";
    }

    private void NotifyTransformCommands()
    {
        RemoveSelectedTransformCommand.NotifyCanExecuteChanged();
        MoveTransformUpCommand.NotifyCanExecuteChanged();
        MoveTransformDownCommand.NotifyCanExecuteChanged();
    }

    private void OnDataAssetsChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (SelectedDataAsset is null ||
            !DataAssets.Any(asset => asset.Id == SelectedDataAsset.Id))
        {
            SelectedDataAsset = DataAssets.FirstOrDefault();
        }
    }
}

public sealed class PlotTransformEditorViewModel : ObservableObject
{
    private double? _parameter;
    private int? _windowSize;
    private PlotMovingAverageAlignment? _alignment;

    public PlotTransformEditorViewModel(
        DataColumn column,
        PlotDataTransform transform)
    {
        Column = column ?? throw new ArgumentNullException(nameof(column));
        Kind = transform.Kind;
        _parameter = transform.Parameter;
        _windowSize = transform.WindowSize;
        _alignment = transform.Alignment;
    }

    public DataColumn Column { get; }

    public PlotTransformKind Kind { get; }

    public double? Parameter
    {
        get => _parameter;
        set
        {
            if (SetProperty(ref _parameter, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public int? WindowSize
    {
        get => _windowSize;
        set
        {
            if (SetProperty(ref _windowSize, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public PlotMovingAverageAlignment? Alignment
    {
        get => _alignment;
        set
        {
            if (SetProperty(ref _alignment, value))
            {
                OnPropertyChanged(nameof(Summary));
            }
        }
    }

    public string Summary => Kind switch
    {
        PlotTransformKind.Offset => $"{Column.Name} · offset {Parameter}",
        PlotTransformKind.MovingAverage =>
            $"{Column.Name} · moving average · window {WindowSize} · {Alignment}",
        PlotTransformKind.NormalizeMinMax => $"{Column.Name} · normalize min–max",
        PlotTransformKind.Log10 => $"{Column.Name} · log10",
        _ => $"{Column.Name} · {Kind}",
    };

    public PlotDataTransform CreateModel() =>
        new(Column.Id, Kind, Parameter, WindowSize, Alignment);
}

public sealed class PlotAxisEditorViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _unit = string.Empty;
    private PlotAxisScale _scale;
    private double? _minimum;
    private double? _maximum;
    private double? _majorTickInterval;
    private int _minorTickCount;

    public PlotAxisEditorViewModel(PlotAxisDefinition model) => Load(model);

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? string.Empty);
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value ?? string.Empty);
    }

    public PlotAxisScale Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public double? Minimum
    {
        get => _minimum;
        set => SetProperty(ref _minimum, value);
    }

    public double? Maximum
    {
        get => _maximum;
        set => SetProperty(ref _maximum, value);
    }

    public double? MajorTickInterval
    {
        get => _majorTickInterval;
        set => SetProperty(ref _majorTickInterval, value);
    }

    public int MinorTickCount
    {
        get => _minorTickCount;
        set => SetProperty(ref _minorTickCount, value);
    }

    public void Load(PlotAxisDefinition model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Title = model.Title;
        Unit = model.Unit ?? string.Empty;
        Scale = model.Scale;
        Minimum = model.Minimum;
        Maximum = model.Maximum;
        MajorTickInterval = model.MajorTickInterval;
        MinorTickCount = model.MinorTickCount;
    }

    public PlotAxisDefinition CreateModel() => new(
        Title.Trim(),
        string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
        Scale,
        Minimum,
        Maximum,
        MajorTickInterval,
        MinorTickCount);
}

public sealed class PlotTextStyleEditorViewModel : ObservableObject
{
    private string _fontFamily = "Arial";
    private double _fontSizePt = 8;
    private string _color = "#FF111111";
    private bool _isBold;

    public PlotTextStyleEditorViewModel(TextStyle model) => Load(model);

    public string FontFamily
    {
        get => _fontFamily;
        set => SetProperty(ref _fontFamily, value ?? string.Empty);
    }

    public double FontSizePt
    {
        get => _fontSizePt;
        set => SetProperty(ref _fontSizePt, value);
    }

    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, value ?? string.Empty);
    }

    public bool IsBold
    {
        get => _isBold;
        set => SetProperty(ref _isBold, value);
    }

    public void Load(TextStyle model)
    {
        ArgumentNullException.ThrowIfNull(model);
        FontFamily = model.FontFamily;
        FontSizePt = model.FontSizePt;
        Color = model.Color;
        IsBold = model.IsBold;
    }

    public TextStyle CreateModel() =>
        new(FontFamily.Trim(), FontSizePt, IsBold, Color.Trim());
}

public sealed class PlotSeriesStyleEditorViewModel : ObservableObject
{
    private string _lineColor = "#FF2563EB";
    private double _lineWidthPt = 1.25;
    private PlotLineStyle _lineStyle;
    private PlotMarkerShape _markerShape;
    private double _markerSizePt = 5;
    private string _markerFill = "#FFFFFFFF";
    private string _markerStroke = "#FF2563EB";

    public PlotSeriesStyleEditorViewModel(PlotSeriesStyle model) => Load(model);

    public string LineColor
    {
        get => _lineColor;
        set => SetProperty(ref _lineColor, value ?? string.Empty);
    }

    public double LineWidthPt
    {
        get => _lineWidthPt;
        set => SetProperty(ref _lineWidthPt, value);
    }

    public PlotLineStyle LineStyle
    {
        get => _lineStyle;
        set => SetProperty(ref _lineStyle, value);
    }

    public PlotMarkerShape MarkerShape
    {
        get => _markerShape;
        set => SetProperty(ref _markerShape, value);
    }

    public double MarkerSizePt
    {
        get => _markerSizePt;
        set => SetProperty(ref _markerSizePt, value);
    }

    public string MarkerFill
    {
        get => _markerFill;
        set => SetProperty(ref _markerFill, value ?? string.Empty);
    }

    public string MarkerStroke
    {
        get => _markerStroke;
        set => SetProperty(ref _markerStroke, value ?? string.Empty);
    }

    public void Load(PlotSeriesStyle model)
    {
        ArgumentNullException.ThrowIfNull(model);
        LineColor = model.LineColor;
        LineWidthPt = model.LineWidthPt;
        LineStyle = model.LineStyle;
        MarkerShape = model.MarkerShape;
        MarkerSizePt = model.MarkerSizePt;
        MarkerFill = model.MarkerFill;
        MarkerStroke = model.MarkerStroke;
    }

    public PlotSeriesStyle CreateModel() => new(
        LineColor.Trim(),
        LineWidthPt,
        LineStyle,
        MarkerShape,
        MarkerSizePt,
        MarkerFill.Trim(),
        MarkerStroke.Trim());
}
