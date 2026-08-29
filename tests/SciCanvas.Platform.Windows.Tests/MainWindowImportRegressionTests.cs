using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SciCanvas.App;
using SciCanvas.Core.Export;
using SciCanvas.Core.Geometry;
using SciCanvas.Core.Images;
using SciCanvas.Core.Science;
using SciCanvas.Core.Sources;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Presentation;
using SciCanvas.Templates;
using CoreImageMetadata = SciCanvas.Core.Images.ImageMetadata;

namespace SciCanvas.Platform.Windows.Tests;

public sealed class MainWindowImportRegressionTests
{
    [Fact]
    public void RightSidebarTabs_RenderOnlyTheActivePage()
    {
        string? captureDirectory = Environment.GetEnvironmentVariable("SCICANVAS_QA_TABS_SCREENSHOT_DIR");
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1000,
                    Height = 720,
                    WindowState = WindowState.Normal,
                };
                window.Show();
                window.UpdateLayout();

                var inspector = Assert.IsType<ScrollViewer>(window.FindName("InspectorScrollViewer"));
                var layers = Assert.IsType<ScrollViewer>(window.FindName("LayersScrollViewer"));
                var channels = Assert.IsType<ChannelsInspector>(window.FindName("ChannelsInspectorPanel"));
                var linkedViews = Assert.IsType<LinkedViewsInspector>(window.FindName("LinkedViewsInspectorPanel"));
var registration = Assert.IsType<RegistrationWorkspace>(window.FindName("RegistrationWorkspacePanel"));
                var roiPropagation = Assert.IsType<RoiPropagationWorkspace>(window.FindName("RoiPropagationWorkspacePanel"));
                var rightSidebar = Assert.IsType<Border>(window.FindName("RightSidebarPanel"));
                var inspectorButton = Assert.IsType<Button>(window.FindName("InspectorTabButton"));
                var layersButton = Assert.IsType<Button>(window.FindName("LayersTabButton"));
                var channelsButton = Assert.IsType<Button>(window.FindName("ChannelsTabButton"));
                var linkedViewsButton = Assert.IsType<Button>(window.FindName("LinkedViewsTabButton"));
var registrationButton = Assert.IsType<Button>(window.FindName("RegistrationTabButton"));
                var roiPropagationButton = Assert.IsType<Button>(window.FindName("RoiPropagationTabButton"));

                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed);
                Assert.Equal(Visibility.Collapsed, linkedViews.Visibility);
                Assert.Equal(Visibility.Collapsed, registration.Visibility);
                Assert.Equal(Visibility.Collapsed, roiPropagation.Visibility);
                CaptureSidebarIfRequested(window, rightSidebar, captureDirectory, "inspector");

                ExecuteBoundButtonCommand(layersButton);
                window.UpdateLayout();
                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Collapsed, Visibility.Visible, Visibility.Collapsed);
                Assert.Equal(Visibility.Collapsed, linkedViews.Visibility);
                Assert.Equal(Visibility.Collapsed, registration.Visibility);
                Assert.Equal(Visibility.Collapsed, roiPropagation.Visibility);
                CaptureSidebarIfRequested(window, rightSidebar, captureDirectory, "layers");

                ExecuteBoundButtonCommand(channelsButton);
                window.UpdateLayout();
                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Collapsed, Visibility.Collapsed, Visibility.Visible);
                Assert.Equal(Visibility.Collapsed, linkedViews.Visibility);
                Assert.Equal(Visibility.Collapsed, registration.Visibility);
                Assert.Equal(Visibility.Collapsed, roiPropagation.Visibility);
                CaptureSidebarIfRequested(window, rightSidebar, captureDirectory, "channels");

                ExecuteBoundButtonCommand(linkedViewsButton);
                window.UpdateLayout();
                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
                Assert.Equal(Visibility.Visible, linkedViews.Visibility);
                Assert.Equal(Visibility.Collapsed, registration.Visibility);
                Assert.Equal(Visibility.Collapsed, roiPropagation.Visibility);
                CaptureSidebarIfRequested(window, rightSidebar, captureDirectory, "linked-views");

                ExecuteBoundButtonCommand(registrationButton);
                window.UpdateLayout();
                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
                Assert.Equal(Visibility.Collapsed, linkedViews.Visibility);
                Assert.Equal(Visibility.Visible, registration.Visibility);
                Assert.Equal(Visibility.Collapsed, roiPropagation.Visibility);
