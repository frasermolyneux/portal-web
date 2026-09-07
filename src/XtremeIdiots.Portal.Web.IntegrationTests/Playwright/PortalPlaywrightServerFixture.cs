using Microsoft.Playwright;

using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;
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
    private TestDiagnosticScope? setupDiagnostics;

    public Uri BaseAddress => host.BaseAddress;

    public async Task InitializeAsync()
    {
        var diagnostics = new TestDiagnosticScope("PortalPlaywright shared fixture initialization", GetType().FullName!, nameof(InitializeAsync));
        setupDiagnostics = diagnostics;
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        try
        {
            host = await PortalWebKestrelHost.CreateAsync().ConfigureAwait(false);
            playwright = await Microsoft.Playwright.Playwright.CreateAsync().ConfigureAwait(false);
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
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
    internal async Task<PlaywrightRoleSession> CreateRoleSessionAsync(PortalTestRole role)
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

        try
        {
            var browserContext = await browser.NewContextAsync(new BrowserNewContextOptions { ExtraHTTPHeaders = headers }).ConfigureAwait(false);
            return await PlaywrightRoleSession.CreateAsync(browserContext, host.BaseAddress, role.ToString(), ownsDiagnostics).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("Creating shared role context", exception);
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
        if (browser is not null)
        {
            await cleanup.RunAsync("Closing shared browser", () => browser.DisposeAsync().AsTask(), _ => !browser.IsConnected).ConfigureAwait(false);
            browser = null!;
        }

        if (playwright is not null)
        {
            cleanup.Run("Disposing shared Playwright", playwright.Dispose);
            playwright = null!;
        }

        if (host is not null)
        {
            await cleanup.RunAsync("Disposing shared application host", () => host.DisposeAsync().AsTask()).ConfigureAwait(false);
            host = null!;
        }

        return cleanup;
    }
}
