using System.Windows.Controls;

namespace SciCanvas.App;

public partial class LayersWorkspace : UserControl
{
    public LayersWorkspace()
    {
        InitializeComponent();
    }

    public ScrollViewer ScrollViewer => LayersScrollViewer;
}
