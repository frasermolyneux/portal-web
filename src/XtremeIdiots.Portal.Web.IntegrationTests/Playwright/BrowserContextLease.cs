using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

internal sealed class BrowserContextLease(BrowserRuntime runtime, TestDiagnosticScope diagnostics) : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private IBrowserContext? context;
    private Func<Task>? beforeClose;
    private Task? disposal;
    private bool initialized;
    private bool disposed;

    internal void BeforeClose(Func<Task> capture)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            beforeClose = capture;
        }
    }

    internal async Task<IBrowserContext> CreateContextAsync(BrowserNewContextOptions options, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (initialized)
            {
                throw new InvalidOperationException("A browser lease can create only one context.");
            }

            initialized = true;
        }

        try
        {
            await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                lock (sync)
                {
                    ObjectDisposedException.ThrowIf(disposed, this);
                }

                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, runtime.Stopping);
                linked.Token.ThrowIfCancellationRequested();
                var browser = await runtime.GetBrowserAsync(diagnostics).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                if (!browser.IsConnected)
                {
                    throw new InvalidOperationException("The assembly Chromium browser disconnected; it will not be restarted.");
                }

                // Playwright context allocation has no cancellation API. Observe its completion, then
                // close a newly allocated context if cancellation arrived while it was in flight.
                context = await browser.NewContextAsync(options).ConfigureAwait(false);
                context.SetDefaultTimeout((float)runtime.Options.ActionTimeout.TotalMilliseconds);
                context.SetDefaultNavigationTimeout((float)runtime.Options.NavigationTimeout.TotalMilliseconds);
                linked.Token.ThrowIfCancellationRequested();
                return context;
            }
            finally
            {
                operation.Release();
            }
        }
        catch (Exception exception)
        {
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            await cleanup.RunAsync("Releasing failed browser context lease", () => DisposeAsync().AsTask()).ConfigureAwait(false);
            cleanup.AttachToPrimaryException(exception);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposed = true;
            return new ValueTask(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        await operation.WaitAsync().ConfigureAwait(false);
        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        try
        {
            if (beforeClose is not null)
            {
                await cleanup.RunAsync("Capturing leased browser diagnostics", beforeClose).ConfigureAwait(false);
            }

            if (context is not null)
            {
                await cleanup.RunAsync("Closing leased browser context", async () =>
                {
                    try
                    {
                        await context.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        // Release the permit, but do not exceed the context cap if native close failed.
                        runtime.ContextCleanupFailed(exception);
                        throw;
                    }
                }).ConfigureAwait(false);
            }
        }
        finally
        {
            runtime.Release(this);
            operation.Release();
        }

        cleanup.ThrowIfFailed();
    }
}
