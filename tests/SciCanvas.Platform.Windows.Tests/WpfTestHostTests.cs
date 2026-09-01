namespace SciCanvas.Platform.Windows.Tests;

[Collection(WpfTestCollection.Name)]
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
        try
        {
            Assert.True(
                firstStarted.Wait(TimeSpan.FromSeconds(2)),
                "The first WPF operation did not start.");

            using var secondInvoking = new ManualResetEventSlim();
            Task<WpfTestInvocationTiming> second = Task.Run(() =>
            {
                secondInvoking.Set();
                return WpfTestHost.Invoke(() => { }, TimeSpan.FromMilliseconds(100));
            });
            Assert.True(
                secondInvoking.Wait(TimeSpan.FromSeconds(2)),
                "The second WPF invocation did not start.");
            await Task.Delay(250);
            releaseFirst.Set();

            WpfTestInvocationTiming secondTiming = await second;
            Assert.True(secondTiming.SerializationWait >= TimeSpan.FromMilliseconds(100));
            Assert.True(secondTiming.DispatcherQueueWait < TimeSpan.FromMilliseconds(100));
            Assert.True(secondTiming.Execution < TimeSpan.FromMilliseconds(100));
        }
        finally
        {
            releaseFirst.Set();
            await first;
        }
    }
}
