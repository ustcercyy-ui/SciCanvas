using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
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
        ProjectSelectedRoiToFigureCommand = new RelayCommand(
            ProjectSelectedRoiToFigure,
            () => SelectedRoi is not null);
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
        set
        {
            if (ReferenceEquals(_selectedRoi, value))
            {
                return;
            }

            if (_selectedRoi is not null)
            {
                _selectedRoi.IsSelected = false;
            }

            if (SetProperty(ref _selectedRoi, value))
            {
                if (_selectedRoi is not null)
                {
                    _selectedRoi.IsSelected = true;
                }
                ProjectSelectedRoiToFigureCommand.NotifyCanExecuteChanged();
            }
        }
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

    public RelayCommand ProjectSelectedRoiToFigureCommand { get; }

    public IReadOnlyList<RoiObject> CreateModels() =>
        Rois.Select(item => item.Model.EnsureValid()).ToArray();

    public RoiObjectItemViewModel AddDirectRoi(
        SourceAssetItemViewModel source,
        RoiGeometryKind geometryKind,
        IReadOnlyList<MeasurementPoint> sourceGeometry,
        string? label = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceGeometry);
        string resolvedLabel = string.IsNullOrWhiteSpace(label)
            ? $"ROI {Rois.Count + 1}"
            : label.Trim();
        var roi = new RoiObject
        {
            Id = Guid.NewGuid(),
            AssetId = source.Asset.Id,
            SourceRevision = source.SourceRevision,
            GeometryKind = geometryKind,
            FrameIndex = 0,
            SourceGeometry = sourceGeometry.ToArray(),
            Style = RoiStyle.Default with { Label = resolvedLabel },
        }.EnsureValid();
        RoiGeometryValidationResult validation = RoiGeometryValidator.Validate(
            roi,
            source.Asset.Metadata.PixelSize);
        RoiBoundaryPolicyResult policy = RoiOutOfBoundsPolicy.Evaluate(
            validation,
            RoiBoundaryRole.Reference);
        if (!policy.CanPersist || !policy.CanAnalyze)
        {
            throw new InvalidOperationException(string.Join(" ", policy.Validity.Reasons));
        }

        roi = (roi with { Validity = policy.Validity }).EnsureValid();
        var item = new RoiObjectItemViewModel(roi, source.DisplayName);
        Rois.Add(item);
        SelectedRoi = item;
        StatusText = $"已在 {source.DisplayName} 创建 canonical {geometryKind} ROI；几何保存为 source pixels。";
        OnPropertyChanged(nameof(SummaryText));
        AnalyzeAcrossChannelsCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
        return item;
    }

    public bool TryMoveSelectedRoi(double deltaX, double deltaY)
    {
        if (SelectedRoi is null)
        {
            return false;
        }

        MeasurementPoint[] moved = SelectedRoi.Model.SourceGeometry
            .Select(point => new MeasurementPoint(point.X + deltaX, point.Y + deltaY))
            .ToArray();
        return TryReplaceSelectedGeometry(moved, "移动");
    }

    public bool TryUpdateSelectedRoiVertex(int vertexIndex, MeasurementPoint point)
    {
        if (SelectedRoi is null ||
            vertexIndex < 0 ||
            vertexIndex >= SelectedRoi.Model.SourceGeometry.Count)
        {
            return false;
        }

        MeasurementPoint[] points = SelectedRoi.Model.SourceGeometry.ToArray();
        points[vertexIndex] = point;
        return TryReplaceSelectedGeometry(points, $"更新顶点 {vertexIndex + 1}");
    }

    public bool TryInsertSelectedPolygonVertex(MeasurementPoint point)
    {
        if (SelectedRoi?.Model is not { GeometryKind: RoiGeometryKind.Polygon } roi)
        {
            StatusText = "只有 Polygon ROI 支持插入顶点。";
            return false;
        }

        int segmentIndex = FindNearestPolygonSegment(roi.SourceGeometry, point);
        var points = roi.SourceGeometry.ToList();
        points.Insert(segmentIndex + 1, point);
        return TryReplaceSelectedGeometry(points, $"在边 {segmentIndex + 1} 插入顶点");
    }

    public bool TryDeleteSelectedPolygonVertex(int vertexIndex)
    {
        if (SelectedRoi?.Model is not { GeometryKind: RoiGeometryKind.Polygon } roi ||
            vertexIndex < 0 ||
            vertexIndex >= roi.SourceGeometry.Count)
        {
            return false;
        }

        if (roi.SourceGeometry.Count <= 3)
        {
            StatusText = "Polygon ROI 至少需要 3 个顶点，不能继续删除。";
            return false;
        }

        var points = roi.SourceGeometry.ToList();
        points.RemoveAt(vertexIndex);
        return TryReplaceSelectedGeometry(points, $"删除顶点 {vertexIndex + 1}");
    }

    public bool RemoveSelectedRoi()
    {
        if (SelectedRoi is not { } selected)
        {
            return false;
        }

        if (_figure.RoiProjections.Any(projection => projection.RoiId == selected.Model.Id))
        {
            StatusText = "该 canonical ROI 已被 Figure Projection 引用；请先删除对应 Projection。";
            return false;
        }

        int index = Rois.IndexOf(selected);
        Rois.Remove(selected);
        SelectedRoi = Rois.Count == 0
            ? null
            : Rois[Math.Clamp(index, 0, Rois.Count - 1)];
        StatusText = "已删除 canonical ROI。";
        OnPropertyChanged(nameof(SummaryText));
        AnalyzeAcrossChannelsCommand.NotifyCanExecuteChanged();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

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
            RoiGeometryValidationResult referenceValidation = RoiGeometryValidator.Validate(
                reference,
                referenceSource.Asset.Metadata.PixelSize);
            RoiBoundaryPolicyResult referencePolicy = RoiOutOfBoundsPolicy.Evaluate(
                referenceValidation,
                RoiBoundaryRole.Reference);
            if (!referencePolicy.CanPersist || !referencePolicy.CanAnalyze)
            {
                throw new InvalidOperationException(string.Join(" ", referencePolicy.Validity.Reasons));
            }

            reference = (reference with { Validity = referencePolicy.Validity }).EnsureValid();
            Dictionary<Guid, RoiSourceGeometryContext> sourceContexts = _sources
                .Where(source => group.AssetIds.Contains(source.Asset.Id))
                .ToDictionary(
                    source => source.Asset.Id,
                    source => new RoiSourceGeometryContext(
                        source.SourceRevision,
                        source.Asset.Metadata.PixelSize));
            IReadOnlyList<RoiObject> propagated = RoiPropagationService.PropagatePolygon(
                reference,
                group,
                sourceContexts);
            IReadOnlyDictionary<Guid, int[]> framesByAsset = SelectedChannelGroup?.Model.Members
                .GroupBy(member => member.AssetId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(member => member.PlaneSelector.FrameIndex)
                        .Distinct()
                        .Order()
                        .ToArray()) ?? new Dictionary<Guid, int[]>();
            var bundle = new List<RoiObject> { reference };
            foreach (RoiObject propagatedRoi in propagated)
            {
                int[] frames = framesByAsset.GetValueOrDefault(propagatedRoi.AssetId!.Value) ??
                    [propagatedRoi.FrameIndex];
                for (int index = 0; index < frames.Length; index++)
                {
                    Guid targetRoiId = index == 0 ? propagatedRoi.Id : Guid.NewGuid();
                    bundle.Add((propagatedRoi with
                    {
                        Id = targetRoiId,
                        FrameIndex = frames[index],
                        Propagation = propagatedRoi.Propagation is { } propagation
                            ? propagation with { TargetRoiId = targetRoiId }
                            : null,
                    }).EnsureValid());
                }
            }
            foreach (RoiObject roi in bundle)
            {
                Rois.Add(new RoiObjectItemViewModel(roi, GetAssetName(roi.AssetId!.Value)));
            }

            SelectedRoi = Rois.LastOrDefault();
            int reviewCount = propagated.Count(roi =>
                roi.Validity.State is ScientificValidityState.Warning or ScientificValidityState.ReviewRequired);
            int outsideCount = propagated.Count(roi => roi.Validity.State == ScientificValidityState.Invalid);
            StatusText =
                $"已创建 reference polygon 并通过 {propagated.Count} 个 SpatialMapping 传播；" +
                $"部分越界待复核 {reviewCount}，完全越界 {outsideCount}。";
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

    private void ProjectSelectedRoiToFigure()
    {
        try
        {
            RoiObject roi = SelectedRoi?.Model ??
                throw new InvalidOperationException("请先选择一个 canonical ROI。");
            FigureRoiProjectionViewModel projection = _figure.AddRoiProjection(roi);
            StatusText =
                $"已创建 Figure ROI projection {projection.Id.ToString("N")[..8]}；" +
                $"仅保存 ROI {projection.RoiId.ToString("N")[..8]} 与 Panel {projection.PanelId.ToString("N")[..8]} 的引用，不复制几何。";
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or ArgumentException)
        {
            StatusText = exception.Message;
        }
    }

    private bool TryReplaceSelectedGeometry(
        IReadOnlyList<MeasurementPoint> sourceGeometry,
        string operation)
    {
        if (SelectedRoi is not { } selected ||
            selected.Model.AssetId is not Guid assetId ||
            _sources.FirstOrDefault(source => source.Asset.Id == assetId) is not { } source)
        {
            return false;
        }

        try
        {
            RoiObject candidate = (selected.Model with
            {
                SourceGeometry = sourceGeometry.ToArray(),
            }).EnsureValid();
            RoiGeometryValidationResult validation = RoiGeometryValidator.Validate(
                candidate,
                source.Asset.Metadata.PixelSize);
            RoiBoundaryPolicyResult policy = RoiOutOfBoundsPolicy.Evaluate(
                validation,
                RoiBoundaryRole.Reference);
            if (!policy.CanPersist || !policy.CanAnalyze)
            {
                StatusText = $"{operation}已拒绝：{string.Join(" ", policy.Validity.Reasons)}";
                return false;
            }

            RoiObject updated = (candidate with { Validity = policy.Validity }).EnsureValid();
            _figure.ValidateRoiProjectionSource(updated);
            selected.UpdateModel(updated);
            _figure.RefreshRoiProjectionSource(updated);
            StatusText = $"{operation} canonical ROI；几何仍为 source pixels，未执行边界 clamp。";
            Changed?.Invoke(this, EventArgs.Empty);
            return true;
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException)
        {
            StatusText = $"{operation}已拒绝：{exception.Message}";
            return false;
        }
    }

    private static int FindNearestPolygonSegment(
        IReadOnlyList<MeasurementPoint> points,
        MeasurementPoint target)
    {
        int nearest = 0;
        double bestDistanceSquared = double.PositiveInfinity;
        for (int index = 0; index < points.Count; index++)
        {
            MeasurementPoint start = points[index];
            MeasurementPoint end = points[(index + 1) % points.Count];
            double distanceSquared = DistanceToSegmentSquared(target, start, end);
            if (distanceSquared < bestDistanceSquared)
            {
                bestDistanceSquared = distanceSquared;
                nearest = index;
            }
        }

        return nearest;
    }

    private static double DistanceToSegmentSquared(
        MeasurementPoint point,
        MeasurementPoint start,
        MeasurementPoint end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= double.Epsilon)
        {
            deltaX = point.X - start.X;
            deltaY = point.Y - start.Y;
            return deltaX * deltaX + deltaY * deltaY;
        }

        double t = Math.Clamp(
            ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared,
            0,
            1);
        double projectedX = start.X + t * deltaX;
        double projectedY = start.Y + t * deltaY;
        double offsetX = point.X - projectedX;
        double offsetY = point.Y - projectedY;
        return offsetX * offsetX + offsetY * offsetY;
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

public sealed class RoiObjectItemViewModel : ObservableObject
{
    private RoiObject _model;
    private bool _isSelected;

    public RoiObjectItemViewModel(RoiObject model, string assetName)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        AssetName = assetName;
    }

    public RoiObject Model => _model;

    public string AssetName { get; }

    public Guid AssetId => Model.AssetId ?? Guid.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        internal set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(SelectionVisibility));
            }
        }
    }

    public Visibility SelectionVisibility => IsSelected ? Visibility.Visible : Visibility.Collapsed;

    public Visibility RectangleVisibility => Model.GeometryKind == RoiGeometryKind.Rectangle
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility EllipseVisibility => Model.GeometryKind == RoiGeometryKind.Ellipse
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PolygonVisibility => Model.GeometryKind == RoiGeometryKind.Polygon
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility PolylineVisibility => Model.GeometryKind == RoiGeometryKind.Polyline
        ? Visibility.Visible
        : Visibility.Collapsed;

    public double ShapeX => Model.SourceGeometry.Count >= 2
        ? Math.Min(Model.SourceGeometry[0].X, Model.SourceGeometry[1].X)
        : 0;

    public double ShapeY => Model.SourceGeometry.Count >= 2
        ? Math.Min(Model.SourceGeometry[0].Y, Model.SourceGeometry[1].Y)
        : 0;

    public double ShapeWidth => Model.SourceGeometry.Count >= 2
        ? Math.Abs(Model.SourceGeometry[1].X - Model.SourceGeometry[0].X)
        : 0;

    public double ShapeHeight => Model.SourceGeometry.Count >= 2
        ? Math.Abs(Model.SourceGeometry[1].Y - Model.SourceGeometry[0].Y)
        : 0;

    public PointCollection Points
    {
        get
        {
            var points = new PointCollection(
                Model.SourceGeometry.Select(point => new Point(point.X, point.Y)));
            points.Freeze();
            return points;
        }
    }

    public IReadOnlyList<RoiVertexHandleViewModel> VertexHandles => Model.SourceGeometry
        .Select((point, index) => new RoiVertexHandleViewModel(index, point.X - 5, point.Y - 5))
        .ToArray();

    public Brush StrokeBrush => CreateBrush(Model.Style.Shape.StrokeColor, Colors.DeepSkyBlue);

    public Brush FillBrush
    {
        get
        {
            Color color = ParseColor(Model.Style.Shape.FillColor, Colors.DeepSkyBlue);
            color.A = (byte)Math.Round(
                color.A * Model.Style.Shape.FillOpacityPercent / 100.0);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    public double StrokeWidth => Math.Max(0.5, Model.Style.Shape.StrokeWidthPt);

    public string Label => Model.Style.Label ?? "Polygon ROI";

    public string GeometryText => $"{Model.SourceGeometry.Count} points · source px";

    public string ProvenanceText => Model.Propagation is null
        ? $"Reference · revision {Model.SourceRevision} · {Model.Validity.State}"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Mapping {Model.Propagation.MappingId.ToString("N")[..8]} · revision {Model.SourceRevision} · coverage {Model.Propagation.TargetCoverageFraction:0.###} · {Model.Validity.State}");

    internal void UpdateModel(RoiObject model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        OnPropertyChanged(nameof(Model));
        OnPropertyChanged(nameof(AssetId));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(GeometryText));
        OnPropertyChanged(nameof(ProvenanceText));
        OnPropertyChanged(nameof(RectangleVisibility));
        OnPropertyChanged(nameof(EllipseVisibility));
        OnPropertyChanged(nameof(PolygonVisibility));
        OnPropertyChanged(nameof(PolylineVisibility));
        OnPropertyChanged(nameof(ShapeX));
        OnPropertyChanged(nameof(ShapeY));
        OnPropertyChanged(nameof(ShapeWidth));
        OnPropertyChanged(nameof(ShapeHeight));
        OnPropertyChanged(nameof(Points));
        OnPropertyChanged(nameof(VertexHandles));
        OnPropertyChanged(nameof(StrokeBrush));
        OnPropertyChanged(nameof(FillBrush));
        OnPropertyChanged(nameof(StrokeWidth));
    }

    private static Brush CreateBrush(string value, Color fallback)
    {
        var brush = new SolidColorBrush(ParseColor(value, fallback));
        brush.Freeze();
        return brush;
    }

    private static Color ParseColor(string value, Color fallback)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(value);
        }
        catch (FormatException)
        {
            return fallback;
        }
    }
}

public sealed record RoiVertexHandleViewModel(int Index, double X, double Y);

public sealed class CrossChannelRoiStatisticsItemViewModel(CrossChannelRoiStatisticsEntry entry)
{
    public string ChannelName => entry.ChannelMember.Name;

    public string Summary => string.Create(
        CultureInfo.InvariantCulture,
        $"n={entry.Statistics.PixelCount} · mean={entry.Statistics.Mean:0.###} · min={entry.Statistics.Minimum:0.###} · max={entry.Statistics.Maximum:0.###}");

    public string Provenance => string.Create(
        CultureInfo.InvariantCulture,
        $"raw {entry.Statistics.SourceBitDepth}-bit · channel {entry.ChannelMember.ChannelId.ToString("N")[..8]} · coverage {entry.Statistics.CoverageFraction:0.###}{(entry.Statistics.ClippedToImage ? " · clipped/review" : string.Empty)}");
}
