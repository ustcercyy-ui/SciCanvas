namespace SciCanvas.Platform.Windows.Tests;

public sealed class WpfTestHostTests
{
    [Fact]
    public async Task ConcurrentInvocations_DoNotSpendOperationTimeoutWaitingBehindAnotherTest()
    {
        using var firstStarted = new ManualResetEventSlim();
        using var releaseFirst = new ManualResetEventSlim();
        Task first = Task.Run(() => WpfTestHost.Invoke(() =>
        {
            firstStarted.Set();
            Assert.True(
                releaseFirst.Wait(TimeSpan.FromSeconds(2)),
                "Timed out waiting to release the first WPF operation.");
        }));
        Assert.True(
            firstStarted.Wait(TimeSpan.FromSeconds(2)),
            "The first WPF operation did not start.");

        Task<WpfTestInvocationTiming> second = Task.Run(() =>
            WpfTestHost.Invoke(() => { }, TimeSpan.FromMilliseconds(100)));
        await Task.Delay(250);
        releaseFirst.Set();

        await first;
        WpfTestInvocationTiming secondTiming = await second;
        Assert.True(secondTiming.SerializationWait >= TimeSpan.FromMilliseconds(100));
        Assert.True(secondTiming.DispatcherQueueWait < TimeSpan.FromMilliseconds(100));
        Assert.True(secondTiming.Execution < TimeSpan.FromMilliseconds(100));
    }
}
