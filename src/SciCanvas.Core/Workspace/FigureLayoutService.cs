namespace SciCanvas.Core.Workspace;

public enum AlignMode
{
    Left,
    HorizontalCenter,
    Right,
    Top,
    VerticalCenter,
    Bottom,
}

public enum MatchSizeMode
{
    Width,
    Height,
    Size,
}

public sealed record LayoutMutation(
    IReadOnlyList<FigurePanel> Before,
    IReadOnlyList<FigurePanel> After,
    string Description);

public static class FigureLayoutService
{
    public static LayoutMutation Align(
        IReadOnlyList<FigurePanel> panels,
        Guid referencePanelId,
        AlignMode mode)
    {
        FigurePanel reference = GetReference(panels, referencePanelId);
        FigurePanel[] updated = panels.Select(panel => panel with
        {
            Frame = mode switch
            {
                AlignMode.Left => panel.Frame.WithPosition(reference.Frame.X, panel.Frame.Y),
                AlignMode.HorizontalCenter => panel.Frame.WithPosition(
                    reference.Frame.X + (reference.Frame.Width - panel.Frame.Width) / 2,
                    panel.Frame.Y),
                AlignMode.Right => panel.Frame.WithPosition(
                    reference.Frame.Right - panel.Frame.Width,
                    panel.Frame.Y),
                AlignMode.Top => panel.Frame.WithPosition(panel.Frame.X, reference.Frame.Y),
                AlignMode.VerticalCenter => panel.Frame.WithPosition(
                    panel.Frame.X,
                    reference.Frame.Y + (reference.Frame.Height - panel.Frame.Height) / 2),
                AlignMode.Bottom => panel.Frame.WithPosition(
                    panel.Frame.X,
                    reference.Frame.Bottom - panel.Frame.Height),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            },
        }).ToArray();
        return new LayoutMutation(panels.ToArray(), updated, $"Align {mode}");
    }

    public static LayoutMutation Distribute(
        IReadOnlyList<FigurePanel> panels,
        bool horizontal)
    {
        if (panels.Count < 3)
        {
            throw new InvalidOperationException("Distribute requires at least three panels." );
        }

        FigurePanel[] ordered = horizontal
            ? panels.OrderBy(panel => panel.Frame.X).ToArray()
            : panels.OrderBy(panel => panel.Frame.Y).ToArray();
        double start = horizontal ? ordered[0].Frame.X : ordered[0].Frame.Y;
        double end = horizontal ? ordered[^1].Frame.Right : ordered[^1].Frame.Bottom;
        double totalSize = ordered.Sum(panel => horizontal ? panel.Frame.Width : panel.Frame.Height);
        double gap = (end - start - totalSize) / (ordered.Length - 1);
        double cursor = start;
        Dictionary<Guid, FigurePanel> updates = [];
        foreach (FigurePanel panel in ordered)
        {
            updates[panel.Id] = panel with
            {
                Frame = horizontal
                    ? panel.Frame.WithPosition(cursor, panel.Frame.Y)
                    : panel.Frame.WithPosition(panel.Frame.X, cursor),
            };
            cursor += (horizontal ? panel.Frame.Width : panel.Frame.Height) + gap;
        }

        return new LayoutMutation(
            panels.ToArray(),
            panels.Select(panel => updates[panel.Id]).ToArray(),
            horizontal ? "Distribute horizontally" : "Distribute vertically");
    }

    public static LayoutMutation SetGap(
        IReadOnlyList<FigurePanel> panels,
        double gapMm,
        bool horizontal)
    {
        if (panels.Count < 2 || !double.IsFinite(gapMm) || gapMm < 0)
        {
            throw new InvalidOperationException("Set Gap requires at least two panels and a non-negative gap." );
        }

        FigurePanel[] ordered = horizontal
            ? panels.OrderBy(panel => panel.Frame.X).ToArray()
            : panels.OrderBy(panel => panel.Frame.Y).ToArray();
        double cursor = horizontal ? ordered[0].Frame.X : ordered[0].Frame.Y;
        Dictionary<Guid, FigurePanel> updates = [];
        foreach (FigurePanel panel in ordered)
        {
            updates[panel.Id] = panel with
            {
                Frame = horizontal
                    ? panel.Frame.WithPosition(cursor, panel.Frame.Y)
                    : panel.Frame.WithPosition(panel.Frame.X, cursor),
            };
            cursor += (horizontal ? panel.Frame.Width : panel.Frame.Height) + gapMm;
        }

        return new LayoutMutation(
            panels.ToArray(),
            panels.Select(panel => updates[panel.Id]).ToArray(),
            $"Set {(horizontal ? "horizontal" : "vertical")} gap to {gapMm:0.###} mm");
    }

    public static LayoutMutation MatchSize(
        IReadOnlyList<FigurePanel> panels,
        Guid referencePanelId,
        MatchSizeMode mode)
    {
        FigurePanel reference = GetReference(panels, referencePanelId);
        FigurePanel[] updated = panels.Select(panel => panel with
        {
            Frame = mode switch
            {
                MatchSizeMode.Width => panel.Frame.WithSize(reference.Frame.Width, panel.Frame.Height),
                MatchSizeMode.Height => panel.Frame.WithSize(panel.Frame.Width, reference.Frame.Height),
                MatchSizeMode.Size => panel.Frame.WithSize(reference.Frame.Width, reference.Frame.Height),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            },
        }).ToArray();
        return new LayoutMutation(panels.ToArray(), updated, $"Match {mode}");
    }

    private static FigurePanel GetReference(
        IReadOnlyList<FigurePanel> panels,
        Guid referencePanelId)
    {
        if (panels.Count < 2)
        {
            throw new InvalidOperationException("Layout operation requires at least two panels." );
        }

        return panels.FirstOrDefault(panel => panel.Id == referencePanelId)
            ?? throw new InvalidOperationException("Reference panel is not part of the selection." );
    }
}

public static class BuiltInFigureTemplates
{
    public static FigureTemplate Grid2X2(double widthMm = 178, double heightMm = 120, double gapMm = 2) =>
        CreateGrid("grid-2x2", "2 × 2", 2, 2, widthMm, heightMm, gapMm);

    public static FigureTemplate Grid2X3(double widthMm = 178, double heightMm = 120, double gapMm = 2) =>
        CreateGrid("grid-2x3", "2 × 3", 2, 3, widthMm, heightMm, gapMm);

    public static FigureTemplate Grid3X2(double widthMm = 178, double heightMm = 120, double gapMm = 2) =>
        CreateGrid("grid-3x2", "3 × 2", 3, 2, widthMm, heightMm, gapMm);

    private static FigureTemplate CreateGrid(
        string id,
        string name,
        int rows,
        int columns,
        double widthMm,
        double heightMm,
        double gapMm)
    {
        double panelWidth = (widthMm - gapMm * (columns - 1)) / columns;
        double panelHeight = (heightMm - gapMm * (rows - 1)) / rows;
        FigureRectMm[] frames = Enumerable.Range(0, rows)
            .SelectMany(row => Enumerable.Range(0, columns)
                .Select(column => new FigureRectMm(
                    column * (panelWidth + gapMm),
                    row * (panelHeight + gapMm),
                    panelWidth,
                    panelHeight)))
            .ToArray();
        return new FigureTemplate(id, name, widthMm, heightMm, frames, null);
    }
}
