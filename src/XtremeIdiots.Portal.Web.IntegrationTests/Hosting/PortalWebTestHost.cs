using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XtremeIdiots.Portal.Web;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal sealed class PortalWebTestHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly PortalTestApplicationContext applicationContext;
    private readonly TestHostTimeouts timeouts;
    private readonly TestDiagnosticScope diagnostics;
    private readonly bool ownsDiagnostics;
    private int disposed;

    private PortalWebTestHost(WebApplication app, PortalTestApplicationContext applicationContext,
        TestHostTimeouts timeouts, TestDiagnosticScope diagnostics, bool ownsDiagnostics)
    {
        this.app = app;
        this.applicationContext = applicationContext;
        this.timeouts = timeouts;
        this.diagnostics = diagnostics;
        this.ownsDiagnostics = ownsDiagnostics;
        Client = app.GetTestClient();
        Client.DefaultRequestHeaders.Add(TestDiagnosticScope.HeaderName, diagnostics.Id);
    }

    public HttpClient Client { get; }

    public IServiceProvider Services => app.Services;

    public async static Task<PortalWebTestHost> CreateAsync(
        Action<IServiceCollection>? configureServices = null,
        TestHostTimeouts? timeouts = null,
        CancellationToken cancellationToken = default)
    {
        timeouts ??= new TestHostTimeouts();
        timeouts.Validate();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(timeouts.Startup);
        var ownsDiagnostics = TestDiagnosticScope.Current is null;
        var diagnostics = TestDiagnosticScope.Current ?? new TestDiagnosticScope("HTTP test host", typeof(PortalWebTestHost).FullName!, nameof(CreateAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        PortalTestApplicationContext? applicationContext = null;
        WebApplication? app = null;

        try
        {
            startup.Token.ThrowIfCancellationRequested();
            applicationContext = await PortalTestApplicationContext.CreateAsync(configureServices, startup.Token).ConfigureAwait(false);
            applicationContext.Builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = timeouts.Shutdown);
            applicationContext.Builder.WebHost.UseTestServer();
            app = PortalWebApplication.Build(applicationContext.Builder);
            await PortalWebApplication.InitializeAsync(app, startup.Token).ConfigureAwait(false);
            await app.StartAsync(startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();

            return new PortalWebTestHost(app, applicationContext, timeouts, diagnostics, ownsDiagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("Starting HTTP test host", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (app is not null)
            {
                using var shutdown = new CancellationTokenSource(timeouts.Shutdown);
                await cleanup.RunAsync("Stopping failed HTTP host", () => app.StopAsync(shutdown.Token)).ConfigureAwait(false);
                await cleanup.RunAsync("Disposing failed HTTP host", () => app.DisposeAsync().AsTask()).ConfigureAwait(false);
            }

            if (applicationContext is not null)
            {
                await cleanup.RunAsync("Disposing failed HTTP database", () => applicationContext.DisposeAsync().AsTask()).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        using var activation = TestDiagnosticScope.Activate(diagnostics);
        using var shutdown = new CancellationTokenSource(timeouts.Shutdown);
        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        cleanup.Run("Disposing HTTP client", Client.Dispose);
        await cleanup.RunAsync("Stopping HTTP host", () => app.StopAsync(shutdown.Token)).ConfigureAwait(false);
        await cleanup.RunAsync("Disposing HTTP host", () => app.DisposeAsync().AsTask()).ConfigureAwait(false);
        await cleanup.RunAsync("Disposing HTTP database", () => applicationContext.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (ownsDiagnostics)
        {
            diagnostics.Complete(cleanup.HasFailures ? "CleanupFailed" : "Passed");
            if (diagnostics.HasErrors)
            {
                await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
            }
        }

        cleanup.ThrowIfFailed();
    }
}
