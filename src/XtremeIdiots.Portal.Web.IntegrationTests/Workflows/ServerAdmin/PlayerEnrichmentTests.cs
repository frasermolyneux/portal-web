using Microsoft.Playwright;
using System.Text.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Synchronization;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "Browser")]
public sealed class PlayerEnrichmentTests
{
    [Fact]
    public async Task FailedRosterRefreshRetainsPlayersButClearsSessionDuration()
    {
        var scenario = new PlayerModerationScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.LiveServerKick, scenario.ConfigureServices);
        var liveRequests = 0;
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayers/**", async route =>
        {
            if (Interlocked.Increment(ref liveRequests) == 1)
                await route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = LivePlayers("first-guid", "203.0.113.7", "First player") });
            else
                await route.FulfillAsync(new RouteFulfillOptions { Status = 503, ContentType = "application/json", Body = "{}" });
        });
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayerSessions/**", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = /*lang=json,strict*/ """{"data":[{"num":7,"guid":"first-guid","ipAddress":"203.0.113.7","currentSessionObservedSeconds":7260}]}"""
            }));
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayerEnrichment/**", route =>
            route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = Enrichment(scenario.PlayerId) }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{scenario.GameServerId}").AbsoluteUri);
        var row = browser.Page.Locator("#sd-playersTable tbody tr");
        await Assertions.Expect(row).ToContainTextAsync("2h 1m");

        var refresh = browser.Page.RunAndWaitForResponseAsync(
            () => browser.Page.EvaluateAsync("() => RconPlayers.reload()"), IsLivePlayersResponse);
        Assert.Equal(503, (await refresh).Status);
        await Assertions.Expect(row).ToContainTextAsync("First player");
        await Assertions.Expect(row).ToContainTextAsync("Unknown");
        await Assertions.Expect(row).Not.ToContainTextAsync("2h 1m");
    }

    [Fact]
    public async Task CurrentSessionAppearsAsynchronouslyAndIsClearedForReusedSlot()
    {
        var scenario = new PlayerModerationScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.LiveServerKick, scenario.ConfigureServices);
        var secondGate = new RequestGate(hold: true);
        var liveRequests = 0;
        var sessionRequests = 0;
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayers/**", async route =>
        {
            var first = Interlocked.Increment(ref liveRequests) == 1;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = first ? LivePlayers("first-guid", "203.0.113.7", "First player") : LivePlayers("second-guid", "203.0.113.8", "Current player")
            });
        });
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayerSessions/**", async route =>
        {
            var first = Interlocked.Increment(ref sessionRequests) == 1;
            if (!first)
                await secondGate.WaitAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = first
                    ? /*lang=json,strict*/ """{"data":[{"num":7,"guid":"first-guid","ipAddress":"203.0.113.7","currentSessionObservedSeconds":7260}]}"""
                    : /*lang=json,strict*/ """{"data":[]}"""
            });
        });
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayerEnrichment/**", async route =>
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = Enrichment(scenario.PlayerId)
            }));

        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{scenario.GameServerId}").AbsoluteUri);
            var row = browser.Page.Locator("#sd-playersTable tbody tr");
            await Assertions.Expect(row).ToContainTextAsync("2h 1m");
            var refresh = browser.Page.RunAndWaitForResponseAsync(
                () => browser.Page.EvaluateAsync("() => RconPlayers.reload()"), IsLivePlayersResponse);
            Assert.True((await refresh).Ok);
            await secondGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await Assertions.Expect(row).ToContainTextAsync("Current player");
            await Assertions.Expect(row).ToContainTextAsync("Unknown");
            await Assertions.Expect(row).Not.ToContainTextAsync("2h 1m");
            browser.AssertNoBrowserErrors();
        }
        finally
        {
            secondGate.Release();
        }
    }

    [Fact]
    public async Task LiveRosterWithoutConnectionEvidenceShowsUnknownSession()
    {
        var scenario = new PlayerModerationScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.LiveServerKick, scenario.ConfigureServices);
        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{scenario.GameServerId}").AbsoluteUri);
        Assert.True(response?.Ok);

        var row = browser.Page.Locator("#sd-playersTable tbody tr");
        await Assertions.Expect(row).ToContainTextAsync(PlayerModerationScenario.PlayerName);
        await Assertions.Expect(row).ToContainTextAsync("Unknown");
        await Assertions.Expect(browser.Page.GetByTestId("player-kick")).ToBeEnabledAsync();
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task LivePlayersRemainUsableWhileEnrichmentLoadsAndReuseCachedEnrichment()
    {
        var scenario = new PlayerModerationScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.LiveServerKick, scenario.ConfigureServices);
        var enrichmentGate = new RequestGate(hold: true);
        var liveGate = new RequestGate(hold: true);
        var liveRequests = 0;
        var enrichmentRequests = 0;
        await browser.Page.Clock.InstallAsync();
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayers/**", async route =>
        {
            if (Interlocked.Increment(ref liveRequests) == 2)
                await liveGate.WaitAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = LivePlayers("first-guid", "203.0.113.7", "Live name", 7260)
            });
        });
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayerEnrichment/**", async route =>
        {
            Interlocked.Increment(ref enrichmentRequests);
            await enrichmentGate.WaitAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = Enrichment(scenario.PlayerId)
            });
        });

        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{scenario.GameServerId}").AbsoluteUri);
            await enrichmentGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var row = browser.Page.Locator("#sd-playersTable tbody tr");
            await Assertions.Expect(row).ToContainTextAsync("Live name");
            await Assertions.Expect(row).ToContainTextAsync("Risk pending");
            await Assertions.Expect(row).ToContainTextAsync("2h 1m");
            await Assertions.Expect(browser.Page.GetByTestId("player-kick")).ToBeEnabledAsync();

            await browser.Page.Clock.FastForwardAsync(10000);
            await liveGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            await browser.Page.EvaluateAsync("() => { RconPlayers.reload(); RconPlayers.reload(); }");
            await browser.Page.Clock.FastForwardAsync(30000);
            Assert.Equal(2, Volatile.Read(ref liveRequests));
            Assert.Equal(1, Volatile.Read(ref enrichmentRequests));

            var liveResponse = browser.Page.WaitForResponseAsync(IsLivePlayersResponse);
            liveGate.Release();
            Assert.True((await liveResponse).Ok);
            var enrichmentResponse = browser.Page.WaitForResponseAsync(IsEnrichmentResponse);
            enrichmentGate.Release();
            Assert.True((await enrichmentResponse).Ok);
            await Assertions.Expect(row.Locator($"a[href='/Players/Details/{scenario.PlayerId}']")).ToHaveTextAsync("Live name");
            await Assertions.Expect(row).ToContainTextAsync("Risk: 80");

            var refresh = browser.Page.RunAndWaitForResponseAsync(
                () => browser.Page.EvaluateAsync("() => RconPlayers.reload()"), IsLivePlayersResponse);
            Assert.True((await refresh).Ok);
            await Assertions.Expect(row).ToContainTextAsync("Risk: 80");
            Assert.Equal(1, Volatile.Read(ref enrichmentRequests));
            browser.AssertNoBrowserErrors();
        }
        finally
        {
            liveGate.Release();
            enrichmentGate.Release();
        }
    }

    [Theory]
    [InlineData("second-guid", "203.0.113.8")]
    [InlineData("first-guid", "203.0.113.8")]
    public async Task EnrichmentCannotBeAppliedToAReusedSlotOrChangedIp(string nextGuid, string nextIp)
    {
        var scenario = new PlayerModerationScenario();
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.LiveServerKick, scenario.ConfigureServices);
        var firstGate = new RequestGate(hold: true);
        var secondGate = new RequestGate(hold: true);
        var liveRequests = 0;
        var enrichmentRequests = 0;
        var secondPlayerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayers/**", async route =>
        {
            var first = Interlocked.Increment(ref liveRequests) == 1;
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = first ? LivePlayers("first-guid", "203.0.113.7", "First player") : LivePlayers(nextGuid, nextIp, "Current player")
            });
        });
        await browser.Page.RouteAsync("**/ServerAdmin/GetRconPlayerEnrichment/**", async route =>
        {
            var first = Interlocked.Increment(ref enrichmentRequests) == 1;
            await (first ? firstGate : secondGate).WaitAsync();
            await route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "application/json",
                Body = Enrichment(first ? scenario.PlayerId : secondPlayerId)
            });
        });

        try
        {
            await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/ServerAdmin/ServerDetail/{scenario.GameServerId}").AbsoluteUri);
            await firstGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var refresh = browser.Page.RunAndWaitForResponseAsync(
                () => browser.Page.EvaluateAsync("() => RconPlayers.reload()"), IsLivePlayersResponse);
            Assert.True((await refresh).Ok);
            await secondGate.Entered.WaitAsync(TimeSpan.FromSeconds(10));
            var row = browser.Page.Locator("#sd-playersTable tbody tr");
            await Assertions.Expect(row).ToContainTextAsync("Current player");
            await Assertions.Expect(row).ToContainTextAsync("Unknown");
            var firstResponse = browser.Page.WaitForResponseAsync(IsEnrichmentResponse);
            firstGate.Release();
            Assert.True((await firstResponse).Ok);
            var secondResponse = browser.Page.WaitForResponseAsync(IsEnrichmentResponse);
            secondGate.Release();
            Assert.True((await secondResponse).Ok);
            await Assertions.Expect(row.Locator($"a[href='/Players/Details/{secondPlayerId}']")).ToHaveTextAsync("Current player");
            await Assertions.Expect(row.Locator($"a[href='/Players/Details/{scenario.PlayerId}']")).ToHaveCountAsync(0);
            await Assertions.Expect(row).ToContainTextAsync(nextIp);
            browser.AssertNoBrowserErrors();
        }
        finally
        {
            firstGate.Release();
            secondGate.Release();
        }
    }

    private static bool IsLivePlayersResponse(IResponse response)
    {
        return new Uri(response.Url).AbsolutePath.Contains("/ServerAdmin/GetRconPlayers/", StringComparison.Ordinal);
    }

    private static bool IsEnrichmentResponse(IResponse response)
    {
        return new Uri(response.Url).AbsolutePath.Contains("/ServerAdmin/GetRconPlayerEnrichment/", StringComparison.Ordinal);
    }

    private static string LivePlayers(string playerGuid, string ipAddress, string name, int? currentSessionObservedSeconds = null)
    {
        return JsonSerializer.Serialize(new
        {
            data = new[] { new { num = 7, name, guid = playerGuid, ipAddress, rate = 25000, currentSessionObservedSeconds } }
        });
    }

    private static string Enrichment(Guid playerId)
    {
        return JsonSerializer.Serialize(new
        {
            playerId,
            profileStatus = "available",
            countryCode = "GB",
            proxyCheckRiskScore = 80,
            isProxy = true,
            isVpn = false,
            proxyType = "Proxy",
            ipIntelligenceStatus = "available",
            expiresAtUtc = DateTime.UtcNow.AddMinutes(10)
        });
    }
}
