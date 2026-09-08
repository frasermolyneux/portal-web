using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

internal sealed class BrowserFixture : IAsyncDisposable
{
    private readonly BrowserContextLease lease;
    private readonly BrowserDiagnosticCapture capture;
    private readonly TestDiagnosticScope diagnostics;
    private readonly bool ownsDiagnostics;
    private int disposed;

    private BrowserFixture(
        PortalWebKestrelHost host,
        BrowserContextLease lease,
        BrowserDiagnosticCapture capture,
        TestDiagnosticScope diagnostics,
        bool ownsDiagnostics)
    {
        Host = host;
        this.lease = lease;
        this.capture = capture;
        this.diagnostics = diagnostics;
        this.ownsDiagnostics = ownsDiagnostics;
    }

    public PortalWebKestrelHost Host { get; }
    public IPage Page => capture.Page;

    public static Task<BrowserFixture> CreateAsync(
        string? profile = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        return CreateWithRuntimeAsync(AssemblyBrowserRuntime.Current, profile, configureServices, cancellationToken);
    }

    internal async static Task<BrowserFixture> CreateWithRuntimeAsync(
        BrowserRuntime runtime,
        string? profile = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        var ownsDiagnostics = TestDiagnosticScope.Current is null;
        var diagnostics = TestDiagnosticScope.Current ?? new TestDiagnosticScope("BrowserFixture initialization", typeof(BrowserFixture).FullName!, nameof(CreateAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        PortalWebKestrelHost? host = null;
        BrowserContextLease? lease = null;
        BrowserDiagnosticCapture? capture = null;
        try
        {
            lease = await runtime.AcquireAsync(diagnostics, cancellationToken).ConfigureAwait(false);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.Stopping);
            host = await PortalWebKestrelHost.CreateAsync(configureServices, cancellationToken: linked.Token).ConfigureAwait(false);
            var headers = new Dictionary<string, string> { [TestDiagnosticScope.HeaderName] = diagnostics.Id };
            if (profile is not null)
            {
                headers[TestAuthenticationDefaults.HeaderName] = profile;
            }

            var browserContext = await lease.CreateContextAsync(new BrowserNewContextOptions { ExtraHTTPHeaders = headers }, linked.Token).ConfigureAwait(false);
            capture = new BrowserDiagnosticCapture(browserContext, host.BaseAddress, diagnostics, profile ?? "anonymous");
            lease.BeforeClose(capture.CaptureAsync);
            await capture.InitializeAsync().ConfigureAwait(false);
            linked.Token.ThrowIfCancellationRequested();
            return new BrowserFixture(host, lease, capture, diagnostics, ownsDiagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("BrowserFixture initialization", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (capture is not null)
            {
                await cleanup.RunAsync("Capturing failed browser initialization", capture.CaptureAsync).ConfigureAwait(false);
            }

            if (lease is not null)
            {
                await cleanup.RunAsync("Releasing failed browser lease", () => lease.DisposeAsync().AsTask()).ConfigureAwait(false);
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
        await cleanup.RunAsync("Releasing browser context lease", () => lease.DisposeAsync().AsTask()).ConfigureAwait(false);
        await cleanup.RunAsync("Disposing application host", () => Host.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (ownsDiagnostics)
        {
            diagnostics.Complete("Unattributed");
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
        }

        cleanup.ThrowIfFailed();
    }
}
