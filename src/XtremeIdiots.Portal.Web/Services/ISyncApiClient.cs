namespace XtremeIdiots.Portal.Web.Services;

public interface ISyncApiClient
{
    Task<SyncTriggerResult> TriggerSync(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerActivate(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerDeactivate(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<SyncTriggerResult> TriggerRemove(Guid assignmentId, CancellationToken cancellationToken = default);
    Task<OrchestrationStatusQueryResult> GetOrchestrationStatus(string instanceId, CancellationToken cancellationToken = default);
    Task<bool> TerminateOrchestration(string instanceId, CancellationToken cancellationToken = default);
}

public record SyncTriggerResult(bool Success, string? InstanceId = null, string? Error = null);

public record OrchestrationStatusResult(
    string InstanceId,
    string RuntimeStatus,
    DateTime CreatedAt,
    DateTime LastUpdatedAt,
    OrchestrationProgressDto? Progress);

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
