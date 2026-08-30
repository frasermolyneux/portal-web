using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

internal sealed class MapRotationAssignmentScenario
{
    public readonly static Guid RotationId = Guid.Parse("aaaa1111-1111-1111-1111-111111111111");
    public readonly static Guid PermittedServerId = Guid.Parse(TestPrincipalProfiles.MapRotationDeployServerId);
    public readonly static Guid NonPermittedServerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

    public MapRotationAssignmentScenario()
    {
        RepositoryClient = new Mock<IRepositoryApiClient>(MockBehavior.Default)
        {
            DefaultValue = DefaultValue.Mock,
        };

        var rotation = CreateRotation();
        var permittedServer = CreateGameServer(PermittedServerId, GameType.CallOfDuty4x, "COD4x Permitted Server");
        var nonPermittedServer = CreateGameServer(NonPermittedServerId, GameType.CallOfDuty4x, "COD4x Non-Permitted Server");

        // GetMapRotation — returns the COD4 rotation
        RepositoryClient
            .Setup(x => x.MapRotations.V1.GetMapRotation(RotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));

        // GetGameServers — returns both servers (COD4x) for the compatible-server enumeration.
        // Because the rotation is COD4, the controller queries [COD4, COD4x] via equivalence.
        RepositoryClient
            .Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), It.IsAny<Guid[]?>(), It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(
                    new CollectionModel<GameServerDto>([permittedServer, nonPermittedServer]))));

        // GetGameServer — used by CreateAssignment POST to validate the submitted server
        RepositoryClient
            .Setup(x => x.GameServers.V1.GetGameServer(PermittedServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(permittedServer)));

        RepositoryClient
            .Setup(x => x.GameServers.V1.GetGameServer(NonPermittedServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(nonPermittedServer)));

        // GetAssignmentOperations — empty for Details page rendering
        RepositoryClient
            .Setup(x => x.MapRotations.V1.GetAssignmentOperations(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationAssignmentOperationDto>>(
                HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationAssignmentOperationDto>>(
                    new CollectionModel<MapRotationAssignmentOperationDto>([]))));

        // CreateServerAssignment — records the dto for assertion
        RepositoryClient
            .Setup(x => x.MapRotations.V1.CreateServerAssignment(
                It.IsAny<CreateMapRotationServerAssignmentDto>(), It.IsAny<CancellationToken>()))
            .Callback<CreateMapRotationServerAssignmentDto, CancellationToken>((dto, _) => CreatedAssignments.Enqueue(dto))
            .ReturnsAsync((CreateMapRotationServerAssignmentDto dto, CancellationToken _) =>
            {
                var result = CreateAssignmentDto(dto.MapRotationId, dto.GameServerId);
                return new ApiResult<MapRotationServerAssignmentDto>(
                    HttpStatusCode.OK,
                    new ApiResponse<MapRotationServerAssignmentDto>(result));
            });
    }

    public ConcurrentQueue<CreateMapRotationServerAssignmentDto> CreatedAssignments { get; } = new();

    public Mock<IRepositoryApiClient> RepositoryClient { get; }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(RepositoryClient.Object);
    }

    private static MapRotationDto CreateRotation()
    {
        var json = JsonConvert.SerializeObject(new
        {
            MapRotationId = RotationId,
            GameType = GameType.CallOfDuty4.ToString(),
            Title = "COD4 Test Rotation",
            Description = "Test rotation for COD4/COD4x equivalence",
            GameMode = "tdm",
            Status = MapRotationStatus.Published.ToString(),
            Version = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow.AddHours(-1),
            MapRotationMaps = Array.Empty<object>(),
            ServerAssignments = Array.Empty<object>(),
        });

        return JsonConvert.DeserializeObject<MapRotationDto>(json)!;
    }

    private static GameServerDto CreateGameServer(Guid id, GameType gameType, string title)
    {
        var json = JsonConvert.SerializeObject(new
        {
            GameServerId = id,
            Title = title,
            GameType = gameType.ToString(),
            Platform = GameServerPlatform.Windows.ToString(),
            Hostname = "127.0.0.1",
            QueryPort = 28960,
            AgentEnabled = false,
            FileTransportEnabled = false,
            FileTransportType = FileTransportType.Ftp.ToString(),
            RconEnabled = false,
            BanFileSyncEnabled = false,
            BanFileRootPath = "/",
            ServerListEnabled = false,
            ServerListPosition = 1,
        });

        return JsonConvert.DeserializeObject<GameServerDto>(json)!;
    }

    private static MapRotationServerAssignmentDto CreateAssignmentDto(Guid rotationId, Guid serverId)
    {
        var json = JsonConvert.SerializeObject(new
        {
            MapRotationServerAssignmentId = Guid.NewGuid(),
            MapRotationId = rotationId,
            GameServerId = serverId,
            DeploymentState = DeploymentState.Pending.ToString(),
            ActivationState = ActivationState.Inactive.ToString(),
            ConfigFilePath = "server.cfg",
            ConfigVariableName = "sv_maprotation",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        return JsonConvert.DeserializeObject<MapRotationServerAssignmentDto>(json)!;
    }
}
