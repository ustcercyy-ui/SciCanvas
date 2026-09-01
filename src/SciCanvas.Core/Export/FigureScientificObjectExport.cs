using SciCanvas.Core.Workspace;
using SciCanvas.Core.Channels;

namespace SciCanvas.Core.Export;

/// <summary>Scientific figure objects are distinct from generic editorial annotations.</summary>
public enum FigureScientificObjectKind
{
    PolygonAnnotation,
    DirectionMarker,
    Colorbar,
    ChannelLegend,
}

public sealed record FigureScientificPoint(double X, double Y)
{
    public void EnsureValid(int canvasWidth, int canvasHeight)
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) ||
            X < 0 || X > canvasWidth || Y < 0 || Y > canvasHeight)
        {
            throw new InvalidOperationException("科研对象坐标必须是画布内的有限值。");
        }
    }
}

public sealed record FigureChannelLegendEntry(
    string Label,
    string Color,
    Guid? ChannelId = null)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Label) || Label.Length > 128 ||
            !ScientificStyleColor.ValidateColor(Color) || ChannelId == Guid.Empty)
        {
            throw new InvalidOperationException("通道图例必须包含不超过 128 个字符的标签和有效颜色。");
        }
    }
}

public sealed record FigureColorbarExportSpec(
    double Minimum,
    double Maximum,
    string Unit,
    string Colormap,
    Guid? ChannelId,
    ColorbarBindingState BindingState,
    FigureObjectOrientation Orientation,
    IReadOnlyList<ColorbarTick> Ticks)
{
    public FigureColorbarExportSpec EnsureValid()
    {
        _ = new ColorbarObject
        {
            Id = Guid.NewGuid(),
            Minimum = Minimum,
            Maximum = Maximum,
            Unit = Unit,
            Colormap = Colormap,
            ChannelId = ChannelId,
            BindingState = BindingState,
            Orientation = Orientation,
            Ticks = Ticks,
        }.EnsureValid();
        return this;
    }
}

public sealed record FigureChannelLegendExportSpec(
    IReadOnlyList<FigureChannelLegendEntry> Items,
    string FontFamily,
    double FontSizePt,
    bool IsBold,
    string TextColor,
    string BackgroundColor,
    double BackgroundOpacityPercent,
    string BorderColor,
    double BorderWidthPt,
    double PaddingPixels)
{
    public FigureChannelLegendExportSpec EnsureValid()
    {
        var model = new ChannelLegendObject
        {
            Id = Guid.NewGuid(),
            Items = Items.Select(item => new ChannelLegendItem(
                item.ChannelId,
                item.Label,
                item.Color)).ToArray(),
            TextStyle = new TextStyle(FontFamily, FontSizePt, IsBold, TextColor),
            ContainerStyle = new ShapeStyle(
                BorderColor,
                BackgroundColor,
                BackgroundOpacityPercent,
                BorderWidthPt),
            PaddingPixels = PaddingPixels,
        }.EnsureValid();
        _ = model;
        return this;
    }
}

