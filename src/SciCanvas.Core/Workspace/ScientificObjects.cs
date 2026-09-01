using SciCanvas.Core.Export;
using SciCanvas.Core.Science;

namespace SciCanvas.Core.Workspace;

public enum ScientificObjectKind
{
    ScaleBar,
    Measurement,
    AnalysisResult,
    Roi,
    Inset,
    Colorbar,
    PanelLabel,
    DirectionMarker,
    Annotation,
    MeasurementOverlay,
    RoiFigureProjection,
    ChannelLegend,
}

public abstract record ScientificObject
{
    public required Guid Id { get; init; }

    public abstract ScientificObjectKind Kind { get; }

    public Guid? AssetId { get; init; }

    public Guid? PanelId { get; init; }

    public long? SourceRevision { get; init; }

    public StyleOverride? StyleOverride { get; init; }

    public ScientificValidity Validity { get; init; } = ScientificValidity.Valid;
}

public sealed record ScaleBarPlacement(
    ScaleBarAnchor Anchor,
    double OffsetXMm,
    double OffsetYMm);

public sealed record ScaleBarObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.ScaleBar;

    public required double PhysicalLength { get; init; }

    public required string Unit { get; init; }

    public required ScaleBarPlacement Placement { get; init; }

    public string? StyleRef { get; init; }
}

public sealed record MeasurementObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Measurement;

    public required ScientificMeasurement Measurement { get; init; }

    public string? CalibrationRevision { get; init; }

    public (double Value, string Unit)? CachedValue { get; init; }
}


/// <summary>
/// A figure-level scientific object which pins a source measurement to one panel
/// without turning the measurement into a presentation-only annotation.
/// </summary>
public sealed record MeasurementOverlayObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.MeasurementOverlay;

    public required Guid MeasurementId { get; init; }

    public required ScientificMeasurement SourceGeometry { get; init; }

    public FigureMeasurementCalibrationRelationship? CalibrationRelationship { get; init; }

    public required FigureMeasurementOverlayStyle Style { get; init; }

    public string? LabelOverride { get; init; }

    public bool IsVisible { get; init; } = true;

    public int ZIndex { get; init; }
}
public sealed record AnalysisResultObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.AnalysisResult;

    public required ScientificImageAnalysisResult Result { get; init; }
}

public enum RoiGeometryKind
{
    Rectangle,
    Ellipse,
    Polygon,
    Polyline,
}

public sealed record RoiStyle
{
    public RoiStyle(ShapeStyle shape, TextStyle labelStyle, string? label = null)
    {
        Shape = shape ?? throw new ArgumentNullException(nameof(shape));
        LabelStyle = labelStyle ?? throw new ArgumentNullException(nameof(labelStyle));
        Label = label;
    }

    /// <summary>Compatibility constructor for schema 2.4 and earlier flat ROI styles.</summary>
    public RoiStyle(
        string strokeColor,
        double strokeWidth,
        string fillColor,
        double fillOpacity,
        string? label = null,
        string? labelFont = null,
        string? labelColor = null,
        double labelFontSizePt = 7,
        bool labelIsBold = false)
        : this(
            new ShapeStyle(strokeColor, fillColor, fillOpacity * 100, strokeWidth),
            new TextStyle(
                string.IsNullOrWhiteSpace(labelFont) ? "Arial" : labelFont.Trim(),
                labelFontSizePt,
                labelIsBold,
                string.IsNullOrWhiteSpace(labelColor) ? strokeColor : labelColor),
            label)
    {
    }

    public static RoiStyle Default { get; } = new(
        new ShapeStyle("#FF22C7E8", "#FF22C7E8", 12, 2),
        new TextStyle("Arial", 7, false, "#FF22C7E8"));

    public ShapeStyle Shape { get; init; }

    public TextStyle LabelStyle { get; init; }

    public string? Label { get; init; }

    public string StrokeColor => Shape.StrokeColor;

    public double StrokeWidth => Shape.StrokeWidthPt;

    public string FillColor => Shape.FillColor;

    public double FillOpacity => Shape.FillOpacityPercent / 100.0;

    public string LabelFont => LabelStyle.FontFamily;

    public string LabelFontFamily => LabelStyle.FontFamily;

    public double LabelFontSizePt => LabelStyle.FontSizePt;

    public bool LabelIsBold => LabelStyle.IsBold;

    public string LabelColor => LabelStyle.Color;

    public RoiStyle EnsureValid()
    {
        Shape.EnsureValid();
        LabelStyle.EnsureValid();
        if (Label?.Length > 256)
        {
            throw new InvalidOperationException("ROI label 不能超过 256 个字符。");
        }

        return this;
    }
}

