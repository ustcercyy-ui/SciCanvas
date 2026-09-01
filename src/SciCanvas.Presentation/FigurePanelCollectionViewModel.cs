using System.Collections.ObjectModel;

namespace SciCanvas.Presentation;

/// <summary>
/// Owns panel-bound Figure state. FigureCanvasViewModel coordinates commands,
/// while this object defines the panel/overlay/projection aggregate boundary.
/// </summary>
public sealed class FigurePanelCollectionViewModel
{
    public ObservableCollection<FigurePanelViewModel> Panels { get; } = [];

    public ObservableCollection<FigureMeasurementOverlayViewModel> MeasurementOverlays { get; } = [];

    public ObservableCollection<FigureRoiProjectionViewModel> RoiProjections { get; } = [];

    public void Clear()
    {
        Panels.Clear();
        MeasurementOverlays.Clear();
        RoiProjections.Clear();
    }
}
