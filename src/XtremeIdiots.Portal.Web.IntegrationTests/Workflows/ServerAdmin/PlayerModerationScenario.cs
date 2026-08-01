using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

internal sealed class PlayerModerationScenario
{
    public PlayerModerationScenario(
        bool rconSucceeds = true,
        bool persistenceSucceeds = true,
        bool repositoryPlayerMatches = true,
        string playerName = PlayerName)
    {
        GameServerId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        PlayerId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
        GameServer = CreateGameServer(GameServerId);
        LivePlayerName = playerName;
        Player = CreatePlayer(PlayerId, repositoryPlayerMatches ? PlayerGuid : "MISMATCHED-GUID", LivePlayerName);
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        ServersClient = new Mock<IServersApiClient>(MockBehavior.Default) { DefaultValue = DefaultValue.Mock };
        AdminActionTopics = new Mock<IAdminActionTopics>(MockBehavior.Strict);
        AgentTelemetry = new Mock<IAgentTelemetryService>(MockBehavior.Strict);
        var rconStatus = rconSucceeds ? HttpStatusCode.OK : HttpStatusCode.InternalServerError;

        Mock.Get(RepositoryClient.Object.GameServers.V1)
            .Setup(api => api.GetGameServer(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(GameServer)));
        Mock.Get(RepositoryClient.Object.Players.V1)
            .Setup(api => api.GetPlayers(
                GameType.CallOfDuty4,
                PlayersFilter.UsernameAndGuid,
                PlayerGuid,
                0,
                100,
                PlayersOrder.LastSeenDesc,
                PlayerEntityOptions.None))
            .ReturnsAsync(new ApiResult<CollectionModel<PlayerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<PlayerDto>>(new CollectionModel<PlayerDto>([Player]))));
        Mock.Get(RepositoryClient.Object.AdminActions.V1)
            .Setup(api => api.CreateAdminAction(It.IsAny<CreateAdminActionDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateAdminActionDto, CancellationToken>((action, _) => AttemptedAdminActions.Enqueue(action))
            .ReturnsAsync(new ApiResult(persistenceSucceeds ? HttpStatusCode.Created : HttpStatusCode.InternalServerError));

        Mock.Get(ServersClient.Object.Cod4Rcon.V1)
            .Setup(api => api.Status(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RconStatusResponseDto>(
                HttpStatusCode.OK,
                new ApiResponse<RconStatusResponseDto>(CreateStatus(LivePlayerName))));
        Mock.Get(ServersClient.Object.Cod4Rcon.V1)
            .Setup(api => api.Kick(GameServerId, It.IsAny<ClientSlotRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ClientSlotRequest, CancellationToken>((_, request, _) => Commands.Enqueue($"kick:{request.ClientId}"))
            .ReturnsAsync(new ApiResult<string>(rconStatus, new ApiResponse<string>("ok")));
        Mock.Get(ServersClient.Object.Cod4Rcon.V1)
            .Setup(api => api.TempBan(GameServerId, It.IsAny<ClientSlotRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ClientSlotRequest, CancellationToken>((_, request, _) => Commands.Enqueue($"temp ban:{request.ClientId}"))
            .ReturnsAsync(new ApiResult<string>(rconStatus, new ApiResponse<string>("ok")));
        Mock.Get(ServersClient.Object.Cod4Rcon.V1)
            .Setup(api => api.Ban(GameServerId, It.IsAny<ClientSlotRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, ClientSlotRequest, CancellationToken>((_, request, _) => Commands.Enqueue($"ban:{request.ClientId}"))
            .ReturnsAsync(new ApiResult<string>(rconStatus, new ApiResponse<string>("ok")));

        AdminActionTopics
            .Setup(topics => topics.CreateTopicForAdminAction(
                It.IsAny<AdminActionType>(),
                GameType.CallOfDuty4,
                PlayerId,
                LivePlayerName,
                It.IsAny<DateTime>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(654321);
        AgentTelemetry
            .Setup(service => service.GetServerStatusAsync(GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentServerStatus { ActivityStatus = AgentActivityStatus.Offline });
    }

    public const string PlayerGuid = "WORKFLOW-PLAYER-GUID";
    public const string PlayerName = "ConnectedPlayer";
    public const int PlayerSlot = 7;

    public Mock<IAdminActionTopics> AdminActionTopics { get; }
    public Mock<IAgentTelemetryService> AgentTelemetry { get; }
    public ConcurrentQueue<CreateAdminActionDto> AttemptedAdminActions { get; } = new();
    public ConcurrentQueue<string> Commands { get; } = new();
    public GameServerDto GameServer { get; }
    public Guid GameServerId { get; }
    public string LivePlayerName { get; }
    public PlayerDto Player { get; }
    public Guid PlayerId { get; }
    public Mock<IRepositoryApiClient> RepositoryClient { get; }
    public Mock<IServersApiClient> ServersClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.RemoveAll<IServersApiClient>();
        services.RemoveAll<IAdminActionTopics>();
        services.RemoveAll<IAgentTelemetryService>();
        services.AddSingleton(RepositoryClient.Object);
        services.AddSingleton(ServersClient.Object);
        services.AddSingleton(AdminActionTopics.Object);
        services.AddSingleton(AgentTelemetry.Object);
    }

    private static RconStatusResponseDto CreateStatus(string playerName)
    {
        return JsonConvert.DeserializeObject<RconStatusResponseDto>(JsonConvert.SerializeObject(new
        {
            Players = new[]
            {
                new
                {
                    Num = PlayerSlot,
                    Guid = PlayerGuid,
                    Name = playerName,
                    IpAddress = string.Empty,
                    Rate = 25000,
                    Ping = 42,
                },
            },
        }))!;
    }

    private static GameServerDto CreateGameServer(Guid id)
    {
        return JsonConvert.DeserializeObject<GameServerDto>(JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = "Player Moderation CoD4 Server",
            GameType = GameType.CallOfDuty4.ToString(),
            Platform = GameServerPlatform.Linux.ToString(),
            Hostname = "127.0.0.6",
            QueryPort = 28965,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            RconEnabled = true,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 6,
        }))!;
    }

    private static PlayerDto CreatePlayer(Guid id, string playerGuid, string playerName)
    {
        return JsonConvert.DeserializeObject<PlayerDto>(JsonConvert.SerializeObject(new
        {
            PlayerId = id,
            GameType = GameType.CallOfDuty4.ToString(),
            Username = playerName,
            Guid = playerGuid,
            IpAddress = "127.0.0.7",
            FirstSeen = DateTime.UtcNow.AddDays(-7),
            LastSeen = DateTime.UtcNow,
        }))!;
    }
}
