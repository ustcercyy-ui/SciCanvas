using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace SciCanvas.Platform.Windows.Tests;

internal static class WpfTestHost
{
    private static readonly Lazy<Dispatcher> SharedDispatcher = new(
        StartDispatcher,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Invoke(Action action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        Exception? failure = null;
        DispatcherOperation operation = SharedDispatcher.Value.InvokeAsync(
            (Action)(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }),
            DispatcherPriority.Send);
        if (!operation.Task.Wait(timeout ?? TimeSpan.FromSeconds(15)))
        {
            throw new TimeoutException("WPF 测试操作未在限定时间内完成。");
        }

        operation.Task.GetAwaiter().GetResult();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Dispatcher StartDispatcher()
    {
        Dispatcher? dispatcher = null;
        Exception? startupFailure = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                _ = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown,
                };
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception exception)
            {
                startupFailure = exception;
            }
            finally
            {
                ready.Set();
            }

            if (startupFailure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "SciCanvas WPF test host",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(10)))
        {
            throw new TimeoutException("WPF 测试宿主未在 10 秒内启动。");
        }

        if (startupFailure is not null)
        {
            ExceptionDispatchInfo.Capture(startupFailure).Throw();
        }

        return dispatcher ?? throw new InvalidOperationException("WPF 测试宿主未创建 Dispatcher。");
    }
}
