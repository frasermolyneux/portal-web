namespace XtremeIdiots.Portal.Web.Services;

public class NoOpSyncApiClient : ISyncApiClient
{
    public Task<SyncTriggerResult> TriggerSync(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SyncTriggerResult(false, Error: "Sync API not configured"));
    }

    public Task<SyncTriggerResult> TriggerActivate(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SyncTriggerResult(false, Error: "Sync API not configured"));
    }

    public Task<SyncTriggerResult> TriggerDeactivate(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SyncTriggerResult(false, Error: "Sync API not configured"));
    }

    public Task<SyncTriggerResult> TriggerRemove(Guid assignmentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new SyncTriggerResult(false, Error: "Sync API not configured"));
    }

    public Task<OrchestrationStatusResult?> GetOrchestrationStatus(string instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<OrchestrationStatusResult?>(null);
    }
}
