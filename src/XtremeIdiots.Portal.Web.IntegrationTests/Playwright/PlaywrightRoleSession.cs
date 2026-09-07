using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>An isolated per-role context on the shared browser, captured under the current test.</summary>
internal sealed class PlaywrightRoleSession : IAsyncDisposable
{
    private readonly IBrowserContext browserContext;
    private readonly BrowserDiagnosticCapture capture;
    private readonly TestDiagnosticScope diagnostics;
    private readonly bool ownsDiagnostics;
    private int disposed;

    private PlaywrightRoleSession(IBrowserContext browserContext, BrowserDiagnosticCapture capture, TestDiagnosticScope diagnostics, bool ownsDiagnostics)
    {
        this.browserContext = browserContext;
        this.capture = capture;
        this.diagnostics = diagnostics;
        this.ownsDiagnostics = ownsDiagnostics;
    }

    public IPage Page => capture.Page;

    public async static Task<PlaywrightRoleSession> CreateAsync(IBrowserContext browserContext, Uri baseAddress, string name = "role", bool ownsDiagnostics = false)
    {
        ownsDiagnostics |= TestDiagnosticScope.Current is null;
        var diagnostics = TestDiagnosticScope.Current ?? new TestDiagnosticScope("Role session initialization", typeof(PlaywrightRoleSession).FullName!, nameof(CreateAsync));
        var capture = new BrowserDiagnosticCapture(browserContext, baseAddress, diagnostics, name);
        try
        {
            await capture.InitializeAsync().ConfigureAwait(false);
            return new PlaywrightRoleSession(browserContext, capture, diagnostics, ownsDiagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.RecordError("Role session initialization", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            await cleanup.RunAsync("Capturing failed role session", capture.CaptureAsync).ConfigureAwait(false);
            await cleanup.RunAsync("Closing failed role context", () => browserContext.DisposeAsync().AsTask()).ConfigureAwait(false);
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

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        await cleanup.RunAsync("Capturing role session diagnostics", capture.CaptureAsync).ConfigureAwait(false);
        await cleanup.RunAsync("Closing role context", () => browserContext.DisposeAsync().AsTask()).ConfigureAwait(false);
        if (ownsDiagnostics)
        {
            diagnostics.Complete("Unattributed");
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
        }

        cleanup.ThrowIfFailed();
    }
}
