using System.Globalization;
using System.IO;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Plotting;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

public sealed class HeatmapPlotEditorViewModel : ObservableObject
{
    private static readonly PlotColorbarPosition[] VerticalPositions =
        [PlotColorbarPosition.Right, PlotColorbarPosition.Left];
    private static readonly PlotColorbarPosition[] HorizontalPositions =
        [PlotColorbarPosition.Bottom, PlotColorbarPosition.Top];

    private HeatmapGridKind _gridKind;
    private HeatmapDuplicateCellPolicy _duplicateCellPolicy;
    private string _colormap = "viridis";
    private double? _minimum;
    private double? _maximum;
    private PlotColorScaleKind _scale;
    private PlotColorClampMode _clampMode;
    private string _noDataColor = string.Empty;
    private bool _showColorbar = true;
    private PlotColorbarBinding _colorbarBinding;
    private PlotColorbarOrientation _colorbarOrientation;
    private PlotColorbarPosition _colorbarPosition;
    private double? _colorbarMinimum;
    private double? _colorbarMaximum;
    private string _colorbarUnit = string.Empty;
    private string _colorbarTicks = string.Empty;
    private string _colorbarTickLabels = string.Empty;
    private bool _useCustomColorbarFont;

    public HeatmapPlotEditorViewModel(TextStyle inheritedTickStyle)
    {
        ColorbarFont = new PlotTextStyleEditorViewModel(inheritedTickStyle);
        Load(null, null, null, inheritedTickStyle);
    }

    public IReadOnlyList<HeatmapGridKind> GridKindChoices { get; } =
        Enum.GetValues<HeatmapGridKind>();

    public IReadOnlyList<HeatmapDuplicateCellPolicy> DuplicateCellPolicyChoices { get; } =
        Enum.GetValues<HeatmapDuplicateCellPolicy>();

    public IReadOnlyList<string> ColormapChoices => ScientificColormap.Supported;

    public IReadOnlyList<PlotColorScaleKind> ScaleChoices { get; } =
        Enum.GetValues<PlotColorScaleKind>();

    public IReadOnlyList<PlotColorClampMode> ClampModeChoices { get; } =
        Enum.GetValues<PlotColorClampMode>();

    public IReadOnlyList<PlotColorbarBinding> ColorbarBindingChoices { get; } =
        Enum.GetValues<PlotColorbarBinding>();

    public IReadOnlyList<PlotColorbarOrientation> ColorbarOrientationChoices { get; } =
        Enum.GetValues<PlotColorbarOrientation>();

    public IReadOnlyList<PlotColorbarPosition> ColorbarPositionChoices =>
        ColorbarOrientation == PlotColorbarOrientation.Vertical
            ? VerticalPositions
            : HorizontalPositions;

    public HeatmapGridKind GridKind
    {
        get => _gridKind;
        set => SetProperty(ref _gridKind, value);
    }

    public HeatmapDuplicateCellPolicy DuplicateCellPolicy
    {
        get => _duplicateCellPolicy;
        set => SetProperty(ref _duplicateCellPolicy, value);
    }

    public string Colormap
    {
        get => _colormap;
        set => SetProperty(ref _colormap, value ?? string.Empty);
    }

    public double? Minimum
    {
        get => _minimum;
        set => SetProperty(ref _minimum, value);
    }

    public double? Maximum
    {
        get => _maximum;
        set => SetProperty(ref _maximum, value);
    }

    public PlotColorScaleKind Scale
    {
        get => _scale;
        set => SetProperty(ref _scale, value);
    }

    public PlotColorClampMode ClampMode
    {
        get => _clampMode;
        set => SetProperty(ref _clampMode, value);
    }

    public string NoDataColor
    {
        get => _noDataColor;
        set => SetProperty(ref _noDataColor, value ?? string.Empty);
    }

    public bool ShowColorbar
    {
        get => _showColorbar;
        set => SetProperty(ref _showColorbar, value);
    }

    public PlotColorbarBinding ColorbarBinding
    {
        get => _colorbarBinding;
        set
        {
            if (SetProperty(ref _colorbarBinding, value))
            {
                OnPropertyChanged(nameof(IsDetachedColorbar));
            }
        }
    }

    public bool IsDetachedColorbar => ColorbarBinding == PlotColorbarBinding.Detached;

    public PlotColorbarOrientation ColorbarOrientation
    {
        get => _colorbarOrientation;
        set
        {
            if (!SetProperty(ref _colorbarOrientation, value)) return;
            if (value == PlotColorbarOrientation.Vertical &&
                ColorbarPosition is not (PlotColorbarPosition.Right or PlotColorbarPosition.Left))
            {
                ColorbarPosition = PlotColorbarPosition.Right;
            }
            else if (value == PlotColorbarOrientation.Horizontal &&
                ColorbarPosition is not (PlotColorbarPosition.Bottom or PlotColorbarPosition.Top))
            {
                ColorbarPosition = PlotColorbarPosition.Bottom;
            }
            OnPropertyChanged(nameof(ColorbarPositionChoices));
        }
    }

