using System.Collections.ObjectModel;
using System.Globalization;
using SciCanvas.Core.Linking;

namespace SciCanvas.Presentation;

public sealed class RegistrationWorkspaceViewModel : ObservableObject, IDisposable
{
    private FigureCanvasViewModel _figure;
    private RegistrationMappingItemViewModel? _selectedRegistration;
    private bool _isApplying;

    public RegistrationWorkspaceViewModel(FigureCanvasViewModel figure)
    {
        _figure = figure ?? throw new ArgumentNullException(nameof(figure));
        _figure.LinkGroupsChanged += OnLinkGroupsChanged;
        Refresh();
    }

    public ObservableCollection<RegistrationMappingItemViewModel> Registrations { get; } = [];

    public RegistrationMappingItemViewModel? SelectedRegistration
    {
        get => _selectedRegistration;
        set => SetProperty(ref _selectedRegistration, value);
    }

    public string SummaryText => Registrations.Count == 0
        ? "尚无可配准映射；请先创建跨素材联动组。"
        : $"{Registrations.Count} 个 reference → target 映射可配准";

    public void AttachFigure(FigureCanvasViewModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (ReferenceEquals(_figure, figure))
        {
            return;
        }

        _figure.LinkGroupsChanged -= OnLinkGroupsChanged;
        _figure = figure;
        _figure.LinkGroupsChanged += OnLinkGroupsChanged;
        Refresh();
    }

    internal string GetAssetName(Guid assetId) =>
        _figure.Panels.FirstOrDefault(panel => panel.Source.Asset.Id == assetId)?.Source.DisplayName
        ?? assetId.ToString("N")[..8];

    internal SpatialMappingRevisionState GetRevisionState(Guid groupId, Guid targetAssetId) =>
        _figure.GetLinkMappingRevisionState(groupId, targetAssetId);

    internal SpatialRegistrationResult Solve(
        Guid groupId,
        Guid targetAssetId,
        SpatialMappingKind kind,
        IReadOnlyList<RegistrationLandmarkPair> landmarks)
    {
        _isApplying = true;
        try
        {
            return _figure.UpdateLinkRegistration(groupId, targetAssetId, kind, landmarks);
        }
        finally
        {
            _isApplying = false;
        }
    }

    public void Dispose()
    {
        _figure.LinkGroupsChanged -= OnLinkGroupsChanged;
        GC.SuppressFinalize(this);
    }

    private void OnLinkGroupsChanged(object? sender, EventArgs e)
    {
        if (!_isApplying)
        {
            Refresh();
        }
    }

    private void Refresh()
    {
        (Guid GroupId, Guid TargetId)? selected = SelectedRegistration is { } current
            ? (current.GroupId, current.TargetAssetId)
            : null;
        Registrations.Clear();
        foreach (LinkGroup group in _figure.LinkGroups)
        {
            foreach (SpatialMapping mapping in group.Mappings)
            {
                Registrations.Add(new RegistrationMappingItemViewModel(this, group, mapping));
            }
        }

        SelectedRegistration = selected is { } ids
            ? Registrations.FirstOrDefault(item =>
                item.GroupId == ids.GroupId && item.TargetAssetId == ids.TargetId)
                ?? Registrations.FirstOrDefault()
            : Registrations.FirstOrDefault();
        OnPropertyChanged(nameof(SummaryText));
    }
}

public sealed class RegistrationMappingItemViewModel : ObservableObject
{
    private readonly RegistrationWorkspaceViewModel _owner;
    private string _landmarksText;
    private string _statusText = "输入 sourceX,sourceY -> targetX,targetY，每行一对 landmark。";
    private SpatialMapping _mapping;

    internal RegistrationMappingItemViewModel(
        RegistrationWorkspaceViewModel owner,
        LinkGroup group,
        SpatialMapping mapping)
    {
        _owner = owner;
        GroupId = group.Id;
        GroupName = group.Name;
        TargetAssetId = mapping.TargetAssetId;
        SourceAssetName = owner.GetAssetName(mapping.SourceAssetId);
        TargetAssetName = owner.GetAssetName(mapping.TargetAssetId);
        _mapping = mapping;
        _landmarksText = FormatLandmarks(mapping.EffectiveLandmarks);
        SolveTranslationCommand = new RelayCommand(() => Solve(SpatialMappingKind.Translation));
        SolveRigidCommand = new RelayCommand(() => Solve(SpatialMappingKind.Rigid));
        SolveAffineCommand = new RelayCommand(() => Solve(SpatialMappingKind.Affine));
    }

    public Guid GroupId { get; }

    public Guid TargetAssetId { get; }

    public string GroupName { get; }

    public string SourceAssetName { get; }

