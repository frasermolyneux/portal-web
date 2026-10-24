using Microsoft.Playwright;
using System.Text.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "Browser")]
public sealed class MapDownloadsTests
{
    [Fact]
    public async Task PublicMapDownloads_OfferOneNativeActionWithoutDuplicateManagerOrFileLinks()
    {
        await using var browser = await BrowserFixture.CreateAsync();
        const string mapName = "mp_\"><img src=x onerror=\"window.redirectInjection=true\">";
        const string fileName = "<img src=x onerror=\"window.redirectInjection=true\">.ff";
        const string managerUrl = "https://maps.xtremeidiots.com/?game=cod4&map=mp_custom";
        var body = JsonSerializer.Serialize(new
        {
            draw = 1,
            recordsTotal = 1,
            recordsFiltered = 1,
            data = new[]
            {
                new
                {
                    gameType = "CallOfDuty4",
                    mapName,
                    mapFiles = new[]
                    {
                        new { fileName, url = "https://redirect.xtremeidiots.net/redirect/cod4/usermaps/mp_custom/map.ff" },
                        new { fileName = "unsafe.ff", url = "javascript:window.redirectInjection=true" }
                    },
                    redirectManagerUrl = managerUrl,
                    mapImageUri = "/images/noimage.jpg",
                    totalLikes = 0,
                    totalDislikes = 0,
                    totalVotes = 0,
                    likePercentage = 0,
                    dislikePercentage = 0,
                    builtIn = false
                }
            }
        });
        await browser.Page.RouteAsync("**/Maps/GetMapListAjax**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "application/json",
            Body = body
        }));

        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps").AbsoluteUri);

        var row = browser.Page.Locator("#dataTable tbody tr");
        await Assertions.Expect(row).ToHaveCountAsync(1);
        await Assertions.Expect(row.Locator("td").Nth(1)).ToHaveTextAsync(mapName);
        var nativeLink = row.GetByRole(AriaRole.Link, new() { Name = "Download", Exact = true });
        Assert.Equal(mapName, await nativeLink.EvaluateAsync<string>("link => new URL(link.href).searchParams.get('map')"));
        Assert.Equal("/MapDownloads", await nativeLink.EvaluateAsync<string>("link => new URL(link.href).pathname"));
        await Assertions.Expect(row.GetByRole(AriaRole.Link)).ToHaveCountAsync(1);
        await Assertions.Expect(row.Locator("td").Nth(2)).ToContainTextAsync("1 file");
        await Assertions.Expect(row.Locator(".map-files img")).ToHaveCountAsync(0);
        await Assertions.Expect(row.Locator("td").Nth(1).Locator("img")).ToHaveCountAsync(0);
        await Assertions.Expect(row.Locator("img.map-image")).ToHaveAttributeAsync("alt", "Map image for " + mapName);
        Assert.False(await browser.Page.EvaluateAsync<bool>("() => window.redirectInjection === true"));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task MapsWithoutDownloadableFiles_DoNotOfferDownloadLinks()
    {
        await using var browser = await BrowserFixture.CreateAsync();
        var rows = new[]
        {
            new { gameType = "CallOfDuty4", mapName = "empty", mapFiles = Array.Empty<object>(), builtIn = false },
            new { gameType = "CallOfDuty2", mapName = "invalid", mapFiles = new object[] { new { fileName = "unsafe.iwd", url = "javascript:alert(1)" } }, builtIn = false },
            new { gameType = "CallOfDuty2", mapName = "stock", mapFiles = Array.Empty<object>(), builtIn = true }
        };
        await browser.Page.RouteAsync("**/Maps/GetMapListAjax**", route => route.FulfillAsync(new RouteFulfillOptions
        {
            ContentType = "application/json",
            Body = JsonSerializer.Serialize(new { draw = 1, recordsTotal = rows.Length, recordsFiltered = rows.Length, data = rows })
        }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps").AbsoluteUri);
        var tableRows = browser.Page.Locator("#dataTable tbody tr");
        await Assertions.Expect(tableRows).ToHaveCountAsync(3);
        await Assertions.Expect(tableRows.GetByRole(AriaRole.Link)).ToHaveCountAsync(0);
        await Assertions.Expect(tableRows.Nth(0).Locator("td").Nth(2)).ToHaveTextAsync("No Map Files");
        await Assertions.Expect(tableRows.Nth(2).Locator("td").Nth(2)).ToHaveTextAsync("Built-in");
        browser.AssertNoBrowserErrors();
    }
}
