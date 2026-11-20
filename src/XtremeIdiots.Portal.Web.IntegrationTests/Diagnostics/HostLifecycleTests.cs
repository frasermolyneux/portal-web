using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "HttpIntegration")]
public sealed class HostLifecycleTests
{
    [Fact]
    public async Task Disposal_ReleasesWebRootFileWatchers()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        var webRoot = host.Services.GetRequiredService<IWebHostEnvironment>().WebRootFileProvider;
        var physicalProviders = PhysicalProviders(webRoot).ToArray();
        Assert.NotEmpty(physicalProviders);
        host.Services.GetRequiredService<IFileVersionProvider>().AddFileVersionToPath(PathString.Empty, "/css/app.css");

        await host.DisposeAsync();

        foreach (var provider in physicalProviders)
            Assert.Throws<ObjectDisposedException>(() => provider.Watch("css/app.css"));
    }

    private static IEnumerable<PhysicalFileProvider> PhysicalProviders(IFileProvider provider)
        => provider is CompositeFileProvider composite
            ? composite.FileProviders.SelectMany(PhysicalProviders)
            : provider is PhysicalFileProvider physical ? [physical] : [];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StartupTimeout_CancelsCurrentStageAndDisposesOwnedServices(bool kestrel)
    {
        await using var service = new CancellableHostedService(blockStartup: true);
        var timeouts = new TestHostTimeouts { Startup = TimeSpan.FromSeconds(2), Shutdown = TimeSpan.FromMilliseconds(50) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHostAsync(kestrel, service, timeouts));

        // The startup budget also covers configuration and database initialization.
        if (service.Started)
            Assert.True(service.StartCancellationObserved);
        if (service.Resolved)
            Assert.True(service.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallerCancellation_PropagatesIntoHostStartup(bool kestrel)
    {
        using var cancellation = new CancellationTokenSource();
        var service = new CancellableHostedService(blockStartup: true, onStarting: cancellation.Cancel);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateHostAsync(kestrel, service, new TestHostTimeouts(), cancellation.Token));

        Assert.True(service.StartCancellationObserved);
        Assert.True(service.Disposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ShutdownTimeout_StillDisposesHostAndIsIdempotent(bool kestrel)
    {
        var service = new CancellableHostedService(blockShutdown: true);
        var host = await CreateHostAsync(kestrel, service, new TestHostTimeouts { Shutdown = TimeSpan.FromMilliseconds(30) });

        var failure = await Record.ExceptionAsync(() => host.DisposeAsync().AsTask());

        Assert.NotNull(failure);
        Assert.Contains("cancel", failure.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.True(service.StopCancellationObserved);
        Assert.True(service.Disposed);
        await host.DisposeAsync();
        Assert.Equal(1, service.DisposalCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreCancelledStartup_DoesNotConfigureOrAllocateHost(bool kestrel)
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var configured = false;
        void configure(IServiceCollection _)
        {
            configured = true;
        }

        if (kestrel)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PortalWebKestrelHost.CreateAsync(configure, cancellationToken: cancellation.Token));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PortalWebTestHost.CreateAsync(configure, cancellationToken: cancellation.Token));
        }

        Assert.False(configured);
    }

    private async static Task<IAsyncDisposable> CreateHostAsync(
        bool kestrel, CancellableHostedService service, TestHostTimeouts timeouts, CancellationToken cancellationToken = default)
    {
        void configure(IServiceCollection services)
        {
            services.AddSingleton<IHostedService>(_ =>
            {
                service.Resolved = true;
                return service;
            });
        }

        return kestrel
            ? await PortalWebKestrelHost.CreateAsync(configure, timeouts, cancellationToken)
            : await PortalWebTestHost.CreateAsync(configure, timeouts, cancellationToken);
    }

    private sealed class CancellableHostedService(
        bool blockStartup = false,
        bool blockShutdown = false,
        Action? onStarting = null) : IHostedService, IAsyncDisposable
    {
        internal bool StartCancellationObserved { get; private set; }
        internal bool StopCancellationObserved { get; private set; }
        internal bool Resolved { get; set; }
        internal bool Started { get; private set; }
        internal bool Disposed => DisposalCount > 0;
        internal int DisposalCount { get; private set; }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Started = true;
            onStarting?.Invoke();
            if (blockStartup)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    StartCancellationObserved = cancellationToken.IsCancellationRequested;
                    throw;
                }
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (blockShutdown)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    StopCancellationObserved = cancellationToken.IsCancellationRequested;
                    throw;
                }
            }
        }

        public ValueTask DisposeAsync()
        {
            DisposalCount++;
            return ValueTask.CompletedTask;
        }
    }
}
