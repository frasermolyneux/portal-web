using Microsoft.Playwright;
using System.Text.Json;
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
    public async Task MixedIntelligenceBatchRetainsSuccessfulRowsAndShowsOneRetry()
    {
        var unavailable = new PlayerDtoBuilder { Username = "Missing risk lookup", IpAddress = "203.0.113.11" }.Build();
        var available = new PlayerDtoBuilder { Username = "Successful lookup", IpAddress = "203.0.113.12" }.Build();
        var scenario = new PlayersIndexScenario([unavailable, available]);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var gate = new RequestGate(hold: true);
        await browser.Page.RouteAsync("**/Players/GetVisibleIntelligence", async route =>
        {
            await gate.WaitAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    data = new[]
                    {
                        new
                        {
                            playerId = unavailable.PlayerId,
                            ipAddress = unavailable.IpAddress,
                            proxyCheckRiskScore = (int?)null,
                            isProxy = false,
                            isVpn = false,
                            proxyType = "",
                            countryCode = "",
                            status = "unavailable",
                            expiresAtUtc = DateTime.UtcNow.AddSeconds(30)
                        },
                        new
                        {
                            playerId = available.PlayerId,
                            ipAddress = available.IpAddress,
                            proxyCheckRiskScore = (int?)80,
                            isProxy = true,
                            isVpn = false,
                            proxyType = "Hosting",
                            countryCode = "GB",
                            status = "available",
                            expiresAtUtc = DateTime.UtcNow.AddSeconds(30)
                        }
                    }
                })
            });
        });
        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Players/Index").AbsoluteUri,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var goodRow = browser.Page.Locator("#dataTable tbody tr").Filter(new LocatorFilterOptions { HasText = available.Username });
            await Assertions.Expect(goodRow).ToContainTextAsync("Risk pending");

            var response = browser.Page.WaitForResponseAsync(r => new Uri(r.Url).AbsolutePath == "/Players/GetVisibleIntelligence");
            gate.Release();
            Assert.True((await response).Ok);
            await Assertions.Expect(goodRow).ToContainTextAsync("Risk: 80");
            await Assertions.Expect(goodRow).Not.ToContainTextAsync("Risk unavailable");
            var unavailableRow = browser.Page.Locator("#dataTable tbody tr").Filter(new LocatorFilterOptions { HasText = unavailable.Username });
            await Assertions.Expect(unavailableRow).ToContainTextAsync("Risk unavailable");
            var status = browser.Page.Locator("[role='status']").Filter(new LocatorFilterOptions { HasText = "Some IP intelligence is unavailable." });
            await Assertions.Expect(status.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToHaveCountAsync(1);
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
