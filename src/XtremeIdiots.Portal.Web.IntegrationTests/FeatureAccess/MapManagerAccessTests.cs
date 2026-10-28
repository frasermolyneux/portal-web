using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed class MapManagerAccessTests : IAsyncLifetime
{
    private readonly Guid serverId = Guid.NewGuid();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };
    private readonly Mock<IServersApiClient> servers = new() { DefaultValue = DefaultValue.Mock };
    private PortalWebTestHost host = null!;

    public async Task InitializeAsync()
    {
        servers.Setup(x => x.Maps.V1.GetLoadedServerMapsFromHost(serverId))
            .ReturnsAsync(new ApiResult<ServerMapsCollectionDto>(HttpStatusCode.OK,
                new ApiResponse<ServerMapsCollectionDto>(new ServerMapsCollectionDto([]))));
        repository.Setup(x => x.Maps.V1.GetMaps(It.IsAny<GameType?>(), It.IsAny<string[]?>(), null,
            null, 0, 50, MapsOrder.MapNameAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapDto>>(new CollectionModel<MapDto>([]))));
        repository.Setup(x => x.MapRotations.V1.GetServerAssignments(null, serverId, null, 0, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationServerAssignmentDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationServerAssignmentDto>>(new CollectionModel<MapRotationServerAssignmentDto>([]))));
        repository.Setup(x => x.LiveStatus.V1.GetGameServerLiveStatus(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerLiveStatusDto>(HttpStatusCode.NotFound));
        host = await PortalWebTestHost.CreateAsync(services =>
        {
            services.RemoveAll<IRepositoryApiClient>();
            services.AddSingleton(repository.Object);
            services.RemoveAll<IServersApiClient>();
            services.AddSingleton(servers.Object);
        });
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.HeadAdminCod5, GameType.CallOfDuty5, true)]
    [InlineData(TestPrincipalProfiles.GameAdmin, GameType.CallOfDuty4, true)]
    [InlineData(TestPrincipalProfiles.GameAdmin, GameType.CallOfDuty4x, true)]
    [InlineData(TestPrincipalProfiles.HeadAdminCod5, GameType.CallOfDuty4, false)]
    [InlineData(TestPrincipalProfiles.GameAdmin, GameType.CallOfDuty5, false)]
    [InlineData(TestPrincipalProfiles.Moderator, GameType.CallOfDuty4, false)]
    public async Task Manage_UsesResolvedServerGameTypeForAccess(string profile, GameType gameType, bool allowed)
    {
        var server = Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(
            Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                GameServerId = serverId,
                GameType = gameType,
                Title = "Synthetic map server",
                Hostname = "192.0.2.10",
                QueryPort = 28960
            }));
        repository.Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server)));
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/MapManager/Manage/{serverId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Redirect, response.StatusCode);
        if (allowed)
            Assert.Contains("Synthetic map server", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        else
            Assert.Equal("/Errors/Display/401", response.Headers.Location?.ToString());
        servers.Verify(x => x.Maps.V1.GetLoadedServerMapsFromHost(serverId), allowed ? Times.Once() : Times.Never());
    }

    [Fact]
    public async Task Manage_RequiresAuthenticationBeforeLoadingServer()
    {
        using var response = await host.Client.GetAsync($"/MapManager/Manage/{serverId}");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Identity/Login?ReturnUrl=", response.Headers.Location?.OriginalString, StringComparison.Ordinal);
        repository.Verify(x => x.GameServers.V1.GetGameServer(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never());
        servers.Verify(x => x.Maps.V1.GetLoadedServerMapsFromHost(It.IsAny<Guid>()), Times.Never());
    }
}
