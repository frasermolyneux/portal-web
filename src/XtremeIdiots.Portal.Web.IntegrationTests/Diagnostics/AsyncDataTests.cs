using Microsoft.Playwright;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Diagnostics;

[Trait("Category", "Browser")]
public sealed class AsyncDataTests
{
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
