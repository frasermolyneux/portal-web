using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.UserManagement;

[Trait("Category", "Browser")]
public sealed partial class TeamAccessUiTests
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
            response => IsTeamAccessResponse(response, string.Empty));

        Assert.Equal(200, ajaxResponse.Status);
        await Assertions.Expect(fixture.Page.GetByText("Alpha Moderator", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.Locator("#teamAccessSearch")).ToBeVisibleAsync();
    }

    private static bool IsTeamAccessResponse(IResponse response, string search)
    {
        var uri = new Uri(response.Url);
        if (response.Request.Method != "POST" || uri.AbsolutePath != "/User/GetGameModeratorsAjax")
            return false;

        var query = QueryHelpers.ParseQuery(uri.Query);
        if (!query.TryGetValue("gameType", out var gameType) || gameType != GameType.CallOfDuty4.ToString() ||
            response.Request.PostData is not { } postData)
            return false;

        using var json = JsonDocument.Parse(postData);
        return json.RootElement.GetProperty("search").GetProperty("value").GetString() == search;
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
        await Assertions.Expect(fixture.Page.Locator("#filterGameType option")).ToHaveTextAsync(["Call of Duty 4"]);
        var navigationLink = fixture.Page.GetByTestId("nav-users-team-access-CallOfDuty4");
        await Assertions.Expect(navigationLink).ToContainTextAsync("Call of Duty 4 Team Access");
        await Assertions.Expect(navigationLink.Locator("img")).ToHaveCountAsync(1);

        var table = fixture.Page.Locator("#teamAccessTable tbody");
        await Assertions.Expect(table).ToContainTextAsync("Inherited Moderator role", new() { UseInnerText = true });
        await Assertions.Expect(table).ToContainTextAsync("Map Rotations", new() { UseInnerText = true });
        await Assertions.Expect(table).ToContainTextAsync("Call of Duty 4x", new() { UseInnerText = true });
        await Assertions.Expect(table).ToContainTextAsync("COD4x Match Server", new() { UseInnerText = true });
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
            candidate => IsTeamAccessResponse(candidate, "Bravo"));

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

        await Assertions.Expect(fixture.Page).ToHaveURLAsync(new Uri(fixture.Host.BaseAddress, "/User/TeamAccess?gameType=CallOfDuty4").AbsoluteUri);
        Assert.Equal(GameType.CallOfDuty4, scenario.LastGameType);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Team_access_manage_profile_action_targets_permissions_tab()
    {
        var scenario = new TeamAccessScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdmin, scenario.ConfigureServices);

        await GotoTeamAccessAndWaitAsync(fixture, "/User/TeamAccess?gameType=CallOfDuty4");

        await Assertions.Expect(fixture.Page.Locator("#teamAccessTable tbody a", new() { HasTextString = "Manage Profile" }).First)
            .ToHaveAttributeAsync("href", PermissionsTabLinkRegex());
    }

    [GeneratedRegex(@"\?tab=permissions#permissions$")]
    private static partial Regex PermissionsTabLinkRegex();
}
