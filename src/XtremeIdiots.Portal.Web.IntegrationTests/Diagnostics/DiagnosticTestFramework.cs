using Reqnroll.xUnit.ReqnrollPlugin;
using System.Collections.Concurrent;
using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

public sealed class DiagnosticTestFramework(IMessageSink messageSink) : XunitTestFrameworkWithAssemblyFixture(messageSink)
{
    protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
    {
        return new DiagnosticTestExecutor(assemblyName, SourceInformationProvider, DiagnosticMessageSink);
    }
}

internal sealed class DiagnosticTestExecutor(
    AssemblyName assemblyName,
    ISourceInformationProvider sourceInformationProvider,
    IMessageSink diagnosticMessageSink)
    : XunitTestFrameworkExecutorWithAssemblyFixture(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
    async protected override void RunTestCases(IEnumerable<IXunitTestCase> testCases, IMessageSink executionMessageSink, ITestFrameworkExecutionOptions executionOptions)
    {
        using var runner = new DiagnosticTestAssemblyRunner(TestAssembly, testCases, DiagnosticMessageSink, executionMessageSink, executionOptions);
        await runner.RunAsync();
    }
}

internal sealed class DiagnosticTestAssemblyRunner(
    ITestAssembly testAssembly,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageSink executionMessageSink,
    ITestFrameworkExecutionOptions executionOptions)
    : XunitTestAssemblyRunnerWithAssemblyFixture(testAssembly, testCases, diagnosticMessageSink, executionMessageSink, executionOptions)
{
    protected override IMessageBus CreateMessageBus()
    {
        return new DiagnosticMessageBus(base.CreateMessageBus());
    }
}

internal sealed class DiagnosticMessageBus(IMessageBus inner, string? root = null) : IMessageBus
{
    private readonly ConcurrentDictionary<ITest, RunningTest> running = new();
    private readonly ConcurrentDictionary<Guid, IDisposable> collections = new();
    private int disposed;

    public bool QueueMessage(IMessageSinkMessage message)
    {
        // This must run synchronously in the caller, not on xUnit's message-sink thread.
        if (message is ITestCollectionStarting collectionStarting)
        {
            var id = collectionStarting.TestCollection.UniqueID;
            collections[id] = TestDiagnosticScope.ActivateCollection(id);
        }

        if (message is ITestStarting starting)
        {
            var method = starting.Test.TestCase.TestMethod;
            var scope = new TestDiagnosticScope(starting.Test.DisplayName, method.TestClass.Class.Name, method.Method.Name, root);
            running[starting.Test] = new RunningTest(scope, TestDiagnosticScope.Activate(scope));
        }

        if (message is ITestMessage testMessage && running.TryGetValue(testMessage.Test, out var test))
        {
            switch (message)
            {
                case ITestFailed failed:
                    test.Outcome = "Failed";
                    test.Failure = new { failed.ExceptionTypes, failed.Messages, failed.StackTraces, failed.ExceptionParentIndices };
                    test.Scope.IncludeInitializationFailures(failed.Test.TestCase.TestMethod.TestClass.TestCollection.UniqueID);
                    test.Scope.SaveFailure(test.Outcome, test.Failure);
                    inner.QueueMessage(new TestOutput(failed.Test, test.Scope.ArtifactOutput));
                    message = new TestFailed(failed.Test, failed.ExecutionTime, failed.Output + test.Scope.ArtifactOutput,
                        failed.ExceptionTypes, failed.Messages, failed.StackTraces, failed.ExceptionParentIndices);
                    break;
                case ITestPassed:
                    test.Outcome = "Passed";
                    break;
                case ITestSkipped:
                    test.Outcome = "Skipped";
                    break;
                case ITestCleanupFailure cleanup:
                    test.Outcome = test.Outcome == "Failed" ? "Failed" : "CleanupFailed";
                    test.Failure = new
                    {
                        primaryFailure = test.Failure,
                        cleanupFailure = new { cleanup.ExceptionTypes, cleanup.Messages, cleanup.StackTraces },
                    };
                    break;
                case ITestFinished finished:
                    test.Scope.Complete(test.Outcome, test.Failure);
                    if (test.Outcome is not ("Passed" or "Skipped"))
                    {
                        message = new TestFinished(finished.Test, finished.ExecutionTime, finished.Output + test.Scope.ArtifactOutput);
                    }
                    else if (test.Scope.HasErrors)
                    {
                        inner.QueueMessage(new TestOutput(finished.Test, "Diagnostics warnings (successful test artifacts discarded):" + test.Scope.ArtifactOutput));
                    }

                    test.Activation.Dispose();
                    running.TryRemove(finished.Test, out _);
                    break;
                default:
                    break;
            }
        }

        if (message is ITestCollectionFinished collectionFinished &&
            collections.TryRemove(collectionFinished.TestCollection.UniqueID, out var collectionActivation))
        {
            collectionActivation.Dispose();
        }

        return inner.QueueMessage(message);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        try
        {
            foreach (var test in running.Values)
            {
                try
                {
                    test.Scope.Complete("Incomplete", test.Failure);
                    Console.Error.WriteLine(test.Scope.ArtifactOutput);
                }
                finally
                {
                    test.Activation.Dispose();
                }
            }
        }
        finally
        {
            running.Clear();
            foreach (var activation in collections.Values)
            {
                activation.Dispose();
            }

            collections.Clear();
            inner.Dispose();
        }
    }

    private sealed class RunningTest(TestDiagnosticScope scope, IDisposable activation)
    {
        internal TestDiagnosticScope Scope { get; } = scope;
        internal IDisposable Activation { get; } = activation;
        internal string Outcome { get; set; } = "Incomplete";
        internal object? Failure { get; set; }
    }
}
