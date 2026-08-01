using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

internal sealed class BrowserFixture : IAsyncDisposable
{
    private readonly IBrowser browser;
    private readonly IBrowserContext browserContext;
    private readonly IPlaywright playwright;
    private readonly List<string> consoleErrors = [];
    private readonly List<string> failedSameOriginRequests = [];
    private readonly List<string> failedSameOriginResponses = [];
    private readonly List<string> pageErrors = [];
    private readonly List<string> unexpectedExternalRequests = [];

    private BrowserFixture(
        PortalWebKestrelHost host,
        IPlaywright playwright,
        IBrowser browser,
        IBrowserContext browserContext,
        IPage page)
    {
        Host = host;
        this.playwright = playwright;
        this.browser = browser;
        this.browserContext = browserContext;
        Page = page;

        Page.Console += (_, message) =>
        {
            if (string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                consoleErrors.Add($"{message.Text} ({message.Location})");
            }
        };
        Page.PageError += (_, error) => pageErrors.Add(error);
        Page.RequestFailed += (_, request) =>
        {
            if (IsApplicationRequest(request.Url))
            {
                failedSameOriginRequests.Add($"{request.Method} {request.Url}: {request.Failure}");
            }
        };
        Page.Response += (_, response) =>
        {
            if (response.Status >= 400 && IsApplicationRequest(response.Url))
            {
                failedSameOriginResponses.Add($"{response.Status} {response.Request.Method} {response.Url}");
            }
        };
    }

    public PortalWebKestrelHost Host { get; }

    public IPage Page { get; }

    public async static Task<BrowserFixture> CreateAsync(
        string? profile = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var host = await PortalWebKestrelHost.CreateAsync(configureServices);
        var playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
        });
        var browserContext = await browser.NewContextAsync(profile is null
            ? null
            : new BrowserNewContextOptions
            {
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    [TestAuthenticationDefaults.HeaderName] = profile,
                },
            });
        var page = await browserContext.NewPageAsync();
        var fixture = new BrowserFixture(host, playwright, browser, browserContext, page);

        await browserContext.RouteAsync("**/*", async route =>
        {
            if (fixture.IsApplicationRequest(route.Request.Url))
            {
                await route.ContinueAsync().ConfigureAwait(false);
                return;
            }

            if (IsBrowserLocalUrl(route.Request.Url))
            {
                await route.ContinueAsync().ConfigureAwait(false);
                return;
            }

            if (IsCosmeticExternalAsset(route.Request.Url))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 204 }).ConfigureAwait(false);
                return;
            }

            fixture.unexpectedExternalRequests.Add($"{route.Request.Method} {route.Request.Url}");
            await route.AbortAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        return fixture;
    }

    public void AssertNoBrowserErrors()
    {
        Assert.Empty(unexpectedExternalRequests);
        Assert.Empty(failedSameOriginRequests);
        Assert.Empty(failedSameOriginResponses);
        Assert.Empty(consoleErrors);
        Assert.Empty(pageErrors);
    }

    public async ValueTask DisposeAsync()
    {
        await browserContext.DisposeAsync().ConfigureAwait(false);
        await browser.DisposeAsync().ConfigureAwait(false);
        playwright.Dispose();
        await Host.DisposeAsync().ConfigureAwait(false);
    }

    private static bool IsCosmeticExternalAsset(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Host is "cdnjs.cloudflare.com" or "fonts.googleapis.com" or "fonts.gstatic.com";
    }

    private static bool IsBrowserLocalUrl(string value)
    {
        return value.StartsWith("about:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("blob:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsApplicationRequest(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == Host.BaseAddress.Scheme &&
            uri.Host == Host.BaseAddress.Host &&
            uri.Port == Host.BaseAddress.Port;
    }
}
