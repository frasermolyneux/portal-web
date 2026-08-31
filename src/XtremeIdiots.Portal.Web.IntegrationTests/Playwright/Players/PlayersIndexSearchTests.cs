using Microsoft.Playwright;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.Players;

/// <summary>
/// Bug-hunting coverage for the Players Index page (<c>/Players</c>, <c>/Players/GameIndex/{game}</c>)
/// and the server-side DataTable AJAX endpoint (<c>POST /Players/GetPlayersAjax</c>). Unlike the
/// Details tests, the Index grid is populated by a client-side DataTable AJAX round-trip, so each test
/// waits for the <c>GetPlayersAjax</c> response before asserting on the rendered rows. The tests cover
/// the conditional Steam-ID column, the IP intelligence badges rendered by <c>formatIPAddress</c>, the
/// tag drop-down / tag column, and — most importantly for bug hunting — that the game-type, filter-type
/// and tag controls actually forward their values to the repository query (captured by
/// <see cref="PlayersIndexScenario"/>).
/// </summary>
public sealed class PlayersIndexSearchTests
{
    private static TagDto Tag(string name, string? tagHtml = null)
    {
        return new TagDto
        {
            TagId = Guid.NewGuid(),
            Name = name,
            TagHtml = tagHtml,
        };
    }

