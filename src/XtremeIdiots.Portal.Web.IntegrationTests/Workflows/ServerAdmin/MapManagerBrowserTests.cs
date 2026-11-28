using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "Browser")]
public sealed class MapManagerBrowserTests
{
    [Fact]
    public async Task CurrentRotationTable_PreservesImageSortAndRemoteStatusPriority()
    {
        var scenario = new MapControlScenario();
        scenario.ServersClient.Setup(x => x.Maps.V1.GetLoadedServerMapsFromHost(scenario.GameServerId))
            .ReturnsAsync(new ApiResult<ServerMapsCollectionDto>(HttpStatusCode.OK,
                new ApiResponse<ServerMapsCollectionDto>(new ServerMapsCollectionDto([]))));
        scenario.RepositoryClient.Setup(x => x.Maps.V1.GetMaps(It.IsAny<GameType?>(), It.IsAny<string[]?>(), null,
            null, 0, 50, MapsOrder.MapNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>([]))));
        scenario.RepositoryClient.Setup(x => x.MapRotations.V1.GetServerAssignments(null, scenario.GameServerId, null, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationServerAssignmentDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationServerAssignmentDto>>(new CollectionModel<MapRotationServerAssignmentDto>([]))));
        scenario.RepositoryClient.Setup(x => x.MapRotations.V1.GetEffectiveServerRotation(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<PublicServerRotationDto>(HttpStatusCode.OK,
                new ApiResponse<PublicServerRotationDto>(new PublicServerRotationDto
                {
                    GameServerId = scenario.GameServerId,
                    IsAvailable = true,
                    Source = RotationSource.ServerObserved,
                    Maps = [new() { MapName = "mp_first", GameMode = "war" }, new() { MapName = "mp_second", GameMode = "dm" }]
                })));
        scenario.RepositoryClient.Setup(x => x.LiveStatus.V1.GetGameServerLiveStatus(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerLiveStatusDto>(HttpStatusCode.NotFound));

        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.GameAdmin, scenario.ConfigureServices);
        await browser.Page.SetViewportSizeAsync(1440, 900);
        var response = await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/MapManager/Manage/{scenario.GameServerId}").AbsoluteUri);
        Assert.True(response?.Ok);
        var table = browser.Page.Locator("#mapRotationTable");
        await Assertions.Expect(table).ToHaveClassAsync(new Regex(@"\bdataTable\b"));
        await Assertions.Expect(table.Locator("tbody tr")).ToHaveCountAsync(2);
        var initialOrder = await browser.Page.EvaluateAsync<string>("() => JSON.stringify($('#mapRotationTable').DataTable().order())");
        await table.Locator("thead th").Nth(6).ClickAsync();
        Assert.Equal(initialOrder, await browser.Page.EvaluateAsync<string>("() => JSON.stringify($('#mapRotationTable').DataTable().order())"));

        var prioritizedRemoteStatus = false;
        for (var width = 620; width >= 320; width -= 20)
        {
            await browser.Page.SetViewportSizeAsync(width, 900);
            await browser.Page.EvaluateAsync("() => { $('#mapRotationTable').DataTable().columns.adjust().responsive.recalc(); }");
            if (!await table.Locator("thead th").Nth(3).IsVisibleAsync()
                && await table.Locator("thead th").Nth(4).IsVisibleAsync())
            {
                prioritizedRemoteStatus = true;
                break;
            }
        }

        Assert.True(prioritizedRemoteStatus, "Remote status should remain visible after map files are hidden.");
        browser.AssertNoBrowserErrors();
    }
}