    public string TargetAssetName { get; }

    public string PairTitle => $"{SourceAssetName} → {TargetAssetName}";

    public string LandmarksText
    {
        get => _landmarksText;
        set => SetProperty(ref _landmarksText, value ?? string.Empty);
    }

    public string KindText => _mapping.Kind.ToString();

    public string MatrixText => string.Create(
        CultureInfo.InvariantCulture,
        $"{_mapping.Matrix.M11,10:0.######} {_mapping.Matrix.M12,10:0.######} {_mapping.Matrix.M13,10:0.######}\n" +
        $"{_mapping.Matrix.M21,10:0.######} {_mapping.Matrix.M22,10:0.######} {_mapping.Matrix.M23,10:0.######}\n" +
        $"{_mapping.Matrix.M31,10:0.######} {_mapping.Matrix.M32,10:0.######} {_mapping.Matrix.M33,10:0.######}");

    public string RmsText
    {
        get
        {
            if (_mapping.ResidualPixels is not double pixels)
            {
                return "RMS 尚未计算";
            }

            string physical = _mapping.ResidualPhysical is double value
                ? $" · {value.ToString("0.###", CultureInfo.InvariantCulture)} {_mapping.ResidualPhysicalUnit}"
                : string.Empty;
            return $"RMS = {pixels.ToString("0.###", CultureInfo.InvariantCulture)} px{physical}";
        }
    }

    public string RevisionStatusText
    {
        get
        {
            SpatialMappingRevisionState state = _owner.GetRevisionState(GroupId, TargetAssetId);
            return state == SpatialMappingRevisionState.Current
                ? $"Current · revision {_mapping.SourceRevision} → {_mapping.TargetRevision}"
                : $"ReviewRequired · saved revision {_mapping.SourceRevision} → {_mapping.TargetRevision}";
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public RelayCommand SolveTranslationCommand { get; }

    public RelayCommand SolveRigidCommand { get; }

    public RelayCommand SolveAffineCommand { get; }

    private void Solve(SpatialMappingKind kind)
    {
        try
        {
            IReadOnlyList<RegistrationLandmarkPair> landmarks = ParseLandmarks(LandmarksText);
            SpatialRegistrationResult result = _owner.Solve(
                GroupId,
                TargetAssetId,
                kind,
                landmarks);
            _mapping = result.Mapping;
            LandmarksText = FormatLandmarks(_mapping.EffectiveLandmarks);
            StatusText = string.Join(
                " · ",
                result.PointResiduals.Select((residual, index) =>
                    $"P{index + 1}={residual.DistancePixels.ToString("0.###", CultureInfo.InvariantCulture)} px"));
            OnPropertyChanged(nameof(KindText));
            OnPropertyChanged(nameof(MatrixText));
            OnPropertyChanged(nameof(RmsText));
            OnPropertyChanged(nameof(RevisionStatusText));
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidOperationException or ArgumentException)
        {
            StatusText = exception.Message;
        }
    }

    internal static IReadOnlyList<RegistrationLandmarkPair> ParseLandmarks(string text)
    {
        string[] lines = (text ?? string.Empty)
            .Replace(';', '\n')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var landmarks = new List<RegistrationLandmarkPair>(lines.Length);
        foreach (string line in lines)
        {
            string[] sides = line.Split("->", StringSplitOptions.TrimEntries);
            if (sides.Length != 2)
            {
                throw new FormatException($"无法解析 landmark：{line}");
            }

            SpatialPoint source = ParsePoint(sides[0], line);
            SpatialPoint target = ParsePoint(sides[1], line);
            landmarks.Add(new RegistrationLandmarkPair(Guid.NewGuid(), source, target));
        }

        if (landmarks.Count == 0)
        {
            throw new FormatException("请至少输入 1 对 landmark。");
        }

        return landmarks;
    }

    private static SpatialPoint ParsePoint(string text, string line)
    {
        string[] values = text.Split(',', StringSplitOptions.TrimEntries);
        if (values.Length != 2 ||
            !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
            !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
            !double.IsFinite(x) || !double.IsFinite(y))
        {
            throw new FormatException($"landmark 坐标必须使用有限数值 x,y：{line}");
        }

        return new SpatialPoint(x, y);
    }

    private static string FormatLandmarks(IEnumerable<RegistrationLandmarkPair> landmarks) =>
        string.Join(
            Environment.NewLine,
            landmarks.Select(pair => string.Create(
                CultureInfo.InvariantCulture,
                $"{pair.SourcePoint.X:0.######},{pair.SourcePoint.Y:0.######} -> {pair.TargetPoint.X:0.######},{pair.TargetPoint.Y:0.######}")));
}
