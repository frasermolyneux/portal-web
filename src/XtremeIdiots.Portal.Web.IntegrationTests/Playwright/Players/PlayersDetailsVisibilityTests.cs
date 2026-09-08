using Microsoft.Playwright;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

/// <summary>
/// Bug-hunting coverage for the Player Details page (<c>/Players/Details/{id}</c>). The Details view is
/// one of the richest server-rendered surfaces in the portal (778 lines of conditional Razor) and had
/// no prior UI coverage. Each test shapes a single player DTO via <see cref="PlayerDtoBuilder"/> and a
/// geo-intelligence response via <see cref="PlayersDetailsScenario"/>, then asserts on the concrete DOM
/// that the server produced. All assertions target content that is complete at
/// <see cref="WaitUntilState.DOMContentLoaded"/> (gauges, banners, counts, badges, table rows), so no
/// client-side data-table / analytics AJAX is required and browser-error assertions are intentionally
/// avoided (the map and analytics widgets fire best-effort async requests).
/// </summary>
[Trait("Category", "Browser")]
public sealed class PlayersDetailsVisibilityTests
{
    private static Uri DetailsUrl(BrowserFixture fixture, Guid playerId)
    {
        return new Uri(fixture.Host.BaseAddress, $"/Players/Details/{playerId}");
    }

