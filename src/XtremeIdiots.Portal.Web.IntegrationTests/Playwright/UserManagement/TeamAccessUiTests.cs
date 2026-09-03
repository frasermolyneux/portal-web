using Microsoft.Playwright;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.UserManagement;

public sealed class TeamAccessUiTests
{
    private async static Task GotoTeamAccessAndWaitAsync(BrowserFixture fixture, string relativePath)
    {
        var ajaxResponse = await fixture.Page.RunAndWaitForResponseAsync(
            async () =>
            {
                var response = await fixture.Page.GotoAsync(
                    new Uri(fixture.Host.BaseAddress, relativePath).AbsoluteUri,
                    new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });

                Assert.NotNull(response);
                Assert.True(response.Ok, $"{relativePath} returned {response.Status}.");
            },
            response => response.Url.Contains("GetGameModeratorsAjax", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(200, ajaxResponse.Status);
    }

    [Fact]
    public async Task Cod4_team_access_renders_guidance_filters_and_moderator_permissions()
    {
        var scenario = new TeamAccessScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);

        await GotoTeamAccessAndWaitAsync(fixture, "/User/TeamAccess?gameType=CallOfDuty4");

        await Assertions.Expect(fixture.Page.GetByText("Alpha Moderator", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.GetByText("Moderators have deliberately limited permissions by default.")).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.Locator("#teamAccessSearch")).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.Locator("#filterGameType")).ToHaveValueAsync(GameType.CallOfDuty4.ToString());
        Assert.Equal(
            ["Call of Duty 4"],
            (await fixture.Page.Locator("#filterGameType option").AllInnerTextsAsync()).Select(value => value.Trim()));
        var navigationLink = fixture.Page.GetByTestId("nav-users-team-access-CallOfDuty4");
        await Assertions.Expect(navigationLink).ToContainTextAsync("Call of Duty 4 Team Access");
        Assert.Equal(1, await navigationLink.Locator("img").CountAsync());

        var tableText = await fixture.Page.Locator("#teamAccessTable tbody").InnerTextAsync();
        Assert.Contains("Inherited Moderator role", tableText, StringComparison.Ordinal);
        Assert.Contains("Map Rotations", tableText, StringComparison.Ordinal);
        Assert.Contains("Call of Duty 4x", tableText, StringComparison.Ordinal);
        Assert.Contains("COD4x Match Server", tableText, StringComparison.Ordinal);
        Assert.Equal(
            [GameType.CallOfDuty4, GameType.CallOfDuty4x],
            scenario.LastRequestedServerGameTypes);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Team_access_search_forwards_search_and_filters_rendered_rows()
    {
        var scenario = new TeamAccessScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);
        await GotoTeamAccessAndWaitAsync(fixture, "/User/TeamAccess?gameType=CallOfDuty4");

        var response = await fixture.Page.RunAndWaitForResponseAsync(
            async () => await fixture.Page.Locator("#teamAccessSearch").FillAsync("Bravo"),
            candidate => candidate.Url.Contains("GetGameModeratorsAjax", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(200, response.Status);
        await Assertions.Expect(fixture.Page.GetByText("Bravo Moderator", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.GetByText("Alpha Moderator", new() { Exact = true })).ToHaveCountAsync(0);
        Assert.Equal("Bravo", scenario.LastSearch);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Cod4x_team_access_url_redirects_to_canonical_cod4_page()
    {
        var scenario = new TeamAccessScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);

        await GotoTeamAccessAndWaitAsync(fixture, "/User/TeamAccess?gameType=CallOfDuty4x");

        Assert.Equal("/User/TeamAccess?gameType=CallOfDuty4", new Uri(fixture.Page.Url).PathAndQuery);
        Assert.Equal(GameType.CallOfDuty4, scenario.LastGameType);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Team_access_manage_profile_action_targets_permissions_tab()
    {
        var scenario = new TeamAccessScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);

        await GotoTeamAccessAndWaitAsync(fixture, "/User/TeamAccess?gameType=CallOfDuty4");

        var href = await fixture.Page.Locator("#teamAccessTable tbody a", new() { HasTextString = "Manage Profile" }).First.GetAttributeAsync("href");
        Assert.EndsWith("?tab=permissions#permissions", href, StringComparison.Ordinal);
    }
}
