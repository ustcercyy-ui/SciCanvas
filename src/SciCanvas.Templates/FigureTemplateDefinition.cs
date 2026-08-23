namespace SciCanvas.Templates;

public sealed class FigureTemplateDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string? PublisherProfileId { get; init; }

    public TemplateCanvasDefinition Canvas { get; init; } = new();

    public TemplateGridDefinition Grid { get; init; } = new();

    public IReadOnlyList<TemplateSlotDefinition> Slots { get; init; } = [];

    public TemplateLabelStyleDefinition LabelStyle { get; init; } = new();
}

public sealed class TemplateLabelStyleDefinition
{
    public string Sequence { get; init; } = "lowercase";

    public string FontFamily { get; init; } = "Arial";

    public double FontSizePt { get; init; } = 7;

    public int FontWeight { get; init; } = 700;

    public string Position { get; init; } = "top-left-inside";
}

public sealed class TemplateCanvasDefinition
{
    public string Mode { get; init; } = "pixels";

    public double? WidthMm { get; init; }

    public double? HeightMm { get; init; }

    public double? MaxHeightMm { get; init; }

    public int? WidthPx { get; init; }

    public int? HeightPx { get; init; }

    public int Dpi { get; init; } = 300;

    public string Background { get; init; } = "white";
}

public sealed class TemplateGridDefinition
{
    public int Columns { get; init; }

    public int Rows { get; init; }

    public double GutterX { get; init; }

    public double GutterY { get; init; }

    public TemplateMarginDefinition Margin { get; init; } = new();
}

public sealed class TemplateMarginDefinition
{
    public double Top { get; init; }

    public double Right { get; init; }

    public double Bottom { get; init; }

    public double Left { get; init; }
}

public sealed class TemplateSlotDefinition
{
    public string Id { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Role { get; init; } = string.Empty;

    public TemplateGridRectDefinition Rect { get; init; } = new();

    public string DefaultFit { get; init; } = "contain";

    public int MinimumEffectiveDpi { get; init; } = 300;

    public bool RequireScaleBar { get; init; }

    /// <summary>Whether a panel created from this slot keeps its source aspect ratio when resized.</summary>
    public bool LockAspectRatio { get; init; } = true;

    public string? HelpText { get; init; }
}

public sealed class TemplateGridRectDefinition
{
    public int Column { get; init; }

    public int Row { get; init; }

    public int ColumnSpan { get; init; }

    public int RowSpan { get; init; }
}
