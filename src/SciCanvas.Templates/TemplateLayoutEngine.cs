using SciCanvas.Core.Geometry;

namespace SciCanvas.Templates;

public sealed record TemplateSlotLayout(
    string Id,
    string Label,
    string Role,
    PixelRect64 PixelRect,
    int MinimumEffectiveDpi,
    bool RequireScaleBar,
    string? HelpText);

public sealed record TemplateCanvasLayout(
    string TemplateId,
    string TemplateName,
    int WidthPixels,
    int HeightPixels,
    int Dpi,
    IReadOnlyList<TemplateSlotLayout> Slots);

public static class TemplateLayoutEngine
{
    private const double MillimetersPerInch = 25.4;

    public static TemplateCanvasLayout CreateLayout(FigureTemplateDefinition template)
    {
        ArgumentNullException.ThrowIfNull(template);

        (double widthUnits, double heightUnits, int widthPixels, int heightPixels) =
            ResolveCanvas(template.Canvas);

        TemplateGridDefinition grid = template.Grid;
        double availableWidth = widthUnits - grid.Margin.Left - grid.Margin.Right -
                                grid.GutterX * (grid.Columns - 1);
        double availableHeight = heightUnits - grid.Margin.Top - grid.Margin.Bottom -
                                 grid.GutterY * (grid.Rows - 1);
        if (availableWidth <= 0 || availableHeight <= 0)
        {
            throw new InvalidOperationException("模板边距或间距超过画布范围。");
        }

        double cellWidth = availableWidth / grid.Columns;
        double cellHeight = availableHeight / grid.Rows;
        double xScale = widthPixels / widthUnits;
        double yScale = heightPixels / heightUnits;

        List<TemplateSlotLayout> slots = [];
        foreach (TemplateSlotDefinition slot in template.Slots)
        {
            double left = grid.Margin.Left +
                          (slot.Rect.Column - 1) * (cellWidth + grid.GutterX);
            double top = grid.Margin.Top +
                         (slot.Rect.Row - 1) * (cellHeight + grid.GutterY);
            double width = slot.Rect.ColumnSpan * cellWidth +
                           (slot.Rect.ColumnSpan - 1) * grid.GutterX;
            double height = slot.Rect.RowSpan * cellHeight +
                            (slot.Rect.RowSpan - 1) * grid.GutterY;

            long pixelLeft = (long)Math.Round(left * xScale);
            long pixelTop = (long)Math.Round(top * yScale);
            long pixelRight = (long)Math.Round((left + width) * xScale);
            long pixelBottom = (long)Math.Round((top + height) * yScale);

            slots.Add(new TemplateSlotLayout(
                slot.Id,
                slot.Label,
                slot.Role,
                new PixelRect64(
                    pixelLeft,
                    pixelTop,
                    Math.Max(1, pixelRight - pixelLeft),
                    Math.Max(1, pixelBottom - pixelTop)),
                slot.MinimumEffectiveDpi,
                slot.RequireScaleBar,
                slot.HelpText));
        }

        return new TemplateCanvasLayout(
            template.Id,
            template.Name,
            widthPixels,
            heightPixels,
            template.Canvas.Dpi,
            slots);
    }

    private static (double WidthUnits, double HeightUnits, int WidthPixels, int HeightPixels)
        ResolveCanvas(TemplateCanvasDefinition canvas)
    {
        if (string.Equals(canvas.Mode, "physical", StringComparison.OrdinalIgnoreCase))
        {
            double widthMm = canvas.WidthMm
                ?? throw new InvalidOperationException("物理画布缺少宽度。");
            double heightMm = canvas.HeightMm ?? canvas.MaxHeightMm
                ?? throw new InvalidOperationException("物理画布缺少高度。");
            int widthPixels = Math.Max(1, (int)Math.Round(widthMm / MillimetersPerInch * canvas.Dpi));
            int heightPixels = Math.Max(1, (int)Math.Round(heightMm / MillimetersPerInch * canvas.Dpi));
            return (widthMm, heightMm, widthPixels, heightPixels);
        }

        int pixelWidth = canvas.WidthPx
            ?? throw new InvalidOperationException("像素画布缺少宽度。");
        int pixelHeight = canvas.HeightPx
            ?? throw new InvalidOperationException("像素画布缺少高度。");
        return (pixelWidth, pixelHeight, pixelWidth, pixelHeight);
    }
}
