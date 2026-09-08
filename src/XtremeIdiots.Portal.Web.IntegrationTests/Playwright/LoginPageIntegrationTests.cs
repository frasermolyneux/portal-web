using Microsoft.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

[Trait("Category", "Browser")]
public class LoginPageIntegrationTests
{
    [Fact]
    public async Task LoginPage_RendersInChromium()
    {
        await using var fixture = await BrowserFixture.CreateAsync();

        var response = await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Login").AbsoluteUri);
        var loginButton = fixture.Page.GetByRole(AriaRole.Button, new PageGetByRoleOptions
        {
            Name = "XtremeIdiots Login",
        });

        Assert.NotNull(response);
        Assert.True(response.Ok);
        await Assertions.Expect(fixture.Page).ToHaveTitleAsync("Login - XI Portal");
        await Assertions.Expect(loginButton).ToBeVisibleAsync();
        fixture.AssertNoBrowserErrors();
    }
}
