using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Dashboard;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.Services.Settings;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed class ForumIntegrationHttpTests
{
    [Theory]
    [InlineData("https://test.xtremeidiots.com", true)]
    [InlineData("https://www.xtremeidiots.com", true)]
    [InlineData("https://evil.xtremeidiots.com", false)]
    [InlineData("http://test.xtremeidiots.com", false)]
    public async Task Preflight_OnlyAllowsExactForumOrigins(string origin, bool allowed)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/integration/v1/rotations");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(allowed, response.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(origins!));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
    }

    [Fact]
    public async Task Rotations_PreservesObservedHistoryIndicator()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(repository => repository.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationsDto>(new PublicServerRotationsDto
                {
                    Servers = [new() { GameServerId = Guid.NewGuid(), Title = "Server", IsObservedHistory = true }]
                })));

        using var response = await host.Client.GetAsync("/api/integration/v1/rotations");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.GetProperty("servers")[0].GetProperty("isObservedHistory").GetBoolean());
        Assert.False(body.TryGetProperty("telemetryProperties", out _));
    }

    [Fact]
    public async Task Maps_ReturnsPublicProjectionAndPassesPagingAndSearch()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var id = Guid.NewGuid();
        var map = Newtonsoft.Json.JsonConvert.DeserializeObject<MapDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { MapId = id, GameType = GameType.CallOfDuty4, MapName = "mp_test" }))!;
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(repository => repository.Maps.V1.GetMaps(GameType.CallOfDuty4, null, null, "test", 10, 5,
                MapsOrder.MapNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>([map]))));

        using var response = await host.Client.GetAsync("/api/integration/v1/maps?gameType=CallOfDuty4&search=test&skip=10&take=5");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, body.GetProperty("skip").GetInt32());
        Assert.Equal(5, body.GetProperty("take").GetInt32());
        var item = body.GetProperty("items")[0];
        Assert.Equal(id, item.GetProperty("id").GetGuid());
        Assert.Equal("mp_test", item.GetProperty("name").GetString());
        Assert.Equal(4, item.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("?skip=-1")]
    [InlineData("?take=101")]
    [InlineData("?gameType=99999")]
    public async Task Maps_RejectsInvalidPagingAndGameType(string query)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var response = await host.Client.GetAsync("/api/integration/v1/maps" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(TestPrincipalProfiles.MapRotationReader, HttpStatusCode.Forbidden)]
    public async Task Moderation_RequiresExistingReadPolicy(string? profile, HttpStatusCode expected)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/moderation");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>()).Verify(repository =>
            repository.AdminActions.V1.GetAdminActions(It.IsAny<GameType?>(), null, null, null,
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AdminActionOrder>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Banners_ReportsIncompleteConfigurationInsteadOfPartialSuccess(bool complete)
    {
        var id = Guid.NewGuid();
        var settings = new Mock<IGameServerConfigurationReadService>();
        var configs = new Dictionary<Guid, GameServerReadSettings>();
        if (complete)
        {
            var transport = new FileTransportReadSettings(null, 0, null, null);
            configs[id] = new GameServerReadSettings(null, "<b>Server</b>", transport, transport);
        }

        settings.Setup(service => service.GetAsync(It.IsAny<IEnumerable<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);
        await using var host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IGameServerConfigurationReadService>();
            services.AddSingleton(settings.Object);
        });
        var server = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { GameServerId = id }))!;
        Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>())
            .Setup(repository => repository.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([server]))));

        using var response = await host.Client.GetAsync("/api/integration/v1/banners");

        Assert.Equal(complete ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        if (complete)
        {
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("<b>Server</b>", body.GetProperty("banners")[0].GetString());
        }
    }

    [Fact]
    public async Task Banners_ReadsAllServerPages()
    {
        var servers = Enumerable.Range(0, 51).Select(_ => Fixture<GameServerDto>(new { GameServerId = Guid.NewGuid() })).ToArray();
        var transport = new FileTransportReadSettings(null, 0, null, null);
        var configs = servers.ToDictionary(server => server.GameServerId,
            _ => new GameServerReadSettings(null, "<b>Server</b>", transport, transport));
        var settings = new Mock<IGameServerConfigurationReadService>();
        settings.Setup(service => service.GetAsync(It.IsAny<IEnumerable<Guid>>(), false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(configs);
        await using var host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IGameServerConfigurationReadService>();
            services.AddSingleton(settings.Object);
        });
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        repository.Setup(client => client.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                0, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(servers.Take(50)))
                { Pagination = new ApiPagination(51, 51, 0, 50) }));
        repository.Setup(client => client.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                50, 50, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>(servers.Skip(50)))
                { Pagination = new ApiPagination(51, 51, 50, 50) }));

        using var response = await host.Client.GetAsync("/api/integration/v1/banners");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(51, body.GetProperty("banners").GetArrayLength());
    }

    [Fact]
    public async Task Stats_CombinesServerSideCountsForAuthorizedDashboardUser()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var repository = Mock.Get(host.Services.GetRequiredService<IRepositoryApiClient>());
        var id = Guid.NewGuid();
        var server = Fixture<GameServerDto>(new { GameServerId = id, GameType = GameType.CallOfDuty4 });
        var live = Fixture<GameServerLiveStatusDto>(new { ServerId = id, CurrentPlayers = 7 });
        var dashboard = Fixture<DashboardSummaryDto>(new { TotalPlayersOnline = 7, UnclaimedBanCount = 2, OpenReportCount = 3 });
        repository.Setup(client => client.Dashboard.V1.GetDashboardSummary(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<DashboardSummaryDto>(HttpStatusCode.OK, new ApiResponse<DashboardSummaryDto>(dashboard)));
        repository.Setup(client => client.GameServers.V1.GetGameServers(null, null, null, 0, 200,
                GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([server]))
                { Pagination = new ApiPagination(1, 1, 0, 200) }));
        repository.Setup(client => client.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerLiveStatusDto>>(new CollectionModel<GameServerLiveStatusDto>([live]))));
        repository.Setup(client => client.Players.V1.GetPlayers(null, null, null, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([]))
                { Pagination = new ApiPagination(125, 125, 0, 1) }));
        repository.Setup(client => client.PlayerAnalyticsV2.V1.GetOverview(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime from, DateTime to, CancellationToken _) =>
                new ApiResult<PlayersOverviewDto>(HttpStatusCode.OK, new ApiResponse<PlayersOverviewDto>(
                    Fixture<PlayersOverviewDto>(new { ActivePlayers = (to - from).TotalHours < 30 ? 10 : 20 }))));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/stats");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(125, body.GetProperty("trackedPlayers").GetInt32());
        Assert.Equal(10, body.GetProperty("activePlayers24h").GetInt32());
        Assert.Equal(20, body.GetProperty("activePlayers48h").GetInt32());
        Assert.Equal(7, body.GetProperty("onlineByGame")[0].GetProperty("playersOnline").GetInt32());
        Assert.Equal(2, body.GetProperty("unclaimedBans").GetInt32());
    }

    [Fact]
    public async Task Activity_UsesExistingPolicyAndPassesPagingToActivityService()
    {
        var activity = new Mock<IActivityLogService>();
        activity.Setup(service => service.QueryEventsAsync(TimeSpan.FromHours(24), It.IsAny<IReadOnlyList<ActivityLogCategory>>(),
                It.IsAny<IReadOnlyList<string>>(), "audit", 10, 5, "timestamp", "desc", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ActivityLogQueryResult
            {
                TotalCount = 20,
                FilteredCount = 1,
                Entries = [new ActivityLogEntry { EventName = "SyntheticEvent", Category = "Test" }]
            });
        await using var host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IActivityLogService>();
            services.AddSingleton(activity.Object);
        });
        using var denied = await host.Client.GetAsync("/api/integration/v1/activity");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/integration/v1/activity?search=audit&skip=10&take=5");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var response = await host.Client.SendAsync(request);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, body.GetProperty("totalCount").GetInt32());
        Assert.Equal("SyntheticEvent", body.GetProperty("items")[0].GetProperty("eventName").GetString());
    }

    private static T Fixture<T>(object value)
        => Newtonsoft.Json.JsonConvert.DeserializeObject<T>(Newtonsoft.Json.JsonConvert.SerializeObject(value))!;
}
