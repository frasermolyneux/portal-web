using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "Browser")]
public sealed class BrowserContextIsolationTests
{
    [Fact]
    public async Task RoleContexts_IsolateCookiesStorageAndAuthenticationWithoutOwningSharedBrowser()
    {
        var fixture = new PortalPlaywrightServerFixture();
        await fixture.InitializeAsync();
        IBrowser? browser = null;
        try
        {
            await using var admin = await fixture.CreateRoleSessionAsync(PortalTestRole.HeadAdmin);
            await using var anonymous = await fixture.CreateRoleSessionAsync(PortalTestRole.Anonymous);
            browser = admin.Page.Context.Browser;
            Assert.Same(browser, anonymous.Page.Context.Browser);
            Assert.NotSame(admin.Page.Context, anonymous.Page.Context);
            var url = new Uri(fixture.BaseAddress, "/Identity/Account/Login").AbsoluteUri;
            await admin.Page.GotoAsync(url);
            await anonymous.Page.GotoAsync(url);
            await admin.Page.Context.AddCookiesAsync([new Cookie { Name = "lifecycle-cookie", Value = "admin", Url = fixture.BaseAddress.AbsoluteUri }]);
            await admin.Page.EvaluateAsync("() => localStorage.setItem('lifecycle-storage', 'admin')");

            Assert.DoesNotContain(await anonymous.Page.Context.CookiesAsync(), cookie => cookie.Name == "lifecycle-cookie");
            Assert.Null(await anonymous.Page.EvaluateAsync<string?>("() => localStorage.getItem('lifecycle-storage')"));
            foreach (var session in new[] { admin, anonymous })
            {
                await session.Page.RouteAsync("**/lifecycle-identity", async route =>
                {
                    var headers = await route.Request.AllHeadersAsync();
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        ContentType = "text/plain",
                        Body = headers.GetValueOrDefault(TestAuthenticationDefaults.HeaderName.ToLowerInvariant(), "anonymous"),
                    });
                });
            }

            Assert.Equal(TestRoles.ProfileFor(PortalTestRole.HeadAdmin),
                await admin.Page.EvaluateAsync<string>("() => fetch('/lifecycle-identity').then(response => response.text())"));
            Assert.Equal("anonymous",
                await anonymous.Page.EvaluateAsync<string>("() => fetch('/lifecycle-identity').then(response => response.text())"));
            admin.AssertNoBrowserErrors();
            anonymous.AssertNoBrowserErrors();
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        Assert.NotNull(browser);
        Assert.True(browser.IsConnected);
        await using (var ordinary = await BrowserFixture.CreateAsync())
        {
            await using var another = await BrowserFixture.CreateAsync();
            Assert.Same(browser, ordinary.Page.Context.Browser);
            Assert.Same(browser, another.Page.Context.Browser);
            Assert.NotSame(ordinary.Host, another.Host);
            Assert.NotEqual(ordinary.Host.BaseAddress, another.Host.BaseAddress);
        }

        Assert.True(browser.IsConnected);
    }
}
