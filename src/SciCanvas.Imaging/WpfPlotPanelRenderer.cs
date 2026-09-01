using System.Globalization;
using System.Windows;
using System.Windows.Media;
using SciCanvas.Core.Export;
using SciCanvas.Core.Plotting;

namespace SciCanvas.Imaging;

internal static class WpfPlotPanelRenderer
{
    public static void Draw(DrawingContext drawing, FigurePlotPanelExportItem panel, FigureGlobalStyle style, int dpi)
    {
        WpfPlotSceneRenderer.Draw(drawing, PlotSceneBuilder.Build(panel, style, dpi));
    }
}

public static class WpfPlotSceneRenderer
{
    public static void Draw(DrawingContext drawing, PlotScene scene)
    {
        ArgumentNullException.ThrowIfNull(drawing);
        ArgumentNullException.ThrowIfNull(scene);
        foreach (PlotPrimitive primitive in scene.Primitives)
        {
            DrawPrimitive(drawing, primitive);
        }
    }

    private static void DrawPrimitive(DrawingContext drawing, PlotPrimitive primitive)
    {
        switch (primitive)
        {
            case PlotLine line:
                drawing.DrawLine(Pen(line.Stroke, line.Width, line.Dash), Point(line.A), Point(line.B));
                break;
            case PlotPolyline polyline:
                DrawPolyline(drawing, polyline);
                break;
            case PlotRectangle rectangle:
                drawing.DrawRectangle(Brush(rectangle.Fill), OptionalPen(rectangle.Stroke, rectangle.Width, rectangle.Dash), Rect(rectangle.Bounds));
                break;
            case PlotHeatmapCell cell:
                drawing.DrawRectangle(Brush(cell.Fill), null, Rect(cell.Bounds));
                break;
            case PlotEllipse ellipse:
                System.Windows.Rect ellipseRect = Rect(ellipse.Bounds);
                drawing.DrawEllipse(Brush(ellipse.Fill), OptionalPen(ellipse.Stroke, ellipse.Width), new Point(ellipseRect.X + ellipseRect.Width / 2, ellipseRect.Y + ellipseRect.Height / 2), ellipseRect.Width / 2, ellipseRect.Height / 2);
                break;
            case PlotPolygon polygon:
                DrawPolygon(drawing, polygon);
                break;
            case PlotText text:
                DrawText(drawing, text);
                break;
            case PlotClipRegion clip:
                drawing.PushClip(new RectangleGeometry(Rect(clip.Bounds)));
                foreach (PlotPrimitive child in clip.Primitives)
                {
                    DrawPrimitive(drawing, child);
                }
                drawing.Pop();
                break;
            default:
                throw new NotSupportedException($"Unsupported Plot primitive: {primitive.GetType().FullName}");
        }
    }

    private static void DrawPolyline(DrawingContext drawing, PlotPolyline polyline)
    {
        if (polyline.Points.Count < 2) return;
        Pen pen = Pen(polyline.Stroke, polyline.Width, polyline.Dash);
        for (int index = 1; index < polyline.Points.Count; index++)
        {
            drawing.DrawLine(pen, Point(polyline.Points[index - 1]), Point(polyline.Points[index]));
        }
    }

    private static void DrawText(DrawingContext drawing, PlotText item)
    {
        var formatted = new FormattedText(
            item.Value,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily(item.Style.FontFamily), FontStyles.Normal, item.Style.IsBold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal),
            item.FontPixels,
            Brush(item.Style.Color)!,
            1.0);
        double x = item.Anchor switch
        {
            PlotTextAnchor.Middle => item.X - formatted.WidthIncludingTrailingWhitespace / 2,
            PlotTextAnchor.End => item.X - formatted.WidthIncludingTrailingWhitespace,
            _ => item.X,
        };
        drawing.DrawText(formatted, new Point(x, item.Y));
    }

    private static void DrawPolygon(DrawingContext drawing, PlotPolygon polygon)
    {
        if (polygon.Points.Count == 0) return;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            context.BeginFigure(Point(polygon.Points[0]), polygon.Fill is not null, true);
            context.PolyLineTo(polygon.Points.Skip(1).Select(Point).ToArray(), true, false);
        }
        geometry.Freeze();
        drawing.DrawGeometry(Brush(polygon.Fill), OptionalPen(polygon.Stroke, polygon.Width), geometry);
    }

    private static Pen Pen(string color, double width, PlotLineStyle style)
    {
        var pen = new Pen(Brush(color), Math.Max(0.25, width))
        {
            DashStyle = style switch
            {
                PlotLineStyle.Dash => DashStyles.Dash,
                PlotLineStyle.Dot => DashStyles.Dot,
                PlotLineStyle.DashDot => DashStyles.DashDot,
                _ => DashStyles.Solid,
            },
        };
        pen.Freeze();
        return pen;
    }

    private static Pen? OptionalPen(string? color, double width, PlotLineStyle style = PlotLineStyle.Solid) =>
        color is null ? null : Pen(color, width, style);

    private static Brush? Brush(string? value)
    {
        if (value is null) return null;
        var brush = new SolidColorBrush(WpfFigureExporter.ParseColor(value));
        brush.Freeze();
        return brush;
    }

    private static Point Point(PlotPoint point) => new(point.X, point.Y);
    private static System.Windows.Rect Rect(PlotRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);
}
