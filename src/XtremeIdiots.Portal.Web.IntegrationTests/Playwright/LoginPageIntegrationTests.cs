using Microsoft.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

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
        Assert.Equal("Login - XI Portal", await fixture.Page.TitleAsync());
        Assert.True(await loginButton.IsVisibleAsync());
        fixture.AssertNoBrowserErrors();
    }
}
