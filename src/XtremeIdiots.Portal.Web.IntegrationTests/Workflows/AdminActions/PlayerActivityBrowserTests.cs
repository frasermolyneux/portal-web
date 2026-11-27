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

        var response = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details?id={scenario.PlayerId}").AbsoluteUri),
            activityResponse => new Uri(activityResponse.Url).AbsolutePath == $"/api/Analytics/player/{scenario.PlayerId}/activity");
        Assert.True(response.Ok);
        var initialQuery = QueryHelpers.ParseQuery(new Uri(response.Url).Query);
        Assert.Equal(TimeSpan.FromDays(7),
            DateTimeOffset.Parse(initialQuery["to"].ToString(), CultureInfo.InvariantCulture) -
            DateTimeOffset.Parse(initialQuery["from"].ToString(), CultureInfo.InvariantCulture));
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-range")).ToHaveValueAsync("7d");
        var cards = browser.Page.Locator("#analytics-embed-player-kpis .col");
        await Assertions.Expect(cards.Nth(0)).ToContainTextAsync("1.25 h");
        await Assertions.Expect(cards.Nth(3).Locator(".h3")).ToHaveTextAsync("\u2014");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-observation")).ToContainTextAsync("Coverage unknown");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-playtime")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-playtime table, #analytics-embed-player-playtime details")).ToHaveCountAsync(0);
        var chart = await browser.Page.EvaluateAsync<JsonElement>("() => Chart.getChart('analytics-embed-player-trend').data.datasets[0]");
        Assert.Equal("Play time (hours)", chart.GetProperty("label").GetString());
        var values = chart.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(3, values.Length);
        Assert.Equal(JsonValueKind.Null, values[0].ValueKind);
        Assert.Equal(JsonValueKind.Null, values[1].ValueKind);
        Assert.Equal(1.25m, values[2].GetDecimal());
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-trend")).ToHaveAttributeAsync(
            "aria-label", "Daily measured play time (UTC). 2026-11-22: unknown; 2026-11-23: unknown; 2026-11-24: 1.25 h");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-server-rows")).ToContainTextAsync("1.25 h");

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
            async () => await browser.Page.Locator("#analytics-embed-player-range").SelectOptionAsync("30d"),
            activityResponse => activityResponse.Request.Url.Contains($"serverId={ChatOnlyServerId}", StringComparison.OrdinalIgnoreCase));
        var rangeQuery = QueryHelpers.ParseQuery(new Uri(rangeResponse.Url).Query);
        var from = DateTimeOffset.Parse(rangeQuery["from"].ToString(), CultureInfo.InvariantCulture);
        var to = DateTimeOffset.Parse(rangeQuery["to"].ToString(), CultureInfo.InvariantCulture);
        Assert.Equal(TimeSpan.FromDays(30), to - from);
        await Assertions.Expect(server).ToHaveValueAsync(ChatOnlyServerId.ToString());
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededOrRestoredActivityCancelsThePreviousRequest(bool restorePage)
    {
        var scenario = new AdminActionScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.AddInitScriptAsync("""
            window.activitySignals = [];
            const originalFetch = window.fetch;
            window.fetch = (url, options) => {
                if (!String(url).includes('/api/Analytics/player/')) return originalFetch(url, options);
                window.activitySignals.push(options?.signal);
                if (window.activitySignals.length > 1) return originalFetch(url, options);
                return new Promise((resolve, reject) => {
                    options?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')));
                });
            };
            """);

        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details/{scenario.PlayerId}").AbsoluteUri);
        Assert.True(response?.Ok);
        await browser.Page.WaitForFunctionAsync("() => window.activitySignals.length === 1");
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-status")).ToHaveTextAsync("Loading activity\u2026");
        if (restorePage)
        {
            await browser.Page.EvaluateAsync("""
                () => {
                    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
                }
                """);
        }
        else
        {
            await browser.Page.Locator("#analytics-embed-player-range").SelectOptionAsync("30d");
        }

        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-kpis")).ToContainTextAsync("1.25 h");
        Assert.True(await browser.Page.EvaluateAsync<bool>("() => window.activitySignals[0].aborted"));
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-status")).ToBeHiddenAsync();
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task MissingChartShowsUnavailableWithoutADailyTable()
    {
        var scenario = new AdminActionScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.RouteAsync("**/chart.umd.js*", route => route.FulfillAsync(new()
        {
            ContentType = "application/javascript",
            Body = "",
        }));
        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details/{scenario.PlayerId}").AbsoluteUri);
        Assert.True(response?.Ok);
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-chart-unavailable")).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-chart")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-playtime table, #analytics-embed-player-playtime details")).ToHaveCountAsync(0);
        await Assertions.Expect(browser.Page.Locator("#analytics-embed-player-server-rows")).ToContainTextAsync("Activity server");
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
