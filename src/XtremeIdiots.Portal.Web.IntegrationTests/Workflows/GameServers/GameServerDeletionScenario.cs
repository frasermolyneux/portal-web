using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

internal sealed class GameServerDeletionScenario
{
    public GameServerDeletionScenario(bool deleteSucceeds = true)
    {
        GameServerId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        GameServer = CreateGameServer(GameServerId);
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.DeleteGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .Callback<Guid, CancellationToken>((id, _) => DeletedGameServerIds.Enqueue(id))
            .ReturnsAsync(new ApiResult(deleteSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError));
        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServers(
                It.IsAny<GameType[]?>(),
                It.IsAny<Guid[]?>(),
                It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<GameServerOrder>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([GameServer]))));
    }

    public ConcurrentQueue<Guid> DeletedGameServerIds { get; } = new();

    public GameServerDto GameServer { get; }

    public Guid GameServerId { get; }

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);
    }

    private static GameServerDto CreateGameServer(Guid gameServerId)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = gameServerId,
            Title = "CoD4 Server To Delete",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Windows.ToString(),
            Hostname = "127.0.0.2",
            QueryPort = 28961,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            RconEnabled = false,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 2,
        });

        return JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }
}
