using SciCanvas.Core.Data;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Core.Export;

/// <summary>
/// Optional object-level typography overrides for a Plot used as a Figure panel.
/// A null member inherits the canonical panel annotation style, which in turn
/// inherits the effective Figure/Project style.
/// </summary>
public sealed record FigurePlotTypographyOverride(
    TextStyle? Axis = null,
    TextStyle? Tick = null,
    TextStyle? Legend = null,
    TextStyle? Annotation = null)
{
    public bool IsEmpty =>
        Axis is null && Tick is null && Legend is null && Annotation is null;

    public static FigurePlotTypographyOverride FromPlot(PlotTypography typography)
    {
        ArgumentNullException.ThrowIfNull(typography);
        typography.EnsureValid();
        return new(typography.Axis, typography.Tick, typography.Legend, typography.Annotation);
    }

    public FigurePlotTypographyOverride EnsureValid()
    {
        Axis?.EnsureValid();
        Tick?.EnsureValid();
        Legend?.EnsureValid();
        Annotation?.EnsureValid();
        return this;
    }
}

public sealed record ResolvedFigurePlotTypography(
    ResolvedStyleValue<TextStyle> Axis,
    ResolvedStyleValue<TextStyle> Tick,
    ResolvedStyleValue<TextStyle> Legend,
    ResolvedStyleValue<TextStyle> Annotation)
{
    public PlotTypography Value => new(
        Axis.Value,
        Tick.Value,
        Legend.Value,
        Annotation.Value);
}

/// <summary>
/// Immutable render snapshot for a Plot placed directly in a Figure. The item
/// carries projected values rather than a raster screenshot or a mutable table.
/// </summary>
public sealed record FigurePlotPanelExportItem(
    Guid PanelId,
    PlotObject Plot,
    PlotDataProjection Projection,
    PixelRect64 DestinationRect,
    string Label,
    bool IsVisible,
    StyleOverride? StyleOverride = null,
    FigurePlotTypographyOverride? TypographyOverride = null,
    int ZIndex = 0)
{
    public static FigurePlotPanelExportItem Create(
        PlotObject plot,
        TabularDataAsset asset,
        PixelRect64 destinationRect,
        string label,
        bool isVisible = true,
        StyleOverride? styleOverride = null,
        FigurePlotTypographyOverride? typographyOverride = null,
        int zIndex = 0,
        Guid panelId = default)
    {
        ArgumentNullException.ThrowIfNull(plot);
        ArgumentNullException.ThrowIfNull(asset);
        plot.EnsureValid(asset);
        PlotDataProjection projection = PlotDataProjector.Project(plot, asset);
        return new FigurePlotPanelExportItem(
            panelId == Guid.Empty ? Guid.NewGuid() : panelId,
            plot,
            projection,
            destinationRect,
            label,
            isVisible,
            styleOverride,
            typographyOverride,
            zIndex)
            .EnsureValid();
    }

    public FigurePlotPanelExportItem EnsureValid()
    {
        if (PanelId == Guid.Empty || Plot is null || Projection is null ||
            Label is null || Label.Trim().Length > 128 ||
            DestinationRect.Width <= 0 || DestinationRect.Height <= 0 ||
            !Enum.IsDefined(Plot.PlotType) || Plot.Id == Guid.Empty ||
            Plot.Data is null || Plot.Data.DataAssetId == Guid.Empty ||
            Plot.Data.SourceRevision < 1 ||
            Projection.SourceRowCount < 0 || Projection.IncludedRowCount < 0 ||
            Projection.ExcludedRowCount < 0 || Projection.UnplottableRowCount < 0 ||
            Projection.SourceRowCount != Projection.IncludedRowCount + Projection.ExcludedRowCount ||
            Projection.Rows.Count != Projection.IncludedRowCount ||
            Projection.UnplottableRowCount > Projection.IncludedRowCount ||
            Projection.AppliedTransforms.Count != Plot.Transforms.Count)
        {
            throw new InvalidOperationException("Figure Plot panel 快照无效或与投影行数不一致。");
        }

        ArgumentNullException.ThrowIfNull(Plot.XAxis);
        ArgumentNullException.ThrowIfNull(Plot.YAxis);
        ArgumentNullException.ThrowIfNull(Plot.Typography);
        ArgumentNullException.ThrowIfNull(Plot.Style);
        Plot.XAxis.EnsureValid();
        Plot.YAxis.EnsureValid();
        Plot.Typography.EnsureValid();
        Plot.Style.EnsureValid();
        StyleOverride?.EnsureValid();
        TypographyOverride?.EnsureValid();
        return this;
    }

    public ResolvedFigurePlotTypography ResolveTypography(FigureGlobalStyle figureStyle)
        => ResolveTypography(figureStyle, StyleOverride, TypographyOverride);

    public static ResolvedFigurePlotTypography ResolveTypography(
        FigureGlobalStyle figureStyle,
        StyleOverride? styleOverride,
        FigurePlotTypographyOverride? typographyOverride)
    {
        ArgumentNullException.ThrowIfNull(figureStyle);
        figureStyle.EnsureValid();
        styleOverride?.EnsureValid();
        typographyOverride?.EnsureValid();

        var figureText = new TextStyle(
            figureStyle.FontFamily,
            figureStyle.FontSizePt,
            false,
            figureStyle.TextColor);
        TextStyle inherited = styleOverride?.Annotation ?? figureText;
        StyleInheritanceSource inheritedSource = styleOverride?.Annotation is null
            ? StyleInheritanceSource.Figure
            : StyleInheritanceSource.Panel;

        ResolvedStyleValue<TextStyle> Resolve(TextStyle? objectValue) =>
            objectValue is null
                ? new(inherited, inheritedSource)
                : new(objectValue, StyleInheritanceSource.Object);

        return new ResolvedFigurePlotTypography(
            Resolve(typographyOverride?.Axis),
            Resolve(typographyOverride?.Tick),
            Resolve(typographyOverride?.Legend),
            Resolve(typographyOverride?.Annotation));
    }
}