    private async static Task<IResponse> GotoIndexAndWaitAsync(BrowserFixture fixture, string relativePath)
    {
        var url = new Uri(fixture.Host.BaseAddress, relativePath).ToString();

        var ajaxResponse = await fixture.Page.RunAndWaitForResponseAsync(
            async () =>
            {
                var response = await fixture.Page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.DOMContentLoaded,
                });

                Assert.NotNull(response);
                Assert.True(response.Ok, $"{relativePath} returned {response.Status}.");
            },
            resp => resp.Url.Contains("GetPlayersAjax", StringComparison.OrdinalIgnoreCase));

        Assert.True(ajaxResponse.Status == 200, $"GetPlayersAjax for {relativePath} returned {ajaxResponse.Status}.");
        return ajaxResponse;
    }

    [Fact]
    public async Task Index_renders_a_row_per_player_with_details_links()
    {
        var playerOne = new PlayerDtoBuilder { Username = "AlphaPlayer", GameType = GameType.CallOfDuty4 }.Build();
        var playerTwo = new PlayerDtoBuilder { Username = "BravoPlayer", GameType = GameType.CallOfDuty4 }.Build();
        var scenario = new PlayersIndexScenario([playerOne, playerTwo]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        await fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{playerOne.PlayerId}']").WaitForAsync();

        Assert.Equal(1, await fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{playerOne.PlayerId}']").CountAsync());
        Assert.Equal(1, await fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{playerTwo.PlayerId}']").CountAsync());
    }

    public static TheoryData<GameType, bool> SteamColumnCases => new()
    {
        // gameType, expectSteamColumn (only CoD4x exposes the Steam ID column)
        { GameType.CallOfDuty4x, true },
        { GameType.CallOfDuty4, false },
    };

    [Theory]
    [MemberData(nameof(SteamColumnCases))]
    public async Task Steam_id_column_visible_only_for_cod4x(GameType gameType, bool expectSteamColumn)
    {
        var player = new PlayerDtoBuilder
        {
            Username = "SteamPlayer",
            GameType = gameType,
            SteamId = "76561198000000001",
        }.Build();
        var scenario = new PlayersIndexScenario([player]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, $"/Players/GameIndex/{gameType}");

        await fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{player.PlayerId}']").WaitForAsync();

        var steamHeaderCount = await fixture.Page
            .GetByRole(AriaRole.Columnheader, new PageGetByRoleOptions { Name = "Steam ID", Exact = true })
            .CountAsync();

        if (expectSteamColumn)
        {
            Assert.True(steamHeaderCount == 1, $"[{gameType}] expected the Steam ID column header, saw {steamHeaderCount}.");
            Assert.Equal(1, await fixture.Page.Locator("#dataTable tbody").GetByText("76561198000000001").CountAsync());
        }
        else
        {
            Assert.True(steamHeaderCount == 0, $"[{gameType}] did not expect the Steam ID column header, saw {steamHeaderCount}.");
        }
    }

    [Fact]
    public async Task Ip_column_shows_risk_proxy_and_vpn_badges()
    {
        var player = new PlayerDtoBuilder { Username = "RiskyPlayer", IpAddress = "203.0.113.10" }.Build();
        var scenario = new PlayersIndexScenario(
            [player],
            intelligence: new IpIntelligenceOptions { RiskScore = 90, IsProxy = true, IsVpn = true, ProxyType = "VPN" });

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        var ipCell = fixture.Page.Locator("#dataTable tbody tr").First.Locator("td").Nth(2);
        await ipCell.Locator(".badge").First.WaitForAsync();

        var cellText = await ipCell.InnerTextAsync();
        Assert.Contains("Risk: 90", cellText, StringComparison.Ordinal);
        Assert.Contains("Proxy", cellText, StringComparison.Ordinal);
        Assert.Contains("VPN", cellText, StringComparison.Ordinal);
        Assert.Equal(1, await ipCell.Locator(".badge.text-bg-danger", new() { HasTextString = "Risk: 90" }).CountAsync());
    }

    [Fact]
    public async Task Ip_column_omits_risk_badge_when_score_is_zero()
    {
        var player = new PlayerDtoBuilder { Username = "CleanPlayer", IpAddress = "203.0.113.10" }.Build();
        var scenario = new PlayersIndexScenario(
            [player],
            intelligence: new IpIntelligenceOptions { RiskScore = 0, IsProxy = false, IsVpn = false });

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        var ipCell = fixture.Page.Locator("#dataTable tbody tr").First.Locator("td").Nth(2);
        await ipCell.GetByRole(AriaRole.Link).First.WaitForAsync();

        var cellText = await ipCell.InnerTextAsync();
        Assert.DoesNotContain("Risk:", cellText, StringComparison.Ordinal);
        Assert.DoesNotContain("Proxy", cellText, StringComparison.Ordinal);
        Assert.DoesNotContain("VPN", cellText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Tag_filter_dropdown_lists_repository_tags()
    {
        var player = new PlayerDtoBuilder { Username = "TaggedPlayer" }.Build();
        var scenario = new PlayersIndexScenario(
            [player],
            tags: [Tag("VIP"), Tag("Watchlist")]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        var optionTexts = await fixture.Page.Locator("#filterPlayerTag option").AllInnerTextsAsync();
        Assert.Contains("All Tags", optionTexts);
        Assert.Contains("VIP", optionTexts);
        Assert.Contains("Watchlist", optionTexts);
    }

    [Fact]
    public async Task Tags_column_renders_badges_and_overflow_indicator()
    {
        var player = new PlayerDtoBuilder { Username = "MultiTagPlayer" }
            .WithTag("Alpha")
            .WithTag("Bravo")
            .WithTag("Charlie")
            .WithTag("Delta")
            .Build();
        var scenario = new PlayersIndexScenario([player]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        var tagsCell = fixture.Page.Locator("#dataTable tbody tr").First.Locator("td").Nth(1);
        await tagsCell.Locator(".badge").First.WaitForAsync();

        var cellText = await tagsCell.InnerTextAsync();
        Assert.Contains("Alpha", cellText, StringComparison.Ordinal);
        Assert.Contains("Bravo", cellText, StringComparison.Ordinal);
        Assert.Contains("Charlie", cellText, StringComparison.Ordinal);
        // Only the first three tags render as chips; the fourth collapses into a "+1" overflow badge.
        Assert.DoesNotContain("Delta", cellText, StringComparison.Ordinal);
        Assert.Contains("+1", cellText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Selecting_ip_filter_forwards_ipaddress_filter_to_repository()
    {
        var player = new PlayerDtoBuilder { Username = "FilterPlayer" }.Build();
        var scenario = new PlayersIndexScenario([player]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        var reload = await fixture.Page.RunAndWaitForResponseAsync(
            async () => await fixture.Page.Locator("#filterPlayersFilter").SelectOptionAsync([new SelectOptionValue { Value = "IpAddress" }]),
            resp => resp.Url.Contains("GetPlayersAjax", StringComparison.OrdinalIgnoreCase));

        Assert.True(reload.Status == 200, $"GetPlayersAjax reload returned {reload.Status}.");
        Assert.Equal(PlayersFilter.IpAddress, scenario.LastFilter);
    }

    [Fact]
    public async Task Selecting_tag_forwards_tag_filter_and_id_to_repository()
    {
        var tag = Tag("VIP");
        var player = new PlayerDtoBuilder { Username = "TagFilterPlayer" }.Build();
        var scenario = new PlayersIndexScenario([player], tags: [tag]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        var reload = await fixture.Page.RunAndWaitForResponseAsync(
            async () => await fixture.Page.Locator("#filterPlayerTag").SelectOptionAsync([new SelectOptionValue { Value = tag.TagId.ToString() }]),
            resp => resp.Url.Contains("GetPlayersAjax", StringComparison.OrdinalIgnoreCase));

        Assert.True(reload.Status == 200, $"GetPlayersAjax reload returned {reload.Status}.");
        Assert.Equal(PlayersFilter.Tag, scenario.LastFilter);
        Assert.Equal(tag.TagId.ToString(), scenario.LastFilterString);
    }

    [Fact]
    public async Task Game_index_rejects_moderator_outside_game_scope()
    {
        var player = new PlayerDtoBuilder { Username = "AnyGamePlayer", GameType = GameType.CallOfDuty2 }.Build();
        var scenario = new PlayersIndexScenario([player]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.Moderator, scenario.ConfigureServices);
        var response = await fixture.Page.GotoAsync(
            new Uri(fixture.Host.BaseAddress, "/Players/GameIndex/CallOfDuty2").AbsoluteUri,
            new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

        Assert.NotNull(response);
        Assert.NotEqual("/Players/GameIndex/CallOfDuty2", new Uri(fixture.Page.Url).AbsolutePath);
        Assert.Equal(0, scenario.GetPlayersCallCount);
    }

    [Fact]
    public async Task Index_landing_redirects_moderator_to_authorized_game_scope()
    {
        var player = new PlayerDtoBuilder { Username = "ScopedPlayer", GameType = GameType.CallOfDuty4 }.Build();
        var scenario = new PlayersIndexScenario([player]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.Moderator, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        Assert.EndsWith("/Players/GameIndex/CallOfDuty4", fixture.Page.Url, StringComparison.Ordinal);
        Assert.Equal(GameType.CallOfDuty4, scenario.LastGameType);
        Assert.Equal(1, await fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{player.PlayerId}']").CountAsync());
        Assert.Equal(0, await fixture.Page.Locator("#filterGameType option[value='']").CountAsync());
        Assert.Equal(0, await fixture.Page.Locator("#filterGameType option[value='CallOfDuty2']").CountAsync());
        Assert.Equal(1, await fixture.Page.Locator("#filterGameType option[value='CallOfDuty4']").CountAsync());
        Assert.Equal(1, await fixture.Page.Locator("#filterGameType option[value='CallOfDuty4x']").CountAsync());
    }
}
