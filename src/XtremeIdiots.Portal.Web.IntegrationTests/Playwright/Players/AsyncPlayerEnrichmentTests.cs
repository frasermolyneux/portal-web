using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

[Trait("Category", "Browser")]
public sealed class AsyncPlayerEnrichmentTests
{
    [Fact]
    public async Task PlayerGridRowsAreUsableBeforeOptionalIntelligenceCompletes()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersIndexScenario([player], intelligence: new IpIntelligenceOptions { RiskScore = 80 });
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var gate = new RequestGate(hold: true);
        await browser.Page.RouteAsync("**/Players/GetVisibleIntelligence", async route =>
        {
            await gate.WaitAsync();
            await route.ContinueAsync();
        });
        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Players/Index").AbsoluteUri,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var row = browser.Page.Locator("#dataTable tbody tr");
            await Assertions.Expect(row.Locator($"a[href='/Players/Details/{player.PlayerId}']")).ToBeVisibleAsync();
            await Assertions.Expect(row).ToContainTextAsync("203.0.113.10");
            await Assertions.Expect(row).ToContainTextAsync("Risk pending");

            var response = browser.Page.WaitForResponseAsync(r => new Uri(r.Url).AbsolutePath == "/Players/GetVisibleIntelligence");
            gate.Release();
            Assert.True((await response).Ok);
            await Assertions.Expect(row).ToContainTextAsync("Risk: 80");
            browser.AssertNoBrowserErrors();
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task PlayerDetailsRenderImmediatelyAndHistoryEnrichmentWaitsForItsTab()
    {
        var player = new PlayerDtoBuilder()
            .WithIpAddress("203.0.113.10")
            .WithIpAddress("198.51.100.7")
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 80);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var gate = new RequestGate(hold: true);
        var historyRequests = 0;
        await browser.Page.RouteAsync("**/Players/GetIntelligence/**", async route =>
        {
            await gate.WaitAsync();
            await route.ContinueAsync();
        });
        browser.Page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath.StartsWith("/Players/GetIpHistory/", StringComparison.Ordinal))
                Interlocked.Increment(ref historyRequests);
        };
        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details/{player.PlayerId}").AbsoluteUri,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(browser.Page.Locator(".player-admin-summary")).ToBeVisibleAsync();
            await Assertions.Expect(browser.Page.Locator("[data-player-intelligence-url]")).ToContainTextAsync("Loading IP intelligence");
            Assert.Equal(0, Volatile.Read(ref historyRequests));

            var history = browser.Page.WaitForResponseAsync(r =>
                new Uri(r.Url).AbsolutePath.StartsWith("/Players/GetIpHistory/", StringComparison.Ordinal));
            await browser.Page.Locator("#ipAddresses-tab").ClickAsync();
            Assert.True((await history).Ok);
            await Assertions.Expect(browser.Page.Locator("#ipAddressesTable")).ToContainTextAsync("Risk: 80");
            Assert.Equal(1, Volatile.Read(ref historyRequests));

            var current = browser.Page.WaitForResponseAsync(r =>
                new Uri(r.Url).AbsolutePath.StartsWith("/Players/GetIntelligence/", StringComparison.Ordinal));
            gate.Release();
            Assert.True((await current).Ok);
            await Assertions.Expect(browser.Page.Locator(".risk-score-gauge .gauge-value")).ToHaveTextAsync("80");
        }
        finally
        {
            gate.Release();
        }
    }
}
