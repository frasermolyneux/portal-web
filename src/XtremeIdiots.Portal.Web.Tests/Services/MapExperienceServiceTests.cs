using Microsoft.Extensions.Caching.Memory;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class MapExperienceServiceTests : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly DependencyReadCoordinator coordinator = new();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };

    [Fact]
    public async Task Insights_IncludesCurrentVotes_AndCoalescesConcurrentReads()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = DateTime.UtcNow;
        DateTime observedFrom = default, observedTo = default;
        var calls = 0;
        repository.Setup(r => r.MapAnalytics.V1.GetInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            null, null, It.IsAny<CancellationToken>()))
            .Returns(async (DateTime from, DateTime to, GameType? _, Guid? _, CancellationToken ct) =>
            {
                calls++;
                observedFrom = from;
                observedTo = to;
                await gate.Task.WaitAsync(ct);
                return new ApiResult<MapInsightsDto>(HttpStatusCode.OK, new ApiResponse<MapInsightsDto>(new MapInsightsDto { TotalLikes = 7 }));
            });
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var first = service.Insights(30, null, null, CancellationToken.None);
        var second = service.Insights(30, null, null, CancellationToken.None);
        gate.SetResult();
        await Task.WhenAll(first, second);
        var cached = await service.Insights(30, null, null, CancellationToken.None);
        Assert.Equal(1, calls);
        Assert.InRange(observedTo, before, DateTime.UtcNow);
        Assert.Equal(TimeSpan.FromDays(30), observedTo - observedFrom);
        Assert.Equal(7, cached.TotalVotes);
    }

    [Fact]
    public async Task Insights_DoesNotCacheDependencyFailureAsEmptySuccess()
    {
        repository.SetupSequence(r => r.MapAnalytics.V1.GetInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapInsightsDto>(HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(new ApiResult<MapInsightsDto>(HttpStatusCode.OK, new ApiResponse<MapInsightsDto>(new MapInsightsDto { TotalLikes = 3 })));
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.Insights(7, null, null, CancellationToken.None));
        Assert.Equal(3, (await service.Insights(7, null, null, CancellationToken.None)).TotalVotes);
    }

    public void Dispose()
    {
        cache.Dispose();
        coordinator.Dispose();
    }
}
