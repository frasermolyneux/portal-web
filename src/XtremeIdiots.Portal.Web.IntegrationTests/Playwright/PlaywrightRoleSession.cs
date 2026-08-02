using Microsoft.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>
/// A per-role Playwright browsing session: an isolated <see cref="IBrowserContext"/> (carrying the
/// role's authentication header) and a single <see cref="IPage"/>, plus the external-request routing
/// and browser-error collection shared across the scalable navigation tests.
/// </summary>
internal sealed class PlaywrightRoleSession : IAsyncDisposable
{
    private readonly IBrowserContext browserContext;
    private readonly Uri baseAddress;
    private readonly List<string> consoleErrors = [];
    private readonly List<string> failedSameOriginRequests = [];
    private readonly List<string> failedSameOriginResponses = [];
    private readonly List<string> pageErrors = [];
    private readonly List<string> unexpectedExternalRequests = [];

    private PlaywrightRoleSession(IBrowserContext browserContext, IPage page, Uri baseAddress)
    {
        this.browserContext = browserContext;
        this.baseAddress = baseAddress;
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

    public IPage Page { get; }

    public async static Task<PlaywrightRoleSession> CreateAsync(IBrowserContext browserContext, Uri baseAddress)
    {
        var page = await browserContext.NewPageAsync();
        var session = new PlaywrightRoleSession(browserContext, page, baseAddress);

        await browserContext.RouteAsync("**/*", async route =>
        {
            if (session.IsApplicationRequest(route.Request.Url))
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

            session.unexpectedExternalRequests.Add($"{route.Request.Method} {route.Request.Url}");
            await route.AbortAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);

        return session;
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
            uri.Scheme == baseAddress.Scheme &&
            uri.Host == baseAddress.Host &&
            uri.Port == baseAddress.Port;
    }
}
