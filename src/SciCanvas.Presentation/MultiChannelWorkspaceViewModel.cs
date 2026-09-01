using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public sealed class MultiChannelWorkspaceViewModel : ObservableObject
{
    private static readonly string[] DefaultPalette =
    [
        "#FFFF3B30", "#FF34C759", "#FF007AFF", "#FF00C7BE",
        "#FFFF2D55", "#FFFFCC00", "#FFAF52DE", "#FF64D2FF",
    ];

    private readonly ObservableCollection<SourceAssetItemViewModel> _projectSources;
    private MultiChannelAssetGroupViewModel? _selectedGroup;
    private SourceAssetItemViewModel? _selectedReferenceSource;
    private int _wizardStep = 1;
    private bool _isWizardOpen;
    private bool _sameFieldOfViewConfirmed;
    private bool _registrationRequired;
    private string _groupName = "EDS Map Group 1";
    private string _workflowStatus = "从项目内已只读导入的源图创建多文件 EDS 通道组。";
    private bool _suppressChanges;
    private FigureCanvasViewModel? _figure;

    public MultiChannelWorkspaceViewModel(
        ObservableCollection<SourceAssetItemViewModel> projectSources,
        FigureCanvasViewModel? figure = null)
    {
        _projectSources = projectSources ?? throw new ArgumentNullException(nameof(projectSources));
        _figure = figure;
        StartEdsGroupWizardCommand = new RelayCommand(StartWizard, () => AvailableSources.Count >= 2);
        NextWizardStepCommand = new RelayCommand(NextStep, CanAdvance);
        PreviousWizardStepCommand = new RelayCommand(PreviousStep, () => IsWizardOpen && WizardStep > 1);
        CancelWizardCommand = new RelayCommand(CancelWizard, () => IsWizardOpen);
        ConfirmSuggestedNamesCommand = new RelayCommand(ConfirmSuggestedNames, CanConfirmNames);
        CreateGroupCommand = new RelayCommand(CreateGroup, CanCreateGroup);
        RemoveSelectedGroupCommand = new RelayCommand(RemoveSelectedGroup, () => SelectedGroup is not null);
        ApplySelectedGroupToPanelCommand = new RelayCommand(ApplySelectedGroupToPanel, () => SelectedGroup is not null);
        ClearPanelCompositeCommand = new RelayCommand(ClearPanelComposite);
        SynchronizeSources();
    }

    public event EventHandler? Changed;

    public ObservableCollection<SourceAssetItemViewModel> AvailableSources { get; } = [];

    public ObservableCollection<EdsChannelDraftViewModel> EdsCandidates { get; } = [];

    public ObservableCollection<MultiChannelAssetGroupViewModel> Groups { get; } = [];

    public RelayCommand StartEdsGroupWizardCommand { get; }

    public RelayCommand NextWizardStepCommand { get; }

    public RelayCommand PreviousWizardStepCommand { get; }

    public RelayCommand CancelWizardCommand { get; }

    public RelayCommand ConfirmSuggestedNamesCommand { get; }

    public RelayCommand CreateGroupCommand { get; }

    public RelayCommand RemoveSelectedGroupCommand { get; }

    public RelayCommand ApplySelectedGroupToPanelCommand { get; }

    public RelayCommand ClearPanelCompositeCommand { get; }

    public void AttachFigure(FigureCanvasViewModel figure) =>
        _figure = figure ?? throw new ArgumentNullException(nameof(figure));

    public MultiChannelAssetGroupViewModel? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                RemoveSelectedGroupCommand.NotifyCanExecuteChanged();
                ApplySelectedGroupToPanelCommand.NotifyCanExecuteChanged();
                OnPropertyChanged(nameof(SelectedGroupVisibility));
            }
        }
    }

    public SourceAssetItemViewModel? SelectedReferenceSource
    {
        get => _selectedReferenceSource;
        set
        {
            if (SetProperty(ref _selectedReferenceSource, value))
            {
                UpdateReferenceCandidate();
                RefreshWizardCommands();
            }
        }
    }

    public string GroupName
    {
        get => _groupName;
        set
        {
            if (SetProperty(ref _groupName, value))
            {
                RefreshWizardCommands();
            }
        }
    }

    public int WizardStep
    {
        get => _wizardStep;
        private set
        {
            if (SetProperty(ref _wizardStep, Math.Clamp(value, 1, 6)))
            {
                OnPropertyChanged(nameof(WizardStepText));
                OnPropertyChanged(nameof(WizardStepTitle));
                OnPropertyChanged(nameof(WizardStep1Visibility));
                OnPropertyChanged(nameof(WizardStep2Visibility));
                OnPropertyChanged(nameof(WizardStep3Visibility));
                OnPropertyChanged(nameof(WizardStep4Visibility));
                OnPropertyChanged(nameof(WizardStep5Visibility));
                OnPropertyChanged(nameof(WizardStep6Visibility));
                RefreshWizardCommands();
            }
        }
    }

    public bool IsWizardOpen
    {
        get => _isWizardOpen;
        private set
        {
            if (SetProperty(ref _isWizardOpen, value))
            {
                OnPropertyChanged(nameof(WizardVisibility));
                OnPropertyChanged(nameof(GroupListVisibility));
                RefreshWizardCommands();
            }
        }
    }

    public bool SameFieldOfViewConfirmed
    {
        get => _sameFieldOfViewConfirmed;
        set
        {
            if (SetProperty(ref _sameFieldOfViewConfirmed, value) && value)
            {
                _registrationRequired = false;
                OnPropertyChanged(nameof(RegistrationRequired));
                RefreshWizardCommands();
            }
        }
    }

    public bool RegistrationRequired
    {
        get => _registrationRequired;
        set
        {
            if (SetProperty(ref _registrationRequired, value) && value)
            {
                _sameFieldOfViewConfirmed = false;
                OnPropertyChanged(nameof(SameFieldOfViewConfirmed));
                RefreshWizardCommands();
            }
        }
    }

    public string WorkflowStatus
    {
        get => _workflowStatus;
        private set => SetProperty(ref _workflowStatus, value);
    }

    public string WizardStepText => $"Step {WizardStep} / 6";

    public string WizardStepTitle => WizardStep switch
    {
        1 => "选择 HAADF / SEM / BSE 参考图",
        2 => "选择元素分布图源文件",
        3 => "确认科研通道名称",
        4 => "确认可修改的显示颜色",
        5 => "确认是否为同一视场",
        6 => "创建 EDS 多通道组",
        _ => string.Empty,
    };

    public string GroupCountText => $"{Groups.Count} 个多通道组";

    public Visibility WizardVisibility => IsWizardOpen ? Visibility.Visible : Visibility.Collapsed;

    public Visibility GroupListVisibility => IsWizardOpen ? Visibility.Collapsed : Visibility.Visible;

    public Visibility SelectedGroupVisibility => SelectedGroup is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility WizardStep1Visibility => StepVisibility(1);

    public Visibility WizardStep2Visibility => StepVisibility(2);

    public Visibility WizardStep3Visibility => StepVisibility(3);

    public Visibility WizardStep4Visibility => StepVisibility(4);

    public Visibility WizardStep5Visibility => StepVisibility(5);

    public Visibility WizardStep6Visibility => StepVisibility(6);

    public IReadOnlyList<MultiChannelAssetGroup> CreateModels() =>
        Groups.Select(group => group.ToModel()).ToArray();

    public void SynchronizeSources()
    {
        SourceAssetItemViewModel? previousReference = SelectedReferenceSource;
        AvailableSources.Clear();
        foreach (SourceAssetItemViewModel source in _projectSources)
        {
            AvailableSources.Add(source);
        }

        SelectedReferenceSource = previousReference is not null && AvailableSources.Contains(previousReference)
            ? previousReference
            : AvailableSources.FirstOrDefault();
        StartEdsGroupWizardCommand.NotifyCanExecuteChanged();
    }

    public void Restore(IEnumerable<MultiChannelAssetGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        HashSet<Guid> availableAssetIds = AvailableSources.Select(source => source.Asset.Id).ToHashSet();
        _suppressChanges = true;
        try
        {
            foreach (MultiChannelAssetGroupViewModel group in Groups)
            {
                group.Changed -= OnGroupChanged;
            }

            Groups.Clear();
            foreach (MultiChannelAssetGroup model in groups)
            {
                model.EnsureValid(availableAssetIds);
                var viewModel = new MultiChannelAssetGroupViewModel(model, AvailableSources);
                viewModel.Changed += OnGroupChanged;
                Groups.Add(viewModel);
            }

            SelectedGroup = Groups.FirstOrDefault();
            CancelWizardCore();
            NotifyGroupStateChanged();
        }
        finally
        {
            _suppressChanges = false;
        }
    }

    private void StartWizard()
    {
        SynchronizeSources();
        if (AvailableSources.Count < 2)
        {
            WorkflowStatus = "创建 EDS 组至少需要两个已导入源文件。";
            return;
        }

        foreach (EdsChannelDraftViewModel existing in EdsCandidates)
        {
            existing.Changed -= OnCandidateChanged;
        }

        EdsCandidates.Clear();
        for (int index = 0; index < AvailableSources.Count; index++)
        {
            SourceAssetItemViewModel source = AvailableSources[index];
            string suggestion = Path.GetFileNameWithoutExtension(source.DisplayName).Trim();
            if (string.IsNullOrWhiteSpace(suggestion))
            {
                suggestion = $"Channel {index + 1}";
            }

            var candidate = new EdsChannelDraftViewModel(
                source,
                suggestion,
                index == 0 ? "Reference" : "ElementalMap",
                index == 0 ? "#FFFFFFFF" : DefaultPalette[(index - 1) % DefaultPalette.Length]);
            candidate.Changed += OnCandidateChanged;
            EdsCandidates.Add(candidate);
        }

        GroupName = $"EDS Map Group {Groups.Count + 1}";
        SameFieldOfViewConfirmed = false;
        RegistrationRequired = false;
        SelectedReferenceSource = AvailableSources[0];
        UpdateReferenceCandidate();
        WizardStep = 1;
        IsWizardOpen = true;
        WorkflowStatus = "文件名仅作为名称建议；Step 3 必须由用户明确确认。";
    }

    private void NextStep()
    {
        if (!CanAdvance())
        {
            return;
        }

        WizardStep++;
        WorkflowStatus = WizardStep switch
        {
            3 => "文件名建议不是科研事实；请逐项编辑或明确确认建议名称。",
            4 => "默认调色板不绑定特定元素，所有颜色均可修改。",
            5 => "只有人工确认同视场后才可直接联动；否则保留待配准状态。",
            6 when RegistrationRequired => "该组将标记为待配准；PR6/PR7 才会建立跨源联动和空间映射。",
            6 => "同视场已由用户确认，可以创建多通道资产组。",
            _ => WorkflowStatus,
        };
    }

    private void PreviousStep()
    {
        if (WizardStep > 1)
        {
            WizardStep--;
        }
    }

    private void CancelWizard() => CancelWizardCore();

    private void CancelWizardCore()
    {
        IsWizardOpen = false;
        WizardStep = 1;
        WorkflowStatus = "从项目内已只读导入的源图创建多文件 EDS 通道组。";
    }

    private void ConfirmSuggestedNames()
    {
        foreach (EdsChannelDraftViewModel candidate in IncludedCandidates())
        {
            candidate.ConfirmSuggestedName();
        }

        WorkflowStatus = "当前名称已由用户确认；名称来源仍保留为 FilenameSuggestion 以便审计。";
        RefreshWizardCommands();
    }

    private void CreateGroup()
    {
        if (!CanCreateGroup() || SelectedReferenceSource is null)
        {
            return;
        }

        EdsChannelDraftViewModel[] included = IncludedCandidates()
            .OrderByDescending(candidate => candidate.IsReference)
            .ThenBy(candidate => AvailableSources.IndexOf(candidate.Source))
            .ToArray();
        var members = new List<ChannelGroupMember>(included.Length);
        foreach (EdsChannelDraftViewModel candidate in included)
        {
            Guid channelId = Guid.NewGuid();
            int sourceBitDepth = Math.Clamp(candidate.Source.Asset.Metadata.BitsPerChannel, 1, 16);
            double maximum = (1L << sourceBitDepth) - 1;
            var display = new ChannelDisplaySettings(
                channelId,
                Visible: true,
                candidate.Color,
                Opacity: 1,
                DisplayMinimum: 0,
                DisplayMaximum: maximum,
                Gamma: 1,
                Invert: false);
            members.Add(new ChannelGroupMember(
                channelId,
                candidate.Source.Asset.Id,
                candidate.Source.Asset.Metadata.Channels > 1
                    ? ChannelPlaneSelector.InterleavedComponent(frameIndex: 0, componentIndex: 0)
                    : ChannelPlaneSelector.ExternalAsset(frameIndex: 0),
                candidate.Name.Trim(),
                candidate.IsReference ? "Reference" : candidate.Role.Trim(),
                ScientificStyleColor.NormalizeColor(candidate.Color),
                candidate.NameOrigin,
                candidate.IsNameConfirmed,
                display)
            {
                SourceRevision = candidate.Source.SourceRevision,
            });
        }

        var model = new MultiChannelAssetGroup(
            Guid.NewGuid(),
            GroupName.Trim(),
            SelectedReferenceSource.Asset.Id,
            members,
            SameFieldOfViewConfirmed).EnsureValid(
                AvailableSources.Select(source => source.Asset.Id).ToHashSet());
        var group = new MultiChannelAssetGroupViewModel(model, AvailableSources);
        group.Changed += OnGroupChanged;
        Groups.Add(group);
        SelectedGroup = group;
        CancelWizardCore();
        WorkflowStatus = model.RequiresRegistration
            ? $"已创建 {model.Name} · 待配准，尚未启用跨源联动"
            : $"已创建 {model.Name} · 同视场已确认，等待 PR6 建立 linked workspace";
        NotifyGroupStateChanged();
        RaiseChanged();
    }

    private void ApplySelectedGroupToPanel()
    {
        if (SelectedGroup is not { } selected)
        {
            return;
        }

        if (_figure?.SelectedPanel is not { } panel)
        {
            WorkflowStatus = "请先在 Figure 中选择一个属于该多通道组的 Panel。";
            return;
        }

        MultiChannelAssetGroup model = selected.ToModel();
        if (!model.Members.Any(member => member.AssetId == panel.Source.Asset.Id))
        {
            WorkflowStatus = $"Panel {panel.Label} 的源素材不属于 {model.Name}，未创建 composite。";
            return;
        }

        panel.CompositeGroupId = model.Id;
        WorkflowStatus = $"Panel {panel.Label} 已设为 {model.Name} composite；导出将从 raw planes 重建伪彩合成。";
        RaiseChanged();
    }

    private void ClearPanelComposite()
    {
        if (_figure?.SelectedPanel is not { } panel)
        {
            WorkflowStatus = "请先在 Figure 中选择 Panel。";
            return;
        }

        panel.CompositeGroupId = null;
        WorkflowStatus = $"Panel {panel.Label} 已恢复为 single-source display。";
        RaiseChanged();
    }

    private void RemoveSelectedGroup()
    {
        if (SelectedGroup is not { } selected)
        {
            return;
        }

        int index = Groups.IndexOf(selected);
        selected.Changed -= OnGroupChanged;
        Groups.Remove(selected);
        if (_figure is not null)
        {
            foreach (FigurePanelViewModel panel in _figure.Panels.Where(panel => panel.CompositeGroupId == selected.Id))
            {
                panel.CompositeGroupId = null;
            }
        }
        SelectedGroup = Groups.ElementAtOrDefault(Math.Clamp(index, 0, Math.Max(0, Groups.Count - 1)));
        WorkflowStatus = $"已移除多通道组 {selected.Name}；源素材未修改。";
        NotifyGroupStateChanged();
        RaiseChanged();
    }

    private bool CanAdvance() => IsWizardOpen && WizardStep switch
    {
        1 => SelectedReferenceSource is not null,
        2 => IncludedCandidates().Count() >= 2,
        3 => NamesAreConfirmedAndUnique(),
        4 => IncludedCandidates().All(candidate => ScientificStyleColor.ValidateColor(candidate.Color)),
        5 => SameFieldOfViewConfirmed || RegistrationRequired,
        _ => false,
    };

    private bool CanConfirmNames() =>
        IsWizardOpen && WizardStep == 3 && IncludedCandidates().Any() &&
        IncludedCandidates().All(candidate => !string.IsNullOrWhiteSpace(candidate.Name));

    private bool CanCreateGroup() =>
        IsWizardOpen && WizardStep == 6 &&
        !string.IsNullOrWhiteSpace(GroupName) && GroupName.Trim().Length <= 128 &&
        SelectedReferenceSource is not null && IncludedCandidates().Count() >= 2 &&
        NamesAreConfirmedAndUnique() &&
        IncludedCandidates().All(candidate => ScientificStyleColor.ValidateColor(candidate.Color)) &&
        (SameFieldOfViewConfirmed || RegistrationRequired);

    private bool NamesAreConfirmedAndUnique()
    {
        EdsChannelDraftViewModel[] included = IncludedCandidates().ToArray();
        return included.All(candidate => candidate.IsNameConfirmed &&
                                         !string.IsNullOrWhiteSpace(candidate.Name) &&
                                         candidate.Name.Trim().Length <= 128) &&
               included.Select(candidate => candidate.Name.Trim())
                   .Distinct(StringComparer.OrdinalIgnoreCase).Count() == included.Length;
    }

    private IEnumerable<EdsChannelDraftViewModel> IncludedCandidates() =>
        EdsCandidates.Where(candidate => candidate.IsIncluded);

    private void UpdateReferenceCandidate()
    {
        foreach (EdsChannelDraftViewModel candidate in EdsCandidates)
        {
            candidate.IsReference = ReferenceEquals(candidate.Source, SelectedReferenceSource);
        }
    }

    private void OnCandidateChanged(object? sender, EventArgs e) => RefreshWizardCommands();

    private void OnGroupChanged(object? sender, EventArgs e)
    {
        if (sender is MultiChannelAssetGroupViewModel group)
        {
            try
            {
                group.ToModel();
            }
            catch (InvalidOperationException)
            {
                WorkflowStatus = "当前通道设置尚未形成有效值；修正后才会写入历史或项目文件。";
                return;
            }
            catch (FormatException)
            {
                WorkflowStatus = "当前通道颜色格式无效；请使用 #RRGGBB 或 #AARRGGBB。";
                return;
            }
        }

        WorkflowStatus = "通道显示设置已更新；原始像素数据未改变。";
        RaiseChanged();
    }

    private void RefreshWizardCommands()
    {
        NextWizardStepCommand.NotifyCanExecuteChanged();
        PreviousWizardStepCommand.NotifyCanExecuteChanged();
        CancelWizardCommand.NotifyCanExecuteChanged();
        ConfirmSuggestedNamesCommand.NotifyCanExecuteChanged();
        CreateGroupCommand.NotifyCanExecuteChanged();
    }

    private void NotifyGroupStateChanged()
    {
        OnPropertyChanged(nameof(GroupCountText));
        OnPropertyChanged(nameof(SelectedGroupVisibility));
        RemoveSelectedGroupCommand.NotifyCanExecuteChanged();
    }

    private void RaiseChanged()
    {
        if (!_suppressChanges)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private Visibility StepVisibility(int step) =>
        IsWizardOpen && WizardStep == step ? Visibility.Visible : Visibility.Collapsed;
}

