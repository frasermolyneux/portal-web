using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Playtime;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class CurrentSessionServiceTests
{
    [Fact]
    public void FreshnessAllowsOneBackoffIntervalButRejectsOlderData()
    {
        var now = DateTime.UtcNow;
        Assert.True(CurrentSessionService.IsFresh(now.AddMinutes(-6), now));
        Assert.False(CurrentSessionService.IsFresh(now.AddMinutes(-6).AddTicks(-1), now));
        Assert.False(CurrentSessionService.IsFresh(now.AddSeconds(31), now));
    }

    [Fact]
    public async Task ReturnsOnlyValidFreshUniqueSessions()
    {
        var serverId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var good = Guid.NewGuid();
        var stale = Guid.NewGuid();
        var future = Guid.NewGuid();
        var wrongServer = Guid.NewGuid();
        var wrongGame = Guid.NewGuid();
        var bot = Guid.NewGuid();
        var duplicate = Guid.NewGuid();
        var inconsistent = Guid.NewGuid();
        var unavailable = Guid.NewGuid();
        var session = new CurrentPlayerSessionDto
        {
            SessionId = Guid.NewGuid(),
            ServerId = serverId,
            GameType = GameType.CallOfDuty4,
            StartUtc = now.AddHours(-2),
            LastConfirmedUtc = now,
            ObservedSeconds = 7200
        };
        static CurrentPlayerSessionResultDto Found(Guid id, CurrentPlayerSessionDto value) =>
            new() { PlayerId = id, Status = CurrentPlayerSessionStatus.Found, Session = value };
        CurrentPlayerSessionResultDto[] response =
        [
            Found(good, session),
            Found(stale, session with { LastConfirmedUtc = now.AddMinutes(-11) }),
            Found(future, session with { LastConfirmedUtc = now.AddMinutes(2) }),
            Found(wrongServer, session with { ServerId = Guid.NewGuid() }),
            Found(wrongGame, session with { GameType = GameType.CallOfDuty2 }),
            Found(bot, session with { IsBot = true }),
            Found(duplicate, session), Found(duplicate, session),
            Found(inconsistent, session with { ObservedSeconds = 100000 }),
            new() { PlayerId = unavailable, Status = CurrentPlayerSessionStatus.Unavailable }
        ];
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(client => client.Playtime.V1.GetCurrentSessions(serverId, It.IsAny<CurrentPlayerSessionsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<IReadOnlyList<CurrentPlayerSessionResultDto>>(HttpStatusCode.OK, new ApiResponse<IReadOnlyList<CurrentPlayerSessionResultDto>>(response)));
        var service = new CurrentSessionService(repository.Object, NullLogger<CurrentSessionService>.Instance);

        var result = await service.GetAsync(serverId, GameType.CallOfDuty4,
            [good, stale, future, wrongServer, wrongGame, bot, duplicate, inconsistent, unavailable], CancellationToken.None);

        Assert.Equal(7200, Assert.Single(result).Value);
        Assert.True(result.ContainsKey(good));
    }

    [Fact]
    public async Task BatchesDistinctIdsAndPreservesResultsWhenAnotherBatchFails()
    {
        var serverId = Guid.NewGuid();
        var ids = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToArray();
        var requests = new List<Guid[]>();
        var now = DateTime.UtcNow;
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(client => client.Playtime.V1.GetCurrentSessions(serverId, It.IsAny<CurrentPlayerSessionsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, CurrentPlayerSessionsRequestDto request, CancellationToken _) =>
            {
                requests.Add(request.PlayerIds.ToArray());
                if (requests.Count == 2)
                    return new ApiResult<IReadOnlyList<CurrentPlayerSessionResultDto>>(HttpStatusCode.ServiceUnavailable);
                IReadOnlyList<CurrentPlayerSessionResultDto> data =
                [
                    new()
                    {
                        PlayerId = request.PlayerIds[0], Status = CurrentPlayerSessionStatus.Found,
                        Session = new CurrentPlayerSessionDto
                        {
                            SessionId = Guid.NewGuid(), ServerId = serverId, GameType = GameType.CallOfDuty4,
                            StartUtc = now.AddMinutes(-1), LastConfirmedUtc = now, ObservedSeconds = 60
                        }
                    }
                ];
                return new ApiResult<IReadOnlyList<CurrentPlayerSessionResultDto>>(HttpStatusCode.OK, new ApiResponse<IReadOnlyList<CurrentPlayerSessionResultDto>>(data));
            });
        var service = new CurrentSessionService(repository.Object, NullLogger<CurrentSessionService>.Instance);

        var result = await service.GetAsync(serverId, GameType.CallOfDuty4, ids.Append(ids[0]), CancellationToken.None);

        Assert.Equal(new[] { 100, 1 }, requests.Select(batch => batch.Length));
        Assert.Equal(60, Assert.Single(result).Value);
    }
}
