using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Analytics.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;
using XtremeIdiots.Redirect.Api.Abstractions.V1;
using XtremeIdiots.Redirect.Api.Client.Testing;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "Browser")]
public sealed class PublicMapsBrowserTests
{
    private readonly static Guid serverId = Guid.Parse("58e49525-1bcd-42d2-b882-64409c327987");

    [Theory]
    [InlineData(390)]
    [InlineData(1440)]
    public async Task MapsNavigationAndButtons_ArePublicReadableAndFitViewport(int width)
    {
        await using var browser = await BrowserFixture.CreateAsync(null, services => services.AddSingleton(Repository().Object));
        await browser.Page.SetViewportSizeAsync(width, 900);
        await browser.Page.RouteAsync("**/Maps/GetMapListAjax**", route => Json(route,
            new { draw = 1, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<object>() }));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#filterGameType option")).ToHaveCountAsync(2);
        await Assertions.Expect(browser.Page.Locator("#filterGameType")).ToContainTextAsync("COD4 / COD4x");
        await browser.Page.Locator(".wrapper-content").EvaluateAsync("async root => await Promise.all(root.getAnimations().map(animation => animation.finished))");
        await Assertions.Expect(browser.Page.Locator("[data-testid=nav-maps-toggle]")).ToBeAttachedAsync();
        Assert.True(await browser.Page.EvaluateAsync<bool>("""
            () => {
                const servers = document.querySelector('[data-testid=nav-servers-toggle]');
                const maps = document.querySelector('[data-testid=nav-maps-toggle]');
                return !!(servers.compareDocumentPosition(maps) & Node.DOCUMENT_POSITION_FOLLOWING);
            }
            """));
        await Assertions.Expect(browser.Page.Locator(".ibox-tools a")).ToHaveCountAsync(1);
        var link = browser.Page.GetByRole(AriaRole.Link, new() { Name = "Map and mod downloads", Exact = true });
        await Assertions.Expect(link).ToBeVisibleAsync();
        Assert.True(await link.EvaluateAsync<bool>(Contrast));
        await link.HoverAsync();
        await link.EvaluateAsync("async element => await Promise.all(element.getAnimations().map(animation => animation.finished))");
        Assert.True(await link.EvaluateAsync<bool>(Contrast));
        await browser.Page.Mouse.DownAsync();
        await link.EvaluateAsync("async element => await Promise.all(element.getAnimations().map(animation => animation.finished))");
        Assert.True(await link.EvaluateAsync<bool>("element => element.matches(':active')"));
        Assert.True(await link.EvaluateAsync<bool>(Contrast));
        await browser.Page.Mouse.MoveAsync(0, 0);
        await browser.Page.Mouse.UpAsync();
        await browser.Page.Keyboard.PressAsync("Tab");
        await link.FocusAsync();
        await link.EvaluateAsync("async element => await Promise.all(element.getAnimations().map(animation => animation.finished))");
        Assert.True(await link.EvaluateAsync<bool>("element => element.matches(':focus-visible')"));
        Assert.True(await link.EvaluateAsync<bool>(Contrast));
        Assert.True(await browser.Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData("CallOfDuty4x", "CallOfDuty4")]
    [InlineData("CallOfDuty5", "")]
    public async Task BookmarkedCatalogue_UsesOnlyAvailableFamilies_AndCanClearFilters(string bookmarked, string selected)
    {
        await using var browser = await BrowserFixture.CreateAsync(null, services => services.AddSingleton(Repository().Object));
        await browser.Page.RouteAsync("**/Maps/GetMapListAjax**", route => Json(route,
            new { draw = 1, recordsTotal = 0, recordsFiltered = 0, data = Array.Empty<object>() }));
        var expected = "/Maps/GetMapListAjax" + (selected.Length > 0 ? "/" + selected : "");
        var initial = browser.Page.WaitForResponseAsync(r => new Uri(r.Url).AbsolutePath == expected);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps/GameIndex/" + bookmarked).AbsoluteUri);
        Assert.Equal(200, (await initial).Status);
        await Assertions.Expect(browser.Page.Locator("#filterGameType")).ToHaveValueAsync(selected);
        var cleared = browser.Page.WaitForResponseAsync(r => new Uri(r.Url).AbsolutePath == "/Maps/GetMapListAjax");
        await browser.Page.Locator("#resetFilters").ClickAsync();
        Assert.Equal(200, (await cleared).Status);
        await Assertions.Expect(browser.Page.Locator("#filterGameType")).ToHaveValueAsync("");
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(TestPrincipalProfiles.GameAdmin, true)]
    public async Task Analytics_ShowsVoteTilesRankingsAndFilters_WithRestrictedVoteLog(string? profile, bool logVisible)
    {
        await using var browser = await BrowserFixture.CreateAsync(profile,
            services => services.AddSingleton(Repository().Object));
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps/Analytics").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#insightsTiles")).ToContainTextAsync("Votes");
        await Assertions.Expect(browser.Page.Locator("#insightsRankings")).ToContainTextAsync("A few votes so far");
        await Assertions.Expect(browser.Page.Locator("#insightsServer")).ToContainTextAsync("Public COD4x");
        await Assertions.Expect(browser.Page.Locator("#insightsServer")).ToContainTextAsync("Empty server");
        await Assertions.Expect(browser.Page.Locator("#insightsServer")).Not.ToContainTextAsync("Teamspeak");
        await Assertions.Expect(browser.Page.Locator("body")).Not.ToContainTextAsync("telemetry");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Link, new() { Name = "Vote Log", Exact = true }))
            .ToHaveCountAsync(logVisible ? 1 : 0);
        if (logVisible)
        {
            var log = browser.Page.GetByRole(AriaRole.Link, new() { Name = "Vote Log", Exact = true });
            await browser.Page.Keyboard.PressAsync("Tab");
            await log.FocusAsync();
            await log.EvaluateAsync("async element => await Promise.all(element.getAnimations().map(animation => animation.finished))");
            Assert.True(await log.EvaluateAsync<bool>("element => element.matches(':focus-visible')"));
            Assert.True(await log.EvaluateAsync<bool>(Contrast));
        }

        await browser.Page.Locator("#insightsRanking").SelectOptionAsync("popular");
        await Assertions.Expect(browser.Page.Locator("#insightsRankings")).Not.ToContainTextAsync("A few votes so far");
        await browser.Page.Locator("#insightsServer").SelectOptionAsync(serverId.ToString());
        await browser.Page.Locator("#insightsGame").SelectOptionAsync("CallOfDuty5");
        await Assertions.Expect(browser.Page.Locator("#insightsServer")).ToHaveValueAsync("");
        var response = browser.Page.WaitForResponseAsync(r => r.Url.Contains("/Maps/Insights?", StringComparison.Ordinal)
            && r.Url.Contains("days=7", StringComparison.Ordinal));
        await browser.Page.Locator("#insightsDays").SelectOptionAsync("7");
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Apply filters" }).ClickAsync();
        Assert.Equal(200, (await response).Status);
        await Assertions.Expect(browser.Page.Locator("#insightsStatus")).ToContainTextAsync("Showing votes");
        browser.AssertNoBrowserErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(TestPrincipalProfiles.GameAdmin)]
    public async Task EmptyRotationPreview_DoesNotSuggestUnavailablePreparation(string? profile)
    {
        var fake = new FakeRedirectApiClient().WithData("Collection", RedirectDtoFactory.CreateCatalogue());
        await using var browser = await BrowserFixture.CreateAsync(profile, services =>
        {
            services.AddSingleton(Repository().Object);
            services.AddSingleton<IRedirectApiClient>(fake);
        });
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapDownloads?rotation={serverId}").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator("#downloadStatus")).ToHaveTextAsync("No extra maps are available to download for this server right now.");
        await Assertions.Expect(browser.Page.Locator("#downloadRotationZip")).ToBeHiddenAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadLogin")).Not.ToBeVisibleAsync();
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("CreateArchive:", StringComparison.Ordinal));
        browser.AssertNoBrowserErrors();
    }

    [Fact]
    public async Task PublicRotations_ShowAssignedAndHistoryPopularity_AndOnlyExplicitAuthenticatedZipCreation()
    {
        var fake = new FakeRedirectApiClient()
            .WithData("Collection", RedirectDtoFactory.CreateCatalogue(new ContentEntry(
                "redirect/cod4/usermaps/mp_custom", "mp_custom", true, 123, DateTimeOffset.UnixEpoch, "v1", null)))
            .WithData("CreateArchive", RedirectDtoFactory.CreateArchive(status: "complete"));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin, services =>
        {
            services.AddSingleton(Repository().Object);
            services.AddSingleton<IRedirectApiClient>(fake);
        });
        await browser.Page.SetViewportSizeAsync(390, 900);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, "/Maps/Rotations").AbsoluteUri);
        await Assertions.Expect(browser.Page.Locator(".public-map-rotation")).ToHaveCountAsync(3);
        await browser.Page.Locator(".wrapper-content").EvaluateAsync("async root => await Promise.all(root.getAnimations().map(animation => animation.finished))");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Region, new() { Name = "Public COD4x" }))
            .ToContainTextAsync("This server's map rotation");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Region, new() { Name = "Synthetic COD5" }))
            .ToContainTextAsync("Recently played on this server (last 2 days)");
        await Assertions.Expect(browser.Page.Locator(".public-map-card").First).ToContainTextAsync("A few votes so far");
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Region, new() { Name = "Synthetic COD2" })
            .GetByRole(AriaRole.Link, new() { Name = "Download", Exact = false })).ToHaveCountAsync(0);
        Assert.True(await browser.Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= innerWidth"));
        await browser.Page.Locator("#rotationServer").SelectOptionAsync(serverId.ToString());
        await Assertions.Expect(browser.Page.Locator(".public-map-rotation:visible")).ToHaveCountAsync(1);
        await browser.Page.Locator("#rotationGame").SelectOptionAsync("CallOfDuty5");
        await Assertions.Expect(browser.Page.Locator("#rotationServer")).ToHaveValueAsync("");
        await Assertions.Expect(browser.Page.Locator(".public-map-rotation:visible")).ToHaveCountAsync(1);
        await browser.Page.Locator("#rotationGame").SelectOptionAsync("");
        var progress = browser.Page.Locator(".public-map-card .map-approval").Nth(1);
        Assert.True(await progress.EvaluateAsync<bool>("""e => { const rgb = node => getComputedStyle(node).backgroundColor.match(/\d+/g).map(Number); const track = rgb(e), value = rgb(e.firstElementChild); const fillWidth = e.firstElementChild.getBoundingClientRect().width; return track[0] > track[1] && value[1] > value[0] && fillWidth > 0 && fillWidth < e.getBoundingClientRect().width; }"""));
        await browser.Page.GetByRole(AriaRole.Region, new() { Name = "Public COD4x" })
            .GetByRole(AriaRole.Link, new() { Name = "Download map pack", Exact = true }).ClickAsync();
        await Assertions.Expect(browser.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare map pack" })).ToBeVisibleAsync();
        await Assertions.Expect(browser.Page.Locator("#rotationPreview .public-map-card")).ToHaveCountAsync(1);
        await Assertions.Expect(browser.Page.Locator("#rotationPreview")).ToContainTextAsync("Public COD4x");
        Assert.DoesNotContain(fake.Calls, call => call.StartsWith("CreateArchive:", StringComparison.Ordinal));
        await browser.Page.GetByRole(AriaRole.Button, new() { Name = "Prepare map pack" }).ClickAsync();
        await Assertions.Expect(browser.Page.Locator("#downloadTasks")).ToContainTextAsync("ZIP ready to save");
        Assert.Contains("CreateArchive:redirect/cod4/usermaps/mp_custom", fake.Calls);
        Assert.All(fake.PortalCalls, caller => Assert.Equal("12345", caller.MemberId));
        browser.AssertNoBrowserErrors();
    }

    private static Mock<IRepositoryApiClient> Repository()
    {
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        repository.Setup(r => r.Maps.V1.GetMapGames(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapGamesDto>(HttpStatusCode.OK, new ApiResponse<MapGamesDto>(
                new MapGamesDto { Games = [GameType.CallOfDuty4] })));
        var live = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerLiveStatusDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new { ServerId = serverId, Title = "^2Public COD4x" }))!;
        repository.Setup(r => r.LiveStatus.V1.GetAllGameServerLiveStatuses(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerLiveStatusDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerLiveStatusDto>>(new CollectionModel<GameServerLiveStatusDto>([live]))));
        repository.Setup(r => r.MapRotations.V1.GetPublicServerRotations(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationsDto>(HttpStatusCode.OK, new ApiResponse<PublicServerRotationsDto>(
                new PublicServerRotationsDto
                {
                    Servers =
                    [
                        new() { GameServerId = serverId, Title = "Synthetic COD4x", GameType = GameType.CallOfDuty4x, RotationTitle = "Normal",
                            Maps = [new() { MapName = "mp_custom", Likes = 2 }] },
                        new() { GameServerId = Guid.NewGuid(), Title = "Synthetic COD5", GameType = GameType.CallOfDuty5,
                            IsObservedHistory = true, Maps = [new() { MapName = "mp_history", Likes = 5, Dislikes = 1 }] },
                        new() { GameServerId = Guid.NewGuid(), Title = "Synthetic COD2", GameType = GameType.CallOfDuty2,
                            IsObservedHistory = true, Maps = [new() { MapName = "mp_cod2" }] },
                        new() { GameServerId = Guid.NewGuid(), Title = "Teamspeak", GameType = GameType.Unknown,
                            Maps = [new() { MapName = "not-a-game-map" }] },
                        new() { GameServerId = Guid.NewGuid(), Title = "Empty server", GameType = GameType.CallOfDuty4 }
                    ]
                })));
        var popular = new MapRatingDto { MapId = Guid.NewGuid(), MapName = "mp_popular", GameType = GameType.CallOfDuty4, Likes = 10, Dislikes = 2 };
        repository.Setup(r => r.MapAnalytics.V1.GetVoteInsights(It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<GameType?>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapInsightsDto>(HttpStatusCode.OK, new ApiResponse<MapInsightsDto>(
                new MapInsightsDto
                {
                    TotalLikes = 12, TotalDislikes = 2, RatedMaps = 2, Popular = [popular], Unpopular = [popular],
                    MostVoted = [popular, new() { MapName = "mp_low", GameType = GameType.CallOfDuty4, Likes = 2 }]
                })));
        return repository;
    }

    private static Task Json(IRoute route, object value)
    {
        return route.FulfillAsync(new RouteFulfillOptions { ContentType = "application/json", Body = System.Text.Json.JsonSerializer.Serialize(value) });
    }

    private const string Contrast = """
        element => {
            const luminance = color => {
                const rgb = color.match(/\d+/g).slice(0, 3).map(Number).map(value => {
                    const c = value / 255;
                    return c <= .04045 ? c / 12.92 : ((c + .055) / 1.055) ** 2.4;
                });
                return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
            };
            const style = getComputedStyle(element);
            const a = luminance(style.color), b = luminance(style.backgroundColor);
            return (Math.max(a, b) + .05) / (Math.min(a, b) + .05) >= 4.5;
        }
        """;
}
