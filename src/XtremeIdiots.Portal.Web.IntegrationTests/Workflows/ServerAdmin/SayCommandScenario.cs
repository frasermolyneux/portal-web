using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

internal sealed class SayCommandScenario
{
    public SayCommandScenario(bool saySucceeds = true)
    {
        GameServerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        GameServer = CreateGameServer(GameServerId);
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        ServersClient = new Mock<IServersApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        AgentTelemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(ServersClient.Object.Cod4Rcon.V1)
            .Setup(api => api.Say(GameServerId, It.IsAny<SayRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, SayRequest, CancellationToken>((_, request, _) => Messages.Enqueue(request.Message ?? string.Empty))
            .ReturnsAsync(new ApiResult<SayRequest>(saySucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError));
        AgentTelemetry
            .Setup(service => service.GetServerStatusAsync(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentServerStatus { ActivityStatus = AgentActivityStatus.Offline });
    }

    public Mock<IAgentTelemetryService> AgentTelemetry { get; }
    public GameServerDto GameServer { get; }
    public Guid GameServerId { get; }
    public ConcurrentQueue<string> Messages { get; } = new();
    public Mock<IRepositoryApiClient> RepositoryClient { get; }
    public Mock<IServersApiClient> ServersClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IServersApiClient>();
        services.RemoveAll<IAgentTelemetryService>();
        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(ServersClient.Object);
        services.AddSingleton(AgentTelemetry.Object);
    }

    private static GameServerDto CreateGameServer(Guid id)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = "Live CoD4 Server",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Linux.ToString(),
            Hostname = "127.0.0.4",
            QueryPort = 28963,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 4,
        }))!;
    }
}