    public PlotColorbarPosition ColorbarPosition
    {
        get => _colorbarPosition;
        set => SetProperty(ref _colorbarPosition, value);
    }

    public double? ColorbarMinimum
    {
        get => _colorbarMinimum;
        set => SetProperty(ref _colorbarMinimum, value);
    }

    public double? ColorbarMaximum
    {
        get => _colorbarMaximum;
        set => SetProperty(ref _colorbarMaximum, value);
    }

    public string ColorbarUnit
    {
        get => _colorbarUnit;
        set => SetProperty(ref _colorbarUnit, value ?? string.Empty);
    }

    public string ColorbarTicks
    {
        get => _colorbarTicks;
        set => SetProperty(ref _colorbarTicks, value ?? string.Empty);
    }

    public string ColorbarTickLabels
    {
        get => _colorbarTickLabels;
        set => SetProperty(ref _colorbarTickLabels, value ?? string.Empty);
    }

    public bool UseCustomColorbarFont
    {
        get => _useCustomColorbarFont;
        set => SetProperty(ref _useCustomColorbarFont, value);
    }

    public PlotTextStyleEditorViewModel ColorbarFont { get; }

    public void Load(
        HeatmapGridDefinition? grid,
        PlotColorScale? colorScale,
        PlotColorbarDefinition? colorbar,
        TextStyle inheritedTickStyle)
    {
        HeatmapGridDefinition effectiveGrid = grid ?? HeatmapGridDefinition.Default;
        PlotColorScale effectiveScale = colorScale ?? PlotColorScale.Default;
        PlotColorbarDefinition effectiveColorbar = colorbar ?? PlotColorbarDefinition.Default;
        GridKind = effectiveGrid.Kind;
        DuplicateCellPolicy = effectiveGrid.DuplicateCellPolicy;
        Colormap = effectiveScale.Colormap;
        Minimum = effectiveScale.Minimum;
        Maximum = effectiveScale.Maximum;
        Scale = effectiveScale.Scale;
        ClampMode = effectiveScale.ClampMode;
        NoDataColor = effectiveScale.NoDataColor ?? string.Empty;
        ShowColorbar = effectiveScale.ShowColorbar;
        ColorbarBinding = effectiveColorbar.Binding;
        ColorbarOrientation = effectiveColorbar.Orientation;
        ColorbarPosition = effectiveColorbar.Position;
        ColorbarMinimum = effectiveColorbar.Minimum;
        ColorbarMaximum = effectiveColorbar.Maximum;
        ColorbarUnit = effectiveColorbar.Unit ?? string.Empty;
        ColorbarTicks = string.Join(
            ", ",
            (effectiveColorbar.Ticks ?? []).Select(value =>
                value.ToString("G17", CultureInfo.InvariantCulture)));
        ColorbarTickLabels = string.Join(" | ", effectiveColorbar.TickLabels ?? []);
        UseCustomColorbarFont = effectiveColorbar.LabelStyle is not null;
        ColorbarFont.Load(effectiveColorbar.LabelStyle ?? inheritedTickStyle);
    }

    public HeatmapGridDefinition CreateGrid() =>
        new(GridKind, DuplicateCellPolicy);

    public PlotColorScale CreateColorScale() => new(
        Colormap.Trim(),
        Minimum,
        Maximum,
        Scale,
        ClampMode,
        string.IsNullOrWhiteSpace(NoDataColor) ? null : NoDataColor.Trim(),
        ShowColorbar);

    public PlotColorbarDefinition CreateColorbar() => new(
        ColorbarBinding,
        ColorbarOrientation,
        ColorbarPosition,
        IsDetachedColorbar ? ColorbarMinimum : null,
        IsDetachedColorbar ? ColorbarMaximum : null,
        string.IsNullOrWhiteSpace(ColorbarUnit) ? null : ColorbarUnit.Trim(),
        ParseTicks(ColorbarTicks),
        UseCustomColorbarFont ? ColorbarFont.CreateModel() : null,
        ParseTickLabels(ColorbarTickLabels));

    private static IReadOnlyList<double> ParseTicks(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        string[] tokens = value.Split([',', ';', ' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ticks = new double[tokens.Length];
        for (int index = 0; index < tokens.Length; index++)
        {
            if (!double.TryParse(tokens[index], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out ticks[index]) ||
                !double.IsFinite(ticks[index]))
            {
                throw new InvalidDataException(
                    "Colorbar ticks must be finite invariant numbers separated by commas.");
            }
        }
        return ticks;
    }

    private static IReadOnlyList<string> ParseTickLabels(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
