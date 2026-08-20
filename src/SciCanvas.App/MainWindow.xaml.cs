using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public partial class MainWindow : Window
{
    private CropGesture _gesture;
    private Point _anchor;
    private double _moveOffsetX;
    private double _moveOffsetY;
    private FigurePanelViewModel? _draggedFigurePanel;
    private Point _figureDragAnchor;
    private FigureAnnotationViewModel? _draggedAnnotation;
    private Point _annotationDragAnchor;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ImageCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        Point position = ClampToSource(e.GetPosition(ImageCanvas), viewModel);
        bool moveExisting = ReferenceEquals(e.OriginalSource, CropOverlay);

        if (moveExisting)
        {
            _gesture = CropGesture.Move;
            _moveOffsetX = position.X - viewModel.Crop.X;
            _moveOffsetY = position.Y - viewModel.Crop.Y;
        }
        else
        {
            _gesture = CropGesture.Create;
            _anchor = position;
            viewModel.Crop.X = (long)Math.Floor(position.X);
            viewModel.Crop.Y = (long)Math.Floor(position.Y);
            viewModel.Crop.Width = 1;
            viewModel.Crop.Height = 1;
        }

        ImageCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ImageCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture == CropGesture.None ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        Point position = ClampToSource(e.GetPosition(ImageCanvas), viewModel);
        long sourceWidth = viewModel.SelectedSource.Width;
        long sourceHeight = viewModel.SelectedSource.Height;

        if (_gesture == CropGesture.Create)
        {
            long left = Math.Clamp((long)Math.Floor(Math.Min(_anchor.X, position.X)), 0, sourceWidth - 1);
            long top = Math.Clamp((long)Math.Floor(Math.Min(_anchor.Y, position.Y)), 0, sourceHeight - 1);
            long right = Math.Clamp((long)Math.Ceiling(Math.Max(_anchor.X, position.X)), left + 1, sourceWidth);
            long bottom = Math.Clamp((long)Math.Ceiling(Math.Max(_anchor.Y, position.Y)), top + 1, sourceHeight);

            viewModel.Crop.X = left;
            viewModel.Crop.Y = top;
            viewModel.Crop.Width = right - left;
            viewModel.Crop.Height = bottom - top;
        }
        else
        {
            long maxX = Math.Max(0, sourceWidth - viewModel.Crop.Width);
            long maxY = Math.Max(0, sourceHeight - viewModel.Crop.Height);
            viewModel.Crop.X = Math.Clamp((long)Math.Round(position.X - _moveOffsetX), 0, maxX);
            viewModel.Crop.Y = Math.Clamp((long)Math.Round(position.Y - _moveOffsetY), 0, maxY);
        }

        e.Handled = true;
    }

    private void ImageCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndGesture();
        e.Handled = true;
    }

    private void ImageCanvas_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _gesture = CropGesture.None;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void EndGesture()
    {
        _gesture = CropGesture.None;
        if (ImageCanvas.IsMouseCaptured)
        {
            ImageCanvas.ReleaseMouseCapture();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private static Point ClampToSource(Point point, MainWindowViewModel viewModel)
    {
        double width = viewModel.SelectedSource!.Width;
        double height = viewModel.SelectedSource.Height;
        return new Point(
            Math.Clamp(point.X, 0, Math.Max(0, width - 1)),
            Math.Clamp(point.Y, 0, Math.Max(0, height - 1)));
    }

    private void FigurePanel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigurePanelViewModel panel } element ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        bool toggle = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        viewModel.Figure.SelectPanel(panel, toggle);
        if (!panel.IsSelected || panel.IsLocked)
        {
            e.Handled = true;
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        _draggedFigurePanel = panel;
        _figureDragAnchor = position;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void FigurePanel_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedFigurePanel is null ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        long deltaX = (long)Math.Round(position.X - _figureDragAnchor.X);
        long deltaY = (long)Math.Round(position.Y - _figureDragAnchor.Y);
        (long movedX, long movedY) = viewModel.Figure.MoveSelectedPanelsBy(deltaX, deltaY);
        _figureDragAnchor = new Point(
            _figureDragAnchor.X + movedX,
            _figureDragAnchor.Y + movedY);
        e.Handled = true;
    }

    private void FigurePanel_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        _draggedFigurePanel = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
        e.Handled = true;
    }

    private void FigurePanel_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _draggedFigurePanel = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void Annotation_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigureAnnotationViewModel annotation } element ||
            DataContext is not MainWindowViewModel viewModel ||
            annotation.IsLocked)
        {
            return;
        }

        _draggedAnnotation = annotation;
        _annotationDragAnchor = e.GetPosition(FigureSurface);
        viewModel.Figure.SelectedAnnotation = annotation;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void Annotation_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedAnnotation is null ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        viewModel.Figure.MoveAnnotation(
            _draggedAnnotation,
            position.X - _annotationDragAnchor.X,
            position.Y - _annotationDragAnchor.Y);
        _annotationDragAnchor = position;
        e.Handled = true;
    }

    private void Annotation_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.IsMouseCaptured)
        {
            element.ReleaseMouseCapture();
        }

        _draggedAnnotation = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
        e.Handled = true;
    }

    private void Annotation_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _draggedAnnotation = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose || DataContext is not MainWindowViewModel { IsDirty: true } viewModel)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            "当前工程有未保存更改。是否先保存再退出？",
            "SciCanvas",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.No)
        {
            e.Cancel = true;
            await viewModel.DiscardRecoveryBeforeCloseAsync();
            _allowClose = true;
            Close();
            return;
        }

        e.Cancel = true;
        if (result == MessageBoxResult.Cancel)
        {
            return;
        }

        if (await viewModel.SaveBeforeCloseAsync())
        {
            _allowClose = true;
            Close();
        }
    }

    private enum CropGesture
    {
        None,
        Create,
        Move,
    }
}
