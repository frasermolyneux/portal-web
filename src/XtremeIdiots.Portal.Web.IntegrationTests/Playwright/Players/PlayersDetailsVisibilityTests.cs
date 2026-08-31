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
        Assert.Equal(1, await gauge.CountAsync());

        var level = await gauge.GetAttributeAsync("data-risk-level");
        Assert.Equal(expectedLevel, level);

        var value = (await gauge.Locator(".gauge-value").InnerTextAsync()).Trim();
        Assert.Equal(riskScore.ToString(), value);
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

        Assert.Equal(isProxy ? 1 : 0, await proxyBadge.CountAsync());
        Assert.Equal(isVpn ? 1 : 0, await vpnBadge.CountAsync());
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
        var bannerCount = await banner.CountAsync();

        if (expectBanner)
        {
            Assert.True(bannerCount == 1, $"[{caseName}] expected an active-ban banner, saw {bannerCount}.");
            var text = await banner.InnerTextAsync();
            Assert.Contains(expectedStrongText!, text, StringComparison.Ordinal);
            Assert.Contains(expectedQualifier!, text, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.True(bannerCount == 0, $"[{caseName}] expected no active-ban banner, saw {bannerCount}.");
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
        var summaryText = await summary.InnerTextAsync();

        Assert.Contains("Kicks", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Warnings", summaryText, StringComparison.OrdinalIgnoreCase);
        // No bans/temp-bans/observations were created, so those labels must be absent.
        Assert.DoesNotContain("Bans", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Observations", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("No admin actions", summaryText, StringComparison.OrdinalIgnoreCase);

        var warningsValue = await summary
            .Locator("xpath=.//div[contains(@class,'detail-field')][.//dt[contains(., 'Warnings')]]//dd")
            .InnerTextAsync();
        Assert.Equal("2", warningsValue.Trim());

        var kicksValue = await summary
            .Locator("xpath=.//div[contains(@class,'detail-field')][.//dt[contains(., 'Kicks')]]//dd")
            .InnerTextAsync();
        Assert.Equal("1", kicksValue.Trim());
    }

    [Fact]
    public async Task Admin_action_summary_shows_placeholder_when_empty()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        var summary = fixture.Page.Locator(".player-admin-summary");
        var summaryText = await summary.InnerTextAsync();

        Assert.Contains("No admin actions", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Kicks", summaryText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Warnings", summaryText, StringComparison.OrdinalIgnoreCase);
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

        var aliasBadge = (await fixture.Page.Locator("#aliases-tab .badge").InnerTextAsync()).Trim();
        var aliasRows = await fixture.Page.Locator("#aliasesTable tbody tr").CountAsync();
        Assert.Equal("2", aliasBadge);
        Assert.Equal(2, aliasRows);

        var protectedBadge = (await fixture.Page.Locator("#protectedNames-tab .badge").InnerTextAsync()).Trim();
        var protectedRows = await fixture.Page.Locator("#protectedNamesTable tbody tr").CountAsync();
        Assert.Equal("3", protectedBadge);
        Assert.Equal(3, protectedRows);
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

        var badge = (await fixture.Page.Locator("#ipAddresses-tab .badge").InnerTextAsync()).Trim();
        Assert.Equal("15", badge);

        var rows = await fixture.Page.Locator("#ipAddressesTable tbody tr").CountAsync();
        Assert.Equal(10, rows);

        var notice = fixture.Page.Locator("#ipAddresses .alert-info");
        Assert.Equal(1, await notice.CountAsync());
        Assert.Contains("Showing 10 of 15", await notice.InnerTextAsync(), StringComparison.Ordinal);
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
        Assert.Equal(1, await table.CountAsync());
        Assert.Equal(2, await table.Locator("tbody tr").CountAsync());

        Assert.Equal(1, await table.GetByText("Banned", new() { Exact = true }).CountAsync());
        Assert.Equal(1, await table.GetByText("OK", new() { Exact = true }).CountAsync());
        Assert.Equal(1, await table.GetByText("Current", new() { Exact = true }).CountAsync());
        Assert.Equal(1, await table.GetByText("Historical", new() { Exact = true }).CountAsync());
    }

    [Fact]
    public async Task Related_players_section_hidden_when_none()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, riskScore: 5);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        Assert.Equal(0, await fixture.Page.Locator("#relatedPlayersTable").CountAsync());
    }

    [Fact]
    public async Task Intelligence_panel_hidden_when_no_geo_data()
    {
        var player = new PlayerDtoBuilder().WithIpAddress("203.0.113.10").Build();
        var scenario = new PlayersDetailsScenario(player, includeIntelligence: false);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoDetailsAsync(fixture, player.PlayerId);

        // The risk gauge and IP-address detail field are gated on Model.Intelligence != null.
        Assert.Equal(0, await fixture.Page.Locator(".risk-score-gauge").CountAsync());
        // The IP Intelligence ibox itself is always present (only its populated body is conditional).
        Assert.Equal(1, await fixture.Page.GetByText("IP Intelligence", new() { Exact = true }).CountAsync());
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
        Assert.Equal(0, await fixture.Page.GetByText("CrossGamePlayer").CountAsync());
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

        Assert.Equal(1, await fixture.Page.GetByText("ScopedPlayer").First.CountAsync());
    }
}
