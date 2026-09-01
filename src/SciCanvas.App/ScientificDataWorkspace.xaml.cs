using System.Windows.Controls;

namespace SciCanvas.App;

public partial class ScientificDataWorkspace : UserControl
{
    public ScientificDataWorkspace()
    {
        InitializeComponent();
    }

    public ScrollViewer ScrollViewer => DataWorkspaceScrollViewer;
}
