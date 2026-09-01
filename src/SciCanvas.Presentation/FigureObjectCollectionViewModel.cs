using System.Collections.ObjectModel;

namespace SciCanvas.Presentation;

/// <summary>
/// Owns free-standing Figure objects and their project-scoped scientific
/// palette. It is deliberately independent from panel/link coordination.
/// </summary>
public sealed class FigureObjectCollectionViewModel
{
    public ObservableCollection<FigureAnnotationViewModel> Annotations { get; } = [];

    public ObservableCollection<FigureScientificObjectViewModel> ScientificObjects { get; } = [];

    public ObservableCollection<FigureGuideViewModel> Guides { get; } = [];

    public ObservableCollection<ScientificColorEntryViewModel> ScientificColors { get; } = [];

    public void ClearFigureObjects()
    {
        Annotations.Clear();
        ScientificObjects.Clear();
        Guides.Clear();
    }
}
