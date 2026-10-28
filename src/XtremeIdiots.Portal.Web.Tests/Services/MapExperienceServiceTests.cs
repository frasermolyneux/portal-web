using Microsoft.Extensions.Caching.Memory;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class MapExperienceServiceTests : IDisposable
{
    private readonly MemoryCache cache = new(new MemoryCacheOptions());
    private readonly DependencyReadCoordinator coordinator = new();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };

    [Theory]
    [InlineData("^2Public ^1name", "Public name")]
    [InlineData("^2", "Backend name")]
    [InlineData("^7   ", "Backend name")]
    public async Task Rotations_UsePlainPublicNames_WithoutChangingCachedServerIdentity(string liveTitle, string expectedTitle)
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
            Newtonsoft.Json.JsonConvert.SerializeObject(new { ServerId = id, Title = liveTitle }))!;
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerLiveStatusDto>>(new CollectionModel<GameServerLiveStatusDto>([live]))));
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var server = Assert.Single((await service.BrowsableRotations(CancellationToken.None)).Servers);
        Assert.Equal(expectedTitle, server.Title);
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
    public async Task CatalogueGames_ObserveFirstMapAdded_AndLastMapRemoved()
    {
        static ApiResult<MapGamesDto> Games(params GameType[] games)
        {
            return new(HttpStatusCode.OK,
            new ApiResponse<MapGamesDto>(new MapGamesDto { Games = [.. games] }));
        }

        repository.SetupSequence(r => r.Maps.V1.GetMapGames(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Games())
            .ReturnsAsync(Games(GameType.CallOfDuty4))
            .ReturnsAsync(Games());
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        Assert.Empty((await service.Games(CancellationToken.None)).Games);
        Assert.Equal(GameType.CallOfDuty4, Assert.Single((await service.Games(CancellationToken.None)).Games));
        Assert.Empty((await service.Games(CancellationToken.None)).Games);
    }

    [Fact]
    public async Task Rotations_FallBackWhenLiveNamesReachTheirOwnDeadline()
    {
        repository.Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto
                {
                    Servers = [new() { Title = "Fallback name" }]
                })));
        var names = new TaskCompletionSource<ApiResult<CollectionModel<GameServerLiveStatusDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => names.Task.WaitAsync(ct));
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        Assert.Equal("Fallback name", Assert.Single((await service.Rotations(CancellationToken.None)).Servers).Title);
    }

    [Fact]
    public async Task Rotations_PropagateActualRequestCancellation()
    {
        using var request = new CancellationTokenSource();
        repository.Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto())));
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                await request.CancelAsync();
                return await Task.FromCanceled<ApiResult<CollectionModel<GameServerLiveStatusDto>>>(ct);
            });
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.Rotations(request.Token));
    }

    [Fact]
    public async Task DownloadPreviews_CoalesceConcurrentReads_AndNeverCacheFailures()
    {
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var id = Guid.NewGuid();
        var server = new PublicServerRotationDto { GameServerId = id };
        var calls = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ApiResult<PublicServerRotationDto>> Read(CancellationToken ct)
        {
            if (++calls == 1)
                return new ApiResult<PublicServerRotationDto>(HttpStatusCode.ServiceUnavailable);
            await gate.Task.WaitAsync(ct);
            return new ApiResult<PublicServerRotationDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationDto>(new PublicServerRotationDto { GameServerId = id }));
        }

        Assert.False((await service.DownloadPreview(server, Read, CancellationToken.None)).IsSuccess);
        var first = service.DownloadPreview(server, Read, CancellationToken.None);
        var second = service.DownloadPreview(server, Read, CancellationToken.None);
        gate.SetResult();
        await Task.WhenAll(first, second);
        var cached = await service.DownloadPreview(server, Read, CancellationToken.None);
        Assert.True(cached.IsSuccess);
        Assert.Equal(id, cached.Result!.Data!.GameServerId);
        Assert.InRange(calls, 1, 2);
    }

    [Fact]
    public async Task CatalogueGames_ShareOnlyInFlightReads_AcrossRequests()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        repository.Setup(r => r.Maps.V1.GetMapGames(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                calls++;
                await gate.Task.WaitAsync(ct);
                return new ApiResult<MapGamesDto>(HttpStatusCode.OK, new ApiResponse<MapGamesDto>(
                    new MapGamesDto { Games = [GameType.CallOfDuty4] }));
            });
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var otherRequest = new MapExperienceService(repository.Object, cache, coordinator);
        using var cancelled = new CancellationTokenSource();
        var first = service.Games(cancelled.Token);
        var second = otherRequest.Games(CancellationToken.None);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(1, calls);
        gate.SetResult();
        Assert.Single((await second).Games);
        Assert.Single((await service.Games(CancellationToken.None)).Games);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task DownloadPreviews_ChangeWithTheRotationSnapshot()
    {
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var server = new PublicServerRotationDto { GameServerId = Guid.NewGuid(), GameType = GameType.CallOfDuty4 };
        var calls = 0;
        Task<ApiResult<PublicServerRotationDto>> Read(CancellationToken _)
        {
            calls++;
            return Task.FromResult(new ApiResult<PublicServerRotationDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationDto>(server)));
        }

        Assert.Empty((await service.DownloadPreview(server, Read, CancellationToken.None)).Result!.Data!.Maps);
        server = server with { Maps = [new() { MapName = "mp_added" }] };
        Assert.Single((await service.DownloadPreview(server, Read, CancellationToken.None)).Result!.Data!.Maps);
        Assert.Single((await service.DownloadPreview(server, Read, CancellationToken.None)).Result!.Data!.Maps);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task SlowDownloadPreviews_DoNotBlockRepositoryReads()
    {
        var service = new MapExperienceService(repository.Object, cache, coordinator);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        async Task<ApiResult<PublicServerRotationDto>> Read(CancellationToken ct)
        {
            Interlocked.Increment(ref calls);
            await gate.Task.WaitAsync(ct);
            return new ApiResult<PublicServerRotationDto>(HttpStatusCode.OK, new ApiResponse<PublicServerRotationDto>(new PublicServerRotationDto()));
        }

        var previews = Enumerable.Range(0, 4).Select(_ => service.DownloadPreview(
            new PublicServerRotationDto { GameServerId = Guid.NewGuid() }, Read, CancellationToken.None)).ToArray();
        repository.Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto())));
        try
        {
            await service.Rotations(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, calls);
        }
        finally
        {
            gate.SetResult();
            await Task.WhenAll(previews);
        }
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