/// <summary>
/// Figure display reference for a canonical ROI. It intentionally stores no ROI geometry:
/// source-pixel geometry remains owned solely by <see cref="RoiObject"/>.
/// </summary>
public sealed record RoiFigureProjectionObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.RoiFigureProjection;

    public required Guid RoiId { get; init; }

    public bool IsVisible { get; init; } = true;

    public int ZIndex { get; init; }

    public RoiFigureProjectionObject EnsureValid(RoiObject roi, FigurePanel panel)
    {
        ArgumentNullException.ThrowIfNull(roi);
        ArgumentNullException.ThrowIfNull(panel);
        roi.EnsureValid();
        if (Id == Guid.Empty || RoiId == Guid.Empty || RoiId != roi.Id ||
            PanelId is not Guid panelId || panelId == Guid.Empty || panelId != panel.Id ||
            AssetId is not Guid assetId || assetId == Guid.Empty || assetId != roi.AssetId ||
            panel.AssetId != assetId ||
            SourceRevision is not long revision || revision < 1 || revision != roi.SourceRevision ||
            panel.FrameIndex != roi.FrameIndex)
        {
            throw new InvalidOperationException(
                "ROI Figure Projection 必须引用同一 ROI、Panel、Asset、source revision 和 frame。");
        }

        StyleOverride?.EnsureValid();
        return this;
    }
}

public sealed record RoiPropagationProvenance(
    Guid ReferenceRoiId,
    Guid TargetRoiId,
    Guid LinkGroupId,
    Guid MappingId,
    double TargetCoverageFraction = 1)
{
    public RoiPropagationProvenance EnsureValid()
    {
        if (ReferenceRoiId == Guid.Empty || TargetRoiId == Guid.Empty ||
            LinkGroupId == Guid.Empty || MappingId == Guid.Empty ||
            ReferenceRoiId == TargetRoiId ||
            !double.IsFinite(TargetCoverageFraction) ||
            TargetCoverageFraction is < 0 or > 1)
        {
            throw new InvalidOperationException(
                "ROI propagation provenance 缺少有效 ROI、LinkGroup、Mapping ID 或 target coverage fraction。");
        }

        return this;
    }
}

/// <summary>Canonical ROI geometry is always stored in source pixel coordinates.</summary>
public sealed record RoiObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Roi;

    public required IReadOnlyList<MeasurementPoint> SourceGeometry { get; init; }

    public RoiGeometryKind GeometryKind { get; init; } = RoiGeometryKind.Polygon;

    public int FrameIndex { get; init; }

    public RoiStyle Style { get; init; } = RoiStyle.Default;

    public RoiPropagationProvenance? Propagation { get; init; }

    public RoiObject EnsureValid()
    {
        int minimumPoints = GeometryKind switch
        {
            RoiGeometryKind.Rectangle or RoiGeometryKind.Ellipse => 2,
            RoiGeometryKind.Polygon => 3,
            RoiGeometryKind.Polyline => 2,
            _ => int.MaxValue,
        };
        if (Id == Guid.Empty || AssetId is not Guid assetId || assetId == Guid.Empty ||
            SourceRevision is not long revision || revision < 1 || FrameIndex < 0 ||
            !Enum.IsDefined(GeometryKind) || SourceGeometry.Count < minimumPoints ||
            SourceGeometry.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)))
        {
            throw new InvalidOperationException("Canonical ROI 必须绑定素材/revision/frame，并保存有效 source-pixel geometry。");
        }

        Style.EnsureValid();
        if (Propagation is not null)
        {
            Propagation.EnsureValid();
            if (Propagation.TargetRoiId != Id)
            {
                throw new InvalidOperationException("ROI propagation 的 TargetRoiId 必须等于当前 ROI ID。");
            }

            if (Propagation.TargetCoverageFraction == 0 &&
                Validity.State != ScientificValidityState.Invalid)
            {
                throw new InvalidOperationException("完全位于目标图像外的 propagated ROI 必须标记为 Invalid。");
            }

            if (Propagation.TargetCoverageFraction is > 0 and < 1 &&
                Validity.State is not (ScientificValidityState.Warning or ScientificValidityState.ReviewRequired))
            {
                throw new InvalidOperationException("部分越界的 propagated ROI 必须标记为 Warning 或 ReviewRequired。");
            }
        }

        return this;
    }
}

public sealed record InsetObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Inset;

    public required Guid RoiObjectId { get; init; }

    public required Guid InsetPanelId { get; init; }
}

public enum FigureObjectOrientation
{
    Vertical,
    Horizontal,
}

public enum ColorbarBindingState
{
    Detached,
    Linked,
}

public sealed record ColorbarTick(double Value, string Label)
{
    public ColorbarTick EnsureValid()
    {
        if (!double.IsFinite(Value) || string.IsNullOrWhiteSpace(Label) || Label.Length > 64)
        {
            throw new InvalidOperationException("Colorbar tick 必须包含有限数值和不超过 64 字符的标签。");
        }

        return this;
    }
}

