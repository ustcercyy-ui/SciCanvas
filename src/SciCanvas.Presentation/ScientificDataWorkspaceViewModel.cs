using System.Collections.ObjectModel;
using System.IO;
using SciCanvas.Core.Data;

namespace SciCanvas.Presentation;

public interface ITabularDataFilePicker
{
    string? PickTabularDataFile();
}

public sealed class NullTabularDataFilePicker : ITabularDataFilePicker
{
    public string? PickTabularDataFile() => null;
}

public sealed class ScientificDataWorkspaceViewModel : ObservableObject
{
    private readonly ITabularDataImporter _importer;
    private readonly ITabularDataFilePicker _filePicker;
    private readonly Func<TabularDataAsset, bool> _canRemoveAsset;
    private TabularDataImportPreview? _preview;
    private TabularDataAsset? _selectedAsset;
    private string? _sourcePath;
    private string? _selectedSheetName;
    private string _selectedRange = string.Empty;
    private int _headerRow = 1;
    private string _assetName = string.Empty;
    private string _statusText = "选择 CSV、TSV 或 XLSX；确认前只显示预览，不创建数据资产。";
    private bool _isBusy;

    public ScientificDataWorkspaceViewModel(
        ObservableCollection<TabularDataAsset> assets,
        ITabularDataImporter importer,
        ITabularDataFilePicker? filePicker = null,
        Func<TabularDataAsset, bool>? canRemoveAsset = null)
    {
        Assets = assets ?? throw new ArgumentNullException(nameof(assets));
        _importer = importer ?? throw new ArgumentNullException(nameof(importer));
        _filePicker = filePicker ?? new NullTabularDataFilePicker();
        _canRemoveAsset = canRemoveAsset ?? (_ => true);
        SelectFileCommand = new AsyncRelayCommand(
            SelectFileAsync,
            () => !IsBusy,
            HandleError);
        RefreshPreviewCommand = new AsyncRelayCommand(
            RefreshPreviewAsync,
            () => SourcePath is not null && !IsBusy,
            HandleError);
        ConfirmImportCommand = new AsyncRelayCommand(
            async () => _ = await ConfirmImportAsync(),
            () => _preview is not null && PreviewColumns.Count > 0 && !IsBusy,
            HandleError);
        RemoveSelectedAssetCommand = new RelayCommand(
            RemoveSelectedAsset,
            () => SelectedAsset is not null && !IsBusy);
    }

    public ObservableCollection<TabularDataAsset> Assets { get; }

    public ObservableCollection<string> AvailableSheets { get; } = [];

    public ObservableCollection<DataColumnImportViewModel> PreviewColumns { get; } = [];

    public ObservableCollection<TabularPreviewRowViewModel> PreviewRows { get; } = [];

    public AsyncRelayCommand SelectFileCommand { get; }

    public AsyncRelayCommand RefreshPreviewCommand { get; }

    public AsyncRelayCommand ConfirmImportCommand { get; }

    public RelayCommand RemoveSelectedAssetCommand { get; }

    public IReadOnlyList<TabularDataType> DataTypeChoices { get; } =
        Enum.GetValues<TabularDataType>();

    public IReadOnlyList<DataColumnRole?> RoleChoices { get; } =
        [null, .. Enum.GetValues<DataColumnRole>().Cast<DataColumnRole?>()];

