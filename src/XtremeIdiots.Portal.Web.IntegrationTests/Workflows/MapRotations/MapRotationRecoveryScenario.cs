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
    public Guid RotationId { get; } = Guid.NewGuid();
    public Mock<ISyncApiClient> Sync { get; } = new();
    private readonly Mock<IRepositoryApiClient> repository = new() { DefaultValue = DefaultValue.Mock };

    public MapRotationRecoveryScenario(DeploymentState state = DeploymentState.Synced, bool busy = false, bool permitted = true)
    {
        var serverId = permitted ? Guid.Parse(TestPrincipalProfiles.MapRotationDeployServerId) : Guid.NewGuid();
        var assignment = new MapRotationServerAssignmentDto(AssignmentId, RotationId, serverId,
            state, ActivationState.Active, 1, 1, "synthetic.cfg", "scr_aacp_maps_small",
            "Synthetic verification error on active configuration", DateTime.UtcNow,
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null);
        var rotation = new MapRotationDto(RotationId, GameType.CallOfDuty4, "Synthetic Recovery", null, "dm",
            2, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, [], [assignment]);
        repository.Setup(x => x.MapRotations.V1.GetMapRotation(RotationId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationDto>(HttpStatusCode.OK, new ApiResponse<MapRotationDto>(rotation)));
        repository.Setup(x => x.MapRotations.V1.GetServerAssignment(AssignmentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<MapRotationServerAssignmentDto>(HttpStatusCode.OK, new ApiResponse<MapRotationServerAssignmentDto>(assignment)));
        repository.Setup(x => x.GameServers.V1.GetGameServer(serverId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(new GameServerDto())));
        repository.Setup(x => x.MapRotations.V1.GetMapRotations(
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
                    StartedAt = DateTime.UtcNow,
                    Status = AssignmentOperationStatus.InProgress
                }))!);
        }

        repository.Setup(x => x.MapRotations.V1.GetAssignmentOperations(
                AssignmentId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<MapRotationAssignmentOperationDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<MapRotationAssignmentOperationDto>>(new CollectionModel<MapRotationAssignmentOperationDto>(operations))));
        Sync.Setup(x => x.TriggerApply(AssignmentId, It.IsAny<CancellationToken>())).ReturnsAsync(new SyncTriggerResult(true));
        Sync.Setup(x => x.GetOrchestrationStatus(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Found,
                new OrchestrationStatusResult($"maprot-apply-{AssignmentId}", "Running", DateTime.UtcNow, DateTime.UtcNow, null)));
    }

    public void ConfigureServices(IServiceCollection services)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(repository.Object);
        services.RemoveAll<ISyncApiClient>();
        services.AddSingleton(Sync.Object);
    }
}
