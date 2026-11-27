using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json.Linq;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RecentPlayers;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "HttpIntegration")]
public sealed class AnalyticsCorrectionEndpointTests
{
    [Theory]
    [InlineData("/api/Analytics/dashboard/home")]
    [InlineData("/api/Analytics/server/timeseries")]
    public async Task RecordedAnalyticsEndpointsRequireDashboardRead(string path)
    {
        var scenario = new MapControlScenario();
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var anonymous = await host.Client.GetAsync(path);
        Assert.True(anonymous.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized);
        using var restrictedRequest = new HttpRequestMessage(HttpMethod.Get, path);
        restrictedRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.GameServerWriterWithoutRcon);
        using var restricted = await host.Client.SendAsync(restrictedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, restricted.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicMapDoesNotDescribeUnavailableObservationsAsEmptyOrOnline(bool unavailable)
    {
        var scenario = new MapControlScenario();
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServers(
                null, null, GameServerFilter.ServerListEnabled, 0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));
        scenario.RepositoryClient.Setup(r => r.RecentPlayers.V1.GetRecentPlayers(
                null, null, It.IsAny<DateTime?>(), RecentPlayersFilter.GeoLocated, 0, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unavailable
                ? new ApiResult<CollectionModel<RecentPlayerDto>>(HttpStatusCode.ServiceUnavailable)
                : new ApiResult<CollectionModel<RecentPlayerDto>>(HttpStatusCode.OK,
                    new ApiResponse<CollectionModel<RecentPlayerDto>>(new CollectionModel<RecentPlayerDto>([]))));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var response = await host.Client.GetAsync("/Servers/Map");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Players Online", html);
        Assert.Contains("Not live players", html);
        Assert.Contains("up to 200 records before public-server filtering", html);
        if (unavailable)
        {
            Assert.Contains("currently unavailable", html);
            Assert.DoesNotContain("0 location observations shown", html);
        }
        else
            Assert.Contains("0 location observations shown", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerMapFailuresAreNotSuccessfulEmptySnapshots(bool metadataUnavailable)
    {
        var scenario = new MapControlScenario();
        var server = JObject.FromObject(scenario.GameServer);
        server[nameof(GameServerDto.ServerListEnabled)] = true;
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadataUnavailable
                ? new ApiResult<GameServerDto>(HttpStatusCode.ServiceUnavailable)
                : new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server.ToObject<GameServerDto>())));
        scenario.RepositoryClient.Setup(r => r.RecentPlayers.V1.GetRecentPlayers(
                null, scenario.GameServerId, It.IsAny<DateTime?>(), RecentPlayersFilter.GeoLocated, 0, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RecentPlayerDto>>(HttpStatusCode.ServiceUnavailable));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var response = await host.Client.GetAsync($"/Servers/GetRecentPlayersForMap/{scenario.GameServerId}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotNull(json["error"]);
        Assert.Null(json["players"]);
    }
}
