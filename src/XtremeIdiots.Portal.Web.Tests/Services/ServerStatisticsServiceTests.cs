using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class ServerStatisticsServiceTests
{
    [Fact]
    public async Task MapSpansEndAtMatchingObservationsNotNextMapOrNow()
    {
        var serverId = Guid.NewGuid();
        var start = DateTime.UtcNow.AddHours(-3);
        static GameServerStatDto Sample(string map, DateTime timestamp, int players)
            => JsonConvert.DeserializeObject<GameServerStatDto>(JsonConvert.SerializeObject(new { MapName = map, Timestamp = timestamp, PlayerCount = players }))!;
        GameServerStatDto[] samples =
        [
            Sample("a", start, 1),
            Sample("a", start.AddMinutes(5), 2),
            Sample("b", start.AddMinutes(10), 3),
            Sample("b", start.AddMinutes(15), 4),
            Sample("single", start.AddMinutes(20), 5),
            Sample("future", DateTime.UtcNow.AddHours(1), 99),
            Sample("outside", DateTime.UtcNow.AddDays(-3), 99)
        ];
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(r => r.GameServersStats.V1.GetGameServerStatusStats(serverId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerStatDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerStatDto>>(new CollectionModel<GameServerStatDto>(samples.Reverse().ToList()))));
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        var service = new ServerStatisticsService(repository.Object, cache, coordinator, NullLogger<ServerStatisticsService>.Instance);

        var result = await service.GetAsync(serverId);

        Assert.Equal(OptionalDataState.Available, result.State);
        Assert.NotNull(result.Data);
        Assert.Equal(5, result.Data.PlayerCounts.Count);
        Assert.Collection(result.Data.MapTimeline,
            span =>
            {
                Assert.Equal("a", span.MapName);
                Assert.Equal(start, span.Start);
                Assert.Equal(start.AddMinutes(5), span.End);
            },
            span =>
            {
                Assert.Equal("b", span.MapName);
                Assert.Equal(start.AddMinutes(10), span.Start);
                Assert.Equal(start.AddMinutes(15), span.End);
            });
    }

    [Fact]
    public async Task PlayerAndTimelineReadersShareOnePendingStatisticsRequest()
    {
        var serverId = Guid.NewGuid();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<ApiResult<CollectionModel<GameServerStatDto>>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(r => r.GameServersStats.V1.GetGameServerStatusStats(serverId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                entered.TrySetResult();
                return release.Task;
            });
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var coordinator = new DependencyReadCoordinator();
        ServerStatisticsService Create() => new(repository.Object, cache, coordinator, NullLogger<ServerStatisticsService>.Instance);
        var first = Create().GetAsync(serverId);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = Create().GetAsync(serverId);
        release.SetResult(new ApiResult<CollectionModel<GameServerStatDto>>(HttpStatusCode.OK,
            new ApiResponse<CollectionModel<GameServerStatDto>>(new CollectionModel<GameServerStatDto>([]))));

        var results = await Task.WhenAll(first, second);

        Assert.Same(results[0].Data, results[1].Data);
        Assert.Equal(OptionalDataState.Available, results[0].State);
        Assert.Empty(results[0].Data!.MapTimeline);
        repository.Verify(r => r.GameServersStats.V1.GetGameServerStatusStats(serverId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
