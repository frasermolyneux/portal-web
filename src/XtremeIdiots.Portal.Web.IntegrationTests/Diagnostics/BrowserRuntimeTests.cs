using Microsoft.Playwright;
using Moq;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "HttpIntegration")]
public sealed class BrowserRuntimeTests
{
    [Fact]
    public async Task AssemblyWithoutBrowserUse_NeverCreatesDriver()
    {
        var mocks = new RuntimeMocks();
        var starts = 0;
        var ends = 0;
        var fixture = new ReqnrollDiagnosticAssemblyFixture(mocks.Runtime,
            () =>
            {
                starts++;
                return Task.CompletedTask;
            },
            () =>
            {
                ends++;
                return Task.CompletedTask;
            });

        await fixture.InitializeAsync();
        await fixture.InitializeAsync();
        await fixture.DisposeAsync();
        await fixture.DisposeAsync();

        Assert.Equal(1, starts);
        Assert.Equal(1, ends);
        Assert.Equal(0, mocks.DriverCreations);
        mocks.Driver.Verify(value => value.Dispose(), Times.Never);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => mocks.Runtime.AcquireAsync(TestDiagnosticScope.Current!));
    }

    [Fact]
    public async Task MultipleContexts_ShareExactlyOneLaunchAndReceiveIndependentOptions()
    {
        var mocks = new RuntimeMocks();
        await using var runtime = mocks.Runtime;
        var headers = new[] { "anonymous", "admin", "moderator", "another-admin" };
        var contexts = new List<IBrowserContext>();
        foreach (var header in headers)
        {
            await using var lease = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
            contexts.Add(await lease.CreateContextAsync(new BrowserNewContextOptions
            {
                ExtraHTTPHeaders = new Dictionary<string, string> { ["identity"] = header },
            }));
        }

        Assert.Equal(4, contexts.Distinct().Count());
        Assert.Equal(headers, mocks.ContextOptions.Select(options => options.ExtraHTTPHeaders!.Single().Value));
        Assert.Equal(1, mocks.DriverCreations);
        mocks.BrowserType.Verify(value => value.LaunchAsync(It.Is<BrowserTypeLaunchOptions>(options =>
            options.Headless == true && options.Timeout == 60000)), Times.Once);
        mocks.Browser.Verify(value => value.DisposeAsync(), Times.Never);
        mocks.Driver.Verify(value => value.Dispose(), Times.Never);
        foreach (var context in mocks.Contexts)
        {
            context.Verify(value => value.SetDefaultTimeout(30000), Times.Once);
            context.Verify(value => value.SetDefaultNavigationTimeout(30000), Times.Once);
            context.Verify(value => value.DisposeAsync(), Times.Once);
        }

        await runtime.DisposeAsync();
        mocks.Browser.Verify(value => value.DisposeAsync(), Times.Once);
        mocks.Driver.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task ConcurrentInitialization_AwaitsTheSameNativeLaunch()
    {
        var mocks = new RuntimeMocks();
        await using var runtime = mocks.Runtime;
        var launch = new TaskCompletionSource<IBrowser>(TaskCreationOptions.RunContinuationsAsynchronously);
        mocks.BrowserType.Setup(value => value.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>())).Returns(launch.Task);
        await using var first = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await using var second = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var firstContext = first.CreateContextAsync(new());
        var secondContext = second.CreateContextAsync(new());
        Assert.False(firstContext.IsCompleted);
        Assert.False(secondContext.IsCompleted);
        mocks.BrowserType.Verify(value => value.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()), Times.Once);
        launch.SetResult(mocks.Browser.Object);

        var contexts = await Task.WhenAll(firstContext, secondContext);

        Assert.NotSame(contexts[0], contexts[1]);
        Assert.Equal(1, mocks.DriverCreations);
    }

    [Fact]
    public async Task ContendingContexts_NeverExceedTwoAndResumeAfterRelease()
    {
        var mocks = new RuntimeMocks();
        await using var runtime = mocks.Runtime;
        var first = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var second = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await Task.WhenAll(first.CreateContextAsync(new()), second.CreateContextAsync(new()));
        var thirdAcquisition = runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var fourthAcquisition = runtime.AcquireAsync(TestDiagnosticScope.Current!);
        Assert.False(thirdAcquisition.IsCompleted);
        Assert.False(fourthAcquisition.IsCompleted);

        await first.DisposeAsync();
        var resumed = await Task.WhenAny(thirdAcquisition, fourthAcquisition);
        var remaining = ReferenceEquals(resumed, thirdAcquisition) ? fourthAcquisition : thirdAcquisition;
        await using var third = await resumed;
        await third.CreateContextAsync(new());
        Assert.False(remaining.IsCompleted);
        await second.DisposeAsync();
        await using var fourth = await remaining;
        await fourth.CreateContextAsync(new());

        Assert.Equal(2, mocks.MaximumLiveContexts);
        Assert.Equal(1, mocks.DriverCreations);
    }

    [Fact]
    public async Task PermitTimeoutAndCancellation_DoNotConsumePermitsOrLaunchDriver()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1, PermitTimeout = TimeSpan.FromMilliseconds(30) });
        await using var runtime = mocks.Runtime;
        var first = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var timeout = await Assert.ThrowsAsync<TimeoutException>(() => runtime.AcquireAsync(TestDiagnosticScope.Current!));
        Assert.Contains("limit: 1", timeout.Message);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.AcquireAsync(TestDiagnosticScope.Current!, cancellation.Token));
        await first.DisposeAsync();
        await first.DisposeAsync();
        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        Assert.Equal(0, mocks.DriverCreations);
    }

    [Fact]
    public async Task FailedContextAllocation_ReleasesPermitWithoutRestartingBrowser()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var primary = new PlaywrightException("context allocation failed");
        mocks.Browser.SetupSequence(value => value.NewContextAsync(It.IsAny<BrowserNewContextOptions>()))
            .ThrowsAsync(primary)
            .ReturnsAsync(Mock.Of<IBrowserContext>());
        var failed = await runtime.AcquireAsync(TestDiagnosticScope.Current!);

        Assert.Same(primary, await Assert.ThrowsAsync<PlaywrightException>(() => failed.CreateContextAsync(new())));
        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await replacement.CreateContextAsync(new());
        Assert.Equal(1, mocks.DriverCreations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CaptureOrContextDisposalFailure_StillReleasesPermit(bool captureFails)
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var lease = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var context = await lease.CreateContextAsync(new());
        var failure = new IOException("cleanup failed");
        if (captureFails)
        {
            lease.BeforeClose(() => Task.FromException(failure));
        }
        else
        {
            Mock.Get(context).Setup(value => value.DisposeAsync()).Returns(() => ValueTask.FromException(failure));
        }

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask()));
        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => lease.DisposeAsync().AsTask()));
        Mock.Get(context).Verify(value => value.DisposeAsync(), Times.Once);
        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        if (captureFails)
        {
            await replacement.CreateContextAsync(new());
        }
        else
        {
            var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() => replacement.CreateContextAsync(new()));
            Assert.Same(failure, blocked.InnerException);
        }

        mocks.Browser.Verify(value => value.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task FailedPageInitialization_ClosesContextAndReleasesRoleLease()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var context = new Mock<IBrowserContext> { DefaultValue = DefaultValue.Mock };
        context.Setup(value => value.NewPageAsync()).ThrowsAsync(new PlaywrightException("page failed"));
        mocks.Browser.Setup(value => value.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).ReturnsAsync(context.Object);
        var lease = await runtime.AcquireAsync(TestDiagnosticScope.Current!);

        await Assert.ThrowsAsync<PlaywrightException>(() => PlaywrightRoleSession.CreateAsync(
            lease, new(), new Uri("http://127.0.0.1:12345")));

        context.Verify(value => value.DisposeAsync(), Times.Once);
        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        mocks.Browser.Verify(value => value.DisposeAsync(), Times.Never);
    }

    [Fact]
    public async Task FailedHostInitialization_ReleasesReservedPermitWithoutLaunchingBrowser()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var failure = new InvalidOperationException("scenario services failed");
        Assert.Same(failure, await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BrowserFixture.CreateWithRuntimeAsync(runtime, configureServices: _ => throw failure)));

        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        Assert.Equal(0, mocks.DriverCreations);
    }

    [Fact]
    public async Task CancellationDuringAllocation_ObservesAndClosesLateContext()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var allocation = new TaskCompletionSource<IBrowserContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        mocks.Browser.Setup(value => value.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).Returns(allocation.Task);
        var context = new Mock<IBrowserContext>();
        var lease = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        using var cancellation = new CancellationTokenSource();
        var creating = lease.CreateContextAsync(new(), cancellation.Token);
        await cancellation.CancelAsync();
        Assert.False(creating.IsCompleted);
        allocation.SetResult(context.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
        context.Verify(value => value.DisposeAsync(), Times.Once);
        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
    }

    [Fact]
    public async Task ShutdownWaitsForAllocation_CancelsWaitersAndClosesDriverLast()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        var allocation = new TaskCompletionSource<IBrowserContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        mocks.Browser.Setup(value => value.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).Returns(allocation.Task);
        var context = new Mock<IBrowserContext>();
        var lease = await mocks.Runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var creating = lease.CreateContextAsync(new());
        var waiting = mocks.Runtime.AcquireAsync(TestDiagnosticScope.Current!);
        var stopping = mocks.Runtime.DisposeAsync().AsTask();
        Assert.False(stopping.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        mocks.Driver.Verify(value => value.Dispose(), Times.Never);
        allocation.SetResult(context.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
        await stopping;
        await mocks.Runtime.DisposeAsync();
        context.Verify(value => value.DisposeAsync(), Times.Once);
        mocks.Browser.Verify(value => value.DisposeAsync(), Times.Once);
        mocks.Driver.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task CancelledLaunchWait_IsObservedAndNeverRetried()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var launching = new TaskCompletionSource<IBrowser>(TaskCreationOptions.RunContinuationsAsynchronously);
        mocks.BrowserType.Setup(value => value.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>())).Returns(launching.Task);
        var lease = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        using var cancellation = new CancellationTokenSource();
        var creating = lease.CreateContextAsync(new(), cancellation.Token);
        await cancellation.CancelAsync();
        Assert.False(creating.IsCompleted);
        launching.SetResult(mocks.Browser.Object);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => creating);
        mocks.Browser.Verify(value => value.NewContextAsync(It.IsAny<BrowserNewContextOptions>()), Times.Never);
        await using var replacement = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await replacement.CreateContextAsync(new());
        Assert.Equal(1, mocks.DriverCreations);
    }

    [Fact]
    public async Task FailedLaunch_IsNotRetriedAndDriverIsDisposed()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1, LaunchTimeout = TimeSpan.FromMilliseconds(25) });
        await using var runtime = mocks.Runtime;
        var failure = new TimeoutException("native launch timeout");
        mocks.BrowserType.Setup(value => value.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>())).ThrowsAsync(failure);
        for (var index = 0; index < 2; index++)
        {
            var lease = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
            Assert.Same(failure, await Assert.ThrowsAsync<TimeoutException>(() => lease.CreateContextAsync(new())));
        }

        mocks.BrowserType.Verify(value => value.LaunchAsync(It.Is<BrowserTypeLaunchOptions>(options => options.Timeout == 25)), Times.Once);
        mocks.Driver.Verify(value => value.Dispose(), Times.Once);
        Assert.Equal(1, mocks.DriverCreations);
    }

    [Fact]
    public async Task DisconnectedBrowser_FailsVisiblyWithoutReplacement()
    {
        var mocks = new RuntimeMocks(new BrowserRuntimeOptions { MaximumContexts = 1 });
        await using var runtime = mocks.Runtime;
        var first = await runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await first.CreateContextAsync(new());
        await first.DisposeAsync();
        mocks.Browser.SetupGet(value => value.IsConnected).Returns(false);
        var second = await runtime.AcquireAsync(TestDiagnosticScope.Current!);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => second.CreateContextAsync(new()));

        Assert.Contains("disconnected", failure.Message);
        Assert.Equal(1, mocks.DriverCreations);
        mocks.BrowserType.Verify(value => value.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>()), Times.Once);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AssemblyClosesBrowserAfterEndHooks_EvenWhenHooksFail(bool hooksFail)
    {
        var mocks = new RuntimeMocks();
        var events = new List<string>();
        mocks.Browser.Setup(value => value.DisposeAsync()).Returns(() =>
        {
            events.Add("browser");
            return ValueTask.CompletedTask;
        });
        mocks.Driver.Setup(value => value.Dispose()).Callback(() => events.Add("driver"));
        var fixture = new ReqnrollDiagnosticAssemblyFixture(mocks.Runtime, () => Task.CompletedTask, () =>
        {
            events.Add("hooks");
            return hooksFail ? Task.FromException(new InvalidOperationException("end hooks failed")) : Task.CompletedTask;
        });
        await fixture.InitializeAsync();
        var lease = await mocks.Runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await lease.CreateContextAsync(new());
        lease.BeforeClose(() =>
        {
            events.Add("capture");
            return Task.CompletedTask;
        });

        if (hooksFail)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(fixture.DisposeAsync);
        }
        else
        {
            await fixture.DisposeAsync();
            await fixture.DisposeAsync();
        }

        Assert.Equal(["hooks", "capture", "browser", "driver"], events);
        mocks.Contexts.Single().Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task AssemblyBrowserCloseFailure_StillDisposesDriver()
    {
        var mocks = new RuntimeMocks();
        var lease = await mocks.Runtime.AcquireAsync(TestDiagnosticScope.Current!);
        await lease.CreateContextAsync(new());
        var failure = new IOException("browser close failed");
        mocks.Browser.Setup(value => value.DisposeAsync()).Returns(() => ValueTask.FromException(failure));

        Assert.Same(failure, await Assert.ThrowsAsync<IOException>(() => mocks.Runtime.DisposeAsync().AsTask()));
        mocks.Contexts.Single().Verify(value => value.DisposeAsync(), Times.Once);
        mocks.Driver.Verify(value => value.Dispose(), Times.Once);
    }

    [Fact]
    public async Task AssemblyStartFailure_PreservesPrimaryAndRunsEndHooksBeforeBrowserCleanup()
    {
        var mocks = new RuntimeMocks();
        var primary = new InvalidOperationException("assembly start failed");
        var endHooks = 0;
        var fixture = new ReqnrollDiagnosticAssemblyFixture(mocks.Runtime, async () =>
        {
            var lease = await mocks.Runtime.AcquireAsync(TestDiagnosticScope.Current!);
            await lease.CreateContextAsync(new());
            throw primary;
        }, () =>
        {
            mocks.Browser.Verify(value => value.DisposeAsync(), Times.Never);
            endHooks++;
            return Task.CompletedTask;
        });

        Assert.Same(primary, await Assert.ThrowsAsync<InvalidOperationException>(fixture.InitializeAsync));
        await fixture.DisposeAsync();

        Assert.Equal(1, endHooks);
        mocks.Contexts.Single().Verify(value => value.DisposeAsync(), Times.Once);
        mocks.Browser.Verify(value => value.DisposeAsync(), Times.Once);
        mocks.Driver.Verify(value => value.Dispose(), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void InvalidContextLimit_IsRejected(int maximumContexts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BrowserRuntime(new BrowserRuntimeOptions { MaximumContexts = maximumContexts }));
    }

    private sealed class RuntimeMocks
    {
        private int liveContexts;

        internal RuntimeMocks(BrowserRuntimeOptions? options = null)
        {
            Driver.SetupGet(value => value.Chromium).Returns(BrowserType.Object);
            BrowserType.Setup(value => value.LaunchAsync(It.IsAny<BrowserTypeLaunchOptions>())).ReturnsAsync(Browser.Object);
            Browser.SetupGet(value => value.IsConnected).Returns(true);
            Browser.Setup(value => value.NewContextAsync(It.IsAny<BrowserNewContextOptions>())).Returns((BrowserNewContextOptions contextOptions) =>
            {
                lock (Contexts)
                {
                    var context = new Mock<IBrowserContext>();
                    context.Setup(value => value.DisposeAsync()).Returns(() =>
                    {
                        lock (Contexts)
                        {
                            liveContexts--;
                        }

                        return ValueTask.CompletedTask;
                    });
                    Contexts.Add(context);
                    ContextOptions.Add(contextOptions);
                    MaximumLiveContexts = Math.Max(MaximumLiveContexts, ++liveContexts);
                    return Task.FromResult(context.Object);
                }
            });
            Runtime = new BrowserRuntime(options, () =>
            {
                DriverCreations++;
                return Task.FromResult(Driver.Object);
            });
        }

        internal Mock<IPlaywright> Driver { get; } = new();
        internal Mock<IBrowserType> BrowserType { get; } = new();
        internal Mock<IBrowser> Browser { get; } = new();
        internal List<Mock<IBrowserContext>> Contexts { get; } = [];
        internal List<BrowserNewContextOptions> ContextOptions { get; } = [];
        internal BrowserRuntime Runtime { get; }
        internal int DriverCreations { get; private set; }
        internal int MaximumLiveContexts { get; private set; }
    }
}
