using Microsoft.Extensions.Caching.Memory;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.Services;

public sealed record ServerStatisticsOverview(
    IReadOnlyList<ServerPlayerCountPoint> PlayerCounts,
    IReadOnlyList<MapTimelineDataPoint> MapTimeline);

public sealed record ServerPlayerCountPoint(DateTime Timestamp, int PlayerCount);

public sealed class ServerStatisticsService(
    IRepositoryApiClient repositoryApiClient,
    IMemoryCache cache,
    DependencyReadCoordinator coordinator,
    ILogger<ServerStatisticsService> logger)
{
    public Task<OptionalOverviewData<ServerStatisticsOverview>> GetAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        return OptionalOverviewCache.ReadAsync(cache, coordinator, $"server-overview:stats:{serverId}", async token =>
        {
            var now = DateTime.UtcNow;
            var response = await repositoryApiClient.GameServersStats.V1
                .GetGameServerStatusStats(serverId, now.AddDays(-2), token).ConfigureAwait(false);
            if (!response.IsSuccess || response.Result?.Data?.Items is null)
                throw new InvalidOperationException("Server statistics are unavailable.");

            var stats = response.Result.Data.Items.OrderBy(s => s.Timestamp).ToList();
            var timeline = new List<MapTimelineDataPoint>();
            if (stats.Count > 0)
            {
                var current = stats[0];
                foreach (var stat in stats.Skip(1))
                {
                    if (current.MapName == stat.MapName)
                        continue;
                    timeline.Add(new MapTimelineDataPoint(current.MapName, current.Timestamp, stat.Timestamp));
                    current = stat;
                }

                timeline.Add(new MapTimelineDataPoint(current.MapName, current.Timestamp, now));
            }

            return new ServerStatisticsOverview(
                stats.Select(s => new ServerPlayerCountPoint(s.Timestamp, s.PlayerCount)).ToArray(), timeline);
        }, logger, cancellationToken);
    }
}
