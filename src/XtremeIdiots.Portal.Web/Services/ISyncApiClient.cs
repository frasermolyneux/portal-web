using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;

namespace XtremeIdiots.Portal.Web.Services;

public interface ISyncApiClient
{
    Task<SyncTriggerResult> TriggerApply(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerSync(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerActivate(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerDeactivate(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerRemove(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerVerify(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerPushMap(Guid gameServerId, GameType gameType, string mapName, CancellationToken cancellationToken = default);
    Task<OrchestrationStatusQueryResult> GetOrchestrationStatus(string instanceId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TerminateOrchestration(string instanceId, CancellationToken cancellationToken = default);
}

public record SyncTriggerResult(bool Success, string? InstanceId = null, string? Error = null, bool CancellationRequested = false);

public record OrchestrationStatusResult(
    string InstanceId,
    string RuntimeStatus,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    OrchestrationProgressDto? Progress,
    bool? Success = null,
    string? Error = null);

public record OrchestrationStatusQueryResult(
    OrchestrationStatusQueryOutcome Outcome,
    OrchestrationStatusResult? Result = null);

public enum OrchestrationStatusQueryOutcome
{
    Found,
    NotFound,
    Error
}

public record OrchestrationProgressDto(
    string Operation,
    int TotalMaps,
    int CompletedMaps,
    List<MapProgressDto> Maps);

public record MapProgressDto(string MapName, string Status, string? Error);