    private async static Task<IResponse> GotoDetailsAsync(BrowserFixture fixture, Guid playerId)
    {
        var response = await fixture.Page.GotoAsync(DetailsUrl(fixture, playerId).ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        Assert.NotNull(response);
        Assert.True(response.Ok, $"/Players/Details returned {response.Status}.");
        return response;
    }

    public static TheoryData<int, string> RiskScores => new()
    {
        // riskScore, expected data-risk-level (thresholds: >=80 critical, >=50 high, >=25 medium, else low)
        { 0, "low" },
        { 24, "low" },
        { 25, "medium" },
        { 49, "medium" },
        { 50, "high" },
        { 79, "high" },
        { 80, "critical" },
        { 100, "critical" },
    };

    [Theory]
    [MemberData(nameof(RiskScores))]
    public async Task Risk_gauge_level_matches_risk_score(int riskScore, string expectedLevel)
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: riskScore);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var gauge = fixture.Page.Locator(".risk-score-gauge .gauge");
        await Assertions.Expect(gauge).ToHaveCountAsync(1);
        await Assertions.Expect(gauge).ToHaveAttributeAsync("data-risk-level", expectedLevel);
        await Assertions.Expect(gauge.Locator(".gauge-value")).ToHaveTextAsync(riskScore.ToString());
    }

    public static TheoryData<bool, bool> ProxyVpnFlags => new()
    {
        { false, false },
        { true, false },
        { false, true },
        { true, true },
    };

    [Theory]
    [MemberData(nameof(ProxyVpnFlags))]
    public async Task Proxy_and_vpn_badges_reflect_intelligence_flags(bool isProxy, bool isVpn)
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 10, isProxy: isProxy, isVpn: isVpn);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var intelligencePanel = fixture.Page.Locator(".risk-score-gauge").Locator("xpath=ancestor::div[contains(@class,'ibox-content')]");
        var proxyBadge = intelligencePanel.GetByText("Proxy", new() { Exact = true });
        var vpnBadge = intelligencePanel.GetByText("VPN", new() { Exact = true });

        await Assertions.Expect(proxyBadge).ToHaveCountAsync(isProxy ? 1 : 0);
        await Assertions.Expect(vpnBadge).ToHaveCountAsync(isVpn ? 1 : 0);
    }

    public static TheoryData<string, bool, string?, string?> BanCases => new()
    {
        // caseName, expectBanner, expectedStrongText, expectedQualifier
        { "permanent-ban", true, "Active Ban", "permanent" },
        { "active-temp-ban", true, "Active Temp Ban", "expires" },
        { "expired-temp-ban", false, null, null },
        { "ban-with-future-expiry", true, "Active Ban", "expires" },
    };

    [Theory]
    [MemberData(nameof(BanCases))]
    public async Task Active_ban_banner_reflects_admin_action_state(
        string caseName,
        bool expectBanner,
        string? expectedStrongText,
        string? expectedQualifier)
    {
        var builder = new PlayerDtoBuilder().WithIpAddress("203.0.113.10");

        switch (caseName)
        {
            case "permanent-ban":
                builder.WithAdminAction(AdminActionType.Ban, expires: null, adminDisplayName: "PermaAdmin");
                break;
            case "active-temp-ban":
                builder.WithAdminAction(AdminActionType.TempBan, expires: DateTime.UtcNow.AddDays(3), adminDisplayName: "TempAdmin");
                break;
            case "expired-temp-ban":
                builder.WithAdminAction(AdminActionType.TempBan, expires: DateTime.UtcNow.AddDays(-3));
                break;
            case "ban-with-future-expiry":
                builder.WithAdminAction(AdminActionType.Ban, expires: DateTime.UtcNow.AddDays(3));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown ban case.");
        }

        var player = builder.Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var banner = fixture.Page.Locator(".alert-danger[role='alert']");
        await Assertions.Expect(banner).ToHaveCountAsync(expectBanner ? 1 : 0);

        if (expectBanner)
        {
            await Assertions.Expect(banner).ToContainTextAsync(expectedStrongText!, new() { UseInnerText = true });
            await Assertions.Expect(banner).ToContainTextAsync(expectedQualifier!, new() { IgnoreCase = true, UseInnerText = true });
        }
    }

    [Fact]
    public async Task Admin_action_counts_render_only_present_types()
    {
        var player = new PlayerDtoBuilder()
            .WithIpAddress("203.0.113.10")
            .WithAdminAction(AdminActionType.Kick)
            .WithAdminAction(AdminActionType.Warning)
            .WithAdminAction(AdminActionType.Warning)
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var summary = fixture.Page.Locator(".player-admin-summary");
        await Assertions.Expect(summary).ToContainTextAsync("Kicks", new() { IgnoreCase = true, UseInnerText = true });
        await Assertions.Expect(summary).ToContainTextAsync("Warnings", new() { IgnoreCase = true, UseInnerText = true });
        // No bans/temp-bans/observations were created, so those labels must be absent.
        await Assertions.Expect(summary).Not.ToContainTextAsync("Bans", new() { IgnoreCase = true, UseInnerText = true });
        await Assertions.Expect(summary).Not.ToContainTextAsync("Observations", new() { IgnoreCase = true, UseInnerText = true });
        await Assertions.Expect(summary).Not.ToContainTextAsync("No admin actions", new() { IgnoreCase = true, UseInnerText = true });

        await Assertions.Expect(summary.Locator("xpath=.//div[contains(@class,'detail-field')][.//dt[contains(., 'Warnings')]]//dd"))
            .ToHaveTextAsync("2");
        await Assertions.Expect(summary.Locator("xpath=.//div[contains(@class,'detail-field')][.//dt[contains(., 'Kicks')]]//dd"))
            .ToHaveTextAsync("1");
    }

    [Fact]
    public async Task Admin_action_summary_shows_placeholder_when_empty()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var summary = fixture.Page.Locator(".player-admin-summary");
        await Assertions.Expect(summary).ToContainTextAsync("No admin actions", new() { IgnoreCase = true, UseInnerText = true });
        await Assertions.Expect(summary).Not.ToContainTextAsync("Kicks", new() { IgnoreCase = true, UseInnerText = true });
        await Assertions.Expect(summary).Not.ToContainTextAsync("Warnings", new() { IgnoreCase = true, UseInnerText = true });
    }

    [Fact]
    public async Task Tab_badges_match_rendered_row_counts()
    {
        var player = new PlayerDtoBuilder()
            .WithIpAddress("203.0.113.10")
            .WithAlias("AliasOne")
            .WithAlias("AliasTwo")
            .WithProtectedName("ProtectedOne")
            .WithProtectedName("ProtectedTwo")
            .WithProtectedName("ProtectedThree")
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        await Assertions.Expect(fixture.Page.Locator("#aliases-tab .badge")).ToHaveTextAsync("2");
        await Assertions.Expect(fixture.Page.Locator("#aliasesTable tbody tr")).ToHaveCountAsync(2);
        await Assertions.Expect(fixture.Page.Locator("#protectedNames-tab .badge")).ToHaveTextAsync("3");
        await Assertions.Expect(fixture.Page.Locator("#protectedNamesTable tbody tr")).ToHaveCountAsync(3);
    }

    [Fact]
    public async Task Ip_history_caps_at_ten_rows_and_shows_notice()
    {
        var builder = new PlayerDtoBuilder().WithIpAddress("203.0.113.10");
        for (var i = 0; i < 12; i++)
        {
            builder.WithIpAddress($"198.51.100.{i}", lastUsed: DateTime.UtcNow.AddMinutes(-i));
        }

        // The repository reports more IPs than were returned in the collection; the view caps the table
        // at 10 and surfaces a "Showing 10 of N" notice driven by the count property.
        builder.IpAddressCountOverride = 15;
        var player = builder.Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        await Assertions.Expect(fixture.Page.Locator("#ipAddresses-tab .badge")).ToHaveTextAsync("15");
        await Assertions.Expect(fixture.Page.Locator("#ipAddressesTable tbody tr")).ToHaveCountAsync(10);

        var notice = fixture.Page.Locator("#ipAddresses .alert-info");
        await Assertions.Expect(notice).ToHaveCountAsync(1);
        await Assertions.Expect(notice).ToContainTextAsync("Showing 10 of 15");
    }

    [Fact]
    public async Task Related_players_table_renders_status_badges()
    {
        var player = new PlayerDtoBuilder()
            .WithIpAddress("203.0.113.10")
            .WithRelatedPlayer("BannedNeighbour", "203.0.113.10", hasActiveBan: true, adminActionCount: 4, isCurrentIp: true, sharedIpCount: 3)
            .WithRelatedPlayer("CleanNeighbour", "198.51.100.7", hasActiveBan: false, adminActionCount: 0, isCurrentIp: false, sharedIpCount: 1)
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var table = fixture.Page.Locator("#relatedPlayersTable");
        await Assertions.Expect(table).ToHaveCountAsync(1);
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(2);

        await Assertions.Expect(table.GetByText("Banned", new() { Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(table.GetByText("OK", new() { Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(table.GetByText("Current", new() { Exact = true })).ToHaveCountAsync(1);
        await Assertions.Expect(table.GetByText("Historical", new() { Exact = true })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Related_players_section_hidden_when_none()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        await Assertions.Expect(fixture.Page.Locator("#relatedPlayersTable")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Intelligence_panel_hidden_when_no_geo_data()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, includeIntelligence: false);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        // The risk gauge and IP-address detail field are gated on Model.Intelligence != null.
        await Assertions.Expect(fixture.Page.Locator(".risk-score-gauge")).ToHaveCountAsync(0);
        // The IP Intelligence ibox itself is always present (only its populated body is conditional).
        await Assertions.Expect(fixture.Page.GetByText("IP Intelligence", new() { Exact = true })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Details_read_rejects_moderator_outside_game_scope()
    {
        var player = new PlayerDtoBuilder { GameType = GameType.CallOfDuty2, Username = "CrossGamePlayer" }
            .WithIpAddress("203.0.113.10")
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.Moderator, scenario.ConfigureServices);

        var response = await fixture.Page.GotoAsync(DetailsUrl(fixture, player.PlayerId).ToString(), new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded,
        });

        Assert.NotNull(response);
        Assert.NotEqual($"/Players/Details/{player.PlayerId}", new Uri(fixture.Page.Url).AbsolutePath);
        await Assertions.Expect(fixture.Page.GetByText("CrossGamePlayer")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Details_read_allows_moderator_with_matching_game_scope()
    {
        var player = new PlayerDtoBuilder { GameType = GameType.CallOfDuty4, Username = "ScopedPlayer" }
            .WithIpAddress("203.0.113.10")
            .Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.Moderator, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        await Assertions.Expect(fixture.Page.GetByText("ScopedPlayer").First).ToHaveCountAsync(1);
    }
}
