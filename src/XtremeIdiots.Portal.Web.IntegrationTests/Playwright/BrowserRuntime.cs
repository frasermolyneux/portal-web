using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

/// <summary>Owned by the assembly fixture; constructing it never starts the Playwright driver.</summary>
internal sealed class BrowserRuntime : IAsyncDisposable
{
    private readonly Lock sync = new();
    private readonly Func<Task<IPlaywright>> createPlaywright;
    private readonly SemaphoreSlim permits;
    private readonly CancellationTokenSource stopping = new();
    private readonly HashSet<BrowserContextLease> leases = [];
    private Task<IBrowser>? launch;
    private IPlaywright? playwright;
    private IBrowser? browser;
    private Task? disposal;
    private Exception? contextCleanupFailure;
    private bool disposed;

    internal BrowserRuntime(BrowserRuntimeOptions? options = null, Func<Task<IPlaywright>>? createPlaywright = null)
    {
        Options = options ?? new BrowserRuntimeOptions();
        Options.Validate();
        this.createPlaywright = createPlaywright ?? Microsoft.Playwright.Playwright.CreateAsync;
        permits = new SemaphoreSlim(Options.MaximumContexts, Options.MaximumContexts);
    }

    internal BrowserRuntimeOptions Options { get; }
    internal CancellationToken Stopping => stopping.Token;

    internal async Task<BrowserContextLease> AcquireAsync(TestDiagnosticScope diagnostics, CancellationToken cancellationToken = default)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stopping.Token);
        if (!await permits.WaitAsync(Options.PermitTimeout, linked.Token).ConfigureAwait(false))
        {
            throw new TimeoutException($"No browser context lease became available within {Options.PermitTimeout.TotalSeconds} seconds (limit: {Options.MaximumContexts}).");
        }

        lock (sync)
        {
            if (disposed || linked.IsCancellationRequested)
            {
                permits.Release();
                linked.Token.ThrowIfCancellationRequested();
                throw new ObjectDisposedException(nameof(BrowserRuntime));
            }

            var lease = new BrowserContextLease(this, diagnostics);
            leases.Add(lease);
            return lease;
        }
    }

    internal Task<IBrowser> GetBrowserAsync(TestDiagnosticScope diagnostics)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (contextCleanupFailure is not null)
            {
                throw new InvalidOperationException("A shared browser context could not be closed. No further contexts can be created safely.", contextCleanupFailure);
            }

            // Cache failures as well as success: a broken assembly browser must never be restarted.
            return launch ??= LaunchAsync(diagnostics);
        }
    }

    private async Task<IBrowser> LaunchAsync(TestDiagnosticScope diagnostics)
    {
        try
        {
            playwright = await createPlaywright().ConfigureAwait(false);
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true,
                Timeout = (float)Options.LaunchTimeout.TotalMilliseconds,
            }).ConfigureAwait(false);
            return browser;
        }
        catch (Exception exception)
        {
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            if (playwright is not null)
            {
                cleanup.Run("Disposing failed assembly Playwright driver", playwright.Dispose);
                playwright = null;
            }

            cleanup.AttachToPrimaryException(exception);
            throw;
        }
    }

    internal void Release(BrowserContextLease lease)
    {
        lock (sync)
        {
            if (leases.Remove(lease))
            {
                permits.Release();
            }
        }
    }

    internal void ContextCleanupFailed(Exception exception)
    {
        lock (sync)
        {
            contextCleanupFailure ??= exception;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (sync)
        {
            disposed = true;
            return new ValueTask(disposal ??= DisposeCoreAsync([.. leases]));
        }
    }

    private async Task DisposeCoreAsync(BrowserContextLease[] remainingLeases)
    {
        await Task.Yield();
        var diagnostics = new TestDiagnosticScope("Assembly browser shutdown", GetType().FullName!, nameof(DisposeAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        cleanup.Run("Cancelling browser lease waiters", stopping.Cancel);
        // Disposal waits for in-flight allocations instead of abandoning tasks that could leak resources.
        foreach (var lease in remainingLeases)
        {
            await cleanup.RunAsync("Closing remaining assembly browser context", () => lease.DisposeAsync().AsTask()).ConfigureAwait(false);
        }

        if (browser is not null)
        {
            await cleanup.RunAsync("Closing assembly Chromium", () => browser.DisposeAsync().AsTask()).ConfigureAwait(false);
        }

        if (playwright is not null)
        {
            cleanup.Run("Disposing assembly Playwright driver", playwright.Dispose);
        }

        diagnostics.Complete(cleanup.HasFailures ? "CleanupFailed" : "Passed");
        if (diagnostics.HasErrors)
        {
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
        }

        cleanup.ThrowIfFailed();
    }
}

/// <summary>The registration is explicitly installed and removed by the assembly fixture.</summary>
internal static class AssemblyBrowserRuntime
{
    private static BrowserRuntime? current;

    internal static BrowserRuntime Current => Volatile.Read(ref current)
        ?? throw new InvalidOperationException("The test assembly browser runtime has not been initialized.");

    internal static void Register(BrowserRuntime runtime)
    {
        if (Interlocked.CompareExchange(ref current, runtime, null) is not null)
        {
            throw new InvalidOperationException("A test assembly browser runtime is already registered.");
        }
    }

    internal static void Unregister(BrowserRuntime runtime)
    {
        Interlocked.CompareExchange(ref current, null, runtime);
    }
}
