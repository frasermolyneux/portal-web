using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.MapRotations;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

internal sealed class MapRotationRecoveryScenario
{
    public Guid AssignmentId { get; } = Guid.NewGuid();
    public Guid ChildAssignmentId { get; } = Guid.NewGuid();
    public Guid RotationId { get; } = Guid.NewGuid();
    public Mock<ISyncApiClient> Sync { get; } = new();
    public Mock<IRepositoryApiClient> Repository { get; } = new() { DefaultValue = DefaultValue.Mock };

    public MapRotationRecoveryScenario(DeploymentState state = DeploymentState.Synced, bool busy = false, bool permitted = true,
        AssignmentOperationStatus operationStatus = AssignmentOperationStatus.InProgress, DateTime? completedAt = null,
        bool combined = false, bool childFailed = false, bool childBusy = false, bool fileTransportEnabled = false, bool mainOnly = false,
        GameType gameType = GameType.CallOfDuty4)
    {
        var serverId = permitted ? Guid.Parse(TestPrincipalProfiles.MapRotationDeployServerId) : Guid.NewGuid();
        var assignment = new MapRotationServerAssignmentDto(AssignmentId, RotationId, serverId,
            state, ActivationState.Active, 1, 1, "synthetic.cfg", combined || mainOnly ? "sv_maprotation" : "scr_aacp_maps_small",
            combined ? null : "Synthetic verification error on active configuration", DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null);
        var picker = new MapRotationServerAssignmentDto(ChildAssignmentId, RotationId, serverId,
            state, childFailed ? ActivationState.Failed : ActivationState.Active, 1, 1, "aacp.cfg", "scr_aacp_maps_1",
            childFailed ? "Synthetic AACP configuration failure" : null, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null);
        var rotation = new MapRotationDto(RotationId, gameType, "Synthetic Recovery", null, "dm",
            2, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, [], combined ? [picker, assignment] : [assignment]);
        Repository.Setup(x => x.MapRotations.V1.GetMapRotation(RotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));
        Repository.Setup(x => x.MapRotations.V1.GetServerAssignment(AssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(assignment)));
        Repository.Setup(x => x.MapRotations.V1.GetServerAssignment(ChildAssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(picker)));
        Repository.Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(
                Newtonsoft.Json.JsonConvert.DeserializeObject<GameServerDto>(Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    GameServerId = serverId,
                    GameType = GameType.CallOfDuty4,
                    FileTransportEnabled = fileTransportEnabled,
                    FileTransportType = fileTransportEnabled ? FileTransportType.Sftp : FileTransportType.Unknown
                }))!)));
        Repository.Setup(x => x.GameServers.V1.GetGameServers(
                It.IsAny<GameType[]>(), It.IsAny<Guid[]?>(), It.IsAny<GameServerFilter?>(),
                It.IsAny<int>(), It.IsAny<int>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));
        Repository.Setup(x => x.MapRotations.V1.GetMapRotations(
                It.IsAny<GameType[]>(), null, null, 0, 100, MapRotationsOrder.TitleAsc, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationDto>>(new CollectionModel<MapRotationDto>([rotation]))));
        var operations = new List<MapRotationAssignmentOperationDto>();
        if (busy)
        {
            operations.Add(Newtonsoft.Json.JsonConvert.DeserializeObject<MapRotationAssignmentOperationDto>(
                Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    MapRotationAssignmentOperationId = Guid.NewGuid(),
                    MapRotationServerAssignmentId = AssignmentId,
                    DurableFunctionInstanceId = $"maprot-apply-{AssignmentId}",
                    StartedAt = completedAt?.AddMinutes(-2) ?? DateTime.UtcNow,
                    CompletedAt = completedAt,
                    Status = operationStatus
                }))!);
        }

        Repository.Setup(x => x.MapRotations.V1.GetAssignmentOperations(
                AssignmentId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationAssignmentOperationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationAssignmentOperationDto>>(new CollectionModel<MapRotationAssignmentOperationDto>(operations))));
        var childOperations = new List<MapRotationAssignmentOperationDto>();
        if (childFailed || childBusy)
        {
            childOperations.Add(Newtonsoft.Json.JsonConvert.DeserializeObject<MapRotationAssignmentOperationDto>(
                Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    MapRotationAssignmentOperationId = Guid.NewGuid(),
                    MapRotationServerAssignmentId = ChildAssignmentId,
                    DurableFunctionInstanceId = $"maprot-apply-{AssignmentId}",
                    StartedAt = DateTime.UtcNow.AddMinutes(-3),
                    CompletedAt = childFailed ? DateTime.UtcNow.AddMinutes(-1) : (DateTime?)null,
                    Status = childFailed ? AssignmentOperationStatus.Failed : AssignmentOperationStatus.InProgress,
                    Error = childFailed ? "Synthetic child operation failed" : null
                }))!);
        }

        Repository.Setup(x => x.MapRotations.V1.GetAssignmentOperations(
                ChildAssignmentId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationAssignmentOperationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationAssignmentOperationDto>>(new CollectionModel<MapRotationAssignmentOperationDto>(childOperations))));
        Sync.Setup(x => x.TriggerApply(AssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        Sync.Setup(x => x.GetOrchestrationStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult($"maprot-apply-{AssignmentId}", "Running", DateTime.UtcNow, DateTime.UtcNow, null)));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(Repository.Object);
        services.RemoveAll<ISyncApiClient>();
        services.AddSingleton(Sync.Object);
    }
}