public sealed class EdsChannelDraftViewModel : ObservableObject
{
    private bool _isIncluded = true;
    private bool _isReference;
    private string _name;
    private string _role;
    private string _color;
    private bool _isNameConfirmed;
    private ChannelNameOrigin _nameOrigin = ChannelNameOrigin.FilenameSuggestion;

    public EdsChannelDraftViewModel(
        SourceAssetItemViewModel source,
        string suggestedName,
        string role,
        string color)
    {
        Source = source ?? throw new ArgumentNullException(nameof(source));
        _name = suggestedName;
        _role = role;
        _color = color;
    }

    public event EventHandler? Changed;

    public SourceAssetItemViewModel Source { get; }

    public string SourceDisplayName => Source.DisplayName;

    public bool IsIncluded
    {
        get => _isIncluded;
        set
        {
            bool normalized = IsReference || value;
            if (SetProperty(ref _isIncluded, normalized))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsReference
    {
        get => _isReference;
        set
        {
            if (SetProperty(ref _isReference, value))
            {
                if (value)
                {
                    _isIncluded = true;
                    _role = "Reference";
                    OnPropertyChanged(nameof(IsIncluded));
                    OnPropertyChanged(nameof(Role));
                }
                else if (string.Equals(_role, "Reference", StringComparison.Ordinal))
                {
                    _role = "ElementalMap";
                    OnPropertyChanged(nameof(Role));
                }

                OnPropertyChanged(nameof(CanExclude));
                OnPropertyChanged(nameof(ReferenceText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool CanExclude => !IsReference;

    public string ReferenceText => IsReference ? "REFERENCE" : "MAP";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                _nameOrigin = ChannelNameOrigin.User;
                _isNameConfirmed = !string.IsNullOrWhiteSpace(value);
                OnPropertyChanged(nameof(NameOrigin));
                OnPropertyChanged(nameof(NameOriginText));
                OnPropertyChanged(nameof(IsNameConfirmed));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Role
    {
        get => _role;
        set
        {
            if (SetProperty(ref _role, value))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public bool IsNameConfirmed
    {
        get => _isNameConfirmed;
        private set
        {
            if (SetProperty(ref _isNameConfirmed, value))
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ChannelNameOrigin NameOrigin => _nameOrigin;

    public string NameOriginText => NameOrigin switch
    {
        ChannelNameOrigin.User => "User",
        ChannelNameOrigin.OmeMetadata => "OME metadata",
        _ => "Filename suggestion",
    };

    public void ConfirmSuggestedName()
    {
        if (!string.IsNullOrWhiteSpace(Name))
        {
            IsNameConfirmed = true;
        }
    }
}

public sealed class MultiChannelAssetGroupViewModel : ObservableObject
{
    private string _name;

    public MultiChannelAssetGroupViewModel(
        MultiChannelAssetGroup model,
        IEnumerable<SourceAssetItemViewModel> sources)
    {
        ArgumentNullException.ThrowIfNull(model);
        Dictionary<Guid, SourceAssetItemViewModel> sourceMap = sources.ToDictionary(source => source.Asset.Id);
        model.EnsureValid(sourceMap.Keys.ToHashSet());
        Id = model.Id;
        _name = model.Name;
        ReferenceAssetId = model.ReferenceAssetId;
        SameFieldOfViewConfirmed = model.SameFieldOfViewConfirmed;
        foreach (ChannelGroupMember member in model.Members)
        {
            var viewModel = new ChannelGroupMemberViewModel(member, sourceMap[member.AssetId]);
            viewModel.Changed += OnMemberChanged;
            Members.Add(viewModel);
        }
    }

    public event EventHandler? Changed;

    public Guid Id { get; }

    public Guid ReferenceAssetId { get; }

    public bool SameFieldOfViewConfirmed { get; }

    public ObservableCollection<ChannelGroupMemberViewModel> Members { get; } = [];

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                OnPropertyChanged(nameof(Summary));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Summary => $"{Members.Count} channels · " +
                             (SameFieldOfViewConfirmed ? "same field confirmed" : "registration required");

    public MultiChannelAssetGroup ToModel() => new MultiChannelAssetGroup(
        Id,
        Name.Trim(),
        ReferenceAssetId,
        Members.Select(member => member.ToModel()).ToArray(),
        SameFieldOfViewConfirmed).EnsureValid();

    private void OnMemberChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

public sealed class ChannelGroupMemberViewModel : ObservableObject
{
    private string _name;
    private string _role;
    private string _color;
    private bool _visible;
    private double _opacity;
    private double _displayMinimum;
    private double _displayMaximum;
    private double _gamma;
    private bool _invert;
    private string _colormap;
    private ScientificChannelSourceKind _sourceKind;
    private int _frameIndex;
    private int? _componentIndex;
    private readonly int? _zIndex;
    private readonly int? _cIndex;
    private readonly int? _tIndex;

    public ChannelGroupMemberViewModel(ChannelGroupMember model, SourceAssetItemViewModel source)
    {
        model.EnsureValid();
        Source = source ?? throw new ArgumentNullException(nameof(source));
        ChannelId = model.ChannelId;
        AssetId = model.AssetId;
        SourceRevision = model.SourceRevision ?? source.SourceRevision;
        _sourceKind = model.PlaneSelector.SourceKind;
        _frameIndex = model.PlaneSelector.FrameIndex;
        _componentIndex = model.PlaneSelector.ComponentIndex;
        _zIndex = model.PlaneSelector.ZIndex;
        _cIndex = model.PlaneSelector.CIndex;
        _tIndex = model.PlaneSelector.TIndex;
        _name = model.Name;
        _role = model.Role ?? string.Empty;
        _color = model.Color;
        _nameOrigin = model.NameOrigin;
        IsNameConfirmed = model.IsNameConfirmed;
        _visible = model.DisplaySettings.Visible;
        _opacity = model.DisplaySettings.Opacity;
        _displayMinimum = model.DisplaySettings.DisplayMinimum;
        _displayMaximum = model.DisplaySettings.DisplayMaximum;
        _gamma = model.DisplaySettings.Gamma;
        _invert = model.DisplaySettings.Invert;
        _colormap = model.DisplaySettings.Colormap;
    }

    public event EventHandler? Changed;

    public SourceAssetItemViewModel Source { get; }

    public Guid ChannelId { get; }

    public Guid AssetId { get; }

    public IReadOnlyList<ScientificChannelSourceKind> SourceKindChoices =>
        Source.Asset.Metadata.Channels > 1
            ? [ScientificChannelSourceKind.InterleavedComponent]
            : Source.Asset.Metadata.FrameCount > 1
                ? [ScientificChannelSourceKind.ExternalAsset, ScientificChannelSourceKind.FramePlane]
                : [ScientificChannelSourceKind.ExternalAsset];

    public ScientificChannelSourceKind SourceKind
    {
        get => _sourceKind;
        set
        {
            ScientificChannelSourceKind normalized = SourceKindChoices.Contains(value)
                ? value
                : SourceKindChoices[0];
            if (SetProperty(ref _sourceKind, normalized))
            {
                ComponentIndex = normalized == ScientificChannelSourceKind.InterleavedComponent
                    ? Math.Clamp(_componentIndex ?? 0, 0, Math.Max(0, Source.Asset.Metadata.Channels - 1))
                    : null;
                OnPropertyChanged(nameof(ComponentIndexVisibility));
                OnPropertyChanged(nameof(PlaneIdentityText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int FrameIndex
    {
        get => _frameIndex;
        set
        {
            int normalized = Math.Clamp(value, 0, Math.Max(0, Source.Asset.Metadata.FrameCount - 1));
            if (SetProperty(ref _frameIndex, normalized))
            {
                OnPropertyChanged(nameof(PlaneIdentityText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public int? ComponentIndex
    {
        get => _componentIndex;
        set
        {
            int? normalized = SourceKind == ScientificChannelSourceKind.InterleavedComponent
                ? Math.Clamp(value ?? 0, 0, Math.Max(0, Source.Asset.Metadata.Channels - 1))
                : null;
            if (SetProperty(ref _componentIndex, normalized))
            {
                OnPropertyChanged(nameof(PlaneIdentityText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public Visibility ComponentIndexVisibility =>
        SourceKind == ScientificChannelSourceKind.InterleavedComponent
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string PlaneIdentityText =>
        $"{SourceKind} · frame {FrameIndex}" +
        (ComponentIndex is int component ? $" · component {component}" : string.Empty);

    public long SourceRevision { get; }

    private ChannelNameOrigin _nameOrigin;

    public bool IsNameConfirmed { get; }

    public string SourceDisplayName => Source.DisplayName;

    public ChannelNameOrigin NameOrigin => _nameOrigin;

    public string NameOriginText => NameOrigin switch
    {
        ChannelNameOrigin.User => "User",
        ChannelNameOrigin.OmeMetadata => "OME metadata",
        _ => "Filename suggestion · confirmed",
    };

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                _nameOrigin = ChannelNameOrigin.User;
                OnPropertyChanged(nameof(NameOrigin));
                OnPropertyChanged(nameof(NameOriginText));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Role
    {
        get => _role;
        set => SetAndNotify(ref _role, value);
    }

    public string Color
    {
        get => _color;
        set => SetAndNotify(ref _color, value);
    }

    public bool Visible
    {
        get => _visible;
        set => SetAndNotify(ref _visible, value);
    }

    public double Opacity
    {
        get => _opacity;
        set => SetAndNotify(ref _opacity, value);
    }

    public double DisplayMinimum
    {
        get => _displayMinimum;
        set => SetAndNotify(ref _displayMinimum, value);
    }

    public double DisplayMaximum
    {
        get => _displayMaximum;
        set => SetAndNotify(ref _displayMaximum, value);
    }

    public double Gamma
    {
        get => _gamma;
        set => SetAndNotify(ref _gamma, value);
    }

    public bool Invert
    {
        get => _invert;
        set => SetAndNotify(ref _invert, value);
    }

    public IReadOnlyList<string> ColormapChoices => ScientificColormap.Supported;

    public string Colormap
    {
        get => _colormap;
        set => SetAndNotify(ref _colormap, ScientificColormap.Normalize(value));
    }

    public ChannelGroupMember ToModel()
    {
        string normalizedColor = ScientificStyleColor.NormalizeColor(Color);
        var display = new ChannelDisplaySettings(
            ChannelId,
            Visible,
            normalizedColor,
            Opacity,
            DisplayMinimum,
            DisplayMaximum,
            Gamma,
            Invert,
            Colormap);
        var planeSelector = new ChannelPlaneSelector(
            SourceKind,
            FrameIndex,
            ComponentIndex,
            _zIndex,
            _cIndex,
            _tIndex).EnsureValid();
        return new ChannelGroupMember(
            ChannelId,
            AssetId,
            planeSelector,
            Name.Trim(),
            string.IsNullOrWhiteSpace(Role) ? null : Role.Trim(),
            normalizedColor,
            NameOrigin,
            IsNameConfirmed,
            display)
        {
            SourceRevision = this.SourceRevision,
        }.EnsureValid();
    }

    private void SetAndNotify<T>(ref T storage, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref storage, value, propertyName))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
