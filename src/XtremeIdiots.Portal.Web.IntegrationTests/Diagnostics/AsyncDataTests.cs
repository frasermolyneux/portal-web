using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "Browser")]
public sealed class AsyncDataTests
{
    [Fact]
    public async Task SectionMarkup_PreservesUiAttributesWithoutExecutableContent()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        foreach (var script in new[] { "/js/vendor/dompurify.min.js", "/js/async-data.js" })
        {
            await page.AddScriptTagAsync(new() { Url = new Uri(fixture.Host.BaseAddress, script).AbsoluteUri });
        }

        await page.EvaluateAsync("""
            () => {
                const host = document.createElement('div');
                host.id = 'section-sanitization';
                document.body.append(host);
                PortalAsync.renderHtml(host,
                    '<span class="badge" data-testid="safe-fragment">Ready</span>' +
                    '<a href="javascript:window.unexpectedExecution=true">Unsafe link</a>' +
                    '<img onerror="window.unexpectedExecution=true">' +
                    '<script>window.unexpectedExecution=true</script>');
            }
            """);

        await Assertions.Expect(page.GetByTestId("safe-fragment")).ToHaveTextAsync("Ready");
        await Assertions.Expect(page.Locator("#section-sanitization script, #section-sanitization [onerror]")).ToHaveCountAsync(0);
        Assert.Null(await page.Locator("#section-sanitization a").GetAttributeAsync("href"));
        Assert.False(await page.EvaluateAsync<bool>("() => window.unexpectedExecution === true"));
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Sections_EnhanceDatesAfterDynamicInsertion()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        foreach (var script in new[] { "/js/vendor/dompurify.min.js", "/js/date-utils.js", "/js/async-data.js" })
        {
            await page.AddScriptTagAsync(new() { Url = new Uri(fixture.Host.BaseAddress, script).AbsoluteUri });
        }

