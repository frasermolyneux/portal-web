using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

[Trait("Category", "Browser")]
public class AuthorizationNavigationIntegrationTests
{
    [Fact]
    public async Task AnonymousUser_IsRedirectedToLogin()
    {
        await using var fixture = await BrowserFixture.CreateAsync();

        var response = await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Analytics").AbsoluteUri);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        Assert.Contains("/Identity/Login", fixture.Page.Url, StringComparison.Ordinal);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Moderator_CannotInvokeGlobalSettingsDirectly()
    {
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.Moderator);

        var response = await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/GlobalSettings").AbsoluteUri);

        Assert.NotNull(response);
        Assert.Equal(403, response.Status);
    }

    [Fact]
    public async Task Moderator_DoesNotSeeGlobalSettingsNavigation()
    {
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.Moderator);

        var response = await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Analytics").AbsoluteUri);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        await Assertions.Expect(fixture.Page.GetByTestId("nav-global-settings")).ToHaveCountAsync(0);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task SeniorAdmin_SeesGlobalSettingsNavigation()
    {
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin);

        var response = await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Analytics").AbsoluteUri);

        Assert.NotNull(response);
        Assert.True(response.Ok);
        await Assertions.Expect(fixture.Page.GetByTestId("nav-global-settings")).ToBeVisibleAsync();
        fixture.AssertNoBrowserErrors();
    }
}
