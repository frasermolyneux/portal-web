using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
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
        var requests = 0;
        await browser.Page.RouteAsync("**/Players/GetVisibleIntelligence", async route =>
        {
            Interlocked.Increment(ref requests);
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

            var redraw = await browser.Page.RunAndWaitForResponseAsync(
                () => browser.Page.EvaluateAsync("() => { $('#dataTable').DataTable().order([0, 'asc']).draw(); }"),
                r => new Uri(r.Url).AbsolutePath == "/Players/GetPlayersAjax");
            Assert.True(redraw.Ok);
            await Assertions.Expect(goodRow).ToContainTextAsync("Risk: 80");
            await Assertions.Expect(unavailableRow).ToContainTextAsync("Risk unavailable");
            await Assertions.Expect(status.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true })).ToHaveCountAsync(1);
            Assert.Equal(1, Volatile.Read(ref requests));
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

    [Fact]
    public async Task HistoryBatchEnrichesRowsDetachedByFilteringWhileRequestIsPending()
    {
        var player = new PlayerDtoBuilder()
            .WithIpAddress("203.0.113.10")
            .WithIpAddress("198.51.100.7")
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 80);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        var gate = new RequestGate(hold: true);
        var requests = 0;
        await browser.Page.RouteAsync("**/Players/GetIpHistory/**", async route =>
        {
            Interlocked.Increment(ref requests);
            await gate.WaitAsync();
            await route.ContinueAsync();
        });
        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details/{player.PlayerId}").AbsoluteUri,
                new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
            await Assertions.Expect(browser.Page.Locator(".risk-score-gauge .gauge-value")).ToHaveTextAsync("80");
            await browser.Page.Locator("#ipAddresses-tab").ClickAsync();
            await gate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await browser.Page.EvaluateAsync("() => { $('#ipAddressesTable').DataTable().search('203.0.113.10').draw(); }");
            await Assertions.Expect(browser.Page.Locator("#ipAddressesTable [data-history-ip='198.51.100.7']")).ToHaveCountAsync(0);

            var response = browser.Page.WaitForResponseAsync(r =>
                new Uri(r.Url).AbsolutePath.StartsWith("/Players/GetIpHistory/", StringComparison.Ordinal));
            gate.Release();
            Assert.True((await response).Ok);
            await Assertions.Expect(browser.Page.Locator("#ipAddressesTable [data-history-ip='203.0.113.10']")).ToContainTextAsync("Risk: 80");
            await browser.Page.EvaluateAsync("() => { $('#ipAddressesTable').DataTable().search('').draw(); }");
            await Assertions.Expect(browser.Page.Locator("#ipAddressesTable [data-history-ip='198.51.100.7']")).ToContainTextAsync("Risk: 80");
            Assert.Equal(1, Volatile.Read(ref requests));
        }
        finally
        {
            gate.Release();
        }
    }

    [Fact]
    public async Task CachedPageRestoreResumesInterruptedIpCardsWithoutReplacingAssociatedPlayers()
    {
        var player = new PlayerDtoBuilder().Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 80);
        scenario.RepositoryClient.Setup(x => x.Players.V1.GetPlayersWithIpAddress(
                player.IpAddress, 0, 100, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([player]))));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.AddInitScriptAsync("""
            (() => {
                const originalFetch = window.fetch;
                let requests = 0;
                window.fetch = (url, options) => {
                    if (String(url).includes('/IPAddresses/GetIntelligence')) {
                        document.documentElement.dataset.testIpRequests = String(++requests);
                        if (requests === 1) {
                            return new Promise((resolve, reject) =>
                                options.signal.addEventListener('abort', () => reject(new DOMException('Paused', 'AbortError')), { once: true }));
                        }
                    }
                    return originalFetch(url, options);
                };
            })();
            """);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/IPAddresses/Details?ipAddress={player.IpAddress}").AbsoluteUri,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(browser.Page.Locator("html")).ToHaveAttributeAsync("data-test-ip-requests", "1");
        await Assertions.Expect(browser.Page.Locator("#ip-intelligence-content")).ToContainTextAsync("Loading risk assessment");
        await Assertions.Expect(browser.Page.Locator("#ipPlayersTable")).ToContainTextAsync(player.Username);

        var restored = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.EvaluateAsync("""
                () => {
                    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
                }
                """),
            r => new Uri(r.Url).AbsolutePath == "/IPAddresses/GetIntelligence");
        Assert.True(restored.Ok);
        await Assertions.Expect(browser.Page.Locator("#ip-intelligence-content")).ToContainTextAsync("Risk Score: 80");
        await Assertions.Expect(browser.Page.Locator("#ipPlayersTable")).ToContainTextAsync(player.Username);
        await Assertions.Expect(browser.Page.Locator("html")).ToHaveAttributeAsync("data-test-ip-requests", "2");
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task CachedPageRestoreKeepsInactiveHistoryLazyAndResumesInterruptedActiveHistory()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 80);
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.AddInitScriptAsync("""
            (() => {
                const originalFetch = window.fetch;
                let requests = 0;
                document.addEventListener('DOMContentLoaded', () => {
                    document.documentElement.dataset.testHistoryRequests = String(requests);
                }, { once: true });
                window.fetch = (url, options) => {
                    if (String(url).includes('/Players/GetIpHistory/')) {
                        document.documentElement.dataset.testHistoryRequests = String(++requests);
                        if (requests === 1) {
                            return new Promise((resolve, reject) =>
                                options.signal.addEventListener('abort', () => reject(new DOMException('Paused', 'AbortError')), { once: true }));
                        }
                    }
                    return originalFetch(url, options);
                };
            })();
            """);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/Players/Details/{player.PlayerId}").AbsoluteUri,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await Assertions.Expect(browser.Page.Locator(".risk-score-gauge .gauge-value")).ToHaveTextAsync("80");
        await browser.Page.EvaluateAsync("""
            () => {
                window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
            }
            """);
        await Assertions.Expect(browser.Page.Locator("html")).ToHaveAttributeAsync("data-test-history-requests", "0");

        await browser.Page.Locator("#ipAddresses-tab").ClickAsync();
        await Assertions.Expect(browser.Page.Locator("html")).ToHaveAttributeAsync("data-test-history-requests", "1");
        var restored = await browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.EvaluateAsync("""
                () => {
                    window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                    window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
                }
                """),
            r => new Uri(r.Url).AbsolutePath.StartsWith("/Players/GetIpHistory/", StringComparison.Ordinal));
        Assert.True(restored.Ok);
        await Assertions.Expect(browser.Page.Locator("#ipAddressesTable")).ToContainTextAsync("Risk: 80");
        await Assertions.Expect(browser.Page.Locator("html")).ToHaveAttributeAsync("data-test-history-requests", "2");
    }
}
