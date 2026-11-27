using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;
using System.Globalization;
using System.Text.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.AdminActions;

[Trait("Category", "Browser")]
public sealed class PlayerActivityBrowserTests
{
    private readonly static Guid ChatOnlyServerId = Guid.Parse("99999999-9999-4999-8999-999999999999");

    [Fact]
    public async Task ActivityShowsRecordedDaysAndKeepsServerScopeWhenChangingRange()
    {
        var scenario = new AdminActionScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.RouteAsync("**/api/Analytics/player/*/activity**", async route =>
        {
            var query = QueryHelpers.ParseQuery(new Uri(route.Request.Url).Query);
            var selected = query.TryGetValue("serverId", out var serverId) && serverId.ToString() == ChatOnlyServerId.ToString();
            object[] daily = selected ? [] : [new { dateUtc = "2026-11-24T00:00:00Z", measuredSeconds = 4500 }];
            object[] servers = selected ? [] : [new
            {
                serverId = AdminActionScenario.ActivityServerId,
                serverName = "Activity server",
                measuredSeconds = 4500,
                trackedSessions = 2,
            }];
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    activity = new
                    {
                        fromUtc = "2026-11-22T00:00:00Z",
                        toUtc = "2026-11-25T00:00:00Z",
                        playtimeCoverage = "unknown",
                        measuredSeconds = selected ? 0 : 4500,
                        unknownMapSeconds = 0,
                        trackedSessions = selected ? 0 : 2,
                        activeDays = selected ? 0 : 1,
                        chatMessages = selected ? (long?)0 : null,
                        daily,
                        servers,
                    },
                    availableServers = new[]
                    {
                        new { serverId = AdminActionScenario.ActivityServerId, serverName = "Activity server" },
                        new { serverId = ChatOnlyServerId, serverName = "Chat-only server" },
                    },
                }),
            });
        });

        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details?id={scenario.PlayerId}").AbsoluteUri);
        Assert.True(response?.Ok);
        var cards = browser.Page.Locator("#analytics-embed-player-kpis .col");
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("1 h 15 min");
        await Assertions.Expect(cards.Nth(3).Locator(".h3")).ToHaveTextAsync("\u2014");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-observation")).ToContainTextAsync("Coverage unknown");
        await browser.Page.Locator("#analytics-embed-player-daily summary").ClickAsync();
        var days = browser.Page.Locator("#analytics-embed-player-daily-rows tr");
        await Assertions.Expect(days).ToHaveCountAsync(3);
        await Assertions.Expect(days.Nth(0)).ToContainTextAsync("\u2014");
        await Assertions.Expect(days.Nth(2)).ToContainTextAsync("1 h 15 min");

        var server = browser.Page.Locator("#analytics-embed-player-server");
        await Assertions.Expect(server.Locator("option")).ToHaveCountAsync(3);
        await browser.Page.RunAndWaitForResponseAsync(
            async () => await server.SelectOptionAsync(ChatOnlyServerId.ToString()),
            activityResponse => activityResponse.Request.Url.Contains($"serverId={ChatOnlyServerId}", StringComparison.OrdinalIgnoreCase));
        await Assertions.Expect(cards.Nth(0).Locator(".h3")).ToHaveTextAsync("\u2014");
        await Assertions.Expect(cards.Nth(3).Locator(".h3")).ToHaveTextAsync("0");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-playtime")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-breakdown")).ToBeHiddenAsync();

        var rangeResponse = await browser.Page.RunAndWaitForResponseAsync(
            async () => await browser.Page.Locator("#analytics-embed-player-range").SelectOptionAsync("7d"),
            activityResponse => activityResponse.Request.Url.Contains($"serverId={ChatOnlyServerId}", StringComparison.OrdinalIgnoreCase));
        var rangeQuery = QueryHelpers.ParseQuery(new Uri(rangeResponse.Url).Query);
        var from = DateTimeOffset.Parse(rangeQuery["from"].ToString(), CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse(rangeQuery["to"].ToString(), CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.FromDays(7), to - from);
        await Assertions.Expect(server).ToHaveValueAsync(ChatOnlyServerId.ToString());
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task UnavailableCoverageDoesNotDisplayZeroActivity()
    {
        var scenario = new AdminActionScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.RouteAsync("**/api/Analytics/player/*/activity**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new
            {
                activity = new { playtimeCoverage = "unavailable" },
                availableServers = new[] { new { serverId = AdminActionScenario.ActivityServerId, serverName = "Activity server" } },
            }),
        }));

        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details/{scenario.PlayerId}").AbsoluteUri);
        Assert.True(response?.Ok);
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-status")).ToHaveTextAsync("Player activity unavailable.");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-results")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-server")).ToBeEnabledAsync();
        browser.AssertNoBrowserErrors();
    }
}
