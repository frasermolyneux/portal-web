using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class DependencyReadCoordinatorTests
{
    [Fact]
    public async Task ProfileReads_AreLimitedAcrossDifferentKeys()
    {
        using var coordinator = new DependencyReadCoordinator();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        var first = coordinator.RunAsync(DependencyWorkload.PlayerProfile, "first", async () =>
        {
            Interlocked.Increment(ref count);
            entered.SetResult();
            await release.Task;
            return 1;
        });
        await entered.Task;
        var second = coordinator.RunAsync(DependencyWorkload.PlayerProfile, "second", () =>
        {
            Interlocked.Increment(ref count);
            return Task.FromResult(2);
        });

        Assert.Equal(1, count);
        release.SetResult();
        var results = await Task.WhenAll(first, second);
        Assert.Equal([1, 2], results);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CancelledWaiter_DoesNotExecuteOrPreventLaterReads()
    {
        using var coordinator = new DependencyReadCoordinator();
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = coordinator.RunAsync(DependencyWorkload.PlayerProfile, "player", async () =>
        {
            await release.Task;
            return 1;
        });
        var called = false;
        var waiter = coordinator.RunAsync(DependencyWorkload.PlayerProfile, "player", () =>
        {
            called = true;
            return Task.FromResult(2);
        }, cancellation.Token);

        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        Assert.False(called);
        release.SetResult();
        await first;
        Assert.Equal(3, await coordinator.RunAsync(DependencyWorkload.PlayerProfile, "player", () => Task.FromResult(3)));
    }

    [Fact]
    public async Task FailedRead_ReleasesBothLimits()
    {
        using var coordinator = new DependencyReadCoordinator();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.RunAsync<int>(DependencyWorkload.PlayerProfile, "player",
                () => throw new InvalidOperationException("Dependency unavailable")));

        Assert.Equal(1, await coordinator.RunAsync(DependencyWorkload.PlayerProfile, "player", () => Task.FromResult(1)));
    }
}
