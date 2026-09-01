using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public partial class InspectorWorkspace : UserControl
{
    public InspectorWorkspace()
    {
        InitializeComponent();
    }

    public ScrollViewer ScrollViewer => InspectorScrollViewer;

    public void BringMeasurementInspectorIntoView() => MeasurementInspectorPanel.BringIntoView();

    private void MeasurementColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                SelectedSource.SelectedMeasurement: { } measurement,
            } viewModel && TryPickColor(measurement.StrokeColor, out string color))
        {
            measurement.StrokeColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void MeasurementFillColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                SelectedSource.SelectedMeasurement: { } measurement,
            } viewModel && TryPickColor(measurement.FillColor, out string color))
        {
            measurement.FillColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void MeasurementMarkerStrokeColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                SelectedSource.SelectedMeasurement: { } measurement,
            } viewModel && TryPickColor(measurement.MarkerStrokeColor, out string color))
        {
            measurement.MarkerStrokeColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void MeasurementMarkerFillColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                SelectedSource.SelectedMeasurement: { } measurement,
            } viewModel && TryPickColor(measurement.MarkerFillColor, out string color))
        {
            measurement.MarkerFillColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void MeasurementLabelColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                SelectedSource.SelectedMeasurement: { } measurement,
            } viewModel && TryPickColor(measurement.LabelColor, out string color))
        {
            measurement.LabelColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void AnnotationTextColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                Figure.SelectedAnnotation: { } annotation,
            } viewModel && TryPickColor(annotation.TextColor, out string color))
        {
            annotation.TextColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void AnnotationStrokeColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                Figure.SelectedAnnotation: { } annotation,
            } viewModel && TryPickColor(annotation.StrokeColor, out string color))
        {
            annotation.StrokeColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void AnnotationFillColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                Figure.SelectedAnnotation: { } annotation,
            } viewModel && TryPickColor(annotation.FillColor, out string color))
        {
            annotation.FillColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void ScientificObjectStrokeColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                Figure.SelectedScientificObject: { } scientificObject,
            } viewModel && TryPickColor(scientificObject.StrokeColor, out string color))
        {
            scientificObject.StrokeColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void ScientificObjectFillColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                Figure.SelectedScientificObject: { } scientificObject,
            } viewModel && TryPickColor(scientificObject.FillColor, out string color))
        {
            scientificObject.FillColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void ScientificObjectTextColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                Figure.SelectedScientificObject: { } scientificObject,
            } viewModel && TryPickColor(scientificObject.TextColor, out string color))
        {
            scientificObject.TextColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void AssistedRegionColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel
            {
                SelectedAssistedRegion: { } candidate,
            } viewModel && TryPickColor(candidate.Color, out string color))
        {
            candidate.Color = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigureGlobalTextColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.GlobalTextColor, out string color))
        {
            viewModel.Figure.GlobalTextColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigureGlobalShapeColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.GlobalShapeColor, out string color))
        {
            viewModel.Figure.GlobalShapeColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigureScaleBarColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.GlobalScaleBarColor, out string color))
        {
            viewModel.Figure.GlobalScaleBarColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigurePanelLabelColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.PanelLabelTextColor, out string color))
        {
            viewModel.Figure.PanelLabelTextColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigureScaleBarLabelColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.ScaleBarLabelColor, out string color))
        {
            viewModel.Figure.ScaleBarLabelColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void SelectedPanelLabelColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.SelectedPanelLabelTextColor, out string color))
        {
            viewModel.Figure.SelectedPanelLabelTextColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void SelectedPanelScaleBarColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.SelectedPanelScaleBarColor, out string color))
        {
            viewModel.Figure.SelectedPanelScaleBarColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private void SelectedPanelScaleBarLabelColorPicker_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel &&
            TryPickColor(viewModel.Figure.SelectedPanelScaleBarLabelColor, out string color))
        {
            viewModel.Figure.SelectedPanelScaleBarLabelColor = color;
            viewModel.CompleteHistoryGesture();
        }
    }

    private static bool TryPickColor(string currentValue, out string selectedValue)
    {
        byte alpha = 255;
        System.Drawing.Color initial = System.Drawing.Color.FromArgb(34, 199, 232);
        try
        {
            if (ColorConverter.ConvertFromString(currentValue) is Color current)
            {
                alpha = current.A;
                initial = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
            }
        }
        catch (FormatException)
        {
            // Keep the safe default when the user is midway through typing a HEX value.
        }

        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            AnyColor = true,
            Color = initial,
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            selectedValue = currentValue;
            return false;
        }

        selectedValue = $"#{alpha:X2}{dialog.Color.R:X2}{dialog.Color.G:X2}{dialog.Color.B:X2}";
        return true;
    }

    private void AnnotationListItem_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: FigureAnnotationViewModel annotation } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject original &&
            FindVisualAncestor<ToggleButton>(original) is not null)
        {
            return;
        }

        viewModel.Figure.SelectAnnotation(
            annotation,
            toggle: (Keyboard.Modifiers & ModifierKeys.Control) != 0);
        e.Handled = true;
    }

    private static T? FindVisualAncestor<T>(DependencyObject? element)
        where T : DependencyObject
    {
        for (DependencyObject? current = element;
             current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
