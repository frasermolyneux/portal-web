using Microsoft.Extensions.Caching.Memory;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class CachedAgentTelemetryService(
    IAgentTelemetryService telemetry,
    IMemoryCache cache,
    DependencyReadCoordinator coordinator,
    ILogger<CachedAgentTelemetryService> logger)
{
    public Task<OptionalOverviewData<AgentServerStatus>> GetServerAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        return OptionalOverviewCache.ReadAsync(cache, coordinator, $"server-overview:telemetry:{serverId}",
            token => telemetry.GetServerStatusAsync(serverId, token), logger, cancellationToken);
    }

    public Task<OptionalOverviewData<IReadOnlyList<AgentServerSummary>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return OptionalOverviewCache.ReadAsync(cache, coordinator, "server-overview:telemetry:all",
            telemetry.GetAllServersStatusAsync, logger, cancellationToken);
    }
}
