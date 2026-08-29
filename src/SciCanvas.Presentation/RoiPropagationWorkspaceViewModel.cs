using System.Collections.ObjectModel;
using System.Globalization;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Linking;
using LinkGroup = SciCanvas.Core.Linking.LinkGroup;
using SciCanvas.Core.Science;
using SciCanvas.Core.Workspace;
using SciCanvas.Imaging;

namespace SciCanvas.Presentation;

public sealed class RoiPropagationWorkspaceViewModel : ObservableObject, IDisposable
{
    private readonly ObservableCollection<SourceAssetItemViewModel> _sources;
    private readonly MultiChannelWorkspaceViewModel _multiChannelWorkspace;
    private readonly IImagePlaneReader _planeReader;
    private FigureCanvasViewModel _figure;
    private RoiLinkGroupItemViewModel? _selectedLinkGroup;
    private RoiChannelGroupItemViewModel? _selectedChannelGroup;
    private RoiObjectItemViewModel? _selectedRoi;
    private string _polygonText = string.Empty;
    private string _label = "ROI 1";
    private string _statusText = "输入 reference image 的 source-pixel polygon，然后传播并运行逐通道 raw statistics。";

    public RoiPropagationWorkspaceViewModel(
        ObservableCollection<SourceAssetItemViewModel> sources,
        MultiChannelWorkspaceViewModel multiChannelWorkspace,
        FigureCanvasViewModel figure,
        IImagePlaneReader? planeReader = null)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _multiChannelWorkspace = multiChannelWorkspace ?? throw new ArgumentNullException(nameof(multiChannelWorkspace));
        _figure = figure ?? throw new ArgumentNullException(nameof(figure));
        _planeReader = planeReader ?? new WpfImagePlaneReader();
        _figure.LinkGroupsChanged += OnWorkspaceChanged;
        _multiChannelWorkspace.Changed += OnWorkspaceChanged;
        CreateAndPropagateCommand = new RelayCommand(CreateAndPropagate);
        AnalyzeAcrossChannelsCommand = new AsyncRelayCommand(
            AnalyzeAcrossChannelsAsync,
            () => SelectedLinkGroup is not null && SelectedChannelGroup is not null && Rois.Count > 0,
            exception => StatusText = exception.Message);
        RefreshGroups();
    }

    public event EventHandler? Changed;

    public ObservableCollection<RoiLinkGroupItemViewModel> LinkGroups { get; } = [];

    public ObservableCollection<RoiChannelGroupItemViewModel> ChannelGroups { get; } = [];

    public ObservableCollection<RoiObjectItemViewModel> Rois { get; } = [];

    public ObservableCollection<CrossChannelRoiStatisticsItemViewModel> Statistics { get; } = [];

    public RoiLinkGroupItemViewModel? SelectedLinkGroup
    {
        get => _selectedLinkGroup;
        set
        {
            if (SetProperty(ref _selectedLinkGroup, value))
            {
                AnalyzeAcrossChannelsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RoiChannelGroupItemViewModel? SelectedChannelGroup
    {
        get => _selectedChannelGroup;
        set
        {
            if (SetProperty(ref _selectedChannelGroup, value))
            {
                AnalyzeAcrossChannelsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public RoiObjectItemViewModel? SelectedRoi
    {
        get => _selectedRoi;
        set => SetProperty(ref _selectedRoi, value);
    }

    public string PolygonText
    {
        get => _polygonText;
        set => SetProperty(ref _polygonText, value ?? string.Empty);
    }

    public string Label
    {
        get => _label;
        set => SetProperty(ref _label, value ?? string.Empty);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public string SummaryText => Rois.Count == 0
        ? "尚无 canonical ROI"
        : $"{Rois.Count} 个 canonical ROI · {Statistics.Count} 个逐通道统计结果";

    public RelayCommand CreateAndPropagateCommand { get; }

    public AsyncRelayCommand AnalyzeAcrossChannelsCommand { get; }

    public IReadOnlyList<RoiObject> CreateModels() =>
        Rois.Select(item => item.Model.EnsureValid()).ToArray();

    public void Restore(IEnumerable<RoiObject> rois)
    {
        ArgumentNullException.ThrowIfNull(rois);
        Rois.Clear();
        foreach (RoiObject roi in rois)
        {
            Rois.Add(new RoiObjectItemViewModel(roi.EnsureValid(), GetAssetName(roi.AssetId!.Value)));
        }

        SelectedRoi = Rois.FirstOrDefault();
        OnPropertyChanged(nameof(SummaryText));
        AnalyzeAcrossChannelsCommand.NotifyCanExecuteChanged();
    }

    public void AttachFigure(FigureCanvasViewModel figure)
    {
        ArgumentNullException.ThrowIfNull(figure);
        if (ReferenceEquals(_figure, figure))
        {
            return;
        }

        _figure.LinkGroupsChanged -= OnWorkspaceChanged;
        _figure = figure;
        _figure.LinkGroupsChanged += OnWorkspaceChanged;
        RefreshGroups();
    }

    public void Dispose()
    {
        _figure.LinkGroupsChanged -= OnWorkspaceChanged;
        _multiChannelWorkspace.Changed -= OnWorkspaceChanged;
        GC.SuppressFinalize(this);
    }

    private void CreateAndPropagate()
    {
        try
        {
            LinkGroup group = SelectedLinkGroup?.Model ??
                throw new InvalidOperationException("请先选择 LinkGroup。");
            IReadOnlyList<MeasurementPoint> points = ParsePolygon(PolygonText);
            SourceAssetItemViewModel referenceSource = _sources.Single(
                source => source.Asset.Id == group.ReferenceAssetId);
            int referenceFrame = SelectedChannelGroup?.Model.Members
                .FirstOrDefault(member => member.AssetId == group.ReferenceAssetId)?.FrameIndex ?? 0;
            var reference = new RoiObject
            {
                Id = Guid.NewGuid(),
                AssetId = group.ReferenceAssetId,
                SourceRevision = referenceSource.SourceRevision,
                GeometryKind = RoiGeometryKind.Polygon,
                FrameIndex = referenceFrame,
                SourceGeometry = points,
                Style = RoiStyle.Default with { Label = string.IsNullOrWhiteSpace(Label) ? null : Label.Trim() },
            }.EnsureValid();
            Dictionary<Guid, long> revisions = _sources
                .Where(source => group.AssetIds.Contains(source.Asset.Id))
                .ToDictionary(source => source.Asset.Id, source => source.SourceRevision);
            IReadOnlyList<RoiObject> propagated = RoiPropagationService.PropagatePolygon(
                reference,
                group,
                revisions);
            Dictionary<Guid, int> frames = SelectedChannelGroup?.Model.Members
                .ToDictionary(member => member.AssetId, member => member.FrameIndex) ?? [];
            RoiObject[] bundle =
            [
                reference,
                .. propagated.Select(roi => frames.TryGetValue(roi.AssetId!.Value, out int frame)
                    ? (roi with { FrameIndex = frame }).EnsureValid()
                    : roi),
            ];
            foreach (RoiObject roi in bundle)
            {
                Rois.Add(new RoiObjectItemViewModel(roi, GetAssetName(roi.AssetId!.Value)));
            }

            SelectedRoi = Rois.LastOrDefault();
            StatusText = $"已创建 reference polygon 并通过 {propagated.Count} 个 SpatialMapping 传播；未使用 bounding rectangle 代替 geometry。";
            OnPropertyChanged(nameof(SummaryText));
            AnalyzeAcrossChannelsCommand.NotifyCanExecuteChanged();
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception) when (
            exception is FormatException or InvalidOperationException or ArgumentException)
        {
            StatusText = exception.Message;
        }
    }

    internal async Task AnalyzeAcrossChannelsAsync()
    {
        LinkGroup group = SelectedLinkGroup?.Model ??
            throw new InvalidOperationException("请先选择 LinkGroup。");
        MultiChannelAssetGroup channelGroup = SelectedChannelGroup?.Model ??
            throw new InvalidOperationException("请先选择 MultiChannel group。");
        RoiObject reference = Rois
            .Select(item => item.Model)
            .LastOrDefault(roi => roi.AssetId == group.ReferenceAssetId &&
                Rois.Any(target => target.Model.Propagation is { } propagation &&
                    propagation.ReferenceRoiId == roi.Id && propagation.LinkGroupId == group.Id))
            ?? throw new InvalidOperationException("当前 LinkGroup 尚无已传播的 reference ROI。");
        RoiObject[] targets = Rois
            .Select(item => item.Model)
            .Where(roi => roi.Propagation is { } propagation &&
                propagation.ReferenceRoiId == reference.Id && propagation.LinkGroupId == group.Id)
            .ToArray();
        Dictionary<Guid, RoiAnalysisSource> sources = _sources
            .Where(source => channelGroup.Members.Any(member => member.AssetId == source.Asset.Id))
            .ToDictionary(
                source => source.Asset.Id,
                source => new RoiAnalysisSource(source.Asset, source.SourceRevision));

        IReadOnlyList<CrossChannelRoiStatisticsEntry> results =
            await CrossChannelRoiStatisticsService.AnalyzeAsync(
                reference,
                targets,
                group,
                channelGroup,
                sources,
                _planeReader);
        Statistics.Clear();
        foreach (CrossChannelRoiStatisticsEntry entry in results)
        {
            _sources.Single(source => source.Asset.Id == entry.Statistics.SourceAssetId)
                .AddAnalysisResult(entry.Statistics, completeEdit: false);
            Statistics.Add(new CrossChannelRoiStatisticsItemViewModel(entry));
        }

        StatusText = $"已对 {results.Count} 个 channel member 完成 polygon raw statistics；未读取 pseudocolor/composite RGB。";
        OnPropertyChanged(nameof(SummaryText));
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnWorkspaceChanged(object? sender, EventArgs e) => RefreshGroups();

    private void RefreshGroups()
    {
        Guid? selectedLinkId = SelectedLinkGroup?.Id;
        Guid? selectedChannelId = SelectedChannelGroup?.Id;
        LinkGroups.Clear();
        foreach (LinkGroup group in _figure.LinkGroups)
        {
            LinkGroups.Add(new RoiLinkGroupItemViewModel(group));
        }

        ChannelGroups.Clear();
        foreach (MultiChannelAssetGroup group in _multiChannelWorkspace.CreateModels())
        {
            ChannelGroups.Add(new RoiChannelGroupItemViewModel(group));
        }

        SelectedLinkGroup = selectedLinkId is Guid linkId
            ? LinkGroups.FirstOrDefault(item => item.Id == linkId) ?? LinkGroups.FirstOrDefault()
            : LinkGroups.FirstOrDefault();
        SelectedChannelGroup = selectedChannelId is Guid channelId
            ? ChannelGroups.FirstOrDefault(item => item.Id == channelId) ?? ChannelGroups.FirstOrDefault()
            : ChannelGroups.FirstOrDefault(item =>
                item.Model.ReferenceAssetId == SelectedLinkGroup?.Model.ReferenceAssetId)
                ?? ChannelGroups.FirstOrDefault();
    }

    private string GetAssetName(Guid assetId) =>
        _sources.FirstOrDefault(source => source.Asset.Id == assetId)?.DisplayName
        ?? assetId.ToString("N")[..8];

    internal static IReadOnlyList<MeasurementPoint> ParsePolygon(string text)
    {
        string[] lines = (text ?? string.Empty)
            .Replace(';', '\n')
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var points = new List<MeasurementPoint>(lines.Length);
        foreach (string line in lines)
        {
            string[] values = line.Split(',', StringSplitOptions.TrimEntries);
            if (values.Length != 2 ||
                !double.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                !double.TryParse(values[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                !double.IsFinite(x) || !double.IsFinite(y))
            {
                throw new FormatException($"polygon point 必须使用有限 source-pixel 坐标 x,y：{line}");
            }

            points.Add(new MeasurementPoint(x, y));
        }

        if (points.Count < 3)
        {
            throw new FormatException("Polygon ROI 至少需要 3 个 source-pixel points。");
        }

        return Array.AsReadOnly(points.ToArray());
    }
}

public sealed record RoiLinkGroupItemViewModel(LinkGroup Model)
{
    public Guid Id => Model.Id;

    public string Name => Model.Name;
}

public sealed record RoiChannelGroupItemViewModel(MultiChannelAssetGroup Model)
{
    public Guid Id => Model.Id;

    public string Name => Model.Name;
}

public sealed class RoiObjectItemViewModel(RoiObject model, string assetName)
{
    public RoiObject Model { get; } = model;

    public string AssetName { get; } = assetName;

    public string Label => Model.Style.Label ?? "Polygon ROI";

    public string GeometryText => $"{Model.SourceGeometry.Count} points · source px";

    public string ProvenanceText => Model.Propagation is null
        ? $"Reference · revision {Model.SourceRevision}"
        : $"Mapping {Model.Propagation.MappingId.ToString("N")[..8]} · revision {Model.SourceRevision}";
}

public sealed class CrossChannelRoiStatisticsItemViewModel(CrossChannelRoiStatisticsEntry entry)
{
    public string ChannelName => entry.ChannelMember.Name;

    public string Summary => string.Create(
        CultureInfo.InvariantCulture,
        $"n={entry.Statistics.PixelCount} · mean={entry.Statistics.Mean:0.###} · min={entry.Statistics.Minimum:0.###} · max={entry.Statistics.Maximum:0.###}");

    public string Provenance =>
        $"raw {entry.Statistics.SourceBitDepth}-bit · channel {entry.ChannelMember.ChannelId.ToString("N")[..8]}";
}
