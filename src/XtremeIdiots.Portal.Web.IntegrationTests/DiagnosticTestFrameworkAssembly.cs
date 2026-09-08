using Reqnroll;
using Reqnroll.xUnit.ReqnrollPlugin;
using System.Runtime.CompilerServices;
using XtremeIdiots.Portal.Web.IntegrationTests;
using XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

[assembly: TestFramework("XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics.DiagnosticTestFramework", "XtremeIdiots.Portal.Web.IntegrationTests")]
[assembly: AssemblyFixture(typeof(ReqnrollDiagnosticAssemblyFixture))]

namespace XtremeIdiots.Portal.Web.IntegrationTests;

// Preserve Reqnroll's generated assembly hooks while selecting our message-bus wrapper.
public sealed class ReqnrollDiagnosticAssemblyFixture : IAsyncLifetime
{
    private readonly Lock sync = new();
    private readonly BrowserRuntime runtime;
    private readonly Func<Task> startHooks;
    private readonly Func<Task> endHooks;
    private readonly bool registerRuntime;
    private Task? initialization;
    private Task? disposal;

    public ReqnrollDiagnosticAssemblyFixture()
        : this(new BrowserRuntime(),
            () => TestRunnerManager.OnTestRunStartAsync(typeof(ReqnrollDiagnosticAssemblyFixture).Assembly),
            () => TestRunnerManager.OnTestRunEndAsync(typeof(ReqnrollDiagnosticAssemblyFixture).Assembly),
            registerRuntime: true)
    {
    }

    internal ReqnrollDiagnosticAssemblyFixture(BrowserRuntime runtime, Func<Task> startHooks, Func<Task> endHooks, bool registerRuntime = false)
    {
        this.runtime = runtime;
        this.startHooks = startHooks;
        this.endHooks = endHooks;
        this.registerRuntime = registerRuntime;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task InitializeAsync()
    {
        lock (sync)
        {
            return initialization ??= InitializeCoreAsync();
        }
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            if (registerRuntime)
            {
                AssemblyBrowserRuntime.Register(runtime);
            }

            await startHooks().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            var diagnostics = new TestDiagnosticScope("Assembly initialization", GetType().FullName!, nameof(InitializeAsync));
            diagnostics.RecordError("Assembly start hooks", exception);
            var cleanup = new DiagnosticResourceCleanup(diagnostics);
            await cleanup.RunAsync("Cleaning failed assembly lifetime", DisposeAsync).ConfigureAwait(false);
            cleanup.AttachToPrimaryException(exception);
            diagnostics.Complete("InitializationFailed", exception.ToString());
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task DisposeAsync()
    {
        lock (sync)
        {
            return disposal ??= DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        await Task.Yield();
        var diagnostics = new TestDiagnosticScope("Assembly teardown", GetType().FullName!, nameof(DisposeAsync));
        using var activation = TestDiagnosticScope.Activate(diagnostics);
        var cleanup = new DiagnosticResourceCleanup(diagnostics);
        await cleanup.RunAsync("Reqnroll test run end hooks", endHooks).ConfigureAwait(false);
        await cleanup.RunAsync("Disposing assembly browser runtime", () => runtime.DisposeAsync().AsTask()).ConfigureAwait(false);
        AssemblyBrowserRuntime.Unregister(runtime);
        diagnostics.Complete(cleanup.HasFailures ? "CleanupFailed" : "Passed");
        if (diagnostics.HasErrors)
        {
            await Console.Error.WriteLineAsync(diagnostics.ArtifactOutput).ConfigureAwait(false);
        }

        cleanup.ThrowIfFailed();
    }
}

[CollectionDefinition("ReqnrollNonParallelizableFeatures", DisableParallelization = true)]
public sealed class ReqnrollNonParallelizableFeaturesCollectionDefinition;
