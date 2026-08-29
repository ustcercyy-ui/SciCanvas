using System.Collections.ObjectModel;
using SciCanvas.Core.Linking;

namespace SciCanvas.Presentation;

public sealed class LinkedViewsWorkspaceViewModel : ObservableObject, IDisposable
{
    private FigureCanvasViewModel _figure;
    private LinkedViewGroupItemViewModel? _selectedGroup;
    private bool _isApplying;

    public LinkedViewsWorkspaceViewModel(FigureCanvasViewModel figure)
    {
        _figure = figure ?? throw new ArgumentNullException(nameof(figure));
        _figure.LinkGroupsChanged += OnLinkGroupsChanged;
        _figure.PropertyChanged += OnFigurePropertyChanged;
        Refresh();
    }

    public ObservableCollection<LinkedViewGroupItemViewModel> Groups { get; } = [];

    public LinkedViewGroupItemViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set => SetProperty(ref _selectedGroup, value);
    }

    public string SummaryText => Groups.Count == 0
        ? "尚无跨素材联动组。请在 Figure 中多选不同素材的面板并执行“关联裁剪”。"
        : $"{Groups.Count} 个跨素材联动组 · TargetPoint = M × SourcePoint";

    public string SynchronizationStatusText => _figure.LinkSynchronizationStatusText;

    public void AttachFigure(FigureCanvasViewModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (ReferenceEquals(_figure, figure))
        {
            return;
        }

        _figure.LinkGroupsChanged -= OnLinkGroupsChanged;
        _figure.PropertyChanged -= OnFigurePropertyChanged;
        _figure = figure;
        _figure.LinkGroupsChanged += OnLinkGroupsChanged;
        _figure.PropertyChanged += OnFigurePropertyChanged;
        Refresh();
    }

    internal string GetAssetName(Guid assetId) =>
        _figure.Panels.FirstOrDefault(panel => panel.Source.Asset.Id == assetId)?.Source.DisplayName
        ?? assetId.ToString("N")[..8];

    internal void ApplySyncOptions(Guid groupId, LinkSyncOptions options)
    {
        _isApplying = true;
        try
        {
            _figure.UpdateLinkSyncOptions(groupId, options);
            OnPropertyChanged(nameof(SynchronizationStatusText));
        }
        finally
        {
            _isApplying = false;
        }
    }

    internal void ApplyTranslation(Guid groupId, Guid targetAssetId, double offsetX, double offsetY)
    {
        _isApplying = true;
        try
        {
            _figure.UpdateLinkTranslation(groupId, targetAssetId, offsetX, offsetY);
            OnPropertyChanged(nameof(SynchronizationStatusText));
        }
        finally
        {
            _isApplying = false;
        }
    }

    internal void ApplyIdentity(Guid groupId, Guid targetAssetId)
    {
        _isApplying = true;
        try
        {
            _figure.UpdateLinkIdentity(groupId, targetAssetId);
            OnPropertyChanged(nameof(SynchronizationStatusText));
        }
        finally
        {
            _isApplying = false;
        }
    }

    public void Dispose()
    {
        _figure.LinkGroupsChanged -= OnLinkGroupsChanged;
        _figure.PropertyChanged -= OnFigurePropertyChanged;
        GC.SuppressFinalize(this);
    }

    private void OnFigurePropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FigureCanvasViewModel.LinkSynchronizationStatusText))
        {
            OnPropertyChanged(nameof(SynchronizationStatusText));
        }
    }
    private void OnLinkGroupsChanged(object? sender, EventArgs e)
    {
        if (!_isApplying)
        {
            Refresh();
        }

        OnPropertyChanged(nameof(SynchronizationStatusText));
    }

    private void Refresh()
    {
        Guid? selectedId = SelectedGroup?.Id;
        Groups.Clear();
        foreach (LinkGroup group in _figure.LinkGroups)
        {
            Groups.Add(new LinkedViewGroupItemViewModel(this, group));
        }

        SelectedGroup = selectedId is Guid id
            ? Groups.FirstOrDefault(group => group.Id == id) ?? Groups.FirstOrDefault()
            : Groups.FirstOrDefault();
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(SynchronizationStatusText));
    }
}

public sealed class LinkedViewGroupItemViewModel : ObservableObject
{
    private readonly LinkedViewsWorkspaceViewModel _owner;
    private LinkSyncOptions _syncOptions;

