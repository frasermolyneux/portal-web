using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class CachedAgentTelemetryServiceTests
{
    [Fact]
    public async Task ConcurrentScopedReadersShareOneQueryAndFreshResult()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var telemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<IReadOnlyList<AgentServerSummary>>(TaskCreationOptions.RunContinuationsAsynchronously);
        telemetry.Setup(t => t.GetAllServersStatusAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            entered.TrySetResult();
            return release.Task;
        });
        CachedAgentTelemetryService CreateService()
        {
            return new(telemetry.Object, cache, coordinator, NullLogger<CachedAgentTelemetryService>.Instance);
        }

        var first = CreateService().GetAllAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = CreateService().GetAllAsync();
        release.SetResult([new AgentServerSummary { ServerId = Guid.NewGuid(), ActivityStatus = AgentActivityStatus.Active }]);
        var results = await Task.WhenAll(first, second);
        Assert.All(results, result => Assert.Equal(OptionalDataState.Available, result.State));
        Assert.Same(results[0].Data, results[1].Data);
        Assert.Same(results[0].Data, (await CreateService().GetAllAsync()).Data);
        telemetry.Verify(t => t.GetAllServersStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailureIsBrieflyCachedAsUnavailableRatherThanOffline()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var telemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);
        telemetry.Setup(t => t.GetAllServersStatusAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("unavailable"));
        var service = new CachedAgentTelemetryService(telemetry.Object, cache, coordinator, NullLogger<CachedAgentTelemetryService>.Instance);

        foreach (var result in new[] { await service.GetAllAsync(), await service.GetAllAsync() })
        {
            Assert.Equal(OptionalDataState.Unavailable, result.State);
            Assert.Null(result.Data);
            Assert.Null(result.ObservedAt);
        }

        telemetry.Verify(t => t.GetAllServersStatusAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FailedRefreshReturnsExplicitlyStaleLastGoodData()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var id = Guid.NewGuid();
        var expected = new AgentServerStatus { EventsLastHour = 17, ActivityStatus = AgentActivityStatus.Active };
        var telemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);
        telemetry.SetupSequence(t => t.GetServerStatusAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected)
            .ThrowsAsync(new InvalidOperationException("unavailable"));
        var service = new CachedAgentTelemetryService(telemetry.Object, cache, coordinator, NullLogger<CachedAgentTelemetryService>.Instance);
        var fresh = await service.GetServerAsync(id);
        cache.Remove($"server-overview:telemetry:{id}");

        var stale = await service.GetServerAsync(id);
        Assert.Equal(OptionalDataState.Stale, stale.State);
        Assert.Same(expected, stale.Data);
        Assert.Equal(fresh.ObservedAt, stale.ObservedAt);
        Assert.Equal(OptionalDataState.Stale, (await service.GetServerAsync(id)).State);
        telemetry.Verify(t => t.GetServerStatusAsync(id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task CallerCancellationDoesNotBecomeCachedUnavailability()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        using var cancellation = new CancellationTokenSource();
        var id = Guid.NewGuid();
        var telemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        telemetry.Setup(t => t.GetServerStatusAsync(id, It.IsAny<CancellationToken>())).Returns(async (Guid _, CancellationToken token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new AgentServerStatus();
        });
        var service = new CachedAgentTelemetryService(telemetry.Object, cache, coordinator, NullLogger<CachedAgentTelemetryService>.Instance);
        var pending = service.GetServerAsync(id, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        telemetry.Setup(t => t.GetServerStatusAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new AgentServerStatus());
        Assert.Equal(OptionalDataState.Available, (await service.GetServerAsync(id)).State);
    }
}
