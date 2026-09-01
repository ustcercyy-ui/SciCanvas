using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Threading;

namespace SciCanvas.Platform.Windows.Tests;

internal static class WpfTestHost
{
    private static readonly SemaphoreSlim InvocationGate = new(1, 1);

    private static readonly Lazy<Dispatcher> SharedDispatcher = new(
        StartDispatcher,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public static WpfTestInvocationTiming Invoke(Action action, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        TimeSpan operationTimeout = timeout ?? TimeSpan.FromSeconds(15);
        long invocationStarted = Stopwatch.GetTimestamp();
        InvocationGate.Wait();
        long gateAcquired = Stopwatch.GetTimestamp();

        try
        {
            return InvokeSerialized(
                action,
                operationTimeout,
                Stopwatch.GetElapsedTime(invocationStarted, gateAcquired),
                invocationStarted);
        }
        finally
        {
            InvocationGate.Release();
        }
    }

    private static WpfTestInvocationTiming InvokeSerialized(
        Action action,
        TimeSpan timeout,
        TimeSpan serializationWait,
        long invocationStarted)
    {
        Exception? failure = null;
        long dispatcherQueued = Stopwatch.GetTimestamp();
        long actionStarted = 0;
        long actionFinished = 0;
        DispatcherOperation operation = SharedDispatcher.Value.InvokeAsync(
            (Action)(() =>
            {
                Volatile.Write(ref actionStarted, Stopwatch.GetTimestamp());
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    Volatile.Write(ref actionFinished, Stopwatch.GetTimestamp());
                }
            }),
            DispatcherPriority.Send);
        if (!operation.Task.Wait(timeout))
        {
            _ = operation.Abort();
            long timeoutObserved = Stopwatch.GetTimestamp();
            long started = Volatile.Read(ref actionStarted);
            string phase = started == 0
                ? "Dispatcher queue"
                : "WPF action";
            TimeSpan dispatcherQueue = started == 0
                ? Stopwatch.GetElapsedTime(dispatcherQueued, timeoutObserved)
                : Stopwatch.GetElapsedTime(dispatcherQueued, started);
            TimeSpan execution = started == 0
                ? TimeSpan.Zero
                : Stopwatch.GetElapsedTime(started, timeoutObserved);
            throw new TimeoutException(
                $"WPF 测试操作未在 {timeout.TotalMilliseconds:0} ms 内完成；" +
                $"phase={phase}，serialization={serializationWait.TotalMilliseconds:0.0} ms，" +
                $"dispatcherQueue={dispatcherQueue.TotalMilliseconds:0.0} ms，" +
                $"execution={execution.TotalMilliseconds:0.0} ms。");
        }

        operation.Task.GetAwaiter().GetResult();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }

        long completed = Stopwatch.GetTimestamp();
        long startedAt = Volatile.Read(ref actionStarted);
        long finishedAt = Volatile.Read(ref actionFinished);
        return new WpfTestInvocationTiming(
            serializationWait,
            Stopwatch.GetElapsedTime(dispatcherQueued, startedAt),
            Stopwatch.GetElapsedTime(startedAt, finishedAt),
            Stopwatch.GetElapsedTime(invocationStarted, completed));
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

internal readonly record struct WpfTestInvocationTiming(
    TimeSpan SerializationWait,
    TimeSpan DispatcherQueueWait,
    TimeSpan Execution,
    TimeSpan Total);
