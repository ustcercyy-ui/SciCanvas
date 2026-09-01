using System.Windows.Controls;

namespace SciCanvas.App;

public partial class PlotWorkspace : UserControl
{
    public PlotWorkspace()
    {
        InitializeComponent();
    }

    public ScrollViewer ScrollViewer => PlotWorkspaceScrollViewer;
}
