using Microsoft.Playwright;
using System.Text.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.Analytics;

[Trait("Category", "Browser")]
public sealed class AnalyticsLabelsBrowserTests
{
    [Theory]
    [InlineData("Global", false, false)]
    [InlineData("Game", false, false)]
    [InlineData("Player", false, false)]
    [InlineData("Player", true, true)]
    [InlineData("Player", true, false)]
    [InlineData("Player", false, true)]
    public async Task AnalyticsDistinguishesRecordedDataFromPlayerActivity(string page, bool trackedOverviewAvailable, bool trackedTrendAvailable)
    {
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin);
        var requests = new List<string>();
        await browser.Page.RouteAsync("**/api/Analytics/**", async route =>
        {
            var path = new Uri(route.Request.Url).AbsolutePath.ToLowerInvariant();
            lock (requests)
                requests.Add(path);

            object response = path switch
            {
                "/api/analytics/global/overview" => new { onlineServers = 1, totalServers = 2, totalPlayersOnline = 3, uniquePlayersWindow = 4, totalEvents = 5, totalChatMessages = 6, openReports = 7, activeBans = 8 },
                "/api/analytics/game/overview" => new { serverCount = 1, avgPlayers = 2.5, peakPlayers = 8, uniquePlayers = 4, moderationActions = 3 },
                "/api/analytics/player/overview" => trackedOverviewAvailable
                    ? new { activePlayers = 4, newPlayers = 2, returningPlayers = 2, totalPlayers = 4, observedPlayerRecords = 12, recordedPlayers = 0, sessionStarts = 0, trackingCoverage = "unknown" }
                    : new { activePlayers = 4, newPlayers = 2, returningPlayers = 2, totalPlayers = 4 },
                "/api/analytics/global/timeseries" => Trend("avgPlayers", "Average Players", "peakPlayers", "Peak Players"),
                "/api/analytics/game/timeseries" => Trend("avgPlayers", "Average Players"),
                "/api/analytics/player/timeseries" => PlayerTrend(trackedTrendAvailable),
                "/api/analytics/global/players" or "/api/analytics/game/players" => new { items = new[] { new { displayName = "Example", gameType = "Game", activityCount = 13 } } },
                "/api/analytics/global/geo" => new { items = new[] { new { countryCode = "GB", playerCount = 4, percentage = 100 } } },
                "/api/analytics/global/moderation" => new { summary = new { totalActions = 3, openReports = 2, closedReports = 1 } },
                "/api/analytics/game/maps" => new { items = new[] { new { mapName = "Example", plays = 30, upVotes = 2, downVotes = 1, score = 1 } } },
                "/api/analytics/player/by-game" => new { items = new[] { new { gameType = "Game", newPlayers = 2, activePlayers = 4, totalPlayers = 4 } } },
                "/api/analytics/player/by-server" => new { items = new[] { new { title = "Example", gameType = "Game", activePlayers = 4 } } },
                _ => new { items = Array.Empty<object>() },
            };
            await route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(response) });
        });

        var result = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Analytics/{page}").AbsoluteUri);
        Assert.True(result?.Ok);

        if (page == "Global")
        {
            await Assertions.Expect(browser.Page.Locator("#global-players")).ToContainTextAsync("Chat messages");
            await Assertions.Expect(browser.Page.Locator("#global-geo")).ToContainTextAsync("Latest observations");
            await Assertions.Expect(browser.Page.Locator("#global-geo")).Not.ToContainTextAsync("%");
            await Assertions.Expect(browser.Page.Locator("#global-kpis")).ToContainTextAsync("Latest seen in range");
            await Assertions.Expect(browser.Page.Locator("#global-kpis")).ToContainTextAsync("Open reports created in range");
            await Assertions.Expect(browser.Page.Locator("#global-kpis .col").First).Not.ToContainTextAsync("/");
            Assert.Equal(0, await browser.Page.Locator("#global-trend-summary").CountAsync());
        }
        else if (page == "Game")
        {
            await Assertions.Expect(browser.Page.Locator("#game-players")).ToContainTextAsync("Chat messages");
            await Assertions.Expect(browser.Page.Locator("#game-maps")).ToContainTextAsync("Samples");
            await Assertions.Expect(browser.Page.Locator("#game-kpis")).ToContainTextAsync("Peak sampled server occupancy");
            Assert.Equal(0, await browser.Page.Locator("#game-trend-summary").CountAsync());
        }
        else
        {
            await Assertions.Expect(browser.Page.Locator("#player-by-game")).ToContainTextAsync("First and latest seen in range");
            await Assertions.Expect(browser.Page.Locator("#player-by-server")).ToContainTextAsync("Latest observations");
            await Assertions.Expect(browser.Page.Locator("#player-kpis")).Not.ToContainTextAsync("Total");
            if (trackedOverviewAvailable)
                await Assertions.Expect(browser.Page.Locator("#player-kpis")).ToContainTextAsync("Tracked session starts");
            else
                await Assertions.Expect(browser.Page.Locator("#player-kpis")).Not.ToContainTextAsync("Tracked session starts");

            if (trackedTrendAvailable)
                await Assertions.Expect(browser.Page.Locator("#player-trend-title")).ToContainTextAsync("tracked session starts");
            else
                await Assertions.Expect(browser.Page.Locator("#player-trend-title")).ToHaveTextAsync("First and latest observations");

            if (trackedOverviewAvailable || trackedTrendAvailable)
            {
                await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToBeVisibleAsync();
                await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToContainTextAsync("only currently non-deleted servers");
                await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToContainTextAsync("Counts can change when a server is deleted; coverage is unknown");
            }
            else
                await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToBeHiddenAsync();
            Assert.Equal(0, await browser.Page.Locator("#player-top").CountAsync());
            lock (requests)
                Assert.DoesNotContain("/api/analytics/player/top", requests);
        }

        var chart = browser.Page.Locator($"#{page.ToLowerInvariant()}-trend");
        await Assertions.Expect(chart).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator($"#{page.ToLowerInvariant()}-trend-fallback")).ToBeHiddenAsync();
        await browser.Page.WaitForFunctionAsync($"() => !!Chart.getChart('{page.ToLowerInvariant()}-trend')");
        var labels = await chart.EvaluateAsync<string[]>("canvas => Chart.getChart(canvas).data.datasets.map(dataset => dataset.label)");
        Assert.Contains(page == "Player" ? "Latest seen" : "Average sampled occupancy", labels);
        if (page == "Player" && trackedTrendAvailable)
            Assert.Contains("Tracked session starts (coverage unknown)", labels);
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(null, "\u2014")]
    [InlineData(0, "\u2014")]
    [InlineData(1, "0")]
    public async Task GameOverviewDistinguishesZeroOccupancyFromMissingSamples(int? sampleCount, string expected)
    {
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin);
        await browser.Page.RouteAsync("**/api/Analytics/**", route =>
        {
            object response = new Uri(route.Request.Url).AbsolutePath.EndsWith("/game/overview", StringComparison.OrdinalIgnoreCase)
                ? new { serverCount = 1, avgPlayers = 0, peakPlayers = 0, uniquePlayers = 0, moderationActions = 0, statsSampleCount = sampleCount }
                : new { items = Array.Empty<object>(), series = Array.Empty<object>(), labels = Array.Empty<object>() };
            return route.FulfillAsync(new() { ContentType = "application/json", Body = JsonSerializer.Serialize(response) });
        });
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Analytics/Game").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#game-kpis .col").Filter(new() { HasText = "Average sampled occupancy" }).Locator(".h3")).ToHaveTextAsync(expected);
        await Assertions.Expect(browser.Page.Locator("#game-kpis .col").Filter(new() { HasText = "Peak sampled server occupancy" }).Locator(".h3")).ToHaveTextAsync(expected);
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task OccupancyGapsRemainUnknownWhenIndexed()
    {
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin);
        await browser.Page.RouteAsync("**/api/Analytics/**", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { items = Array.Empty<object>(), series = Array.Empty<object>(), labels = Array.Empty<object>() }),
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Analytics/Global").AbsoluteUri);
        var result = await browser.Page.EvaluateAsync<string>("""
            () => {
                const values = [0, 0, 10].map((value, i) => ({ bucketStartUtc: `2026-11-2${i + 1}T00:00:00Z`, value }));
                const comparison = [0, 5, 0].map((value, i) => ({ bucketStartUtc: values[i].bucketStartUtc, value }));
                const ts = Analytics.maskUnobservedOccupancy({
                    bucket: 'OneDay',
                    labels: values.map(v => v.bucketStartUtc),
                    points: values.map((v, i) => ({ bucketStartUtc: v.bucketStartUtc, statsSampleCount: [0, 1, null][i] })),
                    series: [
                        { key: 'avgPlayers', label: 'Average', values },
                        { key: 'avgPlayers_cmp1', role: 'comparison', label: 'Previous', values: comparison }
                    ]
                });
                const chart = Analytics.renderComparisonChart({
                    canvasId: 'global-trend', fallbackId: 'global-trend-fallback',
                    timeseries: ts, index100: true, chart: Chart.getChart('global-trend')
                });
                return JSON.stringify(chart.data.datasets.map(dataset => dataset.data));
            }
            """);
        Assert.Equal("[[null,0,100],[null,100,null]]", result);
        browser.AssertNoBrowserErrors();
    }

    private static object PlayerTrend(bool trackedDataAvailable)
    {
        var trend = Trend("newPlayers", "New Players", "activePlayers", "Active Players");
        if (!trackedDataAvailable)
            return trend;
        return new
        {
            bucket = "OneDay",
            labels = new[] { "2026-11-25T00:00:00Z" },
            points = new[] { new { bucketStartUtc = "2026-11-25T00:00:00Z", sessionStarts = 0 } },
            trackingCoverage = "unknown",
            series = new[]
            {
                new { key = "newPlayers", label = "New Players", values = new[] { new { bucketStartUtc = "2026-11-25T00:00:00Z", value = 2 } } },
                new { key = "activePlayers", label = "Active Players", values = new[] { new { bucketStartUtc = "2026-11-25T00:00:00Z", value = 3 } } },
            },
        };
    }

    private static object Trend(string firstKey, string firstLabel, string? secondKey = null, string? secondLabel = null)
    {
        var series = new List<object>
        {
            new { key = firstKey, label = firstLabel, values = new[] { new { bucketStartUtc = "2026-11-25T00:00:00Z", value = 2 } } },
        };
        if (secondKey is not null)
            series.Add(new { key = secondKey, label = secondLabel, values = new[] { new { bucketStartUtc = "2026-11-25T00:00:00Z", value = 3 } } });
        return new { bucket = "OneDay", labels = new[] { "2026-11-25T00:00:00Z" }, series };
    }
}
