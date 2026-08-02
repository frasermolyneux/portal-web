using Microsoft.Playwright;

using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>
/// Shared, expensive Playwright infrastructure for the scalable UI test suite: a single Kestrel
/// host and a single Chromium browser reused across every test in the <c>PortalPlaywright</c>
/// collection. Individual tests create a cheap per-role <see cref="PlaywrightRoleSession"/> (an
/// isolated browser context) rather than standing up their own host and browser.
/// </summary>
public sealed class PortalPlaywrightServerFixture : IAsyncLifetime
{
    private PortalWebKestrelHost host = null!;
    private IPlaywright playwright = null!;
    private IBrowser browser = null!;

    public Uri BaseAddress => host.BaseAddress;

    public async Task InitializeAsync()
    {
        host = await PortalWebKestrelHost.CreateAsync();
        playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
    }

    /// <summary>
    /// Creates an isolated browsing session impersonating the supplied role. Anonymous sends no
    /// authentication header.
    /// </summary>
    internal async Task<PlaywrightRoleSession> CreateRoleSessionAsync(PortalTestRole role)
    {
        var profile = TestRoles.ProfileFor(role);
        var browserContext = await browser.NewContextAsync(profile is null
            ? null
            : new BrowserNewContextOptions
            {
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    [TestAuthenticationDefaults.HeaderName] = profile,
                },
            });

        return await PlaywrightRoleSession.CreateAsync(browserContext, host.BaseAddress);
    }

    public async Task DisposeAsync()
    {
        try
        {
            if (browser is not null && browser.IsConnected)
            {
                await browser.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (PlaywrightException) when (browser is null || !browser.IsConnected)
        {
            // The browser process can exit before teardown under resource pressure.
        }
        finally
        {
            playwright?.Dispose();

            if (host is not null)
            {
                await host.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
