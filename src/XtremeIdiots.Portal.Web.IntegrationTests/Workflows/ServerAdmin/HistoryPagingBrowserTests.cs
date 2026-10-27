using Microsoft.Playwright;
using System.Text.Json;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "Browser")]
public sealed partial class HistoryPagingBrowserTests
{
    [GeneratedRegex("disabled")]
    private static partial Regex DisabledClass();

    [Fact]
    public async Task OrdinaryTables_KeepNumberedPagingTotalsAndPageJump()
    {
        await using var fixture = await BrowserFixture.CreateAsync();
        var page = fixture.Page;
        await page.GotoAsync(new Uri(fixture.Host.BaseAddress, "/Identity/Account/Login").AbsoluteUri);
        foreach (var script in new[] { "/lib/jquery/dist/jquery.js", "/lib/datatables/jquery.dataTables.min.js", "/js/datatable-page-jump.js" })
        {
            await page.AddScriptTagAsync(new() { Url = new Uri(fixture.Host.BaseAddress, script).AbsoluteUri });
        }

        await page.EvaluateAsync("""
            () => {
                const element = document.createElement('table');
                element.id = 'ordinaryTable';
                document.body.append(element);
                const table = $('#ordinaryTable').DataTable({
                    data: Array.from({ length: 25 }, (_, index) => [String(index)]),
                    columns: [{ title: 'Synthetic' }]
                });
                PortalDataTableUi.attachPageJump(table);
            }
            """);

        await Assertions.Expect(page.Locator("#ordinaryTable_info")).ToHaveTextAsync("Showing 1 to 10 of 25 entries");
        await Assertions.Expect(page.Locator("#ordinaryTable_paginate").GetByRole(AriaRole.Link, new() { Name = "2", Exact = true })).ToBeVisibleAsync();
        await page.Locator("#ordinaryTable_pageJumpInput").FillAsync("2");
        await page.Locator("#ordinaryTable_pageJumpInput").PressAsync("Enter");
        await Assertions.Expect(page.Locator("#ordinaryTable_info")).ToHaveTextAsync("Showing 11 to 20 of 25 entries");
        fixture.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task History_NavigatesLookaheadWithoutTotalsOrNumberedPages(bool events)
    {
        await using var fixture = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin);
        var page = fixture.Page;
        var endpoint = events ? "GetServerEventsAjax" : "GetChatLogAjax";
        var requests = 0;
        await page.RouteAsync("**/ServerAdmin/GetGameServers", route => route.FulfillAsync(new()
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(Array.Empty<object>())
        }));
        await page.RouteAsync($"**/ServerAdmin/{endpoint}*", async route =>
        {
            Interlocked.Increment(ref requests);
            using var request = JsonDocument.Parse(route.Request.PostData!);
            var start = request.RootElement.GetProperty("start").GetInt32();
            var length = request.RootElement.GetProperty("length").GetInt32();
            var rows = Enumerable.Range(0, 12).Skip(start).Take(length).Select(index => new
            {
                timestamp = DateTime.UtcNow.AddMinutes(-index).ToString("O"),
                eventType = $"Event-{index}",
                eventData = "Synthetic event",
                chatMessageId = Guid.NewGuid(),
                username = "Synthetic",
                chatType = "Say",
                message = $"Message-{index}",
                locked = false,
                gameServer = new { title = "Synthetic server", liveTitle = "Synthetic server" }
            }).ToArray();
            await route.FulfillAsync(new()
            {
                ContentType = "application/json",
                Body = JsonSerializer.Serialize(new
                {
                    draw = request.RootElement.GetProperty("draw").GetInt32(),
                    start,
                    hasMore = start + rows.Length < 12,
                    data = rows
                })
            });
        });
        var path = events ? "/ServerAdmin/ServerEventsIndex" : "/ServerAdmin/ChatLogIndex";
        await page.RunAndWaitForResponseAsync(
            () => page.GotoAsync(new Uri(fixture.Host.BaseAddress, path).AbsoluteUri),
            response => response.Url.Contains(endpoint, StringComparison.Ordinal) && response.Request.Method == "POST");

        await Assertions.Expect(page.Locator("#dataTable_info")).ToHaveTextAsync("Showing 1 to 10");
        await Assertions.Expect(page.Locator("#dataTable_pageJumpContainer")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("#dataTable_paginate .paginate_button")).ToHaveCountAsync(2);
        await Assertions.Expect(page.GetByText(events ? "Server events are retained for 30 days." : "Unlocked chat messages are retained for 6 months.", new() { Exact = false })).ToBeVisibleAsync();

        await page.RunAndWaitForResponseAsync(
            () => page.Locator("#dataTable_next").ClickAsync(),
            response => response.Url.Contains(endpoint, StringComparison.Ordinal) && response.Request.Method == "POST");
        await Assertions.Expect(page.Locator("#dataTable_info")).ToHaveTextAsync("Showing 11 to 12");
        await Assertions.Expect(page.Locator("#dataTable_next")).ToHaveClassAsync(DisabledClass());
        await Assertions.Expect(page.Locator("#dataTable tbody tr")).ToHaveCountAsync(2);

        await page.RunAndWaitForResponseAsync(
            () => page.Locator("#dataTable_previous").ClickAsync(),
            response => response.Url.Contains(endpoint, StringComparison.Ordinal) && response.Request.Method == "POST");
        await Assertions.Expect(page.Locator("#dataTable_info")).ToHaveTextAsync("Showing 1 to 10");
        await Assertions.Expect(page.Locator("#dataTable_previous")).ToHaveClassAsync(DisabledClass());
        if (events)
        {
            await page.RunAndWaitForResponseAsync(
                () => page.Locator("#dataTable_next").ClickAsync(),
                response => response.Url.Contains(endpoint, StringComparison.Ordinal) && response.Request.Method == "POST");
            await Assertions.Expect(page.Locator("#dataTable_info")).ToHaveTextAsync("Showing 11 to 12");
            await page.Clock.InstallAsync();
            await page.Clock.PauseAtAsync(DateTime.UtcNow.AddSeconds(1));
            var beforeTyping = Volatile.Read(ref requests);
            await page.Locator("#filterEventType").FillAsync("Map");
            await page.Clock.RunForAsync(300);
            await page.Locator("#filterEventType").FillAsync("MapChange");
            await page.Clock.RunForAsync(499);
            Assert.Equal(beforeTyping, Volatile.Read(ref requests));
            await page.RunAndWaitForResponseAsync(
                () => page.Clock.RunForAsync(1),
                response => response.Url.Contains("eventType=MapChange", StringComparison.Ordinal));
            await Assertions.Expect(page.Locator("#dataTable_info")).ToHaveTextAsync("Showing 1 to 10");
            Assert.Equal(beforeTyping + 1, Volatile.Read(ref requests));

            await page.Locator("#filterEventType").FillAsync("pending");
            await page.RunAndWaitForResponseAsync(
                () => page.Locator("#resetFilters").ClickAsync(),
                response => response.Url.Contains(endpoint, StringComparison.Ordinal)
                    && !response.Url.Contains("eventType=", StringComparison.Ordinal));
            var afterReset = Volatile.Read(ref requests);
            await page.Clock.RunForAsync(500);
            Assert.Equal(afterReset, Volatile.Read(ref requests));
            await Assertions.Expect(page.Locator("#filterEventType")).ToHaveValueAsync("");
        }

        fixture.AssertNoBrowserErrors();
    }
}