public sealed record ColorbarObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Colorbar;

    public required double Minimum { get; init; }

    public required double Maximum { get; init; }

    public required string Unit { get; init; }

    public required string Colormap { get; init; }

    public Guid? ChannelId { get; init; }

    public ColorbarBindingState BindingState { get; init; } = ColorbarBindingState.Detached;

    public FigureObjectOrientation Orientation { get; init; } = FigureObjectOrientation.Vertical;

    public IReadOnlyList<ColorbarTick> Ticks { get; init; } = [];

    public ColorbarObject EnsureValid()
    {
        if (Id == Guid.Empty || !double.IsFinite(Minimum) || !double.IsFinite(Maximum) ||
            Maximum <= Minimum || string.IsNullOrWhiteSpace(Unit) || Unit.Length > 64 ||
            !SciCanvas.Core.Channels.ScientificColormap.IsSupported(Colormap) ||
            !Enum.IsDefined(BindingState) || !Enum.IsDefined(Orientation) ||
            ChannelId == Guid.Empty ||
            (BindingState == ColorbarBindingState.Linked && ChannelId is null) ||
            Ticks.Count < 2)
        {
            throw new InvalidOperationException(
                "Canonical Colorbar 必须包含有效范围、单位、colormap、绑定状态、方向及至少两个 ticks。");
        }

        foreach (ColorbarTick tick in Ticks)
        {
            tick.EnsureValid();
        }

        if (Ticks.Any(tick => tick.Value < Minimum || tick.Value > Maximum) ||
            Ticks.Zip(Ticks.Skip(1), (left, right) => right.Value > left.Value).Any(increasing => !increasing))
        {
            throw new InvalidOperationException("Colorbar ticks 必须位于显示范围内并严格递增。");
        }

        StyleOverride?.EnsureValid();
        return this;
    }

    public static IReadOnlyList<ColorbarTick> CreateDefaultTicks(
        double minimum,
        double maximum,
        int count = 5)
    {
        if (!double.IsFinite(minimum) || !double.IsFinite(maximum) || maximum <= minimum ||
            count is < 2 or > 20)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        return Enumerable.Range(0, count)
            .Select(index =>
            {
                double value = minimum + (maximum - minimum) * index / (count - 1);
                return new ColorbarTick(value, value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture));
            })
            .ToArray();
    }
}

public sealed record ChannelLegendItem(
    Guid? ChannelId,
    string Label,
    string Color)
{
    public ChannelLegendItem EnsureValid()
    {
        if (ChannelId == Guid.Empty || string.IsNullOrWhiteSpace(Label) || Label.Length > 128 ||
            !ScientificStyleColor.ValidateColor(Color))
        {
            throw new InvalidOperationException(
                "Channel Legend item 必须包含可选有效 channel、标签和颜色。");
        }

        return this;
    }
}

public sealed record ChannelLegendObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.ChannelLegend;

    public required IReadOnlyList<ChannelLegendItem> Items { get; init; }

    public required TextStyle TextStyle { get; init; }

    public required ShapeStyle ContainerStyle { get; init; }

    public double PaddingPixels { get; init; } = 5;

    public ChannelLegendObject EnsureValid()
    {
        if (Id == Guid.Empty || Items.Count == 0 ||
            Items.Select(item => item.ChannelId)
                .Where(channelId => channelId.HasValue)
                .Distinct().Count() != Items.Count(item => item.ChannelId.HasValue) ||
            !double.IsFinite(PaddingPixels) || PaddingPixels is < 0 or > 100)
        {
            throw new InvalidOperationException(
                "Canonical Channel Legend 必须包含唯一 items 和 0–100 px padding。");
        }

        foreach (ChannelLegendItem item in Items)
        {
            item.EnsureValid();
        }

        TextStyle.EnsureValid();
        ContainerStyle.EnsureValid();
        StyleOverride?.EnsureValid();
        return this;
    }
}

public sealed record PanelLabelObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.PanelLabel;

    public required string Text { get; init; }
}

public sealed record DirectionMarkerObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.DirectionMarker;

    public required string Label { get; init; }

    public required NormalizedPoint Start { get; init; }

    public required NormalizedPoint End { get; init; }
}

public sealed record AnnotationObject : ScientificObject
{
    public override ScientificObjectKind Kind => ScientificObjectKind.Annotation;

    public required string AnnotationKind { get; init; }

    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<NormalizedPoint> PanelGeometry { get; init; } = [];
}

public sealed record LinkGroup(
    Guid Id,
    IReadOnlyList<Guid> PanelIds,
    LinkSyncOptions Sync);

public sealed record LinkSyncOptions(
    bool Pan = false,
    bool Zoom = false,
    bool Crop = false,
    bool Roi = false,
    bool ColorScale = false);

public enum SpatialMappingKind
{
    Identity,
    Affine,
}

public sealed record SpatialMapping(
    Guid SourceAssetId,
    Guid TargetAssetId,
    SpatialMappingKind Kind,
    IReadOnlyList<double> Matrix);
