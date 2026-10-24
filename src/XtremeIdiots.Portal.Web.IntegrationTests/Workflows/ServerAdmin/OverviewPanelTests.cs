using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "Browser")]
public sealed class OverviewPanelTests
{
    [Fact]
    public async Task AgentStatusFailureMarksRowsUnavailableAndRetryCanRecover()
    {
        var scenario = new MapControlScenario();
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]?>(), It.IsAny<Guid[]?>(), GameServerFilter.AgentEnabled,
                0, 100, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([scenario.GameServer]))));
        scenario.AgentTelemetry.Setup(t => t.GetAllServersStatusAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new AgentServerSummary
            {
                ServerId = scenario.GameServerId,
                ActivityStatus = AgentActivityStatus.Active,
                EventsLastHour = 17
            }]);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var requests = 0;
        await browser.Page.RouteAsync("**/Status/GetOverviewTelemetry", async route =>
        {
            var failure = Interlocked.Increment(ref requests) < 3;
            if (failure)
            {
                await route.FulfillAsync(new() { Status = 503, ContentType = "text/html", Body = "" });
            }
            else
            {
                await route.ContinueAsync();
            }
        });

        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Status/AgentStatus").AbsoluteUri,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        Assert.True(response?.Ok);
        var row = browser.Page.Locator($"[data-agent-server='{scenario.GameServerId}']");
        var status = row.Locator("[data-agent-field='status']");
        var panel = browser.Page.Locator("[data-overview-url*='GetOverviewTelemetry']");
        await Assertions.Expect(status).ToHaveTextAsync("Unavailable");
        await Assertions.Expect(row.Locator("[data-agent-field='events']")).ToHaveTextAsync("—");
        await Assertions.Expect(browser.Page.Locator("#agent-status-counts")).ToBeEmptyAsync();

        await panel.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Assertions.Expect(panel).Not.ToHaveAttributeAsync("aria-busy", "true");
        await Assertions.Expect(status).ToHaveTextAsync("Unavailable");
        await Assertions.Expect(row.Locator("[data-agent-field='events']")).ToHaveTextAsync("—");
        await panel.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
        await Assertions.Expect(status).ToHaveTextAsync("Active");
        await Assertions.Expect(row.Locator("[data-agent-field='events']")).ToHaveTextAsync("17");
        await Assertions.Expect(browser.Page.Locator("#agent-status-counts")).ToContainTextAsync("1 Active");
    }

    [Fact]
    public async Task ServerControlsAndPlayersRenderWhileTelemetryIsGatedAndStatisticsFailIndependently()
    {
        var scenario = new PlayerModerationScenario();
        var telemetryGate = new RequestGate(hold: true);
        scenario.AgentTelemetry.Setup(t => t.GetServerStatusAsync(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .Returns(async (Guid _, CancellationToken token) =>
            {
                await telemetryGate.WaitAsync(token);
                return new AgentServerStatus { ActivityStatus = AgentActivityStatus.Active, EventsLastHour = 17 };
            });
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.LiveServerKick, scenario.ConfigureServices);
        try
        {
            var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{scenario.GameServerId}").AbsoluteUri,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.True(response?.Ok);
            await telemetryGate.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            await Assertions.Expect(browser.Page.GetByTestId("player-kick")).ToBeEnabledAsync();
            var telemetry = browser.Page.Locator("[data-overview-url*='GetOverviewTelemetry']");
            await Assertions.Expect(telemetry).ToContainTextAsync("Loading agent telemetry");
            var statistics = browser.Page.Locator("[data-overview-url*='GetOverviewStatistics']").First;
            await Assertions.Expect(statistics).ToContainTextAsync("Server statistics are currently unavailable");
            await Assertions.Expect(statistics.GetByRole(AriaRole.Button, new() { Name = "Retry" })).ToBeVisibleAsync();

            telemetryGate.Release();
            await Assertions.Expect(telemetry).ToContainTextAsync("Active");
            await Assertions.Expect(telemetry).ToContainTextAsync("17");
            await Assertions.Expect(browser.Page.GetByTestId("player-kick")).ToBeEnabledAsync();
            browser.AssertNoBrowserErrors();
        }
        finally
        {
            telemetryGate.Release();
        }
    }

    [Fact]
    public async Task DashboardPanelsLoadIndependentlyAndFailedPanelCanRetry()
    {
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin);
        var summaryGate = new RequestGate(hold: true);
        var leaderboardRequests = 0;
        await browser.Page.RouteAsync("**/Dashboard/GetOverviewSummary", async route =>
        {
            await summaryGate.WaitAsync();
            await route.FulfillAsync(new() { ContentType = "text/html", Body = "<p>Summary loaded</p>" });
        });
        await browser.Page.RouteAsync("**/Dashboard/GetOverviewLeaderboard", async route =>
        {
            var first = Interlocked.Increment(ref leaderboardRequests) == 1;
            await route.FulfillAsync(new() { Status = first ? 503 : 200, ContentType = "text/html", Body = first ? "" : "<p>Leaderboard loaded</p>" });
        });
        await browser.Page.RouteAsync("**/Dashboard/GetOverviewTrend", route =>
            route.FulfillAsync(new() { ContentType = "text/html", Body = "<p>Trend loaded</p>" }));
        try
        {
            var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Dashboard").AbsoluteUri,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            Assert.True(response?.Ok);
            await summaryGate.Entered.WaitAsync(TimeSpan.FromSeconds(5));
            await Assertions.Expect(browser.Page.Locator("[data-overview-url*='GetOverviewTrend']")).ToContainTextAsync("Trend loaded");
            var leaderboard = browser.Page.Locator("[data-overview-url*='GetOverviewLeaderboard']");
            await leaderboard.GetByRole(AriaRole.Button, new() { Name = "Retry" }).ClickAsync();
            await Assertions.Expect(leaderboard).ToContainTextAsync("Leaderboard loaded");
            summaryGate.Release();
            await Assertions.Expect(browser.Page.Locator("[data-overview-url*='GetOverviewSummary']")).ToContainTextAsync("Summary loaded");
        }
        finally
        {
            summaryGate.Release();
        }
    }
}