        await page.RouteAsync("**/test-date-section", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = """<time datetime="2026-06-01T12:34:00Z" data-dt="relative">UTC fallback</time>"""
        }));
        await page.EvaluateAsync("""
            async () => {
                const host = document.createElement('div');
                host.id = 'section-dates';
                document.body.append(host);
                await PortalAsync.loadSection(host, '/test-date-section');
            }
            """);

        var timestamp = page.Locator("#section-dates time");
        await Assertions.Expect(timestamp).Not.ToHaveTextAsync("UTC fallback");
        var expectedTitle = await page.EvaluateAsync<string>("() => portalDate.formatDateTime('2026-06-01T12:34:00Z')");
        await Assertions.Expect(timestamp).ToHaveAttributeAsync("title", expectedTitle);
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Poller_ResumesAfterCachedPageRestoreWithoutRestartingStoppedPollers()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        await page.AddScriptTagAsync(new() { Url = new Uri(fixture.Host.BaseAddress, "/js/async-data.js").AbsoluteUri });
        await page.Clock.InstallAsync();
        await page.EvaluateAsync("""
            async () => {
                const counter = document.createElement('span');
                counter.id = 'restored-poller-count';
                document.body.append(counter);
                let calls = 0;
                window.restoredPoller = PortalAsync.createPoller({
                    intervalMs: 1000,
                    load: async signal => {
                        counter.textContent = String(++calls);
                        if (calls === 1) {
                            await new Promise((resolve, reject) =>
                                signal.addEventListener('abort', () => reject(new DOMException('Paused', 'AbortError')), { once: true }));
                        }
                    }
                });
                let stoppedCalls = 0;
                window.stoppedPoller = PortalAsync.createPoller({
                    intervalMs: 1000,
                    load: async () => { window.stoppedCalls = ++stoppedCalls; }
                });
                window.restoredPoller.start();
                window.stoppedPoller.start();
                await window.stoppedPoller.refresh();
                window.stoppedPoller.stop();
                window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
            }
            """);
        await Assertions.Expect(page.Locator("#restored-poller-count")).ToHaveTextAsync("2");
        await page.Clock.RunForAsync(1000);
        await Assertions.Expect(page.Locator("#restored-poller-count")).ToHaveTextAsync("3");
        Assert.Equal(1, await page.EvaluateAsync<int>("() => window.stoppedCalls"));
        await page.EvaluateAsync("() => { window.restoredPoller.dispose(); window.stoppedPoller.dispose(); }");
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Section_ResumesInterruptedLoadAfterCachedPageRestore()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        foreach (var script in new[] { "/js/vendor/dompurify.min.js", "/js/async-data.js" })
        {
            await page.AddScriptTagAsync(new() { Url = new Uri(fixture.Host.BaseAddress, script).AbsoluteUri });
        }

        await page.RouteAsync("**/test-restored-section", route => route.FulfillAsync(new()
        {
            ContentType = "text/html",
            Body = "<span>Restored data</span>"
        }));
        await page.EvaluateAsync("""
            () => {
                const host = document.createElement('div');
                host.id = 'restored-section';
                document.body.append(host);
                const originalFetch = window.fetch;
                let first = true;
                window.fetch = (url, options) => {
                    if (url === '/test-restored-section' && first) {
                        first = false;
                        return new Promise((resolve, reject) =>
                            options.signal.addEventListener('abort', () => reject(new DOMException('Paused', 'AbortError')), { once: true }));
                    }
                    return originalFetch(url, options);
                };
                void PortalAsync.loadSection(host, '/test-restored-section');
                window.dispatchEvent(new PageTransitionEvent('pagehide', { persisted: true }));
                window.dispatchEvent(new PageTransitionEvent('pageshow', { persisted: true }));
            }
            """);

        await Assertions.Expect(page.Locator("#restored-section")).ToHaveTextAsync("Restored data");
        await Assertions.Expect(page.Locator("#restored-section")).Not.ToHaveAttributeAsync("aria-busy", "true");
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Notifications_LoadCountOnInitializationAndReusePollerAfterMarkingAllRead()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        var reads = 0;
        await page.RouteAsync("**/api/notifications/unread-count", route =>
        {
            var count = Interlocked.Increment(ref reads) == 1 ? 3 : 0;
            return route.FulfillAsync(new() { ContentType = "application/json", Body = $$"""{"count":{{count}}}""" });
        });
        await page.RouteAsync("**/api/notifications/read-all", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = "{}"
        }));
        await page.RouteAsync("**/api/notifications/recent?take=10", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = "[]"
        }));
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        await page.SetContentAsync("""
            <style>.d-none { display: none; }</style>
            <button id="notificationBell">Notifications</button>
            <span id="notificationBadge" class="d-none"></span>
            <button id="markAllReadBtn">Mark all read</button>
            <div id="notificationList"></div>
            """);
        await page.Clock.InstallAsync();
        foreach (var script in new[] { "/lib/jquery/dist/jquery.min.js", "/js/async-data.js", "/js/notifications.js" })
        {
            await page.AddScriptTagAsync(new() { Url = new Uri(fixture.Host.BaseAddress, script).AbsoluteUri });
        }

        await Assertions.Expect(page.Locator("#notificationBadge")).ToHaveTextAsync("3");
        for (var i = 0; i < 2; i++)
        {
            await page.RunAndWaitForResponseAsync(
                () => page.Locator("#markAllReadBtn").ClickAsync(),
                response => response.Url.EndsWith("/api/notifications/unread-count", StringComparison.Ordinal));
        }

        await Assertions.Expect(page.Locator("#notificationBadge")).ToBeHiddenAsync();
        Assert.Equal(3, Volatile.Read(ref reads));
        await page.RunAndWaitForResponseAsync(
            () => page.Clock.RunForAsync(60000),
            response => response.Url.EndsWith("/api/notifications/unread-count", StringComparison.Ordinal));
        Assert.Equal(4, Volatile.Read(ref reads));
        fixture.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task Poller_CoalescesRefreshesAndSchedulesOnlyAfterCompletion()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        await page.AddScriptTagAsync(new()
        {
            Url = new Uri(fixture.Host.BaseAddress, "/js/async-data.js").AbsoluteUri
        });
        await page.Clock.InstallAsync();
        await page.EvaluateAsync("""
            () => {
                const counter = document.createElement('span');
                counter.id = 'poller-count';
                counter.textContent = '0';
                document.body.append(counter);
                let calls = 0;
                window.testPoller = PortalAsync.createPoller({
                    intervalMs: 1000,
                    load: async () => {
                        counter.textContent = String(++calls);
                        if (calls === 1) await new Promise(resolve => window.releasePoller = resolve);
                    }
                });
                window.testPoller.start();
                window.testPoller.refresh();
            }
            """);
        await Assertions.Expect(page.Locator("#poller-count")).ToHaveTextAsync("1");
        await page.Clock.RunForAsync(30000);
        await Assertions.Expect(page.Locator("#poller-count")).ToHaveTextAsync("1");
        await page.EvaluateAsync("async () => { window.releasePoller(); await window.testPoller.refresh(); }");
        await page.Clock.RunForAsync(1000);
        await Assertions.Expect(page.Locator("#poller-count")).ToHaveTextAsync("2");
        await page.EvaluateAsync("() => window.testPoller.dispose()");
        await page.Clock.RunForAsync(10000);
        await Assertions.Expect(page.Locator("#poller-count")).ToHaveTextAsync("2");
        fixture.AssertNoBrowserErrors();
    }
}
