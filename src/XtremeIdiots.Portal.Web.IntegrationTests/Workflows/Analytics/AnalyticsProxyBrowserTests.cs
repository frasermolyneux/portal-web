using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.Analytics;

[Trait("Category", "Browser")]
public sealed class AnalyticsProxyBrowserTests
{
    [Theory]
    [InlineData("Global", true)]
    [InlineData("Global", false)]
    [InlineData("Game", true)]
    [InlineData("Game", false)]
    public async Task OccupancyChartDistinguishesSamplesThroughRepositoryProxy(string page, bool supported)
    {
        using var transport = new AnalyticsProxyHttpTests.AnalyticsTransport(resource =>
        {
            if (resource.EndsWith("/overview", StringComparison.OrdinalIgnoreCase))
                return page == "Game" && supported
                    ? /*lang=json,strict*/ """{"avgPlayers":0,"statsSampleCount":3}"""
                    : page == "Game" ? AnalyticsProxyHttpTests.Response("game/overview", false) : "{}";
            if (resource.EndsWith("/timeseries", StringComparison.OrdinalIgnoreCase))
                return AnalyticsProxyHttpTests.Response($"{page.ToLowerInvariant()}/timeseries", supported);
            return /*lang=json,strict*/ """{"items":[]}""";
        });
        var repository = AnalyticsProxyHttpTests.CreateRepository(transport);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin,
            services => AnalyticsProxyHttpTests.ConfigureServices(services, repository));

        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Analytics/{page}").AbsoluteUri);
        Assert.True(response?.Ok);
        var chartId = $"{page.ToLowerInvariant()}-trend";
        await browser.Page.WaitForFunctionAsync($"() => !!Chart.getChart('{chartId}')");
        var data = await browser.Page.Locator($"#{chartId}").EvaluateAsync<int?[]>(
            "canvas => Chart.getChart(canvas).data.datasets.find(dataset => dataset.label === 'Average sampled occupancy').data");
        Assert.Equal(supported ? [null, 0, 2] : [null, null, 2], data);
        if (page == "Game")
            await Assertions.Expect(browser.Page.Locator("#game-kpis .col").Filter(new() { HasText = "Average sampled occupancy" }).Locator(".h3"))
                .ToHaveTextAsync(supported ? "0" : "\u2014");
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task PlayerPageUsesTrackedMetricsOnlyWhenProvidedByRepository(bool supported)
    {
        using var transport = new AnalyticsProxyHttpTests.AnalyticsTransport(resource =>
        {
            if (resource.EndsWith("/overview", StringComparison.OrdinalIgnoreCase))
                return AnalyticsProxyHttpTests.Response("player/overview", supported);
            if (resource.EndsWith("/timeseries", StringComparison.OrdinalIgnoreCase))
                return AnalyticsProxyHttpTests.Response("player/timeseries", supported);
            return /*lang=json,strict*/ """{"items":[]}""";
        });
        var repository = AnalyticsProxyHttpTests.CreateRepository(transport);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin,
            services => AnalyticsProxyHttpTests.ConfigureServices(services, repository));

        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Analytics/Player").AbsoluteUri);
        Assert.True(response?.Ok);
        await Assertions.Expect(browser.Page.Locator("#player-kpis")).ToContainTextAsync("Latest seen in range");
        await Assertions.Expect(browser.Page.Locator("#player-trend-fallback")).ToBeHiddenAsync();
        await browser.Page.WaitForFunctionAsync("() => !!Chart.getChart('player-trend')");
        if (supported)
        {
            await Assertions.Expect(browser.Page.Locator("#player-kpis")).ToContainTextAsync("Tracked session starts");
            await Assertions.Expect(browser.Page.Locator("#player-kpis .col").Filter(new() { HasText = "Players in tracked sessions" }).Locator(".h3")).ToHaveTextAsync("0");
            await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToBeVisibleAsync();
            await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToContainTextAsync("only currently non-deleted servers");
            var data = await browser.Page.Locator("#player-trend").EvaluateAsync<int[]>(
                "canvas => Chart.getChart(canvas).data.datasets.find(dataset => dataset.label === 'Tracked session starts (coverage unknown)').data");
            Assert.Equal([0, 2], data);
        }
        else
        {
            await Assertions.Expect(browser.Page.Locator("#player-kpis")).Not.ToContainTextAsync("Tracked session starts");
            await Assertions.Expect(browser.Page.Locator("#player-tracking-coverage")).ToBeHiddenAsync();
            var labels = await browser.Page.Locator("#player-trend").EvaluateAsync<string[]>(
                "canvas => Chart.getChart(canvas).data.datasets.map(dataset => dataset.label)");
            Assert.DoesNotContain("Tracked session starts (coverage unknown)", labels);
        }

        browser.AssertNoBrowserErrors();
    }
}
