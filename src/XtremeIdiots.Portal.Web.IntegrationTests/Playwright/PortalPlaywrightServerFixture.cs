using Microsoft.Playwright;

using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>
/// Shares only its Kestrel host. Role sessions lease isolated contexts from the assembly browser.
/// </summary>
public sealed class PortalPlaywrightServerFixture : IAsyncLifetime
{
    private PortalWebKestrelHost host = null!;
    private readonly BrowserRuntime? runtime;
    private TestDiagnosticScope? setupDiagnostics;
    private int disposed;

    public PortalPlaywrightServerFixture() { }

    internal PortalPlaywrightServerFixture(BrowserRuntime runtime)
    {
        this.runtime = runtime;
    }

    public Uri BaseAddress => host.BaseAddress;

    public async Task InitializeAsync()
    {
        var diagnostics = new TestDiagnosticScope("PortalPlaywright shared fixture initialization", GetType().FullName!, nameof(InitializeAsync));
        setupDiagnostics = diagnostics;
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        try
        {
            host = await PortalWebKestrelHost.CreateAsync().ConfigureAwait(false);
            diagnostics.Complete("Passed");
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("Shared browser initialization", exception);
            var cleanup = await CleanupAsync(diagnostics).ConfigureAwait(false);
            cleanup.AttachToPrimaryException(exception);
            diagnostics.Complete("InitializationFailed", exception.ToString());
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Creates an isolated browsing session impersonating the supplied role. Anonymous sends no
    /// authentication header.
    /// </summary>
    internal async Task<PlaywrightRoleSession> CreateRoleSessionAsync(PortalTestRole role, CancellationToken cancellationToken = default)
    {
        var ownsDiagnostics = TestDiagnosticScope.Current is null;
        var diagnostics = TestDiagnosticScope.Current ?? new TestDiagnosticScope($"Role session {role}", GetType().FullName!, nameof(CreateRoleSessionAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        if (setupDiagnostics is not null)
        {
            diagnostics.ImportApplicationLogs(setupDiagnostics);
        }

        var profile = TestRoles.ProfileFor(role);
        var headers = new Dictionary<string, string> { [TestDiagnosticScope.HeaderName] = diagnostics.Id };
        if (profile is not null)
        {
            headers[TestAuthenticationDefaults.HeaderName] = profile;
        }

        BrowserContextLease? lease = null;
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
            lease = await (runtime ?? AssemblyBrowserRuntime.Current).AcquireAsync(diagnostics, cancellationToken).ConfigureAwait(false);
            return await PlaywrightRoleSession.CreateAsync(lease, new BrowserNewContextOptions { ExtraHTTPHeaders = headers },
                host.BaseAddress, role.ToString(), ownsDiagnostics, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("Creating shared role context", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (lease is not null)
            {
                await cleanup.RunAsync("Releasing failed role lease", () => lease.DisposeAsync().AsTask()).ConfigureAwait(false);
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

    public async Task DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var diagnostics = new TestDiagnosticScope("PortalPlaywright shared fixture teardown", GetType().FullName!, nameof(DisposeAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        var cleanup = await CleanupAsync(diagnostics).ConfigureAwait(false);
        diagnostics.Complete(cleanup.HasFailures ? "CleanupFailed" : "Passed");
        if (diagnostics.HasErrors)
        {
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
        }

        cleanup.ThrowIfFailed();
    }

    private async Task<DiagnosticResourceCleanup> CleanupAsync(TestDiagnosticScope diagnostics)
    {
        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        if (host is not null)
        {
            await cleanup.RunAsync("Disposing shared application host", () => host.DisposeAsync().AsTask()).ConfigureAwait(false);
            host = null!;
        }

        return cleanup;
    }
}
