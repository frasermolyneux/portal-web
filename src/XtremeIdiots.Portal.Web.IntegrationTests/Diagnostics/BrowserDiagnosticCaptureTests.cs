using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Authorization;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using Xunit.Sdk;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "Browser")]
public sealed class BrowserDiagnosticCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserFixture_CapturesBeforeDisposalAndRetainsOnlySimulatedAssertionFailure(bool fail)
    {
        var root = Path.Combine(TestDiagnosticScope.Current!.DirectoryPath, "capture-check");
        using var bus = new DiagnosticMessageBus(new DiagnosticInfrastructureTests.RecordingMessageBus(), root);
        var test = DiagnosticInfrastructureTests.CreateTest($"BrowserFixture(fail: {fail})");
        bus.QueueMessage(new TestStarting(test));
        var scope = TestDiagnosticScope.Current!;
        await using (var fixture = await BrowserFixture.CreateAsync())
        {
            await fixture.Page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
            fixture.AssertNoBrowserErrors();
            var pageError = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Page.PageError += (_, error) => pageError.TrySetResult(error);
            await fixture.Page.EvaluateAsync("() => { console.error('diagnostic console marker'); setTimeout(() => { throw new Error('diagnostic page marker'); }, 0); }");
            await pageError.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await fixture.Page.RouteAsync("**/diagnostic-abort", route => route.AbortAsync());
            await fixture.Page.EvaluateAsync("() => fetch('/diagnostic-abort').catch(() => {})");
            await fixture.Page.RouteAsync("**/diagnostic-response", route => route.FulfillAsync(new RouteFulfillOptions { Status = 503, Body = "diagnostic response" }));
            await fixture.Page.EvaluateAsync("() => fetch('/diagnostic-response')");
        }

        var browserDirectory = Assert.Single(Directory.GetDirectories(scope.DirectoryPath));
        Assert.True(new FileInfo(Path.Combine(browserDirectory, "trace.zip")).Length > 0);
        Assert.True(new FileInfo(Path.Combine(browserDirectory, "page-001.png")).Length > 0);
        Assert.Contains("diagnostic console marker", await File.ReadAllTextAsync(Path.Combine(browserDirectory, "console.log")));
        Assert.Contains("diagnostic page marker", await File.ReadAllTextAsync(Path.Combine(browserDirectory, "page-errors.log")));
        var network = await File.ReadAllTextAsync(Path.Combine(browserDirectory, "network.log"));
        Assert.Contains("Failed GET", network);
        Assert.Contains("Response 503", network);

        if (fail)
        {
            bus.QueueMessage(new TestFailed(test, 1, "", new XunitException("simulated assertion after browser disposal")));
        }
        else
        {
            bus.QueueMessage(new TestPassed(test, 1, ""));
        }

        bus.QueueMessage(new TestFinished(test, 1, ""));
        Assert.Equal(fail, Directory.Exists(scope.DirectoryPath));
        if (fail)
        {
            var applicationLog = await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "application.log"));
            Assert.Contains("Request GET /Identity/Account/Login", applicationLog);
            Assert.Equal(string.Empty, await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "diagnostics-errors.log")));
        }
    }

    [Fact]
    public async Task SharedBrowser_CapturesEachRoleUnderCurrentTest()
    {
        var outer = TestDiagnosticScope.Current!;
        var fixture = new PortalPlaywrightServerFixture();
        await fixture.InitializeAsync();
        try
        {
            using var bus = new DiagnosticMessageBus(new DiagnosticInfrastructureTests.RecordingMessageBus(), Path.Combine(outer.DirectoryPath, "roles"));
            var test = DiagnosticInfrastructureTests.CreateTest("multiple role contexts");
            bus.QueueMessage(new TestStarting(test));
            var scope = TestDiagnosticScope.Current!;
            foreach (var role in new[] { PortalTestRole.Anonymous, PortalTestRole.HeadAdmin })
            {
                await using var session = await fixture.CreateRoleSessionAsync(role);
                await session.Page.GotoAsync(new Uri(fixture.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
            }

            bus.QueueMessage(new TestFailed(test, 1, "", new XunitException("simulated role assertion")));
            bus.QueueMessage(new TestFinished(test, 1, ""));
            var directories = Directory.GetDirectories(scope.DirectoryPath);
            Assert.Equal(2, directories.Length);
            Assert.All(directories, directory => Assert.True(File.Exists(Path.Combine(directory, "trace.zip"))));
            var applicationLog = await File.ReadAllTextAsync(Path.Combine(scope.DirectoryPath, "application.log"));
            Assert.Contains("Request GET /Identity/Account/Login", applicationLog);
            Assert.Contains("Shared setup logs: PortalPlaywright shared fixture initialization", applicationLog);
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }
}
