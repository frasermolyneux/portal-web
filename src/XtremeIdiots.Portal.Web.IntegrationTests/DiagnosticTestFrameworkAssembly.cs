using Reqnroll;
using Reqnroll.xUnit.ReqnrollPlugin;
using System.Runtime.CompilerServices;
using XtremeIdiots.Portal.Web.IntegrationTests;

[assembly: TestFramework("XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics.DiagnosticTestFramework", "XtremeIdiots.Portal.Web.IntegrationTests")]
[assembly: AssemblyFixture(typeof(ReqnrollDiagnosticAssemblyFixture))]

namespace XtremeIdiots.Portal.Web.IntegrationTests;

// Preserve Reqnroll's generated assembly hooks while selecting our message-bus wrapper.
public sealed class ReqnrollDiagnosticAssemblyFixture : IAsyncLifetime
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task InitializeAsync()
    {
        return TestRunnerManager.OnTestRunStartAsync(typeof(ReqnrollDiagnosticAssemblyFixture).Assembly);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public Task DisposeAsync()
    {
        return TestRunnerManager.OnTestRunEndAsync(typeof(ReqnrollDiagnosticAssemblyFixture).Assembly);
    }
}

[CollectionDefinition("ReqnrollNonParallelizableFeatures", DisableParallelization = true)]
public sealed class ReqnrollNonParallelizableFeaturesCollectionDefinition;
