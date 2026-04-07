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

    public Task<OrchestrationStatusQueryResult> GetOrchestrationStatus(string instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new OrchestrationStatusQueryResult(OrchestrationStatusQueryOutcome.Error));
    }

    public Task<bool> TerminateOrchestration(string instanceId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }
}
