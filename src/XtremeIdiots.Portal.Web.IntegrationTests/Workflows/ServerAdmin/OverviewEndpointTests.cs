using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;
using XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "HttpIntegration")]
public sealed class OverviewEndpointTests
{
    [Fact]
    public async Task EmptyAgentStatusDoesNotRenderLoadingPanelOrRequestTelemetry()
    {
        var scenario = new MapControlScenario();
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), GameServerFilter.AgentEnabled,
                0, 100, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync("/Status/AgentStatus");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("No agent-enabled servers found.", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Loading agent telemetry", html, StringComparison.Ordinal);
        Assert.DoesNotContain("GetOverviewTelemetry", html, StringComparison.Ordinal);
        Assert.DoesNotContain("overview-panels.js", html, StringComparison.Ordinal);
        scenario.AgentTelemetry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DashboardDoesNotReportMissingOrUnavailableTelemetryAsActive(bool queryFails)
    {
        var scenario = new MapControlScenario();
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), GameServerFilter.AgentEnabled,
                0, 100, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([scenario.GameServer]))));
        var query = scenario.AgentTelemetry.Setup(t => t.GetAllServersStatusAsync(It.IsAny<CancellationToken>()));
        if (queryFails)
            query.ThrowsAsync(new InvalidOperationException("Telemetry unavailable"));
        else
            query.ReturnsAsync(Array.Empty<AgentServerSummary>());
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync("/Dashboard/GetOverviewTelemetry");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        if (queryFails)
        {
            Assert.Contains("Unavailable", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Offline", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Active", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("0 Active", html, StringComparison.Ordinal);
            Assert.Contains("1 Offline", html, StringComparison.Ordinal);
            Assert.Contains("bg-danger", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Unavailable", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task ServerShellDoesNotCallTelemetryAndStillRendersDuringPendingPanelRequest()
    {
        var scenario = new MapControlScenario();
        var gate = new RequestGate(hold: true);
        scenario.AgentTelemetry.Setup(t => t.GetServerStatusAsync(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, CancellationToken token) =>
            {
                await gate.WaitAsync(token);
                return new AgentServerStatus { ActivityStatus = AgentActivityStatus.Active };
            });
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var shell = await host.Client.GetAsync($"/ServerAdmin/ServerDetail/{scenario.GameServerId}");
        Assert.Equal(HttpStatusCode.OK, shell.StatusCode);
        var html = await shell.Content.ReadAsStringAsync();
        Assert.Contains("sd-restartServer", html, StringComparison.Ordinal);
        Assert.Contains("GetOverviewStatistics", html, StringComparison.Ordinal);
        scenario.AgentTelemetry.Verify(t => t.GetServerStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        var panel = host.Client.GetAsync($"/ServerAdmin/GetOverviewTelemetry/{scenario.GameServerId}");
        try
        {
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            using var secondShell = await host.Client.GetAsync($"/ServerAdmin/ServerDetail/{scenario.GameServerId}").WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, secondShell.StatusCode);
        }
        finally
        {
            gate.Release();
        }

        using var telemetry = await panel;
        Assert.Equal(HttpStatusCode.OK, telemetry.StatusCode);
        Assert.Contains("Active", await telemetry.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublicStatisticsRevalidateVisibilityEvenWithCachedOptionalDataAndNeverFetchAdminTelemetry()
    {
        var scenario = new MapControlScenario();
        var serverJson = JsonConvert.SerializeObject(scenario.GameServer);
        var properties = Newtonsoft.Json.Linq.JObject.Parse(serverJson);
        properties[nameof(GameServerDto.ServerListEnabled)] = true;
        var publicServer = properties.ToObject<GameServerDto>();
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(publicServer)));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var visible = await host.Client.GetAsync($"/Servers/GetOverviewStatistics/{scenario.GameServerId}");
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Contains("Server statistics are currently unavailable", await visible.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(scenario.GameServer)));
        using var hidden = await host.Client.GetAsync($"/Servers/GetOverviewStatistics/{scenario.GameServerId}");
        Assert.Equal(HttpStatusCode.Redirect, hidden.StatusCode);
        Assert.Equal("/Errors/Display/404", hidden.Headers.Location?.ToString());
        Assert.DoesNotContain("data-chart-data", await hidden.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        scenario.AgentTelemetry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task OptionalAdminPanelsRequireAuthenticationAndExistingFeaturePolicies()
    {
        var scenario = new MapControlScenario();
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        foreach (var path in new[]
        {
            $"/ServerAdmin/GetOverviewStatistics/{scenario.GameServerId}",
            $"/ServerAdmin/GetOverviewTelemetry/{scenario.GameServerId}",
            $"/ServerAdmin/GetOverviewBanFileMonitors/{scenario.GameServerId}",
            "/Dashboard/GetOverviewSummary", "/Dashboard/GetOverviewLeaderboard", "/Dashboard/GetOverviewTrend",
            "/Dashboard/GetOverviewUtilization", "/Dashboard/GetOverviewTelemetry", "/Status/GetOverviewTelemetry"
        })
        {
            using var anonymous = await host.Client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
            Assert.Contains("Login", anonymous.Headers.Location?.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/ServerAdmin/GetOverviewStatistics/{scenario.GameServerId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.GameServerWriterWithoutRcon);
        using var denied = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        foreach (var (path, expectedStatus) in new[]
        {
            ($"/ServerAdmin/GetOverviewTelemetry/{scenario.GameServerId}?detailed=true", HttpStatusCode.Redirect),
            ($"/ServerAdmin/GetOverviewBanFileMonitors/{scenario.GameServerId}", HttpStatusCode.Forbidden)
        })
        {
            using var restricted = new HttpRequestMessage(HttpMethod.Get, path);
            restricted.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.LiveServerKick);
            using var response = await host.Client.SendAsync(restricted);
            Assert.Equal(expectedStatus, response.StatusCode);
            if (expectedStatus == HttpStatusCode.Redirect)
                Assert.Equal("/Errors/Display/403", response.Headers.Location?.ToString());
        }

        scenario.AgentTelemetry.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.LiveServerKick, false)]
    [InlineData(TestPrincipalProfiles.SeniorAdmin, true)]
    public async Task DetailedTelemetryAlwaysRequiresPermissionEvenAfterBasicCacheWarm(string principal, bool canReadDetailed)
    {
        var scenario = new MapControlScenario();
        scenario.AgentTelemetry.Setup(t => t.GetServerStatusAsync(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentServerStatus
            {
                ActivityStatus = AgentActivityStatus.Active,
                PlayersConnectedLastHour = 123,
                BansDetectedLast24h = 42,
                ModerationTriggersLast24h = 57,
                LastMapName = "restricted-telemetry-map"
            });
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, principal);

        using var basic = await host.Client.GetAsync($"/ServerAdmin/GetOverviewTelemetry/{scenario.GameServerId}?detailed=false");
        Assert.Equal(HttpStatusCode.OK, basic.StatusCode);
        var basicHtml = await basic.Content.ReadAsStringAsync();
        Assert.Contains("Active", basicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Players Connected/Hour", basicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Bans Detected", basicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Moderation Triggers", basicHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("restricted-telemetry-map", basicHtml, StringComparison.Ordinal);

        using var detailed = await host.Client.GetAsync($"/ServerAdmin/GetOverviewTelemetry/{scenario.GameServerId}?detailed=true");
        if (canReadDetailed)
        {
            Assert.Equal(HttpStatusCode.OK, detailed.StatusCode);
            var html = await detailed.Content.ReadAsStringAsync();
            Assert.Contains("Players Connected/Hour", html, StringComparison.Ordinal);
            Assert.Contains("Bans Detected", html, StringComparison.Ordinal);
            Assert.Contains("Moderation Triggers", html, StringComparison.Ordinal);
            Assert.Contains("restricted-telemetry-map", html, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal(HttpStatusCode.Redirect, detailed.StatusCode);
            Assert.Equal("/Errors/Display/403", detailed.Headers.Location?.ToString());
            Assert.DoesNotContain("restricted-telemetry-map", await detailed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        scenario.AgentTelemetry.Verify(t => t.GetServerStatusAsync(scenario.GameServerId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
