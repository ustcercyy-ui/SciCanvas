using System.Windows;
using System.Windows.Threading;
using SciCanvas.Imaging;
using SciCanvas.Persistence;
using SciCanvas.Platform.Windows;
using SciCanvas.Presentation;
using SciCanvas.Templates;

namespace SciCanvas.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        var metadataProbe = new WpfImageMetadataProbe();
        var sourceReader = new ReadOnlySourceAssetReader(
            metadataProbe,
            new WindowsFileIdentityProvider());

        var viewModel = new MainWindowViewModel(
            new WindowsImageFilePicker(),
            sourceReader,
            new WpfImagePreviewLoader(),
            new WindowsExportFilePicker(),
            new WindowsPathSafetyPolicy(),
            new WpfImageCropExporter(),
            new WpfFigureExporter(),
            new BuiltInTemplateCatalog().LoadAll(),
            new WindowsProjectFilePicker(),
            new JsonProjectStore(),
            new JsonProjectRecoveryStore(),
            new WindowsProjectRecoveryPrompt(),
            new WindowsSourceRelinkFilePicker(),
            new WindowsSourceRevisionAcceptancePrompt());

        var window = new MainWindow
        {
            DataContext = viewModel,
        };

        MainWindow = window;
        window.Show();
        await viewModel.TryRestoreLatestAutosaveAsync();
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLogWriter.Write("Dispatcher", e.Exception);
        e.Handled = true;

        MessageBox.Show(
            $"SciCanvas 遇到错误，但已阻止程序闪退。\n\n{e.Exception.Message}\n\n日志：{CrashLogWriter.LogPath}",
            "SciCanvas 错误",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            CrashLogWriter.Write("AppDomain", exception);
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLogWriter.Write("TaskScheduler", e.Exception);
        e.SetObserved();
    }
}
