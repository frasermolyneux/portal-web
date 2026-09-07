using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

internal sealed class BrowserFixture : IAsyncDisposable
{
    private readonly IBrowser browser;
    private readonly IBrowserContext browserContext;
    private readonly IPlaywright playwright;
    private readonly BrowserDiagnosticCapture capture;
    private readonly TestDiagnosticScope diagnostics;
    private readonly bool ownsDiagnostics;
    private int disposed;

    private BrowserFixture(
        PortalWebKestrelHost host,
        IPlaywright playwright,
        IBrowser browser,
        IBrowserContext browserContext,
        BrowserDiagnosticCapture capture,
        TestDiagnosticScope diagnostics,
        bool ownsDiagnostics)
    {
        Host = host;
        this.playwright = playwright;
        this.browser = browser;
        this.browserContext = browserContext;
        this.capture = capture;
        this.diagnostics = diagnostics;
        this.ownsDiagnostics = ownsDiagnostics;
    }

    public PortalWebKestrelHost Host { get; }
    public IPage Page => capture.Page;

    public async static Task<BrowserFixture> CreateAsync(
        string? profile = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var ownsDiagnostics = TestDiagnosticScope.Current is null;
        var diagnostics = TestDiagnosticScope.Current ?? new TestDiagnosticScope("BrowserFixture initialization", typeof(BrowserFixture).FullName!, nameof(CreateAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        PortalWebKestrelHost? host = null;
        IPlaywright? playwright = null;
        IBrowser? browser = null;
        IBrowserContext? browserContext = null;
        BrowserDiagnosticCapture? capture = null;
        try
        {
            host = await PortalWebKestrelHost.CreateAsync(configureServices).ConfigureAwait(false);
            playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
            var headers = new Dictionary<string, string> { [TestDiagnosticScope.HeaderName] = diagnostics.Id };
            if (profile is not null)
            {
                headers[TestAuthenticationDefaults.HeaderName] = profile;
            }

            browserContext = await browser.NewContextAsync(new BrowserNewContextOptions { ExtraHTTPHeaders = headers }).ConfigureAwait(false);
            capture = new BrowserDiagnosticCapture(browserContext, host.BaseAddress, diagnostics, profile ?? "anonymous");
            await capture.InitializeAsync().ConfigureAwait(false);
            return new BrowserFixture(host, playwright, browser, browserContext, capture, diagnostics, ownsDiagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("BrowserFixture initialization", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (capture is not null)
            {
                await cleanup.RunAsync("Capturing failed browser initialization", capture.CaptureAsync).ConfigureAwait(false);
            }

            if (browserContext is not null)
            {
                await cleanup.RunAsync("Closing failed browser context", () => browserContext.DisposeAsync().AsTask()).ConfigureAwait(false);
            }

            if (browser is not null)
            {
                await cleanup.RunAsync("Closing failed browser", () => browser.DisposeAsync().AsTask()).ConfigureAwait(false);
            }

            if (playwright is not null)
            {
                cleanup.Run("Disposing failed Playwright", playwright.Dispose);
            }

            if (host is not null)
            {
                await cleanup.RunAsync("Disposing failed application host", () => host.DisposeAsync().AsTask()).ConfigureAwait(false);
            }

            cleanup.AttachToPrimaryException(exception);
            if (ownsDiagnostics)
            {
                diagnostics.Complete("InitializationFailed", exception.ToString());
                await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
            }

            throw;
        }
    }

    public void AssertNoBrowserErrors()
    {
        capture.AssertNoBrowserErrors();
    }

    public void AssertOnlyExpectedFailedRequest(string method, string absolutePath)
    {
        capture.AssertOnlyExpectedFailedRequest(method, absolutePath);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        using var activation = TestDiagnosticScope.Activate(diagnostics);
        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        await cleanup.RunAsync("Capturing browser diagnostics", capture.CaptureAsync).ConfigureAwait(false);
        await cleanup.RunAsync("Closing browser context", () => browserContext.DisposeAsync().AsTask(), IsExpectedDisconnect).ConfigureAwait(false);
        await cleanup.RunAsync("Closing browser", () => browser.DisposeAsync().AsTask(), IsExpectedDisconnect).ConfigureAwait(false);
        cleanup.Run("Disposing Playwright", playwright.Dispose);
        await cleanup.RunAsync("Disposing application host", () => Host.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (ownsDiagnostics)
        {
            diagnostics.Complete("Unattributed");
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
        }

        cleanup.ThrowIfFailed();
    }

    private bool IsExpectedDisconnect(PlaywrightException exception)
    {
        return !browser.IsConnected || exception.GetType().Name == "TargetClosedException";
    }
}
