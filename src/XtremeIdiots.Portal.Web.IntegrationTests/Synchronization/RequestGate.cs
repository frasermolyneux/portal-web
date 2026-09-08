namespace XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;

internal sealed class RequestGate
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal RequestGate(bool hold = false)
    {
        if (!hold)
            released.SetResult();
    }

    internal Task Entered => entered.Task;

    internal async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        entered.TrySetResult();
        await released.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);
    }

    internal void Release()
    {
        released.TrySetResult();
    }
}
