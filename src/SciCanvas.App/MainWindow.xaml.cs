using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using SciCanvas.Presentation;

namespace SciCanvas.App;

public partial class MainWindow : Window
{
    private const double MinimumZoom = 0.05;
    private const double MaximumZoom = 16;
    private CropGesture _gesture;
    private Point _anchor;
    private double _moveOffsetX;
    private double _moveOffsetY;
    private long _cropResizeStartX;
    private long _cropResizeStartY;
    private long _cropResizeStartWidth;
    private long _cropResizeStartHeight;
    private FrameworkElement? _cropResizeCaptureElement;
    private bool _scientificGestureActive;
    private ScientificMeasurementViewModel? _draggedMeasurement;
    private MeasurementHandle _measurementHandle;
    private FrameworkElement? _measurementCaptureElement;
    private Point _measurementDragAnchor;
    private RoiObjectItemViewModel? _draggedCanonicalRoi;
    private FrameworkElement? _canonicalRoiCaptureElement;
    private Point _canonicalRoiDragAnchor;
    private int _canonicalRoiDragVertexIndex = -1;
    private int _selectedCanonicalRoiVertexIndex = -1;
    private FigurePanelViewModel? _draggedFigurePanel;
    private Point _figureDragAnchor;
    private FigurePanelViewModel? _resizingFigurePanel;
    private FrameworkElement? _resizeHandleElement;
    private Point _figureResizeAnchor;
    private long _figureResizeStartWidth;
    private long _figureResizeStartHeight;
    private FigurePlotPanelViewModel? _draggedFigurePlotPanel;
    private FrameworkElement? _figurePlotDragElement;
    private long _figurePlotDragStartX;
    private long _figurePlotDragStartY;
    private FigurePlotPanelViewModel? _resizingFigurePlotPanel;
    private FrameworkElement? _figurePlotResizeElement;
    private long _figurePlotResizeStartWidth;
    private long _figurePlotResizeStartHeight;
    private FigureAnnotationViewModel? _draggedAnnotation;
    private AnnotationHandle _annotationHandle;
    private FrameworkElement? _annotationCaptureElement;
    private Point _annotationDragAnchor;
    private FigureScientificObjectViewModel? _draggedScientificObject;
    private FrameworkElement? _scientificObjectCaptureElement;
    private Point _scientificObjectDragAnchor;
    private bool _isResizingScientificObject;
    private int _scientificPolygonDragVertexIndex = -1;
    private double _sourceZoom = 1;
    private double _figureZoom = 1;
    private bool _sourceZoomIsFit = true;
    private bool _figureZoomIsFit = true;
    private ScrollViewer? _panningViewport;
    private Point _panAnchor;
    private double _panStartHorizontalOffset;
    private double _panStartVerticalOffset;
    private bool _allowClose;
    private bool _isHeaderExpanded = true;
    private bool _isLeftSidebarExpanded = true;
    private bool _isRightSidebarExpanded = true;
    private bool _isMeasurementDockExpanded = true;
    private GridLength _leftSidebarExpandedWidth = new(0.75, GridUnitType.Star);
    private GridLength _rightSidebarExpandedWidth = new(1.05, GridUnitType.Star);

