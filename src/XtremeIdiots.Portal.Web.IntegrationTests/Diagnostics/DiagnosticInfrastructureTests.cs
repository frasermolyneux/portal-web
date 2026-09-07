using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Moq;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "HttpIntegration")]
public sealed class DiagnosticInfrastructureTests
{
    [Fact]
    public async Task Framework_SetsScopeInActualTestExecutionContext()
    {
        var scope = Assert.IsType<TestDiagnosticScope>(TestDiagnosticScope.Current);
        await Task.Yield();
        Assert.Same(scope, TestDiagnosticScope.Current);
        Assert.Equal(typeof(DiagnosticInfrastructureTests).FullName, scope.ClassName);
        Assert.Equal(nameof(Framework_SetsScopeInActualTestExecutionContext), scope.MethodName);
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("Skipped")]
    [InlineData("Failed")]
    [InlineData("FailedWithCleanup")]
    [InlineData("CleanupFailed")]
    public void MessageBus_RetainsOnlyFailuresAndPreservesOriginalErrorAndOutput(string outcome)
    {
        var outer = TestDiagnosticScope.Current!;
        var sink = new RecordingMessageBus();
        using var bus = new DiagnosticMessageBus(sink, Path.Combine(outer.DirectoryPath, "message-bus"));
        var test = CreateTest("Theory(value: \"invalid:/?*\")");
        bus.QueueMessage(new TestStarting(test));
        var scope = TestDiagnosticScope.Current!;
        scope.WriteText(Path.Combine(scope.DirectoryPath, "captured.txt"), "before result is known");
        scope.Log("application log for this test");

        if (outcome is "Failed" or "FailedWithCleanup")
        {
            scope.RecordError("Screenshot failed", new IOException("simulated diagnostics failure"));
            bus.QueueMessage(new TestFailed(test, 1, "original output", new XunitException("original assertion")));
            if (outcome == "FailedWithCleanup")
            {
                bus.QueueMessage(new TestCleanupFailure(test, new InvalidOperationException("secondary cleanup failure")));
            }
        }
        else if (outcome == "Skipped")
        {
            bus.QueueMessage(new TestSkipped(test, "original skip reason"));
        }
        else
        {
            bus.QueueMessage(new TestPassed(test, 1, "original output"));
            if (outcome == "CleanupFailed")
            {
                bus.QueueMessage(new TestCleanupFailure(test, new InvalidOperationException("cleanup failed")));
            }
        }

        bus.QueueMessage(new TestFinished(test, 1, "original output"));
        Assert.Same(outer, TestDiagnosticScope.Current);
        Assert.Null(TestDiagnosticScope.Find(scope.Id));
        Assert.Equal(outcome is "Failed" or "FailedWithCleanup" or "CleanupFailed", Directory.Exists(scope.DirectoryPath));
        if (outcome is "Failed" or "FailedWithCleanup" or "CleanupFailed")
        {
            Assert.Contains(test.DisplayName, System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(scope.DirectoryPath, "metadata.json"))).RootElement.GetProperty("displayName").GetString());
            Assert.Contains("application log for this test", File.ReadAllText(Path.Combine(scope.DirectoryPath, "application.log")));
        }

        if (outcome is "Failed" or "FailedWithCleanup")
        {
            var failed = Assert.Single(sink.Messages.OfType<ITestFailed>());
            Assert.Equal("original assertion", Assert.Single(failed.Messages));
            Assert.Contains("original output", failed.Output);
            Assert.Contains(scope.DirectoryPath, failed.Output);
            Assert.Contains("simulated diagnostics failure", File.ReadAllText(Path.Combine(scope.DirectoryPath, "diagnostics-errors.log")));
            if (outcome == "FailedWithCleanup")
            {
                var metadata = File.ReadAllText(Path.Combine(scope.DirectoryPath, "metadata.json"));
                Assert.Contains("original assertion", metadata);
                Assert.Contains("secondary cleanup failure", metadata);
            }
        }

        if (outcome == "Skipped")
        {
            Assert.Equal("original skip reason", Assert.Single(sink.Messages.OfType<ITestSkipped>()).Reason);
        }
    }

    [Fact]
    public void MessageBus_DisposalRestoresInterruptedTestScopeAndRetainsEvidence()
    {
        var outer = TestDiagnosticScope.Current!;
        using var bus = new DiagnosticMessageBus(new RecordingMessageBus(), Path.Combine(outer.DirectoryPath, "interrupted"));
        bus.QueueMessage(new TestStarting(CreateTest("Interrupted test")));
        var interrupted = TestDiagnosticScope.Current!;
        interrupted.Log("test interrupted before result");

        bus.Dispose();

        Assert.Same(outer, TestDiagnosticScope.Current);
        Assert.Null(TestDiagnosticScope.Find(interrupted.Id));
        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(interrupted.DirectoryPath, "metadata.json")));
        Assert.Equal("Incomplete", metadata.RootElement.GetProperty("outcome").GetString());
        Assert.Contains("test interrupted before result", File.ReadAllText(Path.Combine(interrupted.DirectoryPath, "application.log")));

        bus.Dispose();
        Assert.Same(outer, TestDiagnosticScope.Current);
    }

    [Fact]
    public async Task Scopes_IsolateConcurrentTestsWithIdenticalDisplayNamesAndApplicationLogs()
    {
        var outer = TestDiagnosticScope.Current!;
        var sink = new RecordingMessageBus();
        using var bus = new DiagnosticMessageBus(sink, Path.Combine(outer.DirectoryPath, "parallel"));
        var scopes = await Task.WhenAll(Enumerable.Range(0, 4).Select(index => Task.Run(async () =>
        {
            var test = CreateTest("same display name");
            bus.QueueMessage(new TestStarting(test));
            var scope = TestDiagnosticScope.Current!;
            await Task.Yield();
            using var provider = new DiagnosticLoggerProvider();
            var logger = provider.CreateLogger("parallel");
            using (logger.BeginScope($"scope-{index}"))
            {
                logger.LogInformation("marker-{Index}", index);
            }

            bus.QueueMessage(new TestFailed(test, 1, "", new XunitException("simulated failure")));
            bus.QueueMessage(new TestFinished(test, 1, ""));
            Assert.Same(outer, TestDiagnosticScope.Current);
            return (index, scope);
        })));

        Assert.Equal(4, scopes.Select(value => value.scope.DirectoryPath).Distinct().Count());
        foreach (var (index, scope) in scopes)
        {
            var log = await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "application.log"));
            Assert.Contains($"marker-{index}", log);
            Assert.Contains($"scope-{index}", log);
            Assert.DoesNotContain($"marker-{(index + 1) % 4}", log);
        }
    }

    [Fact]
    public void Logger_BoundsAndMarksDiscardedEntries()
    {
        var scope = new TestDiagnosticScope("bounded logs", "class", "method", Path.Combine(TestDiagnosticScope.Current!.DirectoryPath, "bounded"));
        using var activation = TestDiagnosticScope.Activate(scope);
        using var provider = new DiagnosticLoggerProvider();
        var logger = provider.CreateLogger("bounded");
        for (var index = 0; index < BoundedDiagnosticBuffer.MaxEntries + 1; index++)
        {
            logger.LogInformation("entry-{Index}", index);
        }

        scope.Complete("Failed");
        var logs = File.ReadAllText(Path.Combine(scope.DirectoryPath, "application.log"));
        Assert.Contains("[1 older entries discarded]", logs);
        Assert.DoesNotContain("entry-0", logs);
        Assert.Contains($"entry-{BoundedDiagnosticBuffer.MaxEntries}", logs);
    }

    [Fact]
    public void MessageBus_DiagnosticWriteFailureDoesNotMaskAssertion()
    {
        var outer = TestDiagnosticScope.Current!;
        var path = Path.Combine(outer.DirectoryPath, "not-a-directory");
        outer.WriteText(path, "file");
        var sink = new RecordingMessageBus();
        using var bus = new DiagnosticMessageBus(sink, path);
        var test = CreateTest("failed diagnostics destination");
        bus.QueueMessage(new TestStarting(test));
        bus.QueueMessage(new TestFailed(test, 1, "", new XunitException("primary assertion")));
        bus.QueueMessage(new TestFinished(test, 1, ""));
        var failed = Assert.Single(sink.Messages.OfType<ITestFailed>());
        Assert.Equal("primary assertion", Assert.Single(failed.Messages));
        Assert.Contains("Diagnostics capture/cleanup errors:", failed.Output);
    }

    [Fact]
    public async Task RoleInitializationFailure_CleansUpWithoutMaskingPrimaryError()
    {
        var scope = new TestDiagnosticScope("failed role initialization", "class", "method", Path.Combine(TestDiagnosticScope.Current!.DirectoryPath, "initialization"));
        using var activation = TestDiagnosticScope.Activate(scope);
        var context = new Mock<IBrowserContext> { DefaultValue = DefaultValue.Mock };
        var primary = new PlaywrightException("original page launch failure");
        context.Setup(value => value.NewPageAsync()).ThrowsAsync(primary);
        context.Setup(value => value.DisposeAsync()).Returns(() => ValueTask.FromException(new IOException("context cleanup failure")));

        var thrown = await Assert.ThrowsAsync<PlaywrightException>(() =>
            PlaywrightRoleSession.CreateAsync(context.Object, new Uri("http://127.0.0.1:12345")));

        Assert.Same(primary, thrown);
        Assert.Contains("context cleanup failure", Assert.Single(Assert.IsType<string[]>(thrown.Data["PortalTestCleanupErrors"])));
        context.Verify(value => value.DisposeAsync(), Times.Once);
        scope.Complete("Failed", thrown.ToString());
        var errors = await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "diagnostics-errors.log"));
        Assert.Contains("original page launch failure", errors);
        Assert.Contains("context cleanup failure", errors);
        Assert.True(File.Exists(Path.Combine(scope.DirectoryPath, "metadata.json")));
    }

    [Fact]
    public async Task DiagnosticImplementationErrors_AreNotSuppressed()
    {
        var scope = TestDiagnosticScope.Current!;
        var syncFailure = new InvalidOperationException("unexpected synchronous capture implementation failure");
        var asyncFailure = new InvalidOperationException("unexpected asynchronous capture implementation failure");

        Assert.Same(syncFailure, Assert.Throws<InvalidOperationException>(() =>
            scope.Try("capture", () => throw syncFailure)));
        Assert.Same(asyncFailure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            scope.TryAsync("capture", () => Task.FromException(asyncFailure))));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("playwright")]
    public async Task ExpectedCaptureErrors_AreRecorded(string kind)
    {
        var scope = new TestDiagnosticScope("expected capture failure", "class", "method", Path.Combine(TestDiagnosticScope.Current!.DirectoryPath, "expected-capture"));
        var failure = kind == "io"
            ? (Exception)new IOException("expected filesystem capture failure")
            : new PlaywrightException("expected browser capture failure");
        scope.Try("synchronous capture", () => throw failure);
        await scope.TryAsync("asynchronous capture", () => Task.FromException(failure));
        scope.Complete("Failed");
        var errors = await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "diagnostics-errors.log"));
        Assert.Contains("synchronous capture", errors);
        Assert.Contains("asynchronous capture", errors);
        Assert.Contains(failure.Message, errors);
    }

    [Theory]
    [InlineData("implementation")]
    [InlineData("io")]
    [InlineData("playwright")]
    public async Task RoleSessionDisposal_PropagatesNonDisconnectFailures(string kind)
    {
        var scope = new TestDiagnosticScope("unexpected disposal failure", "class", "method", Path.Combine(TestDiagnosticScope.Current!.DirectoryPath, "disposal"));
        using var activation = TestDiagnosticScope.Activate(scope);
        var context = new Mock<IBrowserContext> { DefaultValue = DefaultValue.Mock };
        context.Setup(value => value.NewPageAsync()).ReturnsAsync(Mock.Of<IPage>());
        var failure = kind switch
        {
            "io" => (Exception)new IOException("resource disposal IO failure"),
            "playwright" => new PlaywrightException("non-disconnect browser disposal failure"),
            _ => new InvalidOperationException("resource disposal implementation failure"),
        };
        context.Setup(value => value.DisposeAsync()).Returns(() => ValueTask.FromException(failure));
        var session = await PlaywrightRoleSession.CreateAsync(context.Object, new Uri("http://127.0.0.1:12345"));

        var thrown = await Assert.ThrowsAsync(failure.GetType(), async () => await session.DisposeAsync());

        Assert.Same(failure, thrown);
        context.Verify(value => value.DisposeAsync(), Times.Once);
        scope.Complete("Failed", thrown.ToString());
        Assert.Contains(failure.Message, await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "diagnostics-errors.log")));
    }

    [Fact]
    public async Task ResourceCleanup_AttemptsRemainingResourcesBeforeRethrowing()
    {
        var cleanup = new DiagnosticResourceCleanup(TestDiagnosticScope.Current!);
        var failure = new InvalidOperationException("first resource failure");
        var remainingDisposed = false;
        await cleanup.RunAsync("first", () => Task.FromException(failure));
        cleanup.Run("remaining", () => remainingDisposed = true);

        Assert.True(remainingDisposed);
        Assert.True(cleanup.HasFailures);
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(cleanup.ThrowIfFailed));
    }

    [Fact]
    public async Task ResourceCleanup_ToleratesOnlyExplicitExpectedDisconnect()
    {
        var cleanup = new DiagnosticResourceCleanup(TestDiagnosticScope.Current!);
        await cleanup.RunAsync("browser", () => Task.FromException(new PlaywrightException("disconnected")), _ => true);
        cleanup.ThrowIfFailed();
        Assert.False(cleanup.HasFailures);
    }

    [Fact]
    public void CollectionInitializationFailure_IsLinkedFromAffectedTest()
    {
        var root = Path.Combine(TestDiagnosticScope.Current!.DirectoryPath, "collection-setup");
        var collectionId = Guid.NewGuid();
        using var collection = TestDiagnosticScope.ActivateCollection(collectionId);
        var setup = new TestDiagnosticScope("shared collection setup", "fixture", "InitializeAsync", root);
        setup.Log("early application setup log");
        setup.Complete("InitializationFailed", "original setup failure");
        var sink = new RecordingMessageBus();
        using var bus = new DiagnosticMessageBus(sink, root);
        var test = CreateTest("test blocked by setup");
        Mock.Get(test).SetupGet(value => value.TestCase.TestMethod.TestClass.TestCollection.UniqueID).Returns(collectionId);
        bus.QueueMessage(new TestStarting(test));
        var scope = TestDiagnosticScope.Current!;
        bus.QueueMessage(new TestFailed(test, 0, "", new InvalidOperationException("original setup failure")));
        bus.QueueMessage(new TestFinished(test, 0, ""));

        var failed = Assert.Single(sink.Messages.OfType<ITestFailed>());
        Assert.Contains(setup.DirectoryPath, failed.Output);
        Assert.Contains("early application setup log", File.ReadAllText(Path.Combine(setup.DirectoryPath, "application.log")));
        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(scope.DirectoryPath, "metadata.json")));
        Assert.Equal(setup.DirectoryPath, Assert.Single(metadata.RootElement.GetProperty("relatedArtifactDirectories").EnumerateArray()).GetString());
    }

    internal static ITest CreateTest(string displayName)
    {
        var test = new Mock<ITest> { DefaultValue = DefaultValue.Mock };
        test.SetupGet(value => value.DisplayName).Returns(displayName);
        test.SetupGet(value => value.TestCase.TestMethod.TestClass.Class.Name).Returns("Original.TestClass");
        test.SetupGet(value => value.TestCase.TestMethod.Method.Name).Returns("OriginalTestMethod");
        return test.Object;
    }

    internal sealed class RecordingMessageBus : IMessageBus
    {
        internal System.Collections.Concurrent.ConcurrentQueue<IMessageSinkMessage> Messages { get; } = new();
        public bool QueueMessage(IMessageSinkMessage message)
        {
            Messages.Enqueue(message);
            return true;
        }

        public void Dispose() { }
    }
}
