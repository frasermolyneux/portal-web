using Microsoft.Playwright;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright.UserManagement;

public sealed class ManageProfileTabsUiTests
{
    [Fact]
    public async Task Manage_profile_defaults_to_overview_and_exposes_three_tabs()
    {
        var scenario = new UserManageProfileScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdminCod5, scenario.ConfigureServices);

        var response = await fixture.Page.GotoAsync(
            new Uri(fixture.Host.BaseAddress, $"/User/ManageProfile/{scenario.UserProfileId}").AbsoluteUri);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Contains("active", await fixture.Page.Locator("#overview-tab").GetAttributeAsync("class"), StringComparison.Ordinal);
        await Assertions.Expect(fixture.Page.Locator("#overview")).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.Locator("#permissions")).ToBeHiddenAsync();
        await Assertions.Expect(fixture.Page.Locator("#notifications")).ToBeHiddenAsync();
        Assert.Equal(3, await fixture.Page.Locator("#manageProfileTabs [role='tab']").CountAsync());
        Assert.Contains("route-test@example.invalid", await fixture.Page.Locator("#overview").InnerTextAsync(), StringComparison.Ordinal);
        fixture.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData("permissions", "#permissions-tab", "#permissions")]
    [InlineData("notifications", "#notifications-tab", "#notifications")]
    public async Task Manage_profile_direct_tab_route_activates_requested_tab(string tab, string tabSelector, string panelSelector)
    {
        var scenario = new UserManageProfileScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdminCod5, scenario.ConfigureServices);

        var response = await fixture.Page.GotoAsync(
            new Uri(fixture.Host.BaseAddress, $"/User/ManageProfile/{scenario.UserProfileId}?tab={tab}#{tab}").AbsoluteUri);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Contains("active", await fixture.Page.Locator(tabSelector).GetAttributeAsync("class"), StringComparison.Ordinal);
        await Assertions.Expect(fixture.Page.Locator(panelSelector)).ToBeVisibleAsync();
        await Assertions.Expect(fixture.Page.Locator("#overview")).ToBeHiddenAsync();
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Clicking_tabs_updates_url_and_visible_panel()
    {
        var scenario = new UserManageProfileScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdminCod5, scenario.ConfigureServices);
        await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, $"/User/ManageProfile/{scenario.UserProfileId}").AbsoluteUri);

        await fixture.Page.Locator("#notifications-tab").ClickAsync();

        await Assertions.Expect(fixture.Page.Locator("#notifications")).ToBeVisibleAsync();
        Assert.EndsWith("?tab=notifications#notifications", fixture.Page.Url, StringComparison.Ordinal);

        await fixture.Page.Locator("#permissions-tab").ClickAsync();

        await Assertions.Expect(fixture.Page.Locator("#permissions")).ToBeVisibleAsync();
        Assert.EndsWith("?tab=permissions#permissions", fixture.Page.Url, StringComparison.Ordinal);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Adding_permission_returns_to_permissions_tab_and_posts_selected_scope()
    {
        var scenario = new UserManageProfileScenario();
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.HeadAdminCod5, scenario.ConfigureServices);
        await fixture.Page.GotoAsync(
            new Uri(fixture.Host.BaseAddress, $"/User/ManageProfile/{scenario.UserProfileId}?tab=permissions#permissions").AbsoluteUri);

        await fixture.Page.Locator("#claimType").SelectOptionAsync(AdditionalPermission.GameServers_Write);
        await fixture.Page.Locator("#gameTypeSelect").SelectOptionAsync(GameType.CallOfDuty5.ToString());

        var postResponse = fixture.Page.WaitForResponseAsync(response =>
            response.Request.Method == "POST" &&
            new Uri(response.Url).AbsolutePath == "/User/CreateUserClaim");
        await fixture.Page.Locator("#createClaimForm button[type='submit']").ClickAsync();
        Assert.Equal(302, (await postResponse).Status);
        await fixture.Page.WaitForURLAsync("**?tab=permissions#permissions");

        Assert.EndsWith("?tab=permissions#permissions", fixture.Page.Url, StringComparison.Ordinal);
        Assert.Equal(1, scenario.CreateUserProfileClaimCallCount);
        var claim = Assert.Single(scenario.CreatedClaims);
        Assert.Equal(AdditionalPermission.GameServers_Write, claim.ClaimType);
        Assert.Equal(GameType.CallOfDuty5.ToString(), claim.ClaimValue);
        await Assertions.Expect(fixture.Page.Locator("#permissions")).ToBeVisibleAsync();
        fixture.AssertNoBrowserErrors();
    }
}