    public MainWindow()
    {
        InitializeComponent();
        foreach (Thumb handle in new[]
                 {
                     CropTopLeftHandle, CropTopHandle, CropTopRightHandle, CropRightHandle,
                     CropBottomRightHandle, CropBottomHandle, CropBottomLeftHandle, CropLeftHandle,
                 })
        {
            handle.PreviewMouseMove += CropResizeHandle_OnMouseMove;
        }
    }
    private void HeaderToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isHeaderExpanded = !_isHeaderExpanded;
        HeaderContentRow.Height = _isHeaderExpanded ? new GridLength(98) : new GridLength(0);
        HeaderInnerContentRow.Height = _isHeaderExpanded ? new GridLength(98) : new GridLength(0);
        HeaderCommandArea.Visibility = _isHeaderExpanded ? Visibility.Visible : Visibility.Collapsed;
        HeaderToggleButton.Content = _isHeaderExpanded ? "收起" : "展开";
        HeaderToggleButton.ToolTip = _isHeaderExpanded
            ? "收起顶部命令区，扩大画布高度"
            : "展开顶部命令区";
        ScheduleFitVisibleWorkspace();
    }

    private void LeftSidebarToggleButton_OnClick(object sender, RoutedEventArgs e) =>
        SetLeftSidebarExpanded(!_isLeftSidebarExpanded);

    private void SetLeftSidebarExpanded(bool expanded)
    {
        if (_isLeftSidebarExpanded == expanded)
        {
            return;
        }

        if (!expanded)
        {
            _leftSidebarExpandedWidth = LeftSidebarColumn.Width;
        }

        _isLeftSidebarExpanded = expanded;
        LeftSidebarPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        LeftSidebarSplitter.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        LeftSidebarColumn.MinWidth = expanded ? 160 : 0;
        LeftSidebarColumn.MaxWidth = expanded ? 340 : 34;
        LeftSidebarColumn.Width = expanded ? _leftSidebarExpandedWidth : new GridLength(34);
        LeftSplitterColumn.Width = expanded ? new GridLength(6) : new GridLength(0);
        LeftSidebarToggleButton.Content = expanded ? "‹" : "›";
        LeftSidebarToggleButton.ToolTip = expanded
            ? "收起资源库，扩大中央画布"
            : "展开资源库";
        ScheduleFitVisibleWorkspace();
    }

    private void RightSidebarToggleButton_OnClick(object sender, RoutedEventArgs e) =>
        SetRightSidebarExpanded(!_isRightSidebarExpanded);

    private void SetRightSidebarExpanded(bool expanded)
    {
        if (_isRightSidebarExpanded == expanded)
        {
            return;
        }

        if (!expanded)
        {
            _rightSidebarExpandedWidth = RightSidebarColumn.Width;
        }

        _isRightSidebarExpanded = expanded;
        RightSidebarPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        RightSidebarSplitter.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        RightSidebarColumn.MinWidth = expanded ? 230 : 0;
        RightSidebarColumn.MaxWidth = expanded ? 420 : 34;
        RightSidebarColumn.Width = expanded ? _rightSidebarExpandedWidth : new GridLength(34);
        RightSplitterColumn.Width = expanded ? new GridLength(6) : new GridLength(0);
        RightSidebarToggleButton.Content = expanded ? "›" : "‹";
        RightSidebarToggleButton.ToolTip = expanded
            ? "收起检查器，扩大中央画布"
            : "展开检查器";
        ScheduleFitVisibleWorkspace();
    }

    private void MeasurementDockToggleButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isMeasurementDockExpanded = !_isMeasurementDockExpanded;
        MeasurementDockPanel.Height = _isMeasurementDockExpanded ? 230 : 36;
        MeasurementDockContentRow.Height = _isMeasurementDockExpanded
            ? new GridLength(1, GridUnitType.Star)
            : new GridLength(0);
        MeasurementDockDataGrid.Visibility = _isMeasurementDockExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        MeasurementDockActions.Visibility = _isMeasurementDockExpanded
            ? Visibility.Visible
            : Visibility.Collapsed;
        MeasurementDockToggleButton.Content = _isMeasurementDockExpanded ? "收起" : "展开";
        MeasurementDockToggleButton.ToolTip = _isMeasurementDockExpanded
            ? "收起测量表，扩大图像画布高度"
            : "展开测量表";
        ScheduleFitVisibleWorkspace();
    }

    private void ScheduleFitVisibleWorkspace() => Dispatcher.BeginInvoke(
        DispatcherPriority.Background,
        new Action(() =>
        {
            UpdateLayout();
            FitVisibleWorkspace();
        }));


    private void ImageCanvas_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        Point canvasPosition = e.GetPosition(ImageCanvas);
        Point position = viewModel.IsCanonicalRoiCreationTool
            ? canvasPosition
            : ClampToSource(canvasPosition, viewModel);
        if (viewModel.ActiveScienceTool != ScientificToolMode.Crop)
        {
            _scientificGestureActive = viewModel.BeginScientificGesture(
                position.X,
                position.Y,
                finishMultiPoint: e.ClickCount > 1);
            if (_scientificGestureActive)
            {
                ImageCanvas.CaptureMouse();
            }

            e.Handled = true;
            return;
        }

        bool moveExisting = e.OriginalSource is DependencyObject original &&
                            IsDescendantOf(CropOverlay, original);

        if (moveExisting)
        {
            _gesture = CropGesture.Move;
            viewModel.BeginHistoryGesture();
            _moveOffsetX = position.X - viewModel.Crop.X;
            _moveOffsetY = position.Y - viewModel.Crop.Y;
        }
        else
        {
            _gesture = CropGesture.Create;
            _anchor = position;
            viewModel.BeginHistoryGesture();
            viewModel.Crop.SetBounds(
                (long)Math.Floor(position.X),
                (long)Math.Floor(position.Y),
                1,
                1);
        }

        ImageCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void ImageCanvas_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_scientificGestureActive &&
            e.LeftButton == MouseButtonState.Pressed &&
            DataContext is MainWindowViewModel { SelectedSource: not null } scienceViewModel)
        {
            Point canvasPosition = e.GetPosition(ImageCanvas);
            Point sciencePosition = scienceViewModel.IsCanonicalRoiCreationTool
                ? canvasPosition
                : ClampToSource(canvasPosition, scienceViewModel);
            scienceViewModel.UpdateScientificGesture(sciencePosition.X, sciencePosition.Y);
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel { HasPendingCanonicalRoi: true } roiViewModel)
        {
            Point hover = e.GetPosition(ImageCanvas);
            roiViewModel.UpdateCanonicalRoiHover(hover.X, hover.Y);
        }

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

            viewModel.Crop.SetBounds(left, top, right - left, bottom - top);
        }
        else if (_gesture == CropGesture.Move)
        {
            long maxX = Math.Max(0, sourceWidth - viewModel.Crop.Width);
            long maxY = Math.Max(0, sourceHeight - viewModel.Crop.Height);
            viewModel.Crop.SetBounds(
                Math.Clamp((long)Math.Round(position.X - _moveOffsetX), 0, maxX),
                Math.Clamp((long)Math.Round(position.Y - _moveOffsetY), 0, maxY),
                viewModel.Crop.Width,
                viewModel.Crop.Height);
        }
        else
        {
            ResizeCrop(position, viewModel, sourceWidth, sourceHeight);
        }

        e.Handled = true;
    }

    private void ImageCanvas_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_scientificGestureActive)
        {
            bool canonicalRoi = DataContext is MainWindowViewModel
            {
                IsCanonicalRoiCreationTool: true,
            };
            EndScientificGesture();
            if (canonicalRoi && DataContext is MainWindowViewModel roiViewModel)
            {
                roiViewModel.IsRoiPropagationTabActive = true;
            }
            else
            {
                ShowMeasurementInspector();
            }
            e.Handled = true;
            return;
        }

        if (DataContext is MainWindowViewModel
            {
                HasPendingCanonicalRoi: true,
                ActiveScienceTool: ScientificToolMode.CanonicalRoiPolygon or
                    ScientificToolMode.CanonicalRoiPolyline,
            })
        {
            e.Handled = true;
            return;
        }

        EndGesture();
        e.Handled = true;
    }

    private void ImageCanvas_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_scientificGestureActive)
        {
            _scientificGestureActive = false;
            if (DataContext is MainWindowViewModel scienceViewModel)
            {
                scienceViewModel.CompleteScientificGesture();
            }

            return;
        }

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

        FrameworkElement? resizeCapture = _cropResizeCaptureElement;
        _cropResizeCaptureElement = null;
        if (resizeCapture?.IsMouseCaptured == true)
        {
            resizeCapture.ReleaseMouseCapture();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void EndScientificGesture()
    {
        _scientificGestureActive = false;
        if (ImageCanvas.IsMouseCaptured)
        {
            ImageCanvas.ReleaseMouseCapture();
        }

        if (_cropResizeCaptureElement?.IsMouseCaptured == true)
        {
            _cropResizeCaptureElement.ReleaseMouseCapture();
        }
        _cropResizeCaptureElement = null;

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteScientificGesture();
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

    private void CropResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element ||
            DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        viewModel.ActiveScienceTool = ScientificToolMode.Crop;
        _gesture = element.Uid switch
        {
            "TopLeft" => CropGesture.ResizeTopLeft,
            "Top" => CropGesture.ResizeTop,
            "TopRight" => CropGesture.ResizeTopRight,
            "Right" => CropGesture.ResizeRight,
            "BottomRight" => CropGesture.ResizeBottomRight,
            "Bottom" => CropGesture.ResizeBottom,
            "BottomLeft" => CropGesture.ResizeBottomLeft,
            "Left" => CropGesture.ResizeLeft,
            _ => CropGesture.None,
        };
        if (_gesture == CropGesture.None)
        {
            return;
        }

        _cropResizeStartX = viewModel.Crop.X;
        _cropResizeStartY = viewModel.Crop.Y;
        _cropResizeStartWidth = viewModel.Crop.Width;
        _cropResizeStartHeight = viewModel.Crop.Height;
        viewModel.BeginHistoryGesture();
        _cropResizeCaptureElement = element;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void CropResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndGesture();
        e.Handled = true;
    }

    private void CropResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_gesture is CropGesture.None or CropGesture.Create or CropGesture.Move ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        ResizeCrop(
            ClampToSource(e.GetPosition(ImageCanvas), viewModel),
            viewModel,
            viewModel.SelectedSource.Width,
            viewModel.SelectedSource.Height);
        e.Handled = true;
    }

    private void CropResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_gesture is >= CropGesture.ResizeTopLeft and <= CropGesture.ResizeLeft)
        {
            _gesture = CropGesture.None;
            _cropResizeCaptureElement = null;
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.CompleteHistoryGesture();
            }
        }
    }

    private void ResizeCrop(
        Point position,
        MainWindowViewModel viewModel,
        long sourceWidth,
        long sourceHeight)
    {
        long left = _cropResizeStartX;
        long top = _cropResizeStartY;
        long right = _cropResizeStartX + _cropResizeStartWidth;
        long bottom = _cropResizeStartY + _cropResizeStartHeight;
        long x = Math.Clamp((long)Math.Round(position.X), 0, sourceWidth);
        long y = Math.Clamp((long)Math.Round(position.Y), 0, sourceHeight);

        if (_gesture is CropGesture.ResizeTopLeft or CropGesture.ResizeLeft or CropGesture.ResizeBottomLeft)
        {
            left = Math.Clamp(x, 0, right - 1);
        }
        if (_gesture is CropGesture.ResizeTopRight or CropGesture.ResizeRight or CropGesture.ResizeBottomRight)
        {
            right = Math.Clamp(x, left + 1, sourceWidth);
        }
        if (_gesture is CropGesture.ResizeTopLeft or CropGesture.ResizeTop or CropGesture.ResizeTopRight)
        {
            top = Math.Clamp(y, 0, bottom - 1);
        }
        if (_gesture is CropGesture.ResizeBottomLeft or CropGesture.ResizeBottom or CropGesture.ResizeBottomRight)
        {
            bottom = Math.Clamp(y, top + 1, sourceHeight);
        }

        viewModel.Crop.SetBounds(left, top, right - left, bottom - top);
    }

    private static bool IsDescendantOf(DependencyObject ancestor, DependencyObject descendant)
    {
        for (DependencyObject? current = descendant; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }

    private void Measurement_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ScientificMeasurementViewModel measurement } element ||
            DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        viewModel.SelectedSource.SelectedMeasurement = measurement;
        ShowMeasurementInspector();
        if (measurement.IsLocked)
        {
            e.Handled = true;
            return;
        }

        _draggedMeasurement = measurement;
        _measurementHandle = MeasurementHandle.Move;
        _measurementDragAnchor = ClampToSource(e.GetPosition(ImageCanvas), viewModel);
        _measurementCaptureElement = element;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void CanonicalRoi_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RoiObjectItemViewModel roi } element ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SelectCanonicalRoi(roi);
        _selectedCanonicalRoiVertexIndex = -1;
        Point position = e.GetPosition(ImageCanvas);
        if (e.ClickCount > 1 && roi.Model.GeometryKind == SciCanvas.Core.Workspace.RoiGeometryKind.Polygon)
        {
            viewModel.BeginHistoryGesture();
            bool inserted = viewModel.TryInsertSelectedCanonicalRoiVertex(position.X, position.Y);
            viewModel.CompleteHistoryGesture();
            if (inserted)
            {
                _selectedCanonicalRoiVertexIndex = -1;
            }
            e.Handled = true;
            return;
        }

        _draggedCanonicalRoi = roi;
        _canonicalRoiDragVertexIndex = -1;
        _canonicalRoiDragAnchor = position;
        _canonicalRoiCaptureElement = element;
        viewModel.BeginHistoryGesture();
        element.CaptureMouse();
        e.Handled = true;
    }

    private void CanonicalRoi_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCanonicalRoi is null ||
            _canonicalRoiDragVertexIndex >= 0 ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(ImageCanvas);
        double deltaX = position.X - _canonicalRoiDragAnchor.X;
        double deltaY = position.Y - _canonicalRoiDragAnchor.Y;
        if (viewModel.TryMoveSelectedCanonicalRoi(deltaX, deltaY))
        {
            _canonicalRoiDragAnchor = position;
        }
        e.Handled = true;
    }

    private void CanonicalRoi_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndCanonicalRoiGesture();
        e.Handled = true;
    }

    private void CanonicalRoi_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggedCanonicalRoi is not null)
        {
            EndCanonicalRoiGesture();
        }
    }

    private void CanonicalRoiVertexHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                Tag: RoiVertexHandleViewModel vertex,
            } element ||
            FindVisualAncestor<Canvas>(element)?.Tag is not RoiObjectItemViewModel roi ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.SelectCanonicalRoi(roi);
        _draggedCanonicalRoi = roi;
        _canonicalRoiDragVertexIndex = vertex.Index;
        _selectedCanonicalRoiVertexIndex =
            roi.Model.GeometryKind == SciCanvas.Core.Workspace.RoiGeometryKind.Polygon
                ? vertex.Index
                : -1;
        _canonicalRoiCaptureElement = element;
        _canonicalRoiDragAnchor = e.GetPosition(ImageCanvas);
        viewModel.BeginHistoryGesture();
        element.CaptureMouse();
        e.Handled = true;
    }

    private void CanonicalRoiVertexHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedCanonicalRoi is null ||
            _canonicalRoiDragVertexIndex < 0 ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(ImageCanvas);
        _ = viewModel.TryUpdateSelectedCanonicalRoiVertex(
            _canonicalRoiDragVertexIndex,
            position.X,
            position.Y);
        e.Handled = true;
    }

    private void CanonicalRoiVertexHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndCanonicalRoiGesture();
        e.Handled = true;
    }

    private void CanonicalRoiVertexHandle_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggedCanonicalRoi is not null)
        {
            EndCanonicalRoiGesture();
        }
    }

    private void EndCanonicalRoiGesture()
    {
        FrameworkElement? capture = _canonicalRoiCaptureElement;
        _canonicalRoiCaptureElement = null;
        _draggedCanonicalRoi = null;
        _canonicalRoiDragVertexIndex = -1;
        if (capture?.IsMouseCaptured == true)
        {
            capture.ReleaseMouseCapture();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void MeasurementResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: ScientificMeasurementViewModel measurement } element ||
            DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        viewModel.SelectedSource.SelectedMeasurement = measurement;
        ShowMeasurementInspector();
        if (measurement.IsLocked)
        {
            e.Handled = true;
            return;
        }

        _measurementHandle = element.Name switch
        {
            "MeasurementPointAHandle" => MeasurementHandle.PointA,
            "MeasurementPointBHandle" => MeasurementHandle.PointB,
            "MeasurementPointCHandle" => MeasurementHandle.PointC,
            _ => MeasurementHandle.None,
        };
        if (_measurementHandle == MeasurementHandle.None)
        {
            return;
        }

        _draggedMeasurement = measurement;
        _measurementDragAnchor = ClampToSource(e.GetPosition(ImageCanvas), viewModel);
        _measurementCaptureElement = element;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void Measurement_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedMeasurement is null ||
            _measurementHandle == MeasurementHandle.None ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel { SelectedSource: not null } viewModel)
        {
            return;
        }

        Point position = ClampToSource(e.GetPosition(ImageCanvas), viewModel);
        if (_measurementHandle == MeasurementHandle.Move)
        {
            _draggedMeasurement.MoveBy(
                position.X - _measurementDragAnchor.X,
                position.Y - _measurementDragAnchor.Y,
                viewModel.SelectedSource.Width,
                viewModel.SelectedSource.Height);
            _measurementDragAnchor = position;
        }
        else
        {
            ResizeMeasurement(
                _draggedMeasurement,
                _measurementHandle,
                position,
                viewModel.SelectedSource.Width,
                viewModel.SelectedSource.Height);
        }

        e.Handled = true;
    }

    private void Measurement_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedMeasurement is null)
        {
            return;
        }

        EndMeasurementGesture();
        e.Handled = true;
    }

    private void Measurement_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggedMeasurement is null)
        {
            return;
        }

        _draggedMeasurement = null;
        _measurementHandle = MeasurementHandle.None;
        _measurementCaptureElement = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void EndMeasurementGesture()
    {
        FrameworkElement? capture = _measurementCaptureElement;
        _draggedMeasurement = null;
        _measurementHandle = MeasurementHandle.None;
        _measurementCaptureElement = null;
        if (capture?.IsMouseCaptured == true)
        {
            capture.ReleaseMouseCapture();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void ShowMeasurementInspector()
    {
        if (DataContext is not MainWindowViewModel
            {
                SelectedSource.SelectedMeasurement: not null,
            } viewModel)
        {
            return;
        }

        SetRightSidebarExpanded(true);
        viewModel.IsLayersTabActive = false;
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(InspectorWorkspacePanel.BringMeasurementInspectorIntoView));
    }

    private static void ResizeMeasurement(
        ScientificMeasurementViewModel measurement,
        MeasurementHandle handle,
        Point position,
        long sourceWidth,
        long sourceHeight)
    {
        if (measurement.Kind == SciCanvas.Core.Science.ScientificMeasurementKind.CircleRoi &&
            handle is MeasurementHandle.PointA or MeasurementHandle.PointB)
        {
            double anchorX = handle == MeasurementHandle.PointA ? measurement.X2 : measurement.X1;
            double anchorY = handle == MeasurementHandle.PointA ? measurement.Y2 : measurement.Y1;
            double deltaX = position.X - anchorX;
            double deltaY = position.Y - anchorY;
            double maxHorizontal = deltaX < 0 ? anchorX : sourceWidth - anchorX;
            double maxVertical = deltaY < 0 ? anchorY : sourceHeight - anchorY;
            double diameter = Math.Min(
                Math.Max(Math.Abs(deltaX), Math.Abs(deltaY)),
                Math.Min(maxHorizontal, maxVertical));
            double x = anchorX + (deltaX < 0 ? -diameter : diameter);
            double y = anchorY + (deltaY < 0 ? -diameter : diameter);
            if (handle == MeasurementHandle.PointA)
            {
                measurement.UpdatePointA(x, y);
            }
            else
            {
                measurement.UpdatePointB(x, y);
            }
            return;
        }

        switch (handle)
        {
            case MeasurementHandle.PointA:
                measurement.UpdatePointA(position.X, position.Y);
                break;
            case MeasurementHandle.PointB:
                measurement.UpdatePointB(position.X, position.Y);
                break;
            case MeasurementHandle.PointC:
                measurement.UpdatePointC(position.X, position.Y);
                break;
        }
    }

    private void FigurePanel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigurePanelViewModel panel } element ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        // The resize handle is a child of the panel and owns its own preview gesture.
        if (e.OriginalSource is FrameworkElement original && original.Cursor == Cursors.SizeNWSE)
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
        if (_resizingFigurePanel is not null)
        {
            return;
        }

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
        if (_resizingFigurePanel is not null)
        {
            return;
        }

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

    private void FigurePanelResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigurePanelViewModel panel } element ||
            DataContext is not MainWindowViewModel viewModel ||
            panel.IsLocked)
        {
            return;
        }

        viewModel.Figure.SelectPanel(panel, toggle: false);
        _resizingFigurePanel = panel;
        _resizeHandleElement = element;
        _figureResizeAnchor = e.GetPosition(FigureSurface);
        _figureResizeStartWidth = panel.Width;
        _figureResizeStartHeight = panel.Height;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void FigurePanelResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizingFigurePanel is null ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        long deltaX = (long)Math.Round(position.X - _figureResizeAnchor.X);
        long deltaY = (long)Math.Round(position.Y - _figureResizeAnchor.Y);
        long maxWidth = Math.Max(1, viewModel.Figure.CanvasWidth - _resizingFigurePanel.X);
        long maxHeight = Math.Max(1, viewModel.Figure.CanvasHeight - _resizingFigurePanel.Y);

        if (_resizingFigurePanel.IsAspectRatioLocked)
        {
            double aspect = _resizingFigurePanel.SourceRect.Width /
                            (double)Math.Max(1, _resizingFigurePanel.SourceRect.Height);
            long maxAspectWidth = Math.Max(1, Math.Min(
                maxWidth,
                (long)Math.Floor(maxHeight * aspect)));
            long proposedWidth = Math.Abs(deltaX) >= Math.Abs(deltaY * aspect)
                ? _figureResizeStartWidth + deltaX
                : _figureResizeStartWidth + (long)Math.Round(deltaY * aspect);
            _resizingFigurePanel.Width = Math.Clamp(proposedWidth, 1, maxAspectWidth);
        }
        else
        {
            _resizingFigurePanel.Width = Math.Clamp(_figureResizeStartWidth + deltaX, 1, maxWidth);
            _resizingFigurePanel.Height = Math.Clamp(_figureResizeStartHeight + deltaY, 1, maxHeight);
        }

        e.Handled = true;
    }

    private void FigurePanelResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_resizeHandleElement?.IsMouseCaptured == true)
        {
            _resizeHandleElement.ReleaseMouseCapture();
        }

        _resizeHandleElement = null;
        _resizingFigurePanel = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
        e.Handled = true;
    }

    private void FigurePanelResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        _resizeHandleElement = null;
        _resizingFigurePanel = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigurePlotPanel_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigurePlotPanelViewModel panel } element ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }
        if (e.OriginalSource is FrameworkElement original && original.Cursor == Cursors.SizeNWSE)
        {
            return;
        }

        viewModel.Figure.SelectPlotPanel(panel);
        if (panel.IsLocked)
        {
            e.Handled = true;
            return;
        }

        _draggedFigurePlotPanel = panel;
        _figurePlotDragElement = element;
        _figureDragAnchor = e.GetPosition(FigureSurface);
        _figurePlotDragStartX = panel.X;
        _figurePlotDragStartY = panel.Y;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void FigurePlotPanel_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedFigurePlotPanel is not { } panel ||
            _resizingFigurePlotPanel is not null ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        long deltaX = (long)Math.Round(position.X - _figureDragAnchor.X);
        long deltaY = (long)Math.Round(position.Y - _figureDragAnchor.Y);
        viewModel.Figure.MovePlotPanel(
            panel,
            _figurePlotDragStartX + deltaX,
            _figurePlotDragStartY + deltaY);
        e.Handled = true;
    }

    private void FigurePlotPanel_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndFigurePlotPanelDrag();
        e.Handled = true;
    }

    private void FigurePlotPanel_OnLostMouseCapture(object sender, MouseEventArgs e) =>
        EndFigurePlotPanelDrag();

    private void EndFigurePlotPanelDrag()
    {
        if (_figurePlotDragElement?.IsMouseCaptured == true)
        {
            _figurePlotDragElement.ReleaseMouseCapture();
        }
        _figurePlotDragElement = null;
        _draggedFigurePlotPanel = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void FigurePlotPanelResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigurePlotPanelViewModel panel } element ||
            DataContext is not MainWindowViewModel viewModel || panel.IsLocked)
        {
            return;
        }

        viewModel.Figure.SelectPlotPanel(panel);
        _resizingFigurePlotPanel = panel;
        _figurePlotResizeElement = element;
        _figureResizeAnchor = e.GetPosition(FigureSurface);
        _figurePlotResizeStartWidth = panel.Width;
        _figurePlotResizeStartHeight = panel.Height;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void FigurePlotPanelResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_resizingFigurePlotPanel is not { } panel ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        long deltaX = (long)Math.Round(position.X - _figureResizeAnchor.X);
        long deltaY = (long)Math.Round(position.Y - _figureResizeAnchor.Y);
        long maxWidth = Math.Max(120, viewModel.Figure.CanvasWidth - panel.X);
        long maxHeight = Math.Max(100, viewModel.Figure.CanvasHeight - panel.Y);
        panel.Width = Math.Clamp(_figurePlotResizeStartWidth + deltaX, 120, maxWidth);
        panel.Height = Math.Clamp(_figurePlotResizeStartHeight + deltaY, 100, maxHeight);
        e.Handled = true;
    }

    private void FigurePlotPanelResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndFigurePlotPanelResize();
        e.Handled = true;
    }

    private void FigurePlotPanelResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e) =>
        EndFigurePlotPanelResize();

    private void EndFigurePlotPanelResize()
    {
        if (_figurePlotResizeElement?.IsMouseCaptured == true)
        {
            _figurePlotResizeElement.ReleaseMouseCapture();
        }
        _figurePlotResizeElement = null;
        _resizingFigurePlotPanel = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void Annotation_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigureAnnotationViewModel annotation } element ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject original &&
            FindVisualAncestor<Thumb>(original) is not null)
        {
            return;
        }

        bool toggle = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        viewModel.Figure.SelectAnnotation(annotation, toggle);
        if (!annotation.IsSelected || annotation.IsLocked)
        {
            e.Handled = true;
            return;
        }

        _draggedAnnotation = annotation;
        _annotationHandle = AnnotationHandle.Move;
        _annotationCaptureElement = element;
        _annotationDragAnchor = e.GetPosition(FigureSurface);
        element.CaptureMouse();
        e.Handled = true;
    }

    private void Annotation_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedAnnotation is null ||
            _annotationHandle != AnnotationHandle.Move ||
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
        EndAnnotationGesture();
        e.Handled = true;
    }

    private void Annotation_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_annotationCaptureElement is not null &&
            !ReferenceEquals(sender, _annotationCaptureElement))
        {
            return;
        }

        _draggedAnnotation = null;
        _annotationHandle = AnnotationHandle.None;
        _annotationCaptureElement = null;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void AnnotationHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigureAnnotationViewModel annotation } element ||
            DataContext is not MainWindowViewModel viewModel ||
            annotation.IsLocked)
        {
            return;
        }

        viewModel.Figure.SelectAnnotation(annotation, toggle: false);
        _annotationHandle = element.Name switch
        {
            "AnnotationStartHandle" => AnnotationHandle.Start,
            "AnnotationEndHandle" => AnnotationHandle.End,
            _ => AnnotationHandle.None,
        };
        if (_annotationHandle == AnnotationHandle.None)
        {
            return;
        }

        _draggedAnnotation = annotation;
        _annotationCaptureElement = element;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void AnnotationHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedAnnotation is null ||
            _annotationHandle is not (AnnotationHandle.Start or AnnotationHandle.End) ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        if (_annotationHandle == AnnotationHandle.Start)
        {
            _draggedAnnotation.SetStartPoint(position.X, position.Y);
        }
        else
        {
            _draggedAnnotation.SetEndPoint(position.X, position.Y);
        }

        e.Handled = true;
    }

    private void AnnotationHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndAnnotationGesture();
        e.Handled = true;
    }

    private void AnnotationHandle_OnLostMouseCapture(object sender, MouseEventArgs e) =>
        Annotation_OnLostMouseCapture(sender, e);

    private void EndAnnotationGesture()
    {
        FrameworkElement? capture = _annotationCaptureElement;
        _draggedAnnotation = null;
        _annotationHandle = AnnotationHandle.None;
        _annotationCaptureElement = null;
        if (capture?.IsMouseCaptured == true)
        {
            capture.ReleaseMouseCapture();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void ScientificObject_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigureScientificObjectViewModel scientificObject } element ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject original &&
            FindVisualAncestor<Thumb>(original) is not null)
        {
            return;
        }

        viewModel.Figure.SelectedScientificObject = scientificObject;
        if (scientificObject.IsLocked)
        {
            e.Handled = true;
            return;
        }

        if (scientificObject.Kind == SciCanvas.Core.Export.FigureScientificObjectKind.PolygonAnnotation &&
            e.ClickCount >= 2 &&
            e.OriginalSource is DependencyObject polygonSource &&
            FindVisualAncestor<System.Windows.Shapes.Polygon>(polygonSource) is not null)
        {
            Point insertionPoint = e.GetPosition(FigureSurface);
            viewModel.BeginHistoryGesture();
            viewModel.Figure.TryInsertSelectedPolygonAnnotationVertex(
                insertionPoint.X,
                insertionPoint.Y);
            viewModel.CompleteHistoryGesture();
            e.Handled = true;
            return;
        }

        scientificObject.ClearSelectedPolygonVertex();
        viewModel.BeginHistoryGesture();
        _draggedScientificObject = scientificObject;
        _scientificObjectCaptureElement = element;
        _scientificObjectDragAnchor = e.GetPosition(FigureSurface);
        _isResizingScientificObject = false;
        _scientificPolygonDragVertexIndex = -1;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void ScientificObject_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedScientificObject is null ||
            _isResizingScientificObject ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        viewModel.Figure.MoveScientificObject(
            _draggedScientificObject,
            position.X - _scientificObjectDragAnchor.X,
            position.Y - _scientificObjectDragAnchor.Y);
        _scientificObjectDragAnchor = position;
        e.Handled = true;
    }

    private void ScientificObject_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndScientificObjectGesture();
        e.Handled = true;
    }

    private void ScientificObject_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_scientificObjectCaptureElement is not null &&
            !ReferenceEquals(sender, _scientificObjectCaptureElement))
        {
            return;
        }

        _draggedScientificObject = null;
        _scientificObjectCaptureElement = null;
        _isResizingScientificObject = false;
        _scientificPolygonDragVertexIndex = -1;
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void ScientificObjectResizeHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: FigureScientificObjectViewModel scientificObject } element ||
            DataContext is not MainWindowViewModel viewModel ||
            scientificObject.IsLocked)
        {
            return;
        }

        viewModel.Figure.SelectedScientificObject = scientificObject;
        viewModel.BeginHistoryGesture();
        _draggedScientificObject = scientificObject;
        _scientificObjectCaptureElement = element;
        _isResizingScientificObject = true;
        _scientificPolygonDragVertexIndex = -1;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void ScientificObjectResizeHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedScientificObject is null ||
            !_isResizingScientificObject ||
            e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        _draggedScientificObject.SetResizePoint(position.X, position.Y);
        e.Handled = true;
    }

    private void ScientificObjectResizeHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndScientificObjectGesture();
        e.Handled = true;
    }

    private void ScientificObjectResizeHandle_OnLostMouseCapture(object sender, MouseEventArgs e) =>
        ScientificObject_OnLostMouseCapture(sender, e);

    private void ScientificPolygonVertexHandle_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement
            {
                DataContext: FigureScientificVertexHandle handle,
                Tag: FigureScientificObjectViewModel scientificObject,
            } element ||
            DataContext is not MainWindowViewModel viewModel ||
            scientificObject.IsLocked)
        {
            return;
        }

        viewModel.Figure.SelectedScientificObject = scientificObject;
        if (!scientificObject.TrySelectPolygonVertex(handle.Index))
        {
            return;
        }

        viewModel.BeginHistoryGesture();
        _draggedScientificObject = scientificObject;
        _scientificObjectCaptureElement = element;
        _isResizingScientificObject = true;
        _scientificPolygonDragVertexIndex = handle.Index;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void ScientificPolygonVertexHandle_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedScientificObject is null ||
            _scientificPolygonDragVertexIndex < 0 ||
            e.LeftButton != MouseButtonState.Pressed ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        viewModel.Figure.TryMoveSelectedPolygonAnnotationVertex(
            _scientificPolygonDragVertexIndex,
            position.X,
            position.Y);
        e.Handled = true;
    }

    private void ScientificPolygonVertexHandle_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        EndScientificObjectGesture();
        e.Handled = true;
    }

    private void ScientificPolygonVertexHandle_OnLostMouseCapture(object sender, MouseEventArgs e) =>
        ScientificObject_OnLostMouseCapture(sender, e);

    private void FigurePolygonAnnotationDraft_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel ||
            !viewModel.Figure.HasPendingPolygonAnnotation)
        {
            return;
        }

        if (e.ClickCount >= 2)
        {
            viewModel.Figure.CompletePendingPolygonAnnotation();
            e.Handled = true;
            return;
        }

        Point position = e.GetPosition(FigureSurface);
        viewModel.Figure.TryAddPolygonAnnotationDraftVertex(position.X, position.Y);
        e.Handled = true;
    }

    private void EndScientificObjectGesture()
    {
        FrameworkElement? capture = _scientificObjectCaptureElement;
        _draggedScientificObject = null;
        _scientificObjectCaptureElement = null;
        _isResizingScientificObject = false;
        _scientificPolygonDragVertexIndex = -1;
        if (capture?.IsMouseCaptured == true)
        {
            capture.ReleaseMouseCapture();
        }

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.CompleteHistoryGesture();
        }
    }

    private void MainWindow_OnLoaded(object sender, RoutedEventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, FitVisibleWorkspace);

    private void ImageViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_sourceZoomIsFit)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitSourceToViewport);
        }
    }

    private void FigureViewport_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_figureZoomIsFit)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitFigureToViewport);
        }
    }

    private void ImageCanvas_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_sourceZoomIsFit)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitSourceToViewport);
        }
    }

    private void FigureSurface_OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_figureZoomIsFit)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, FitFigureToViewport);
        }
    }

    private void SourceZoomOut_OnClick(object sender, RoutedEventArgs e) =>
        SetSourceZoom(_sourceZoom / 1.25, fit: false, null);

    private void SourceZoomIn_OnClick(object sender, RoutedEventArgs e) =>
        SetSourceZoom(_sourceZoom * 1.25, fit: false, null);

    private void SourceZoomFit_OnClick(object sender, RoutedEventArgs e) => FitSourceToViewport();

    private void SourceZoomActual_OnClick(object sender, RoutedEventArgs e) =>
        SetSourceZoom(1, fit: false, null);

    private void FigureZoomOut_OnClick(object sender, RoutedEventArgs e) =>
        SetFigureZoom(_figureZoom / 1.25, fit: false, null);

    private void FigureZoomIn_OnClick(object sender, RoutedEventArgs e) =>
        SetFigureZoom(_figureZoom * 1.25, fit: false, null);

    private void FigureZoomFit_OnClick(object sender, RoutedEventArgs e) => FitFigureToViewport();

    private void FigureZoomActual_OnClick(object sender, RoutedEventArgs e) =>
        SetFigureZoom(1, fit: false, null);

    private void ImageViewport_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = Math.Pow(1.15, e.Delta / 120.0);
        SetSourceZoom(_sourceZoom * factor, fit: false, e.GetPosition(ImageViewport));
        e.Handled = true;
    }

    private void FigureViewport_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = Math.Pow(1.15, e.Delta / 120.0);
        SetFigureZoom(_figureZoom * factor, fit: false, e.GetPosition(FigureViewport));
        e.Handled = true;
    }

    private void Viewport_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer viewport ||
            (e.ChangedButton != MouseButton.Middle &&
             !(e.ChangedButton == MouseButton.Left && Keyboard.IsKeyDown(Key.Space))))
        {
            return;
        }

        _panningViewport = viewport;
        _panAnchor = e.GetPosition(viewport);
        _panStartHorizontalOffset = viewport.HorizontalOffset;
        _panStartVerticalOffset = viewport.VerticalOffset;
        viewport.Cursor = Cursors.Hand;
        viewport.CaptureMouse();
        e.Handled = true;
    }

    private void Viewport_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not ScrollViewer viewport || !ReferenceEquals(_panningViewport, viewport))
        {
            return;
        }

        Point position = e.GetPosition(viewport);
        viewport.ScrollToHorizontalOffset(_panStartHorizontalOffset - (position.X - _panAnchor.X));
        viewport.ScrollToVerticalOffset(_panStartVerticalOffset - (position.Y - _panAnchor.Y));
        e.Handled = true;
    }

    private void Viewport_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is ScrollViewer viewport && ReferenceEquals(_panningViewport, viewport))
        {
            EndViewportPan(viewport);
            e.Handled = true;
        }
    }

    private void EndViewportPan(ScrollViewer viewport)
    {
        _panningViewport = null;
        viewport.Cursor = Cursors.Arrow;
        if (viewport.IsMouseCaptured)
        {
            viewport.ReleaseMouseCapture();
        }
    }

    private void FitVisibleWorkspace()
    {
        if (DataContext is MainWindowViewModel { WorkspaceMode: WorkspaceMode.Figure })
        {
            FitFigureToViewport();
        }
        else
        {
            FitSourceToViewport();
        }
    }

    private void FitSourceToViewport()
    {
        if (DataContext is MainWindowViewModel { SelectedSource: null })
        {
            SetSourceZoom(1, fit: true, null);
            return;
        }

        if (ImageViewport.ViewportWidth <= 0 || ImageViewport.ViewportHeight <= 0 ||
            ImageCanvas.ActualWidth <= 0 || ImageCanvas.ActualHeight <= 0)
        {
            return;
        }

        double zoom = Math.Min(
            Math.Max(1, ImageViewport.ViewportWidth - 24) / ImageCanvas.ActualWidth,
            Math.Max(1, ImageViewport.ViewportHeight - 24) / ImageCanvas.ActualHeight);
        SetSourceZoom(zoom, fit: true, null);
    }

    private void FitFigureToViewport()
    {
        if (FigureViewport.ViewportWidth <= 0 || FigureViewport.ViewportHeight <= 0 ||
            FigureSurface.ActualWidth <= 0 || FigureSurface.ActualHeight <= 0)
        {
            return;
        }

        double zoom = Math.Min(
            Math.Max(1, FigureViewport.ViewportWidth - 24) / FigureSurface.ActualWidth,
            Math.Max(1, FigureViewport.ViewportHeight - 24) / FigureSurface.ActualHeight);
        SetFigureZoom(zoom, fit: true, null);
    }

    private void SetSourceZoom(double zoom, bool fit, Point? anchor)
    {
        double previous = _sourceZoom;
        _sourceZoom = ApplyZoom(ImageViewport, ImageZoomTransform, previous, zoom, anchor);
        _sourceZoomIsFit = fit;
        SourceZoomText.Text = $"{_sourceZoom:P0}";
    }

    private void SetFigureZoom(double zoom, bool fit, Point? anchor)
    {
        double previous = _figureZoom;
        _figureZoom = ApplyZoom(FigureViewport, FigureZoomTransform, previous, zoom, anchor);
        _figureZoomIsFit = fit;
        FigureZoomText.Text = $"{_figureZoom:P0}";
    }

    private static double ApplyZoom(
        ScrollViewer viewport,
        ScaleTransform transform,
        double previousZoom,
        double requestedZoom,
        Point? anchor)
    {
        double nextZoom = Math.Clamp(requestedZoom, MinimumZoom, MaximumZoom);
        Point point = anchor ?? new Point(viewport.ViewportWidth / 2, viewport.ViewportHeight / 2);
        double contentX = (viewport.HorizontalOffset + point.X) / Math.Max(MinimumZoom, previousZoom);
        double contentY = (viewport.VerticalOffset + point.Y) / Math.Max(MinimumZoom, previousZoom);
        transform.ScaleX = nextZoom;
        transform.ScaleY = nextZoom;
        viewport.UpdateLayout();
        viewport.ScrollToHorizontalOffset(contentX * nextZoom - point.X);
        viewport.ScrollToVerticalOffset(contentY * nextZoom - point.Y);
        return nextZoom;
    }

    private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (IsTextInputFocused())
        {
            return;
        }

        bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (e.Key == Key.Enter &&
            DataContext is MainWindowViewModel
            {
                WorkspaceMode: WorkspaceMode.Figure,
                Figure.HasPendingPolygonAnnotation: true,
            } polygonCompletionViewModel)
        {
            polygonCompletionViewModel.Figure.CompletePendingPolygonAnnotation();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter &&
            DataContext is MainWindowViewModel { HasPendingCanonicalRoi: true } roiCompletionViewModel)
        {
            e.Handled = roiCompletionViewModel.CompletePendingCanonicalRoi();
            return;
        }

        if (e.Key is Key.Delete or Key.Back)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                if (viewModel.WorkspaceMode == WorkspaceMode.Figure &&
                    viewModel.Figure.SelectedScientificObject is
                    {
                        Kind: SciCanvas.Core.Export.FigureScientificObjectKind.PolygonAnnotation,
                        SelectedPolygonVertexIndex: >= 0,
                    } selectedPolygon)
                {
                    viewModel.BeginHistoryGesture();
                    viewModel.Figure.TryDeleteSelectedPolygonAnnotationVertex(
                        selectedPolygon.SelectedPolygonVertexIndex);
                    viewModel.CompleteHistoryGesture();
                }
                else if (_selectedCanonicalRoiVertexIndex >= 0 &&
                    viewModel.RoiPropagationWorkspace.SelectedRoi is { } selected &&
                    selected.AssetId == viewModel.SelectedSource?.Asset.Id)
                {
                    viewModel.BeginHistoryGesture();
                    bool deleted = viewModel.TryDeleteSelectedCanonicalRoiVertex(
                        _selectedCanonicalRoiVertexIndex);
                    viewModel.CompleteHistoryGesture();
                    if (deleted)
                    {
                        _selectedCanonicalRoiVertexIndex = -1;
                    }
                }
                else
                {
                    viewModel.DeleteSelectionCommand.Execute(null);
                }
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (DataContext is MainWindowViewModel
                {
                    WorkspaceMode: WorkspaceMode.Figure,
                } polygonCancellationViewModel &&
                polygonCancellationViewModel.Figure.CancelPendingPolygonAnnotation())
            {
                e.Handled = true;
                return;
            }
            if (DataContext is MainWindowViewModel roiCancellationViewModel &&
                roiCancellationViewModel.CancelPendingCanonicalRoi())
            {
                e.Handled = true;
                return;
            }
            CancelActiveGestureAndClearSelection();
            e.Handled = true;
            return;
        }

        if (e.Key is Key.OemPlus or Key.Add && control)
        {
            ZoomVisibleWorkspace(1.25);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.OemMinus or Key.Subtract && control)
        {
            ZoomVisibleWorkspace(0.8);
            e.Handled = true;
            return;
        }
        if (e.Key is Key.D0 or Key.NumPad0 && control)
        {
            FitVisibleWorkspace();
            e.Handled = true;
            return;
        }
        if (e.Key is Key.D1 or Key.NumPad1 && control)
        {
            SetVisibleWorkspaceZoom(1);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.A && DataContext is MainWindowViewModel { WorkspaceMode: WorkspaceMode.Figure } selectViewModel)
        {
            selectViewModel.Figure.SelectAllPanelsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.C && DataContext is MainWindowViewModel { WorkspaceMode: WorkspaceMode.Crop } copyViewModel)
        {
            copyViewModel.CopyMeasurementsCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
        {
            int distance = shift ? 10 : 1;
            int deltaX = e.Key == Key.Left ? -distance : e.Key == Key.Right ? distance : 0;
            int deltaY = e.Key == Key.Up ? -distance : e.Key == Key.Down ? distance : 0;
            e.Handled = NudgeSelection(deltaX, deltaY);
            return;
        }

        if (!control && Keyboard.Modifiers == ModifierKeys.None && DataContext is MainWindowViewModel tools)
        {
            e.Handled = e.Key switch
            {
                Key.V => ExecuteTool(tools.SelectCropToolCommand),
                Key.C => ExecuteTool(tools.SelectCropToolCommand),
                Key.K => ExecuteTool(tools.SelectCalibrationToolCommand),
                Key.L => ExecuteTool(tools.SelectLengthToolCommand),
                Key.A => ExecuteTool(tools.SelectAngleToolCommand),
                Key.R => ExecuteTool(tools.SelectRectangleRoiToolCommand),
                Key.E => ExecuteTool(tools.SelectCircleRoiToolCommand),
                Key.P => ExecuteTool(tools.SelectPolylineToolCommand),
                Key.F => FitWorkspaceShortcut(),
                Key.D1 or Key.NumPad1 => ActualSizeShortcut(),
                _ => false,
            };
        }
    }

    private static bool ExecuteTool(ICommand command)
    {
        if (command.CanExecute(null))
        {
            command.Execute(null);
        }
        return true;
    }

    private bool FitWorkspaceShortcut()
    {
        FitVisibleWorkspace();
        return true;
    }

    private bool ActualSizeShortcut()
    {
        SetVisibleWorkspaceZoom(1);
        return true;
    }

    private void ZoomVisibleWorkspace(double factor)
    {
        if (DataContext is MainWindowViewModel { WorkspaceMode: WorkspaceMode.Figure })
        {
            SetFigureZoom(_figureZoom * factor, fit: false, null);
        }
        else
        {
            SetSourceZoom(_sourceZoom * factor, fit: false, null);
        }
    }

    private void SetVisibleWorkspaceZoom(double zoom)
    {
        if (DataContext is MainWindowViewModel { WorkspaceMode: WorkspaceMode.Figure })
        {
            SetFigureZoom(zoom, fit: false, null);
        }
        else
        {
            SetSourceZoom(zoom, fit: false, null);
        }
    }

    private bool NudgeSelection(int deltaX, int deltaY)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return false;
        }

        if (viewModel.WorkspaceMode == WorkspaceMode.Crop && viewModel.SelectedSource is { } source)
        {
            if (source.SelectedMeasurement is { IsLocked: false } measurement)
            {
                measurement.MoveBy(deltaX, deltaY, source.Width, source.Height);
                viewModel.CompleteHistoryGesture();
                return true;
            }

            if (viewModel.ActiveScienceTool == ScientificToolMode.Crop)
            {
                viewModel.Crop.X = Math.Clamp(viewModel.Crop.X + deltaX, 0, Math.Max(0, source.Width - viewModel.Crop.Width));
                viewModel.Crop.Y = Math.Clamp(viewModel.Crop.Y + deltaY, 0, Math.Max(0, source.Height - viewModel.Crop.Height));
                viewModel.CompleteHistoryGesture();
                return true;
            }
        }

        if (viewModel.WorkspaceMode == WorkspaceMode.Figure)
        {
            if (viewModel.Figure.SelectedPlotPanel is { IsLocked: false } plotPanel)
            {
                viewModel.Figure.MovePlotPanel(
                    plotPanel,
                    plotPanel.X + deltaX,
                    plotPanel.Y + deltaY);
                viewModel.CompleteHistoryGesture();
                return true;
            }
            if (viewModel.Figure.SelectedAnnotation is { IsLocked: false } annotation)
            {
                viewModel.Figure.MoveAnnotation(annotation, deltaX, deltaY);
                viewModel.CompleteHistoryGesture();
                return true;
            }
            if (viewModel.Figure.SelectedGuide is { IsLocked: false } guide)
            {
                guide.Position += guide.Orientation == FigureGuideOrientation.Vertical ? deltaX : deltaY;
                viewModel.CompleteHistoryGesture();
                return true;
            }
            if (viewModel.Figure.SelectedPanels.Any(panel => !panel.IsLocked))
            {
                viewModel.Figure.MoveSelectedPanelsBy(deltaX, deltaY);
                viewModel.CompleteHistoryGesture();
                return true;
            }
        }

        return false;
    }

    private void CancelActiveGestureAndClearSelection()
    {
        if (_panningViewport is { } viewport)
        {
            EndViewportPan(viewport);
        }
        if (_draggedMeasurement is not null)
        {
            EndMeasurementGesture();
        }
        if (_draggedCanonicalRoi is not null)
        {
            EndCanonicalRoiGesture();
        }
        if (_gesture != CropGesture.None)
        {
            EndGesture();
        }
        if (_scientificGestureActive)
        {
            _scientificGestureActive = false;
            if (ImageCanvas.IsMouseCaptured)
            {
                ImageCanvas.ReleaseMouseCapture();
            }
        }

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (viewModel.WorkspaceMode == WorkspaceMode.Crop)
        {
            if (viewModel.SelectedSource is { } source)
            {
                source.SelectedMeasurement = null;
            }
            viewModel.ActiveScienceTool = ScientificToolMode.Crop;
        }
        else
        {
            if (_draggedFigurePlotPanel is not null)
            {
                EndFigurePlotPanelDrag();
            }
            if (_resizingFigurePlotPanel is not null)
            {
                EndFigurePlotPanelResize();
            }
            viewModel.Figure.SelectedAnnotation = null;
            viewModel.Figure.SelectedGuide = null;
            viewModel.Figure.SelectedPlotPanel = null;
            viewModel.Figure.ClearPanelSelectionCommand.Execute(null);
        }
    }

    private static bool IsTextInputFocused() => Keyboard.FocusedElement is
        TextBoxBase or PasswordBox or ComboBox;

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
            ScheduleClose();
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
            ScheduleClose();
        }
    }

    private void ScheduleClose() => Dispatcher.BeginInvoke(
        DispatcherPriority.ApplicationIdle,
        new Action(Close));

    private enum CropGesture
    {
        None,
        Create,
        Move,
        ResizeTopLeft,
        ResizeTop,
        ResizeTopRight,
        ResizeRight,
        ResizeBottomRight,
        ResizeBottom,
        ResizeBottomLeft,
        ResizeLeft,
    }

    private enum MeasurementHandle
    {
        None,
        Move,
        PointA,
        PointB,
        PointC,
    }

    private enum AnnotationHandle
    {
        None,
        Move,
        Start,
        End,
    }
}