/// <summary>
/// Canonical export representation for scientific figure objects. Geometry is in final
/// figure pixels so the same object renders identically in preview, raster and vector output.
/// </summary>
public sealed record FigureScientificObjectExportItem(
    Guid Id,
    FigureScientificObjectKind Kind,
    IReadOnlyList<FigureScientificPoint> Points,
    string Label,
    string StrokeColor,
    string FillColor,
    double FillOpacityPercent,
    string TextColor,
    string FontFamily,
    double FontSizePt,
    double StrokeWidthPt,
    bool IsBold,
    bool IsVisible,
    int ZIndex,
    double Minimum = 0,
    double Maximum = 1,
    string Unit = "",
    string Colormap = "viridis",
    IReadOnlyList<FigureChannelLegendEntry>? ChannelLegendEntries = null,
    Guid? SourceAssetId = null,
    long? SourceRevision = null,
    Guid? ChannelId = null,
    FigureColorbarExportSpec? Colorbar = null,
    FigureChannelLegendExportSpec? ChannelLegend = null)
{
    public IReadOnlyList<FigureChannelLegendEntry> EffectiveChannelLegendEntries =>
        EffectiveChannelLegend?.Items ?? ChannelLegendEntries ?? [];

    public FigureColorbarExportSpec? EffectiveColorbar =>
        Kind != FigureScientificObjectKind.Colorbar
            ? null
            : Colorbar ?? new FigureColorbarExportSpec(
                Minimum,
                Maximum,
                Unit,
                Colormap,
                ChannelId,
                ChannelId is null ? ColorbarBindingState.Detached : ColorbarBindingState.Linked,
                FigureObjectOrientation.Vertical,
                ColorbarObject.CreateDefaultTicks(Minimum, Maximum));

    public FigureChannelLegendExportSpec? EffectiveChannelLegend =>
        Kind != FigureScientificObjectKind.ChannelLegend
            ? null
            : ChannelLegend ?? new FigureChannelLegendExportSpec(
                ChannelLegendEntries ?? [],
                FontFamily,
                FontSizePt,
                IsBold,
                TextColor,
                FillColor,
                FillOpacityPercent,
                StrokeColor,
                StrokeWidthPt,
                5);

    public void EnsureValid(int canvasWidth, int canvasHeight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(canvasHeight);
        ArgumentNullException.ThrowIfNull(Points);
        if (Id == Guid.Empty ||
            !ScientificStyleColor.ValidateColor(StrokeColor) ||
            !ScientificStyleColor.ValidateColor(FillColor) ||
            !ScientificStyleColor.ValidateColor(TextColor) ||
            !double.IsFinite(FillOpacityPercent) || FillOpacityPercent is < 0 or > 100 ||
            string.IsNullOrWhiteSpace(FontFamily) || FontFamily.Length > 128 ||
            !double.IsFinite(FontSizePt) || FontSizePt is < 4 or > 72 ||
            !double.IsFinite(StrokeWidthPt) || StrokeWidthPt is < 0.25 or > 10 ||
            !Enum.IsDefined(Kind) ||
            SourceAssetId == Guid.Empty || SourceRevision is < 1 || ChannelId == Guid.Empty ||
            SourceAssetId.HasValue != SourceRevision.HasValue)
        {
            throw new InvalidOperationException("科研对象的样式或元数据无效。");
        }

        foreach (FigureScientificPoint point in Points)
        {
            point.EnsureValid(canvasWidth, canvasHeight);
        }

        switch (Kind)
        {
            case FigureScientificObjectKind.PolygonAnnotation:
                if (Points.Count < 3 || Math.Abs(SignedArea(Points)) < 12.5)
                {
                    throw new InvalidOperationException("多边形标注至少需要三个不共线顶点。");
                }
                break;
            case FigureScientificObjectKind.DirectionMarker:
                if (Points.Count != 2 || Distance(Points[0], Points[1]) < 5)
                {
                    throw new InvalidOperationException("方向标记需要两个相距至少 5 px 的端点。");
                }
                break;
            case FigureScientificObjectKind.Colorbar:
                ValidateBounds("色条");
                EffectiveColorbar!.EnsureValid();
                break;
            case FigureScientificObjectKind.ChannelLegend:
                ValidateBounds("通道图例");
                EffectiveChannelLegend!.EnsureValid();
                break;
            default:
                throw new InvalidOperationException("未知科研对象类型。");
        }
    }

    public static bool IsSupportedColormap(string value) =>
        ScientificColormap.IsSupported(value);

    private void ValidateBounds(string displayName)
    {
        if (Points.Count != 2 || Points[1].X - Points[0].X < 12 || Points[1].Y - Points[0].Y < 12)
        {
            throw new InvalidOperationException($"{displayName}需要两个形成至少 12 × 12 px 区域的角点。");
        }
    }

    private static double SignedArea(IReadOnlyList<FigureScientificPoint> points)
    {
        double area = 0;
        for (int index = 0; index < points.Count; index++)
        {
            FigureScientificPoint current = points[index];
            FigureScientificPoint next = points[(index + 1) % points.Count];
            area += current.X * next.Y - next.X * current.Y;
        }

        return area / 2;
    }

    private static double Distance(FigureScientificPoint first, FigureScientificPoint second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));
}
