using System.Windows.Media;
using SciCanvas.Core.Export;

namespace SciCanvas.Presentation;

public sealed class ScientificColorEntryViewModel : ObservableObject
{
    private string _name;
    private string _color;

    public ScientificColorEntryViewModel(ScientificColorDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Id = definition.Id == Guid.Empty ? Guid.NewGuid() : definition.Id;
        _name = definition.Name;
        _color = definition.Color;
    }

    public event EventHandler? Changed;

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(Definition));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string Color
    {
        get => _color;
        set
        {
            if (SetProperty(ref _color, value?.Trim() ?? string.Empty))
            {
                OnPropertyChanged(nameof(ColorBrush));
                OnPropertyChanged(nameof(Definition));
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public Brush ColorBrush
    {
        get
        {
            try
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(Color));
                brush.Freeze();
                return brush;
            }
            catch (Exception exception) when (
                exception is FormatException or NotSupportedException or ArgumentException)
            {
                return Brushes.Transparent;
            }
        }
    }

    public ScientificColorDefinition Definition => new(Id, Name, Color);
}
