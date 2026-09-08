using Microsoft.AspNetCore.WebUtilities;
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
[Trait("Category", "Browser")]
public sealed partial class PlayersIndexSearchTests
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

    private async static Task<IResponse> GotoIndexAndWaitAsync(BrowserFixture fixture, string relativePath, GameType? gameType = null)
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
            response => IsPlayersResponse(response, gameType, PlayersFilter.UsernameAndGuid));

        Assert.True(ajaxResponse.Status == 200, $"GetPlayersAjax for {relativePath} returned {ajaxResponse.Status}.");
        await Assertions.Expect(fixture.Page.Locator("#dataTable tbody a[href^='/Players/Details/']").First).ToBeVisibleAsync();
        return ajaxResponse;
    }

    private static bool IsPlayersResponse(IResponse response, GameType? gameType, PlayersFilter filter, Guid? tagId = null)
    {
        var uri = new Uri(response.Url);
        var expectedPath = gameType is null ? "/Players/GetPlayersAjax" : $"/Players/GetPlayersAjax/{gameType}";
        if (response.Request.Method != "POST" || uri.AbsolutePath != expectedPath)
            return false;

        var query = QueryHelpers.ParseQuery(uri.Query);
        return query.TryGetValue("playersFilter", out var playersFilter) && playersFilter == filter.ToString() &&
            (tagId is null ? !query.ContainsKey("selectedTagId") :
                query.TryGetValue("selectedTagId", out var selectedTagId) && selectedTagId == tagId.ToString());
    }

    private async static Task ReloadAndWaitForDrawAsync(BrowserFixture fixture, Func<Task> action, PlayersFilter filter, Guid? tagId = null)
    {
        var table = fixture.Page.Locator("#dataTable");
        // The mock returns identical rows for each filter; wait for the matching draw, not stale row content.
        await table.EvaluateAsync("""
            table => {
                delete table.dataset.testDraw;
                $(table).on('draw.dt.testSync', (event, settings) => {
                    table.dataset.testDraw = String(settings.json.draw);
                });
            }
            """);
        try
        {
            var response = await fixture.Page.RunAndWaitForResponseAsync(
                action, candidate => IsPlayersResponse(candidate, null, filter, tagId));
            Assert.Equal(200, response.Status);
            var json = await response.JsonAsync();
            Assert.NotNull(json);
            await Assertions.Expect(table).ToHaveAttributeAsync("data-test-draw", json.Value.GetProperty("draw").ToString());
            await Assertions.Expect(table.Locator("tbody a[href^='/Players/Details/']").First).ToBeVisibleAsync();
        }
        finally
        {
            await table.EvaluateAsync("""
                table => {
                    $(table).off('draw.dt.testSync');
                    delete table.dataset.testDraw;
                }
                """);
        }
    }

    [Fact]
    public async Task Index_renders_a_row_per_player_with_details_links()
    {
        var playerOne = new PlayerDtoBuilder { Username = "AlphaPlayer", GameType = GameType.CallOfDuty4 }.Build();
        var playerTwo = new PlayerDtoBuilder { Username = "BravoPlayer", GameType = GameType.CallOfDuty4 }.Build();
        var scenario = new PlayersIndexScenario([playerOne, playerTwo]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        await Assertions.Expect(fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{playerOne.PlayerId}']")).ToHaveCountAsync(1);
        await Assertions.Expect(fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{playerTwo.PlayerId}']")).ToHaveCountAsync(1);
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
        await GotoIndexAndWaitAsync(fixture, $"/Players/GameIndex/{gameType}", gameType);
        await Assertions.Expect(fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{player.PlayerId}']")).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Steam ID", Exact = true }))
            .ToHaveCountAsync(expectSteamColumn ? 1 : 0);

        if (expectSteamColumn)
        {
            await Assertions.Expect(fixture.Page.Locator("#dataTable tbody").GetByText("76561198000000001")).ToHaveCountAsync(1);
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
        await Assertions.Expect(ipCell).ToContainTextAsync("Risk: 90", new() { UseInnerText = true });
        await Assertions.Expect(ipCell).ToContainTextAsync("Proxy", new() { UseInnerText = true });
        await Assertions.Expect(ipCell).ToContainTextAsync("VPN", new() { UseInnerText = true });
        await Assertions.Expect(ipCell.Locator(".badge.text-bg-danger", new() { HasTextString = "Risk: 90" })).ToHaveCountAsync(1);
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
        await Assertions.Expect(ipCell.GetByRole(AriaRole.Link).First).ToBeVisibleAsync();
        await Assertions.Expect(ipCell).Not.ToContainTextAsync("Risk:", new() { UseInnerText = true });
        await Assertions.Expect(ipCell).Not.ToContainTextAsync("Proxy", new() { UseInnerText = true });
        await Assertions.Expect(ipCell).Not.ToContainTextAsync("VPN", new() { UseInnerText = true });
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

        var options = fixture.Page.Locator("#filterPlayerTag option");
        await Assertions.Expect(options.Filter(new() { HasTextRegex = AllTagsOptionRegex() })).Not.ToHaveCountAsync(0);
        await Assertions.Expect(options.Filter(new() { HasTextRegex = VipOptionRegex() })).Not.ToHaveCountAsync(0);
        await Assertions.Expect(options.Filter(new() { HasTextRegex = WatchlistOptionRegex() })).Not.ToHaveCountAsync(0);
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
        await Assertions.Expect(tagsCell).ToContainTextAsync("Alpha", new() { UseInnerText = true });
        await Assertions.Expect(tagsCell).ToContainTextAsync("Bravo", new() { UseInnerText = true });
        await Assertions.Expect(tagsCell).ToContainTextAsync("Charlie", new() { UseInnerText = true });
        // Only the first three tags render as chips; the fourth collapses into a "+1" overflow badge.
        await Assertions.Expect(tagsCell).Not.ToContainTextAsync("Delta", new() { UseInnerText = true });
        await Assertions.Expect(tagsCell).ToContainTextAsync("+1", new() { UseInnerText = true });
    }

    [Fact]
    public async Task Selecting_ip_filter_forwards_ipaddress_filter_to_repository()
    {
        var player = new PlayerDtoBuilder { Username = "FilterPlayer" }.Build();
        var scenario = new PlayersIndexScenario([player]);

        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await GotoIndexAndWaitAsync(fixture, "/Players");

        await ReloadAndWaitForDrawAsync(fixture,
            async () => await fixture.Page.Locator("#filterPlayersFilter").SelectOptionAsync([new SelectOptionValue { Value = "IpAddress" }]),
            PlayersFilter.IpAddress);
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

        await ReloadAndWaitForDrawAsync(fixture,
            async () => await fixture.Page.Locator("#filterPlayerTag").SelectOptionAsync([new SelectOptionValue { Value = tag.TagId.ToString() }]),
            PlayersFilter.UsernameAndGuid, tag.TagId);
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
        await GotoIndexAndWaitAsync(fixture, "/Players", GameType.CallOfDuty4);

        Assert.EndsWith("/Players/GameIndex/CallOfDuty4", fixture.Page.Url, StringComparison.Ordinal);
        Assert.Equal(GameType.CallOfDuty4, scenario.LastGameType);
        await Assertions.Expect(fixture.Page.Locator($"#dataTable tbody a[href='/Players/Details/{player.PlayerId}']")).ToHaveCountAsync(1);
        await Assertions.Expect(fixture.Page.Locator("#filterGameType option[value='']")).ToHaveCountAsync(0);
        await Assertions.Expect(fixture.Page.Locator("#filterGameType option[value='CallOfDuty2']")).ToHaveCountAsync(0);
        await Assertions.Expect(fixture.Page.Locator("#filterGameType option[value='CallOfDuty4']")).ToHaveCountAsync(1);
        await Assertions.Expect(fixture.Page.Locator("#filterGameType option[value='CallOfDuty4x']")).ToHaveCountAsync(1);
    }

    [System.Text.RegularExpressions.GeneratedRegex("^All Tags$")]
    private static partial System.Text.RegularExpressions.Regex AllTagsOptionRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^VIP$")]
    private static partial System.Text.RegularExpressions.Regex VipOptionRegex();

    [System.Text.RegularExpressions.GeneratedRegex("^Watchlist$")]
    private static partial System.Text.RegularExpressions.Regex WatchlistOptionRegex();
}
