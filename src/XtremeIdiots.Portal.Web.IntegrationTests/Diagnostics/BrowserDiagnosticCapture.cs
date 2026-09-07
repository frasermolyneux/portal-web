using Microsoft.Playwright;
using System.Collections.Concurrent;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

internal sealed class BrowserDiagnosticCapture
{
    private readonly IBrowserContext context;
    private readonly Uri baseAddress;
    private readonly TestDiagnosticScope scope;
    private readonly string directory;
    private readonly BoundedDiagnosticBuffer console = new();
    private readonly BoundedDiagnosticBuffer network = new();
    private readonly BoundedDiagnosticBuffer pageErrors = new();
    private readonly ConcurrentQueue<string> consoleErrors = new();
    private readonly ConcurrentQueue<string> failedRequests = new();
    private readonly ConcurrentQueue<string> failedResponses = new();
    private readonly ConcurrentQueue<string> scriptErrors = new();
    private readonly ConcurrentQueue<string> unexpectedExternalRequests = new();
    private readonly ConcurrentQueue<IPage> pages = new();
    private bool traceStarted;
    private int captured;

    internal BrowserDiagnosticCapture(IBrowserContext context, Uri baseAddress, TestDiagnosticScope scope, string name)
    {
        this.context = context;
        this.baseAddress = baseAddress;
        this.scope = scope;
        directory = scope.NextBrowserDirectory(name);
        context.Page += (_, page) => Attach(page);
    }

    internal IPage Page { get; private set; } = null!;

    internal async Task InitializeAsync()
    {
        await scope.TryAsync("Starting Playwright trace", async () =>
        {
            await context.Tracing.StartAsync(new TracingStartOptions
            {
                Screenshots = true,
                Snapshots = true,
                Sources = false,
                Title = scope.DisplayName,
            }).ConfigureAwait(false);
            traceStarted = true;
        }).ConfigureAwait(false);

        Page = await context.NewPageAsync().ConfigureAwait(false);
        await context.RouteAsync("**/*", async route =>
        {
            if (IsApplicationRequest(route.Request.Url) || IsBrowserLocalUrl(route.Request.Url))
            {
                await route.ContinueAsync().ConfigureAwait(false);
                return;
            }

            if (IsCosmeticExternalAsset(route.Request.Url))
            {
                await route.FulfillAsync(new RouteFulfillOptions { Status = 204 }).ConfigureAwait(false);
                return;
            }

            var request = $"{route.Request.Method} {route.Request.Url}";
            unexpectedExternalRequests.Enqueue(request);
            network.Add($"{DateTimeOffset.UtcNow:O} Unexpected external request: {request}");
            await route.AbortAsync().ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    internal void AssertNoBrowserErrors()
    {
        Assert.Empty(unexpectedExternalRequests);
        Assert.Empty(failedRequests);
        Assert.Empty(failedResponses);
        Assert.Empty(consoleErrors);
        Assert.Empty(scriptErrors);
    }

    internal void AssertOnlyExpectedFailedRequest(string method, string absolutePath)
    {
        Assert.Empty(unexpectedExternalRequests);
        var failure = Assert.Single(failedRequests);
        Assert.StartsWith($"{method} {new Uri(baseAddress, absolutePath).AbsoluteUri}?", failure, StringComparison.Ordinal);
        Assert.EndsWith("net::ERR_ABORTED", failure, StringComparison.Ordinal);
        Assert.Empty(failedResponses);
        Assert.Empty(consoleErrors);
        Assert.Empty(scriptErrors);
    }

    internal async Task CaptureAsync()
    {
        if (Interlocked.Exchange(ref captured, 1) != 0)
        {
            return;
        }

        scope.Try("Creating browser diagnostic directory", () => Directory.CreateDirectory(directory));
        var number = 0;
        foreach (var page in pages)
        {
            var prefix = Path.Combine(directory, $"page-{++number:D3}");
            await scope.TryAsync($"Capturing screenshot {prefix}", async () =>
                await page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = prefix + ".png",
                    FullPage = true,
                    Timeout = 5000,
                }).ConfigureAwait(false)).ConfigureAwait(false);
            await scope.TryAsync($"Capturing HTML {prefix}", async () =>
                scope.WriteText(prefix + ".html", await page.ContentAsync().ConfigureAwait(false))).ConfigureAwait(false);
        }

        if (traceStarted)
        {
            await scope.TryAsync("Saving Playwright trace", () =>
                context.Tracing.StopAsync(new TracingStopOptions { Path = Path.Combine(directory, "trace.zip") })).ConfigureAwait(false);
        }

        scope.WriteText(Path.Combine(directory, "console.log"), console.Snapshot());
        scope.WriteText(Path.Combine(directory, "network.log"), network.Snapshot());
        scope.WriteText(Path.Combine(directory, "page-errors.log"), pageErrors.Snapshot());
    }

    private void Attach(IPage page)
    {
        pages.Enqueue(page);
        page.Console += (_, message) =>
        {
            var text = $"{message.Text} ({message.Location})";
            console.Add($"{DateTimeOffset.UtcNow:O} [{message.Type}] {text}");
            if (ReferenceEquals(page, Page) && string.Equals(message.Type, "error", StringComparison.OrdinalIgnoreCase))
            {
                consoleErrors.Enqueue(text);
            }
        };
        page.PageError += (_, error) =>
        {
            if (ReferenceEquals(page, Page))
            {
                scriptErrors.Enqueue(error);
            }

            pageErrors.Add($"{DateTimeOffset.UtcNow:O} {error}");
        };
        page.Request += (_, request) => network.Add($"{DateTimeOffset.UtcNow:O} Request {request.Method} {request.Url}");
        page.RequestFailed += (_, request) =>
        {
            var text = $"{request.Method} {request.Url}: {request.Failure}";
            network.Add($"{DateTimeOffset.UtcNow:O} Failed {text}");
            if (ReferenceEquals(page, Page) && IsApplicationRequest(request.Url))
            {
                failedRequests.Enqueue(text);
            }
        };
        page.Response += (_, response) =>
        {
            var text = $"{response.Status} {response.Request.Method} {response.Url}";
            network.Add($"{DateTimeOffset.UtcNow:O} Response {text}");
            if (ReferenceEquals(page, Page) && response.Status >= 400 && IsApplicationRequest(response.Url))
            {
                failedResponses.Enqueue(text);
            }
        };
    }

    private bool IsApplicationRequest(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            uri.Scheme == baseAddress.Scheme && uri.Host == baseAddress.Host && uri.Port == baseAddress.Port;
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
}
