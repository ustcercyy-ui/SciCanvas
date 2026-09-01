using System.Globalization;
using SciCanvas.Core.Channels;
using SciCanvas.Core.Export;
using SciCanvas.Core.Workspace;

namespace SciCanvas.Presentation;

/// <summary>
/// Typed Presentation adapter for canonical ColorbarObject. Geometry and common figure
/// styling remain owned by the containing FigureScientificObjectViewModel.
/// </summary>
public sealed class ColorbarViewModel : ObservableObject
{
    private double _minimum;
    private double _maximum = 1;
    private string _unit = "a.u.";
    private string _colormap = "viridis";
    private Guid? _channelId;
    private ColorbarBindingState _bindingState = ColorbarBindingState.Detached;
    private FigureObjectOrientation _orientation = FigureObjectOrientation.Vertical;
    private string _ticksText = FormatTicks(ColorbarObject.CreateDefaultTicks(0, 1));

    public event EventHandler? Changed;

    public double Minimum
    {
        get => _minimum;
        set
        {
            if (IsLinked)
            {
                return;
            }
            if (SetProperty(ref _minimum, value))
            {
                RegenerateTicksWhenRangeIsValid();
                NotifyChanged();
            }
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            if (IsLinked)
            {
                return;
            }
            if (SetProperty(ref _maximum, value))
            {
                RegenerateTicksWhenRangeIsValid();
                NotifyChanged();
            }
        }
    }

    public string Unit
    {
        get => _unit;
        set
        {
            if (SetProperty(ref _unit, value?.Trim() ?? string.Empty))
            {
                NotifyChanged();
            }
        }
    }

    public string Colormap
    {
        get => _colormap;
        set
        {
            if (IsLinked)
            {
                return;
            }
            if (SetProperty(ref _colormap, value?.Trim() ?? string.Empty))
            {
                NotifyChanged();
            }
        }
    }

    public Guid? ChannelId
    {
        get => _channelId;
        set
        {
            if (SetProperty(ref _channelId, value))
            {
                NotifyChanged();
            }
        }
    }

    public ColorbarBindingState BindingState
    {
        get => _bindingState;
        set
        {
            if (SetProperty(ref _bindingState, value))
            {
                OnPropertyChanged(nameof(IsLinked));
                OnPropertyChanged(nameof(CanEditRange));
                NotifyChanged();
            }
        }
    }

    public FigureObjectOrientation Orientation
    {
        get => _orientation;
        set
        {
            if (SetProperty(ref _orientation, value))
            {
                NotifyChanged();
            }
        }
    }

    public string TicksText
    {
        get => _ticksText;
        set
        {
            if (SetProperty(ref _ticksText, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(Ticks));
                NotifyChanged();
            }
        }
    }

    public IReadOnlyList<ColorbarTick> Ticks
    {
        get
        {
            try
            {
                return ParseTicks(TicksText);
            }
            catch (FormatException)
            {
                return [];
            }
        }
    }

    public bool IsLinked => BindingState == ColorbarBindingState.Linked;

    public bool CanEditRange => !IsLinked;

    public IReadOnlyList<ColorbarBindingState> BindingStateChoices { get; } =
        Enum.GetValues<ColorbarBindingState>();

    public IReadOnlyList<FigureObjectOrientation> OrientationChoices { get; } =
        Enum.GetValues<FigureObjectOrientation>();

    public IReadOnlyList<string> ColormapChoices => ScientificColormap.Supported;

    public void Restore(
        double minimum,
        double maximum,
        string unit,
        string colormap,
        Guid? channelId,
        ColorbarBindingState bindingState,
        FigureObjectOrientation orientation,
        string? ticksText)
    {
        _minimum = minimum;
        _maximum = maximum;
        _unit = unit?.Trim() ?? string.Empty;
        _colormap = colormap?.Trim() ?? string.Empty;
        _channelId = channelId;
        _bindingState = bindingState;
        _orientation = orientation;
        _ticksText = string.IsNullOrWhiteSpace(ticksText)
            ? TryCreateDefaultTicks(minimum, maximum)
            : ticksText.Trim();
        OnPropertyChanged(string.Empty);
        NotifyChanged();
    }

    public void LinkToChannel(ChannelGroupMember channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        channel.EnsureValid();
        _channelId = channel.ChannelId;
        _bindingState = ColorbarBindingState.Linked;
        ApplyLinkedDisplaySettings(channel.DisplaySettings);
        OnPropertyChanged(string.Empty);
        NotifyChanged();
    }

    public void SynchronizeLinkedChannel(ChannelGroupMember channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        if (!IsLinked || ChannelId != channel.ChannelId)
        {
            return;
        }

        channel.EnsureValid();
        ApplyLinkedDisplaySettings(channel.DisplaySettings);
        OnPropertyChanged(string.Empty);
        NotifyChanged();
    }

    public ColorbarObject CreateModel(Guid id) => new ColorbarObject
    {
        Id = id,
        Minimum = Minimum,
        Maximum = Maximum,
        Unit = Unit,
        Colormap = Colormap,
        ChannelId = ChannelId,
        BindingState = BindingState,
        Orientation = Orientation,
        Ticks = ParseTicks(TicksText),
    }.EnsureValid();

    public FigureColorbarExportSpec CreateExportSpec()
    {
        ColorbarObject model = CreateModel(Guid.NewGuid());
        return new FigureColorbarExportSpec(
            model.Minimum,
            model.Maximum,
            model.Unit,
            model.Colormap,
            model.ChannelId,
            model.BindingState,
            model.Orientation,
            model.Ticks).EnsureValid();
    }

    public static string FormatTicks(IEnumerable<ColorbarTick> ticks) =>
        string.Join(';', ticks.Select(tick =>
            $"{tick.Value.ToString("R", CultureInfo.InvariantCulture)}|{tick.Label}"));

    private static IReadOnlyList<ColorbarTick> ParseTicks(string value)
    {
        var ticks = new List<ColorbarTick>();
        foreach (string token in value.Split(
                     ';',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = token.Split('|', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2 ||
                !double.TryParse(
                    pair[0],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double tickValue))
            {
                throw new FormatException(
                    "Colorbar ticks 必须使用 value|label；value|label 格式。");
            }

            ticks.Add(new ColorbarTick(tickValue, pair[1]).EnsureValid());
        }

        return ticks;
    }

    private void ApplyLinkedDisplaySettings(ChannelDisplaySettings display)
    {
        display.EnsureValid();
        _minimum = display.DisplayMinimum;
        _maximum = display.DisplayMaximum;
        _colormap = ScientificColormap.Normalize(display.Colormap);
        _ticksText = FormatTicks(ColorbarObject.CreateDefaultTicks(_minimum, _maximum));
    }

    private void RegenerateTicksWhenRangeIsValid()
    {
        if (double.IsFinite(Minimum) && double.IsFinite(Maximum) && Maximum > Minimum)
        {
            int count = Math.Clamp(Ticks.Count, 2, 20);
            _ticksText = FormatTicks(ColorbarObject.CreateDefaultTicks(Minimum, Maximum, count));
            OnPropertyChanged(nameof(TicksText));
            OnPropertyChanged(nameof(Ticks));
        }
    }

    private static string TryCreateDefaultTicks(double minimum, double maximum)
    {
        try
        {
            return FormatTicks(ColorbarObject.CreateDefaultTicks(minimum, maximum));
        }
        catch (ArgumentOutOfRangeException)
        {
            return string.Empty;
        }
    }

    private void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