    public TabularDataAsset? SelectedAsset
    {
        get => _selectedAsset;
        set
        {
            if (SetProperty(ref _selectedAsset, value))
            {
                RemoveSelectedAssetCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedAssetSummary));
            }
        }
    }

    public string? SourcePath
    {
        get => _sourcePath;
        private set
        {
            if (SetProperty(ref _sourcePath, value))
            {
                OnPropertyChanged(nameof(SourceFileName));
                RefreshPreviewCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string SourceFileName => SourcePath is null
        ? "尚未选择文件"
        : Path.GetFileName(SourcePath);

    public string? SelectedSheetName
    {
        get => _selectedSheetName;
        set => SetProperty(ref _selectedSheetName, value);
    }

    public string SelectedRange
    {
        get => _selectedRange;
        set => SetProperty(ref _selectedRange, value ?? string.Empty);
    }

    public int HeaderRow
    {
        get => _headerRow;
        set => SetProperty(ref _headerRow, Math.Max(1, value));
    }

    public string AssetName
    {
        get => _assetName;
        set => SetProperty(ref _assetName, value ?? string.Empty);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SelectFileCommand.NotifyCanExecuteChanged();
                RefreshPreviewCommand.NotifyCanExecuteChanged();
                ConfirmImportCommand.NotifyCanExecuteChanged();
                RemoveSelectedAssetCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool IsPreviewReady => _preview is not null;

    public string PreviewSummary => _preview is null
        ? "等待预览"
        : $"{_preview.Format} · {_preview.SuggestedColumns.Count} 列 · {_preview.TotalDataRowCount:N0} 行 · 前 {_preview.FirstRows.Count} 行";

    public string SelectedAssetSummary => SelectedAsset is null
        ? "未选择已导入资产"
        : $"{SelectedAsset.Columns.Count} 列 · {SelectedAsset.Rows.Count:N0} 行 · revision {SelectedAsset.SourceRevision}";

    public async Task PreviewFileAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        SourcePath = Path.GetFullPath(sourcePath);
        SelectedSheetName = null;
        SelectedRange = string.Empty;
        HeaderRow = 1;
        AssetName = Path.GetFileNameWithoutExtension(SourcePath);
        AvailableSheets.Clear();
        if (string.Equals(Path.GetExtension(SourcePath), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<string> sheets = await _importer.DiscoverSheetsAsync(
                SourcePath,
                cancellationToken);
            foreach (string sheet in sheets)
            {
                AvailableSheets.Add(sheet);
            }

            SelectedSheetName = AvailableSheets.FirstOrDefault();
        }

        await RefreshPreviewAsync(cancellationToken);
    }

    public Task RefreshPreviewAsync() => RefreshPreviewAsync(CancellationToken.None);

    public async Task RefreshPreviewAsync(CancellationToken cancellationToken)
    {
        if (SourcePath is null)
        {
            return;
        }

        IsBusy = true;
        StatusText = "正在只读解析并生成预览…";
        try
        {
            bool isXlsx = string.Equals(
                Path.GetExtension(SourcePath),
                ".xlsx",
                StringComparison.OrdinalIgnoreCase);
            var options = new TabularDataImportOptions
            {
                SheetName = isXlsx && !string.IsNullOrWhiteSpace(SelectedSheetName)
                    ? SelectedSheetName
                    : null,
                SelectedRange = isXlsx && !string.IsNullOrWhiteSpace(SelectedRange)
                    ? SelectedRange
                    : null,
                HeaderRow = HeaderRow,
                PreviewRowCount = 20,
                InferenceRowCount = 1000,
            };
            TabularDataImportPreview preview = await _importer.PreviewAsync(
                SourcePath,
                options,
                cancellationToken);
            ApplyPreview(preview);
            StatusText = "预览完成 · 请复核列类型、单位与角色，确认后才会创建 DataAsset。";
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentException)
        {
            ClearPreview();
            StatusText = $"预览失败：{exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<TabularDataAsset?> ConfirmImportAsync(
        CancellationToken cancellationToken = default)
    {
        if (_preview is null)
        {
            return null;
        }

        IsBusy = true;
        StatusText = "正在重新验证来源指纹并确认导入…";
        try
        {
            DataColumn[] columns = PreviewColumns
                .Select(column => column.CreateModel())
                .ToArray();
            TabularDataAsset asset = await _importer.ImportAsync(
                _preview,
                new TabularDataImportConfirmation(AssetName, columns),
                cancellationToken);
            Assets.Add(asset);
            SelectedAsset = asset;
            StatusText = $"已导入 {asset.Name} · {asset.Columns.Count} 列 · {asset.Rows.Count:N0} 行 · 来源保持只读";
            return asset;
        }
        catch (Exception exception) when (exception is
            IOException or UnauthorizedAccessException or InvalidDataException or
            NotSupportedException or ArgumentException)
        {
            StatusText = $"确认导入失败：{exception.Message}";
            return null;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SelectFileAsync()
    {
        string? path = _filePicker.PickTabularDataFile();
        if (path is not null)
        {
            await PreviewFileAsync(path);
        }
    }

    private void ApplyPreview(TabularDataImportPreview preview)
    {
        _preview = preview;
        AvailableSheets.Clear();
        foreach (string sheet in preview.AvailableSheets)
        {
            AvailableSheets.Add(sheet);
        }

        SelectedSheetName = preview.SelectedSheetName;
        SelectedRange = preview.SelectedRange ?? string.Empty;
        PreviewColumns.Clear();
        foreach (DataColumn column in preview.SuggestedColumns)
        {
            PreviewColumns.Add(new DataColumnImportViewModel(column));
        }

        PreviewRows.Clear();
        for (int index = 0; index < preview.FirstRows.Count; index++)
        {
            PreviewRows.Add(new TabularPreviewRowViewModel(
                index + 1,
                preview.FirstRows[index].Values.Select(value => value.RawText ?? "∅").ToArray()));
        }

        OnPropertyChanged(nameof(IsPreviewReady));
        OnPropertyChanged(nameof(PreviewSummary));
        ConfirmImportCommand.NotifyCanExecuteChanged();
    }

    private void ClearPreview()
    {
        _preview = null;
        PreviewColumns.Clear();
        PreviewRows.Clear();
        OnPropertyChanged(nameof(IsPreviewReady));
        OnPropertyChanged(nameof(PreviewSummary));
        ConfirmImportCommand.NotifyCanExecuteChanged();
    }

    private void RemoveSelectedAsset()
    {
        if (SelectedAsset is not { } selected)
        {
            return;
        }

        if (!_canRemoveAsset(selected))
        {
            StatusText = $"无法移除 {selected.Name}：请先移除引用它的 Plot。";
            return;
        }

        if (!Assets.Remove(selected))
        {
            return;
        }

        SelectedAsset = Assets.FirstOrDefault();
        StatusText = $"已从工程移除 {selected.Name}；外部来源文件未修改。";
    }

    private void HandleError(Exception exception)
    {
        IsBusy = false;
        StatusText = $"数据工作区操作失败：{exception.Message}";
    }
}

public sealed class DataColumnImportViewModel : ObservableObject
{
    private string _name;
    private TabularDataType _dataType;
    private string _unit;
    private DataColumnRole? _role;

    public DataColumnImportViewModel(DataColumn column)
    {
        Id = column.Id;
        _name = column.Name;
        _dataType = column.DataType;
        _unit = column.Unit ?? string.Empty;
        _role = column.Role;
    }

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value ?? string.Empty);
    }

    public TabularDataType DataType
    {
        get => _dataType;
        set => SetProperty(ref _dataType, value);
    }

    public string Unit
    {
        get => _unit;
        set => SetProperty(ref _unit, value ?? string.Empty);
    }

    public DataColumnRole? Role
    {
        get => _role;
        set => SetProperty(ref _role, value);
    }

    public DataColumn CreateModel() => new(
        Id,
        Name.Trim(),
        DataType,
        string.IsNullOrWhiteSpace(Unit) ? null : Unit.Trim(),
        Role);
}

public sealed record TabularPreviewRowViewModel(int RowNumber, IReadOnlyList<string> Values)
{
    public string DisplayText => $"{RowNumber,4}  {string.Join("  │  ", Values)}";
}