    internal LinkedViewGroupItemViewModel(LinkedViewsWorkspaceViewModel owner, LinkGroup group)
    {
        _owner = owner;
        Id = group.Id;
        Name = group.Name;
        ReferenceAssetName = owner.GetAssetName(group.ReferenceAssetId);
        MemberSummary = string.Join(" · ", group.AssetIds.Select(owner.GetAssetName));
        _syncOptions = group.SyncOptions;
        Mappings = new ObservableCollection<LinkedSpatialMappingItemViewModel>(
            group.Mappings.Select(mapping => new LinkedSpatialMappingItemViewModel(owner, group.Id, mapping)));
    }

    public Guid Id { get; }

    public string Name { get; }

    public string ReferenceAssetName { get; }

    public string MemberSummary { get; }

    public ObservableCollection<LinkedSpatialMappingItemViewModel> Mappings { get; }

    public bool SyncCrop
    {
        get => _syncOptions.HasFlag(LinkSyncOptions.Crop);
        set => SetOption(LinkSyncOptions.Crop, value, nameof(SyncCrop));
    }

    public bool SyncRoi
    {
        get => _syncOptions.HasFlag(LinkSyncOptions.Roi);
        set => SetOption(LinkSyncOptions.Roi, value, nameof(SyncRoi));
    }

    public bool SyncColorScale
    {
        get => _syncOptions.HasFlag(LinkSyncOptions.ColorScale);
        set => SetOption(LinkSyncOptions.ColorScale, value, nameof(SyncColorScale));
    }

    public string SyncSummary => $"Crop {(SyncCrop ? "ON" : "OFF")} · ROI {(SyncRoi ? "ON" : "OFF")} · ColorScale {(SyncColorScale ? "ON" : "OFF")}";

    private void SetOption(LinkSyncOptions option, bool enabled, string propertyName)
    {
        LinkSyncOptions next = enabled ? _syncOptions | option : _syncOptions & ~option;
        if (next == LinkSyncOptions.None || next == _syncOptions)
        {
            OnPropertyChanged(propertyName);
            return;
        }

        _syncOptions = next;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(SyncSummary));
        _owner.ApplySyncOptions(Id, next);
    }
}

public sealed class LinkedSpatialMappingItemViewModel : ObservableObject
{
    private readonly LinkedViewsWorkspaceViewModel _owner;
    private readonly Guid _groupId;
    private readonly Guid _targetAssetId;
    private double _offsetX;
    private double _offsetY;
    private SpatialMappingKind _kind;
    private SpatialMappingOrigin _origin;

    internal LinkedSpatialMappingItemViewModel(
        LinkedViewsWorkspaceViewModel owner,
        Guid groupId,
        SpatialMapping mapping)
    {
        _owner = owner;
        _groupId = groupId;
        _targetAssetId = mapping.TargetAssetId;
        _offsetX = mapping.Matrix.M13;
        _offsetY = mapping.Matrix.M23;
        _kind = mapping.Kind;
        _origin = mapping.Origin;
        SourceAssetName = owner.GetAssetName(mapping.SourceAssetId);
        TargetAssetName = owner.GetAssetName(mapping.TargetAssetId);
        SourceRevision = mapping.SourceRevision;
        TargetRevision = mapping.TargetRevision;
        ResetIdentityCommand = new RelayCommand(ResetIdentity);
    }

    public string SourceAssetName { get; }

    public string TargetAssetName { get; }

    public long SourceRevision { get; }

    public long TargetRevision { get; }

    public double OffsetX
    {
        get => _offsetX;
        set
        {
            if (double.IsFinite(value) && SetProperty(ref _offsetX, value))
            {
                ApplyTranslation();
            }
        }
    }

    public double OffsetY
    {
        get => _offsetY;
        set
        {
            if (double.IsFinite(value) && SetProperty(ref _offsetY, value))
            {
                ApplyTranslation();
            }
        }
    }

    public string KindText => _kind.ToString();

    public string ProvenanceText => $"{_origin} · revision {SourceRevision} → {TargetRevision}";

    public RelayCommand ResetIdentityCommand { get; }

    private void ApplyTranslation()
    {
        _owner.ApplyTranslation(_groupId, _targetAssetId, OffsetX, OffsetY);
        _kind = SpatialMappingKind.Translation;
        _origin = SpatialMappingOrigin.UserDeclaredTranslation;
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(ProvenanceText));
    }

    private void ResetIdentity()
    {
        _offsetX = 0;
        _offsetY = 0;
        _kind = SpatialMappingKind.Identity;
        _origin = SpatialMappingOrigin.UserDeclaredIdentity;
        OnPropertyChanged(nameof(OffsetX));
        OnPropertyChanged(nameof(OffsetY));
        OnPropertyChanged(nameof(KindText));
        OnPropertyChanged(nameof(ProvenanceText));
        _owner.ApplyIdentity(_groupId, _targetAssetId);
    }
}
