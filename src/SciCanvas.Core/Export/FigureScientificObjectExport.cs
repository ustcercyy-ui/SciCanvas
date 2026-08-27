using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

/// <summary>Scientific figure objects are distinct from generic editorial annotations.</summary>
public enum FigureScientificObjectKind
{
    PolygonAnnotation,
    Roi,
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

public sealed record FigureChannelLegendEntry(string Label, string Color)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(Label) || Label.Length > 128 ||
            !ScientificStyleColor.ValidateColor(Color))
        {
            throw new InvalidOperationException("通道图例必须包含不超过 128 个字符的标签和有效颜色。");
        }
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
    IReadOnlyList<FigureChannelLegendEntry>? ChannelLegendEntries = null)
{
    public IReadOnlyList<FigureChannelLegendEntry> EffectiveChannelLegendEntries =>
        ChannelLegendEntries ?? [];

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
            !Enum.IsDefined(Kind))
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
            case FigureScientificObjectKind.Roi:
                if (Points.Count < 3 || Math.Abs(SignedArea(Points)) < 12.5)
                {
                    throw new InvalidOperationException("多边形标注或 ROI 至少需要三个不共线顶点。");
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
                if (!double.IsFinite(Minimum) || !double.IsFinite(Maximum) || Maximum <= Minimum ||
                    string.IsNullOrWhiteSpace(Unit) || !IsSupportedColormap(Colormap))
                {
                    throw new InvalidOperationException("色条需要递增的有限范围、单位和受支持的 colormap。");
                }
                break;
            case FigureScientificObjectKind.ChannelLegend:
                ValidateBounds("通道图例");
                if (EffectiveChannelLegendEntries.Count == 0)
                {
                    throw new InvalidOperationException("通道图例至少需要一个通道条目。");
                }
                foreach (FigureChannelLegendEntry entry in EffectiveChannelLegendEntries)
                {
                    entry.EnsureValid();
                }
                break;
            default:
                throw new InvalidOperationException("未知科研对象类型。");
        }
    }

    public static bool IsSupportedColormap(string value) =>
        value is not null && (value.Equals("viridis", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("magma", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("grayscale", StringComparison.OrdinalIgnoreCase));

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