CaptureSidebarIfRequested(window, rightSidebar, captureDirectory, "registration");

                ExecuteBoundButtonCommand(roiPropagationButton);
                window.UpdateLayout();
                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Collapsed, Visibility.Collapsed, Visibility.Collapsed);
                Assert.Equal(Visibility.Collapsed, linkedViews.Visibility);
                Assert.Equal(Visibility.Collapsed, registration.Visibility);
                Assert.Equal(Visibility.Visible, roiPropagation.Visibility);
                CaptureSidebarIfRequested(window, rightSidebar, captureDirectory, "roi-propagation");

                ExecuteBoundButtonCommand(inspectorButton);
                window.UpdateLayout();
                AssertSidebarPageVisibility(inspector, layers, channels, Visibility.Visible, Visibility.Collapsed, Visibility.Collapsed);
                Assert.Equal(Visibility.Collapsed, linkedViews.Visibility);
                Assert.Equal(Visibility.Collapsed, registration.Visibility);
                Assert.Equal(Visibility.Collapsed, roiPropagation.Visibility);

                inspector.ScrollToVerticalOffset(200);
                window.UpdateLayout();
                Assert.True(inspector.VerticalOffset > 0);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void EmptyProject_HidesSourceViewportAndDefaultCropOverlay()
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1000,
                    Height = 720,
                };
                window.Show();
                window.UpdateLayout();

                var viewport = Assert.IsType<ScrollViewer>(window.FindName("ImageViewport"));
                var cropOverlay = Assert.IsType<Grid>(window.FindName("CropOverlay"));
                Assert.Equal(Visibility.Collapsed, viewport.Visibility);
                Assert.Equal(Visibility.Collapsed, cropOverlay.Visibility);

                SourceAssetItemViewModel source = CreateSourceItem();
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                window.UpdateLayout();

                Assert.Equal(Visibility.Visible, viewport.Visibility);
                Assert.Equal(Visibility.Visible, cropOverlay.Visibility);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }
    [Fact]
    public void WorkspacePanels_CollapseAndExpandToIncreaseCanvasSpace()
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                SourceAssetItemViewModel source = CreateSourceItem();
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1280,
                    Height = 820,
                };
                window.Show();
                window.UpdateLayout();

                var viewport = Assert.IsType<ScrollViewer>(window.FindName("ImageViewport"));
                double widthBefore = viewport.ActualWidth;
                var leftColumn = Assert.IsType<ColumnDefinition>(window.FindName("LeftSidebarColumn"));
                var rightColumn = Assert.IsType<ColumnDefinition>(window.FindName("RightSidebarColumn"));
                var headerArea = Assert.IsType<Grid>(window.FindName("HeaderCommandArea"));
                var dock = Assert.IsType<Border>(window.FindName("MeasurementDockPanel"));
                var leftToggle = Assert.IsType<Button>(window.FindName("LeftSidebarToggleButton"));
                var rightToggle = Assert.IsType<Button>(window.FindName("RightSidebarToggleButton"));
                var headerToggle = Assert.IsType<Button>(window.FindName("HeaderToggleButton"));
                var dockToggle = Assert.IsType<Button>(window.FindName("MeasurementDockToggleButton"));

                leftToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                rightToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                headerToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                dockToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                window.UpdateLayout();

                Assert.Equal(34, leftColumn.ActualWidth);
                Assert.Equal(34, rightColumn.ActualWidth);
                Assert.Equal(Visibility.Collapsed, headerArea.Visibility);
                Assert.Equal(36, dock.Height);
                Assert.True(viewport.ActualWidth > widthBefore);

                leftToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                rightToggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Assert.True(leftColumn.Width.IsStar);
                Assert.True(rightColumn.Width.IsStar);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void CropPointerGesture_DefersHistoryWorkUntilPointerRelease()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel source = CreateMeasurementSourceItem();
        viewModel.Sources.Add(source);
        viewModel.SelectedSource = source;
        string historyBeforeGesture = viewModel.HistoryStatusText;

        viewModel.BeginHistoryGesture();
        for (int index = 1; index <= 200; index++)
        {
            viewModel.Crop.SetBounds(index, index, 600, 400);
        }

        Assert.Equal(historyBeforeGesture, viewModel.HistoryStatusText);
        Assert.Equal(new PixelRect64(200, 200, 600, 400), AssertCrop(viewModel.Crop));

        viewModel.CompleteHistoryGesture();

        Assert.NotEqual(historyBeforeGesture, viewModel.HistoryStatusText);
        Assert.True(viewModel.IsDirty);
    }

    [Fact]
    public void BlankCanvasPointerDown_StartsNewCropInsteadOfDoingNothing()
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                SourceAssetItemViewModel source = CreateSourceItem();
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1000,
                    Height = 720,
                };
                window.Show();
                window.UpdateLayout();
                var canvas = Assert.IsType<Canvas>(window.FindName("ImageCanvas"));
                Assert.Equal(new PixelRect64(0, 0, 2, 2), AssertCrop(viewModel.Crop));

                canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonDownEvent,
                    Source = canvas,
                });

                Assert.Equal(1, viewModel.Crop.Width);
                Assert.Equal(1, viewModel.Crop.Height);

                canvas.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left)
                {
                    RoutedEvent = UIElement.MouseLeftButtonUpEvent,
                    Source = canvas,
                });
                Assert.False(canvas.IsMouseCaptured);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void CropResizeHandle_ReleasesMouseCaptureOnPointerUp()
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                SourceAssetItemViewModel source = CreateSourceItem();
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1000,
                    Height = 720,
                };
                window.Show();
                window.UpdateLayout();
                var handle = Assert.IsType<Thumb>(window.FindName("CropBottomRightHandle"));

                handle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                    Source = handle,
                });
                Assert.True(handle.IsMouseCaptured);

                handle.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 1, MouseButton.Left)
                {
                    RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
                    Source = handle,
                });
                Assert.False(handle.IsMouseCaptured);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void AddingFirstImportedSource_DoesNotThrowDuringTemplateLayout()
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                window = new MainWindow
                {
                    DataContext = viewModel,
                };

                viewModel.Sources.Add(CreateSourceItem());
                viewModel.SelectedSource = viewModel.Sources[0];
                FigurePanelViewModel? figurePanel = viewModel.Figure.AddPanel(
                    viewModel.Sources[0],
                    new PixelRect64(0, 0, 2, 2));
                Assert.NotNull(figurePanel);
                figurePanel.PhysicalUnitsPerSourcePixel = 0.5;
                figurePanel.ScaleBarPhysicalLength = 0.5;
                figurePanel.ScaleBarUnit = "µm";
                figurePanel.ShowScaleBar = true;
                viewModel.Figure.AddTextAnnotationCommand.Execute(null);
                viewModel.Figure.AddArrowAnnotationCommand.Execute(null);
                viewModel.WorkspaceMode = WorkspaceMode.Figure;

                window.Show();
                window.UpdateLayout();
                window.DataContext = null;
                window.Close();
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        });
    }

    [Fact]
    public void V2ScientificFigureWorkspace_RendersAndCanCaptureVisualEvidence()
    {
        string? capturePath = Environment.GetEnvironmentVariable("SCICANVAS_QA_V2_SCREENSHOT_PATH");
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                SourceAssetItemViewModel source = CreateMeasurementSourceItem();
                source.Calibration.Restore(
                    new SpatialCalibration(
                        source.Asset.Id,
                        0.001603,
                        0.001603,
                        "µm",
                        CalibrationOrigin.Manual),
                    90,
                    85,
                    402,
                    85);
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                viewModel.WorkspaceMode = WorkspaceMode.Figure;
                FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
                    viewModel.Figure.AddPanel(
                        source,
                        new PixelRect64(120, 80, 760, 600)));
                panel.FitMode = SciCanvas.Core.Workspace.PanelFitMode.Fill;
                panel.ShowScaleBar = true;
                panel.ApplySpatialCalibration(source.Calibration.Calibration);
                viewModel.RunFigureQcCommand.Execute(null);

                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1600,
                    Height = 1000,
                    WindowState = WindowState.Normal,
                };
                window.Show();
                window.UpdateLayout();
                var screenshot = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Round(window.ActualWidth)),
                    Math.Max(1, (int)Math.Round(window.ActualHeight)),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                screenshot.Render(window);
                if (!string.IsNullOrWhiteSpace(capturePath))
                {
                    string fullPath = Path.GetFullPath(capturePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(screenshot));
                    using FileStream output = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(output);

                    var inspector = Assert.IsType<ScrollViewer>(window.FindName("InspectorScrollViewer"));
                    inspector.ScrollToVerticalOffset(1050);
                    window.UpdateLayout();
                    var panelScreenshot = new RenderTargetBitmap(
                        Math.Max(1, (int)Math.Round(window.ActualWidth)),
                        Math.Max(1, (int)Math.Round(window.ActualHeight)),
                        96,
                        96,
                        PixelFormats.Pbgra32);
                    panelScreenshot.Render(window);
                    var panelEncoder = new PngBitmapEncoder();
                    panelEncoder.Frames.Add(BitmapFrame.Create(panelScreenshot));
                    string panelPath = Path.Combine(
                        Path.GetDirectoryName(fullPath)!,
                        $"{Path.GetFileNameWithoutExtension(fullPath)}-panel{Path.GetExtension(fullPath)}");
                    using FileStream panelOutput = new(panelPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    panelEncoder.Save(panelOutput);
                }

                window.DataContext = null;
                window.Close();
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Stage1MeasurementWorkspace_RendersAndCanCaptureVisualEvidence()
    {
        string? capturePath = Environment.GetEnvironmentVariable("SCICANVAS_QA_SCREENSHOT_PATH");
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                SourceAssetItemViewModel source = CreateMeasurementSourceItem();
                source.Calibration.Restore(
                    new SpatialCalibration(
                        source.Asset.Id,
                        0.001603,
                        0.001603,
                        "µm",
                        CalibrationOrigin.Manual,
                        312,
                        0.5),
                    90,
                    85,
                    402,
                    85);
                source.AddMeasurement(
                    ScientificMeasurementKind.Length,
                    new MeasurementPoint(250, 235),
                    new MeasurementPoint(768, 390));
                source.AddMeasurement(
                    ScientificMeasurementKind.Length,
                    new MeasurementPoint(390, 585),
                    new MeasurementPoint(865, 455));
                source.AddMeasurement(
                    ScientificMeasurementKind.Angle,
                    new MeasurementPoint(760, 470),
                    new MeasurementPoint(690, 610),
                    new MeasurementPoint(920, 570));
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                viewModel.ActiveScienceTool = ScientificToolMode.Length;
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1440,
                    Height = 900,
                };

                window.Show();
                window.UpdateLayout();
                var screenshot = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Round(window.ActualWidth)),
                    Math.Max(1, (int)Math.Round(window.ActualHeight)),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                screenshot.Render(window);
                if (!string.IsNullOrWhiteSpace(capturePath))
                {
                    string fullPath = Path.GetFullPath(capturePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(screenshot));
                    using FileStream output = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(output);
                }

                window.DataContext = null;
                window.Close();
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void Stage5AssistedAnalysis_RendersAndCanCaptureVisualEvidence()
    {
        string? capturePath = Environment.GetEnvironmentVariable("SCICANVAS_QA_STAGE5_SCREENSHOT_PATH");
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel(new FixedAssistedRegionAnalyzer());
                SourceAssetItemViewModel source = CreateMeasurementSourceItem();
                source.Calibration.Restore(
                    new SpatialCalibration(
                        source.Asset.Id,
                        0.0016,
                        0.0016,
                        "µm",
                        CalibrationOrigin.Manual),
                    90,
                    85,
                    402,
                    85);
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                viewModel.MinimumRegionAreaPixels = 32;
                viewModel.AnalyzeAssistedRegionsCommand.Execute(null);
                window = new MainWindow
                {
                    DataContext = viewModel,
                    Width = 1440,
                    Height = 900,
                };
                window.Show();
                window.UpdateLayout();
                var inspector = Assert.IsType<ScrollViewer>(window.FindName("InspectorScrollViewer"));
                inspector.ScrollToVerticalOffset(360);
                window.UpdateLayout();
                var screenshot = new RenderTargetBitmap(
                    Math.Max(1, (int)Math.Round(window.ActualWidth)),
                    Math.Max(1, (int)Math.Round(window.ActualHeight)),
                    96,
                    96,
                    PixelFormats.Pbgra32);
                screenshot.Render(window);
                if (!string.IsNullOrWhiteSpace(capturePath))
                {
                    string fullPath = Path.GetFullPath(capturePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(screenshot));
                    using FileStream output = new(fullPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    encoder.Save(output);
                }

                window.DataContext = null;
                window.Close();
            }
            finally
            {
                if (window?.IsVisible == true)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Theory]
    [InlineData(720, 480, "minimum supported viewport")]
    [InlineData(1536, 864, "1920x1080 @ 125%")]
    [InlineData(1280, 720, "1920x1080 @ 150%")]
    [InlineData(1920, 1080, "3840x2160 @ 200%")]
    [InlineData(1536, 864, "3840x2160 @ 250%")]
    public void CommonHighDpiLogicalViewports_KeepInspectorScrollableAndShortcutsBound(
        int logicalWidth,
        int logicalHeight,
        string scenario)
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                window = new MainWindow
                {
                    DataContext = CreateViewModel(),
                    Width = logicalWidth,
                    Height = logicalHeight,
                    WindowState = WindowState.Normal,
                };
                window.Show();
                window.UpdateLayout();

                var inspector = Assert.IsType<ScrollViewer>(window.FindName("InspectorScrollViewer"));
                Assert.True(inspector.ActualWidth > 0, scenario);
                Assert.True(inspector.ViewportHeight > 0, scenario);
                Assert.True(inspector.ExtentHeight >= inspector.ViewportHeight, scenario);
                var layers = Assert.IsType<ScrollViewer>(window.FindName("LayersScrollViewer"));
                Assert.Equal(ScrollBarVisibility.Disabled, layers.HorizontalScrollBarVisibility);
                Assert.NotNull(window.FindName("ImageViewport"));
                Assert.NotNull(window.FindName("FigureViewport"));
                Assert.NotNull(window.FindName("CropTopLeftHandle"));
                Assert.NotNull(window.FindName("CropBottomRightHandle"));
                Assert.NotNull(window.FindName("MeasurementInspectorPanel"));

                var bindings = window.InputBindings
                    .OfType<System.Windows.Input.KeyBinding>()
                    .ToArray();
                Assert.Contains(bindings, binding =>
                    binding.Key == System.Windows.Input.Key.S &&
                    binding.Modifiers == System.Windows.Input.ModifierKeys.Control);
                Assert.Contains(bindings, binding =>
                    binding.Key == System.Windows.Input.Key.Z &&
                    binding.Modifiers == System.Windows.Input.ModifierKeys.Control);
                Assert.Contains(bindings, binding =>
                    binding.Key == System.Windows.Input.Key.Enter &&
                    binding.Modifiers == System.Windows.Input.ModifierKeys.Control);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void CanvasShortcuts_SelectToolAndDeleteSelectedMeasurement()
    {
        WpfTestHost.Invoke(() =>
        {
            MainWindow? window = null;
            try
            {
                MainWindowViewModel viewModel = CreateViewModel();
                SourceAssetItemViewModel source = CreateMeasurementSourceItem();
                ScientificMeasurementViewModel measurement = source.AddMeasurement(
                    ScientificMeasurementKind.Length,
                    new MeasurementPoint(20, 30),
                    new MeasurementPoint(120, 160));
                viewModel.Sources.Add(source);
                viewModel.SelectedSource = source;
                window = new MainWindow
                {
                    DataContext = viewModel,
                    WindowState = WindowState.Normal,
                    Width = 1000,
                    Height = 720,
                };
                window.Show();
                window.UpdateLayout();

                var selectLength = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window),
                    timestamp: 0,
                    System.Windows.Input.Key.L)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
                };
                window.RaiseEvent(selectLength);
                Assert.Equal(ScientificToolMode.Length, viewModel.ActiveScienceTool);

                source.SelectedMeasurement = measurement;
                var delete = new System.Windows.Input.KeyEventArgs(
                    System.Windows.Input.Keyboard.PrimaryDevice,
                    PresentationSource.FromVisual(window),
                    timestamp: 0,
                    System.Windows.Input.Key.Delete)
                {
                    RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent,
                };
                window.RaiseEvent(delete);

                Assert.Empty(source.Measurements);
            }
            finally
            {
                if (window is not null)
                {
                    window.DataContext = null;
                    window.Close();
                }
            }
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public void SwitchingTools_CancelsIncompletePolylineInsteadOfKeepingTransientMeasurement()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel source = CreateMeasurementSourceItem();
        viewModel.Sources.Add(source);
        viewModel.SelectedSource = source;
        viewModel.ActiveScienceTool = ScientificToolMode.Polyline;
        viewModel.BeginScientificGesture(10, 10);
        viewModel.BeginScientificGesture(30, 20);
        Assert.Single(source.Measurements);

        viewModel.ActiveScienceTool = ScientificToolMode.Crop;

        Assert.Empty(source.Measurements);
    }

    [Fact]
    public void MeasurementDrawingStyle_CarriesAcrossConsecutiveMeasurements()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel source = CreateMeasurementSourceItem();
        viewModel.Sources.Add(source);
        viewModel.SelectedSource = source;
        viewModel.ActiveScienceTool = ScientificToolMode.Length;

        Assert.True(viewModel.BeginScientificGesture(20, 30));
        viewModel.UpdateScientificGesture(120, 160);
        viewModel.CompleteScientificGesture();
        ScientificMeasurementViewModel first = Assert.Single(source.Measurements);
        first.StrokeColor = "#FF7C3AED";
        first.StrokeWidthPixels = 6;
        first.LineStyle = "dash-dot";
        first.MarkerSizePixels = 28;
        first.ShowMarkers = false;
        first.ShowLabel = false;
        first.FillOpacityPercent = 24;

        Assert.True(viewModel.BeginScientificGesture(220, 230));
        viewModel.UpdateScientificGesture(420, 360);
        viewModel.CompleteScientificGesture();

        ScientificMeasurementViewModel second = source.Measurements.Single(item => item.Id != first.Id);
        Assert.Equal(first.VisualStyle, second.VisualStyle);
    }

    [Fact]
    public void FigureAnnotationStyle_CarriesAcrossAnnotationKinds()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Figure.AddArrowAnnotationCommand.Execute(null);
        FigureAnnotationViewModel arrow = Assert.IsType<FigureAnnotationViewModel>(
            viewModel.Figure.SelectedAnnotation);
        arrow.Color = "#FF0EA5E9";
        arrow.StrokeWidthPt = 2.75;
        arrow.FontSizePt = 12;
        arrow.IsBold = true;

        viewModel.Figure.AddRectangleAnnotationCommand.Execute(null);

        FigureAnnotationViewModel rectangle = Assert.IsType<FigureAnnotationViewModel>(
            viewModel.Figure.SelectedAnnotation);
        Assert.Equal(arrow.Color, rectangle.Color);
        Assert.Equal(arrow.StrokeWidthPt, rectangle.StrokeWidthPt);
        Assert.Equal(arrow.FontSizePt, rectangle.FontSizePt);
        Assert.Equal(arrow.IsBold, rectangle.IsBold);
    }

    [Fact]
    public void TemplateSwitchAndCustomCanvasSize_MigrateExistingFigureContent()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        SourceAssetItemViewModel source = CreateMeasurementSourceItem();
        viewModel.Sources.Add(source);
        viewModel.SelectedSource = source;
        FigurePanelViewModel panel = Assert.IsType<FigurePanelViewModel>(
            viewModel.Figure.AddPanel(source, new PixelRect64(0, 0, 600, 400)));
        viewModel.Figure.AddArrowAnnotationCommand.Execute(null);
        FigureAnnotationViewModel annotation = Assert.IsType<FigureAnnotationViewModel>(
            viewModel.Figure.SelectedAnnotation);
        viewModel.Figure.AddVerticalGuideCommand.Execute(null);
        Guid panelId = panel.Id;
        Guid annotationId = annotation.Id;

        FigureTemplateDefinition alternate = viewModel.AvailableTemplates.First(
            template => !string.Equals(template.Id, viewModel.Figure.Template.Id, StringComparison.Ordinal));
        TemplateCanvasLayout alternateLayout = TemplateLayoutEngine.CreateLayout(alternate);
        viewModel.SelectedFigureTemplate = alternate;

        Assert.Equal(alternate.Id, viewModel.Figure.Template.Id);
        FigurePanelViewModel switchedPanel = Assert.Single(
            viewModel.Figure.Panels, item => item.Id == panelId);
        Assert.Equal(alternateLayout.Slots[0].PixelRect, switchedPanel.DestinationRect);
        Assert.Contains(viewModel.Figure.Annotations, item => item.Id == annotationId);
        Assert.Single(viewModel.Figure.Guides);
        int templateWidth = viewModel.Figure.CanvasWidth;
        int templateHeight = viewModel.Figure.CanvasHeight;

        viewModel.CustomCanvasWidth = 1432;
        viewModel.CustomCanvasHeight = 987;
        viewModel.ApplyCustomCanvasSizeCommand.Execute(null);

        Assert.Equal(1432, viewModel.Figure.CanvasWidth);
        Assert.Equal(987, viewModel.Figure.CanvasHeight);
        FigurePanelViewModel migratedPanel = Assert.Single(viewModel.Figure.Panels);
        Assert.InRange(migratedPanel.X, 0, viewModel.Figure.CanvasWidth - 1);
        Assert.InRange(migratedPanel.Y, 0, viewModel.Figure.CanvasHeight - 1);
        Assert.True(migratedPanel.X + migratedPanel.Width <= viewModel.Figure.CanvasWidth);
        Assert.True(migratedPanel.Y + migratedPanel.Height <= viewModel.Figure.CanvasHeight);
        Assert.Single(viewModel.Figure.Annotations);
        Assert.Single(viewModel.Figure.Guides);

        viewModel.UndoCommand.Execute(null);
        Assert.Equal(templateWidth, viewModel.Figure.CanvasWidth);
        Assert.Equal(templateHeight, viewModel.Figure.CanvasHeight);
    }

    [Fact]
    public void NewProject_WhenCurrentProjectIsDirty_CanDiscardAndReplaceIt()
    {
        var prompt = new AlwaysDiscardUnsavedChangesPrompt();
        MainWindowViewModel viewModel = CreateViewModel(unsavedChangesPrompt: prompt);
        viewModel.Sources.Add(CreateSourceItem());
        viewModel.Figure.BackgroundColor = "#FF101820";
        Assert.True(viewModel.IsDirty);

        viewModel.NewProjectCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(
            () => viewModel.Sources.Count == 0 && !viewModel.IsBusy,
            TimeSpan.FromSeconds(2)));
        Assert.Null(viewModel.ProjectPath);
        Assert.False(viewModel.IsDirty);
        Assert.Equal(1, prompt.CallCount);
    }

    [Fact]
    public void AssistedRegions_RequireHumanDecisionBeforeMeasurementsAreCreated()
    {
        MainWindowViewModel viewModel = CreateViewModel(new FixedAssistedRegionAnalyzer());
        SourceAssetItemViewModel source = CreateMeasurementSourceItem();
        viewModel.Sources.Add(source);
        viewModel.SelectedSource = source;

        viewModel.AnalyzeAssistedRegionsCommand.Execute(null);
        Assert.True(SpinWait.SpinUntil(() => viewModel.AssistedRegions.Count == 2, TimeSpan.FromSeconds(2)));
        Assert.Empty(source.Measurements);
        viewModel.AssistedRegions[1].IsAccepted = false;

        viewModel.CommitAcceptedAssistedRegionsCommand.Execute(null);

        ScientificMeasurementViewModel measurement = Assert.Single(source.Measurements);
        Assert.Equal(ScientificMeasurementKind.CircleRoi, measurement.Kind);
        Assert.True(viewModel.AssistedRegions[0].IsCommitted);
        Assert.False(viewModel.AssistedRegions[1].IsCommitted);
        Assert.Contains("写入 1", viewModel.AssistedRegionDecisionText, StringComparison.Ordinal);
    }

    [Fact]
    public void ParticleBatch_AppliesCurrentRecipeAndPersistsResultsPerSourceRevision()
    {
        MainWindowViewModel viewModel = CreateViewModel(new FixedAssistedRegionAnalyzer());
        SourceAssetItemViewModel first = CreateMeasurementSourceItem();
        SourceAssetItemViewModel second = CreateMeasurementSourceItem();
        viewModel.Sources.Add(first);
        viewModel.Sources.Add(second);
        viewModel.AnalysisChannel = ImageAnalysisChannel.Red;

        viewModel.SelectedSource = first;
        viewModel.AddCurrentCropToBatchQueueCommand.Execute(null);
        viewModel.SelectedSource = second;
        viewModel.AddCurrentCropToBatchQueueCommand.Execute(null);

        viewModel.AnalyzeParticleBatchCommand.Execute(null);

        Assert.True(SpinWait.SpinUntil(() => !viewModel.IsBusy, TimeSpan.FromSeconds(2)));
        Assert.Equal(2, viewModel.BatchCropQueue.Count);
        Assert.All(viewModel.BatchCropQueue, item =>
            Assert.Contains("分析完成", item.StatusText, StringComparison.Ordinal));
        Assert.All([first, second], source =>
        {
            AssistedRegionAnalysisResult result = Assert.IsType<AssistedRegionAnalysisResult>(
                Assert.Single(source.AnalysisResults));
            Assert.Equal(source.Asset.Id, result.SourceAssetId);
            Assert.Equal(source.SourceRevision, result.SourceRevision);
            Assert.Equal(ImageAnalysisChannel.Red, result.Channel);
            Assert.Equal(2, result.Candidates.Count);
        });
        Assert.Contains("4 个候选", viewModel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplainableLayout_SelectsCapacityTemplateAndPlacesSources()
    {
        MainWindowViewModel viewModel = CreateViewModel();
        viewModel.Sources.Add(CreateMeasurementSourceItem());
        viewModel.Sources.Add(CreateMeasurementSourceItem());

        viewModel.ApplySmartLayoutCommand.Execute(null);

        Assert.Equal(2, viewModel.Figure.Panels.Count);
        Assert.True(viewModel.Figure.SlotCount >= 2);
        Assert.All(viewModel.Figure.Panels, panel => Assert.False(panel.IsInset));
        Assert.Contains("源图数量 2", viewModel.SmartAssistStatusText, StringComparison.Ordinal);
    }

    private static MainWindowViewModel CreateViewModel(
        IAssistedRegionAnalyzer? assistedRegionAnalyzer = null,
        IUnsavedChangesPrompt? unsavedChangesPrompt = null) => new(
        new EmptyImageFilePicker(),
        new NoOpSourceAssetReader(),
        new NoOpPreviewLoader(),
        new EmptyExportFilePicker(),
        new AllowPathSafetyPolicy(),
        new NoOpCropExporter(),
        new NoOpFigureExporter(),
        new BuiltInTemplateCatalog().LoadAll(),
        new EmptyProjectFilePicker(),
        new NoOpProjectStore(),
        assistedRegionAnalyzer: assistedRegionAnalyzer,
        unsavedChangesPrompt: unsavedChangesPrompt);

    private static void AssertSidebarPageVisibility(
        ScrollViewer inspector,
        ScrollViewer layers,
        ChannelsInspector channels,
        Visibility expectedInspector,
        Visibility expectedLayers,
        Visibility expectedChannels)
    {
        Assert.Equal(expectedInspector, inspector.Visibility);
        Assert.Equal(expectedLayers, layers.Visibility);
        Assert.Equal(expectedChannels, channels.Visibility);
    }

    private static void ExecuteBoundButtonCommand(Button button)
    {
        Assert.NotNull(button.Command);
        Assert.True(button.Command.CanExecute(button.CommandParameter));
        button.Command.Execute(button.CommandParameter);
    }

    private static void CaptureSidebarIfRequested(
        Window window,
        FrameworkElement sidebar,
        string? captureDirectory,
        string stateName)
    {
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            return;
        }

        string fullDirectory = Path.GetFullPath(captureDirectory);
        Directory.CreateDirectory(fullDirectory);
        const double dpi = 192;
        const double scale = dpi / 96;
        var fullScreenshot = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(window.ActualWidth * scale)),
            Math.Max(1, (int)Math.Ceiling(window.ActualHeight * scale)),
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        fullScreenshot.Render(window);
        Point sidebarOrigin = sidebar.TranslatePoint(new Point(0, 0), window);
        int cropX = Math.Clamp((int)Math.Floor(sidebarOrigin.X * scale), 0, fullScreenshot.PixelWidth - 1);
        int cropY = Math.Clamp((int)Math.Floor(sidebarOrigin.Y * scale), 0, fullScreenshot.PixelHeight - 1);
        int cropWidth = Math.Clamp((int)Math.Ceiling(sidebar.ActualWidth * scale), 1, fullScreenshot.PixelWidth - cropX);
        int cropHeight = Math.Clamp((int)Math.Ceiling(sidebar.ActualHeight * scale), 1, fullScreenshot.PixelHeight - cropY);
        var sidebarScreenshot = new CroppedBitmap(
            fullScreenshot,
            new Int32Rect(cropX, cropY, cropWidth, cropHeight));
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sidebarScreenshot));
        using FileStream output = new(
            Path.Combine(fullDirectory, $"SciCanvas-right-sidebar-{stateName}.png"),
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        encoder.Save(output);
    }

    private static PixelRect64 AssertCrop(CropEditorViewModel crop)
    {
        Assert.True(crop.TryGetCrop(out PixelRect64 bounds));
        return bounds;
    }

    private static SourceAssetItemViewModel CreateSourceItem()
    {
        byte[] pixels =
        [
            0, 0, 0, 255,
            255, 255, 255, 255,
            255, 0, 0, 255,
            0, 255, 0, 255,
        ];
        BitmapSource preview = BitmapSource.Create(
            2,
            2,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: 8);
        preview.Freeze();

        var source = new SourceAsset(
            Guid.NewGuid(),
            "regression.png",
            @"C:\images\regression.png",
            new SourceFingerprint(16, DateTimeOffset.UtcNow, new string('A', 64), "TEST:1"),
            new CoreImageMetadata(new PixelSize64(2, 2), 4, 8, "Bgra32"),
            SourceLinkState.Verified);

        return new SourceAssetItemViewModel(source, preview);
    }

    private static SourceAssetItemViewModel CreateMeasurementSourceItem()
    {
        const int width = 1200;
        const int height = 800;
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                double ridge = Math.Sin(x * 0.045) * 32 + Math.Cos(y * 0.052) * 28;
                double particle = Math.Sin((x + y) * 0.018) * 22 + Math.Cos((x - y) * 0.027) * 18;
                byte value = (byte)Math.Clamp(112 + ridge + particle + ((x * 17 + y * 31) % 29), 22, 232);
                pixels[index] = value;
                pixels[index + 1] = value;
                pixels[index + 2] = value;
                pixels[index + 3] = 255;
            }
        }

        BitmapSource preview = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            palette: null,
            pixels,
            stride: width * 4);
        preview.Freeze();
        var source = new SourceAsset(
            Guid.NewGuid(),
            "SEM_01.tif",
            @"C:\research\SEM_01.tif",
            new SourceFingerprint(pixels.Length, DateTimeOffset.UtcNow, new string('C', 64), "TEST:SEM"),
            new CoreImageMetadata(new PixelSize64(width, height), 1, 16, "Gray16"),
            SourceLinkState.Verified);
        return new SourceAssetItemViewModel(source, preview);
    }

    private sealed class EmptyImageFilePicker : IImageFilePicker
    {
        public IReadOnlyList<string> PickImageFiles() => [];
    }

    private sealed class FixedAssistedRegionAnalyzer : IAssistedRegionAnalyzer
    {
        public Task<AssistedRegionAnalysisResult> AnalyzeAsync(
            SourceAsset source,
            AssistedRegionAnalysisOptions options,
            int frameIndex = 0,
            CancellationToken cancellationToken = default,
            long sourceRevision = 1,
            ImageAnalysisChannel channel = ImageAnalysisChannel.Luminance) => Task.FromResult(
            new AssistedRegionAnalysisResult(
                options,
                [
                    new AssistedRegionCandidate(1, new PixelRect64(100, 100, 40, 40), 120, 120, 800, 120, 0.8, 1)
                    {
                        RawMeanIntensity = 52428,
                    },
                    new AssistedRegionCandidate(2, new PixelRect64(300, 200, 30, 30), 315, 215, 500, 100, 0.7, 1)
                    {
                        RawMeanIntensity = 45874.5,
                    },
                ],
                0.5,
                1300,
                options.RegionOfInterest.Width * options.RegionOfInterest.Height)
            {
                SourceAssetId = source.Id,
                SourceRevision = sourceRevision,
                FrameIndex = frameIndex,
                Channel = channel,
                AnalyzerId = "test.assisted-regions.v2",
                AnalyzedAt = DateTimeOffset.UtcNow,
                SourceBitDepth = source.Metadata.BitsPerChannel > 8 ? 16 : 8,
            });
    }

    private sealed class EmptyExportFilePicker : IExportFilePicker
    {
        public string? PickNewExportPath(string suggestedFileName) => null;
    }

    private sealed class NoOpSourceAssetReader : ISourceAssetReader
    {
        public Task<SourceAsset> ImportAsync(string path, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SourceVerification> VerifyAsync(
            SourceAsset asset,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new SourceVerification(SourceLinkState.Verified, asset.Fingerprint, null));
    }

    private sealed class NoOpPreviewLoader : IImagePreviewLoader
    {
        public Task<BitmapSource> LoadAsync(
            string path,
            int maximumPixelWidth,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AllowPathSafetyPolicy : IPathSafetyPolicy
    {
        public Task<ExportPathDecision> ValidateExportTargetAsync(
            string targetPath,
            IReadOnlyCollection<SourceAsset> sources,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ExportPathDecision.Allow(targetPath));
    }

    private sealed class NoOpCropExporter : IImageCropExporter
    {
        public Task ExportAsync(
            string sourcePath,
            string targetPath,
            PixelRect64 crop,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class NoOpFigureExporter : IFigureExporter
    {
        public Task ExportAsync(
            FigureExportDocument document,
            string targetPath,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class AlwaysDiscardUnsavedChangesPrompt : IUnsavedChangesPrompt
    {
        public int CallCount { get; private set; }

        public UnsavedChangesDecision ConfirmProjectReplacement(
            string actionLabel,
            string currentProjectDisplayName)
        {
            CallCount++;
            return UnsavedChangesDecision.Discard;
        }
    }
    private sealed class EmptyProjectFilePicker : IProjectFilePicker
    {
        public string? PickProjectToOpen() => null;

        public string? PickProjectToSave(string suggestedFileName, string? currentPath) => null;
    }

    private sealed class NoOpProjectStore : IProjectStore
    {
        public Task<SciCanvasProjectDocument> LoadAsync(
            string path,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task SaveAsync(
            string path,
            SciCanvasProjectDocument document,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
