using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Playtime;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "HttpIntegration")]
public sealed class CurrentSessionEndpointTests
{
    [Theory]
    [InlineData(false, false, 7200L)]
    [InlineData(true, false, null)]
    [InlineData(false, true, null)]
    public async Task PublicRosterShowsFreshSessionsWithoutExposingIdentifiers(bool stale, bool statusFails, long? expected)
    {
        var scenario = new MapControlScenario();
        var playerId = Guid.NewGuid();
        var server = JObject.FromObject(scenario.GameServer);
        server[nameof(GameServerDto.ServerListEnabled)] = true;
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server.ToObject<GameServerDto>())));
        var player = JsonConvert.DeserializeObject<LivePlayerDto>(JsonConvert.SerializeObject(new
        {
            PlayerId = playerId,
            GameServerServerId = scenario.GameServerId,
            GameType = GameType.CallOfDuty4,
            Name = "Player",
            Num = 1,
            IpAddress = "203.0.113.12"
        }))!;
        scenario.RepositoryClient.Setup(r => r.LiveStatus.V1.GetGameServerLivePlayers(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<LivePlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<LivePlayerDto>>(new CollectionModel<LivePlayerDto>([player]))));
        var status = JsonConvert.DeserializeObject<GameServerLiveStatusDto>(JsonConvert.SerializeObject(new
        {
            ServerId = scenario.GameServerId,
            GameType = GameType.CallOfDuty4,
            IsOnline = false,
            LastUpdated = DateTime.UtcNow.AddMinutes(stale ? -11 : 0)
        }))!;
        var statusRequest = scenario.RepositoryClient.Setup(r => r.LiveStatus.V1.GetGameServerLiveStatus(scenario.GameServerId, It.IsAny<CancellationToken>()));
        if (statusFails)
            statusRequest.ThrowsAsync(new HttpRequestException("Status unavailable"));
        else
            statusRequest.ReturnsAsync(new ApiResult<GameServerLiveStatusDto>(HttpStatusCode.OK, new ApiResponse<GameServerLiveStatusDto>(status)));
        SetSession(scenario.RepositoryClient, scenario.GameServerId, playerId);
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var response = await host.Client.GetAsync($"/Servers/GetLivePlayers/{scenario.GameServerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        var result = Assert.IsType<JObject>(Assert.Single((JArray)JObject.Parse(body)["data"]!));
        Assert.Equal(expected, result.Value<long?>("currentSessionObservedSeconds"));
        Assert.DoesNotContain(playerId.ToString(), body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("203.0.113.12", body, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("same")]
    [InlineData("changed")]
    [InlineData("duplicate")]
    public async Task AdminSessionRequiresStillMatchingRconSlot(string snapshot)
    {
        var scenario = new PlayerModerationScenario();
        SetSession(scenario.RepositoryClient, scenario.GameServerId, scenario.PlayerId);
        if (snapshot != "same")
        {
            var original = JsonConvert.DeserializeObject<RconStatusResponseDto>(JsonConvert.SerializeObject(new
            {
                Players = new[] { new { Num = PlayerModerationScenario.PlayerSlot, Guid = PlayerModerationScenario.PlayerGuid, IpAddress = "" } }
            }))!;
            var changedPlayers = snapshot == "duplicate"
                ? new[] { new { Num = PlayerModerationScenario.PlayerSlot, Guid = PlayerModerationScenario.PlayerGuid, IpAddress = "" }, new { Num = PlayerModerationScenario.PlayerSlot, Guid = "OTHER-GUID", IpAddress = "" } }
                : new[] { new { Num = PlayerModerationScenario.PlayerSlot, Guid = "OTHER-GUID", IpAddress = "" } };
            var changed = JsonConvert.DeserializeObject<RconStatusResponseDto>(JsonConvert.SerializeObject(new
            {
                Players = changedPlayers
            }))!;
            Mock.Get(scenario.ServersClient.Object.Cod4Rcon.V1)
                .SetupSequence(api => api.Status(scenario.GameServerId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ApiResult<RconStatusResponseDto>(HttpStatusCode.OK, new ApiResponse<RconStatusResponseDto>(original)))
                .ReturnsAsync(new ApiResult<RconStatusResponseDto>(HttpStatusCode.OK, new ApiResponse<RconStatusResponseDto>(changed)));
        }
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.LiveServerKick);

        using var response = await host.Client.GetAsync($"/ServerAdmin/GetRconPlayerSessions/{scenario.GameServerId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (JArray)JObject.Parse(await response.Content.ReadAsStringAsync())["data"]!;
        if (snapshot != "same")
            Assert.Empty(data);
        else
            Assert.Equal(7200, Assert.Single(data).Value<long>("currentSessionObservedSeconds"));
    }

    private static void SetSession(Mock<IRepositoryApiClient> repository, Guid serverId, Guid playerId)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<CurrentPlayerSessionResultDto> results =
        [
            new()
            {
                PlayerId = playerId, Status = CurrentPlayerSessionStatus.Found,
                Session = new CurrentPlayerSessionDto
                {
                    SessionId = Guid.NewGuid(), ServerId = serverId, GameType = GameType.CallOfDuty4,
                    StartUtc = now.AddHours(-2), LastConfirmedUtc = now, ObservedSeconds = 7200
                }
            }
        ];
        repository.Setup(r => r.Playtime.V1.GetCurrentSessions(serverId, It.IsAny<CurrentPlayerSessionsRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<IReadOnlyList<CurrentPlayerSessionResultDto>>(HttpStatusCode.OK,
                new ApiResponse<IReadOnlyList<CurrentPlayerSessionResultDto>>(results)));
    }
}
