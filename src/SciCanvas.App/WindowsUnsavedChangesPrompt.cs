using System.Windows;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using SciCanvas.Presentation;

namespace SciCanvas.App;

internal sealed class WindowsUnsavedChangesPrompt : IUnsavedChangesPrompt
{
    public UnsavedChangesDecision ConfirmProjectReplacement(
        string actionLabel,
        string currentProjectDisplayName)
    {
        MessageBoxResult result = MessageBox.Show(
            $"{currentProjectDisplayName} 有未保存更改。\n\n" +
            $"是否先保存，再{actionLabel}？\n\n" +
            "“是”保存后继续，“否”放弃更改并继续，“取消”留在当前工程。",
            "SciCanvas",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesDecision.Save,
            MessageBoxResult.No => UnsavedChangesDecision.Discard,
            _ => UnsavedChangesDecision.Cancel,
        };
    }
}
