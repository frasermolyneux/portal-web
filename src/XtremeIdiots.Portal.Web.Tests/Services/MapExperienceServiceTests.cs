using Microsoft.Extensions.Caching.Memory;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class MapExperienceServiceTests : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly DependencyReadCoordinator coordinator = new();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };

    [Fact]
    public async Task Rotations_UsePlainPublicNames_WithoutChangingCachedServerIdentity()
    {
        var id = Guid.NewGuid();
        repository.Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto
                {
                    Servers = [new() { GameServerId = id, Title = "Backend name", GameType = GameType.CallOfDuty4x,
                        Maps = [new() { MapName = "mp_custom" }] }]
                })));
        var live = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerLiveStatusDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { ServerId = id, Title = "^2Public ^1name" }))!;
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerLiveStatusDto>>(new CollectionModel<GameServerLiveStatusDto>([live]))));
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var server = Assert.Single((await service.BrowsableRotations(CancellationToken.None)).Servers);
        Assert.Equal("Public name", server.Title);
        Assert.Equal(id, server.GameServerId);
        Assert.Equal(GameType.CallOfDuty4x, server.GameType);
        Assert.Same(server, Assert.Single((await service.Rotations(CancellationToken.None)).Servers));
        repository.Verify(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rotations_StillLoadWhenOptionalLiveNamesAreUnavailable(bool throws)
    {
        repository.Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto
                {
                    Servers = [new() { GameServerId = Guid.NewGuid(), Title = "^2Fallback name" }]
                })));
        var setup = repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()));
        if (throws)
            setup.ThrowsAsync(new HttpRequestException("Live names unavailable"));
        else
            setup.ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.ServiceUnavailable));
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        Assert.Equal("Fallback name", Assert.Single((await service.Rotations(CancellationToken.None)).Servers).Title);
    }

    [Fact]
    public async Task Insights_IncludesCurrentVotes_AndCoalescesConcurrentReads()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var before = DateTime.UtcNow;
        DateTime observedFrom = default, observedTo = default;
        var calls = 0;
        repository.Setup(r => r.MapAnalytics.V1.GetVoteInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
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
        repository.SetupSequence(r => r.MapAnalytics.V1.GetVoteInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
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
