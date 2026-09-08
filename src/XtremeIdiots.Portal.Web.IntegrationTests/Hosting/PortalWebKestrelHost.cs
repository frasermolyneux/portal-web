using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using XtremeIdiots.Portal.Web;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal sealed class PortalWebKestrelHost : IAsyncDisposable
{
    private readonly WebApplication app;
    private readonly PortalTestApplicationContext applicationContext;
    private readonly TestHostTimeouts timeouts;
    private readonly TestDiagnosticScope diagnostics;
    private readonly bool ownsDiagnostics;
    private int disposed;

    private PortalWebKestrelHost(WebApplication app, PortalTestApplicationContext applicationContext, Uri baseAddress,
        TestHostTimeouts timeouts, TestDiagnosticScope diagnostics, bool ownsDiagnostics)
    {
        this.app = app;
        this.applicationContext = applicationContext;
        BaseAddress = baseAddress;
        this.timeouts = timeouts;
        this.diagnostics = diagnostics;
        this.ownsDiagnostics = ownsDiagnostics;
    }

    public Uri BaseAddress { get; }

    public async static Task<PortalWebKestrelHost> CreateAsync(
        Action<IServiceCollection>? configureServices = null,
        TestHostTimeouts? timeouts = null,
        CancellationToken cancellationToken = default)
    {
        timeouts ??= new TestHostTimeouts();
        timeouts.Validate();
        using var startup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        startup.CancelAfter(timeouts.Startup);
        var ownsDiagnostics = TestDiagnosticScope.Current is null;
        var diagnostics = TestDiagnosticScope.Current ?? new TestDiagnosticScope("Kestrel host", typeof(PortalWebKestrelHost).FullName!, nameof(CreateAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        PortalTestApplicationContext? applicationContext = null;
        WebApplication? app = null;

        try
        {
            startup.Token.ThrowIfCancellationRequested();
            applicationContext = await PortalTestApplicationContext.CreateAsync(configureServices, startup.Token).ConfigureAwait(false);
            applicationContext.Builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = timeouts.Shutdown);
            applicationContext.Builder.WebHost.UseUrls("http://127.0.0.1:0");
            app = PortalWebApplication.Build(applicationContext.Builder);
            await PortalWebApplication.InitializeAsync(app, startup.Token).ConfigureAwait(false);
            await app.StartAsync(startup.Token).ConfigureAwait(false);
            startup.Token.ThrowIfCancellationRequested();

            var server = app.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = addresses?.SingleOrDefault(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal))
                ?? throw new InvalidOperationException("Kestrel did not expose a loopback HTTP address.");

            return new PortalWebKestrelHost(app, applicationContext, new Uri(address), timeouts, diagnostics, ownsDiagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("Starting Kestrel host", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (app is not null)
            {
                using var shutdown = new CancellationTokenSource(timeouts.Shutdown);
                await cleanup.RunAsync("Stopping failed Kestrel host", () => app.StopAsync(shutdown.Token)).ConfigureAwait(false);
                await cleanup.RunAsync("Disposing failed Kestrel host", () => app.DisposeAsync().AsTask()).ConfigureAwait(false);
            }

            if (applicationContext is not null)
            {
                await cleanup.RunAsync("Disposing failed Kestrel database", () => applicationContext.DisposeAsync().AsTask()).ConfigureAwait(false);
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
        await cleanup.RunAsync("Stopping Kestrel host", () => app.StopAsync(shutdown.Token)).ConfigureAwait(false);
        await cleanup.RunAsync("Disposing Kestrel host", () => app.DisposeAsync().AsTask()).ConfigureAwait(false);
        await cleanup.RunAsync("Disposing Kestrel database", () => applicationContext.DisposeAsync().AsTask()).ConfigureAwait(false);
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
