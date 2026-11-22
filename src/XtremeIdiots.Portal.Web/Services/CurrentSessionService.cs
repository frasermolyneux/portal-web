using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Playtime;
using XtremeIdiots.Portal.Repository.Api.Client.V1;

namespace XtremeIdiots.Portal.Web.Services;

public sealed class CurrentSessionService(IRepositoryApiClient repository, ILogger<CurrentSessionService> logger)
{
    private readonly static TimeSpan MaximumAge = TimeSpan.FromMinutes(6);
    private readonly static TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);

    public static bool IsFresh(DateTime timestamp, DateTime now) => timestamp >= now - MaximumAge && timestamp <= now + ClockTolerance;

    public async Task<bool> IsRosterFreshAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var status = await repository.LiveStatus.V1.GetGameServerLiveStatus(serverId, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
            return status.IsSuccess && status.Result?.Data is { } liveStatus &&
                liveStatus.ServerId == serverId && liveStatus.GameType == gameType &&
                liveStatus.LastUpdated is { } lastUpdated && IsFresh(lastUpdated, DateTime.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Live roster freshness lookup timed out for server {ServerId}", serverId);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Live roster freshness lookup failed for server {ServerId}", serverId);
            return false;
        }
    }

    public async Task<Dictionary<Guid, long>> GetAsync(
        Guid serverId, GameType gameType, IEnumerable<Guid> playerIds, CancellationToken cancellationToken)
    {
        var ids = playerIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        var durations = new Dictionary<Guid, long>();
        if (ids.Length == 0)
            return durations;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            foreach (var batch in ids.Chunk(100))
            {
                var response = await repository.Playtime.V1.GetCurrentSessions(
                    serverId, new CurrentPlayerSessionsRequestDto { PlayerIds = batch }, timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!response.IsSuccess || response.Result?.Data is null)
                {
                    logger.LogWarning("Current session data unavailable for server {ServerId}", serverId);
                    continue;
                }

                var now = DateTime.UtcNow;
                var requested = batch.ToHashSet();
                foreach (var group in response.Result.Data.Where(result => requested.Contains(result.PlayerId)).GroupBy(result => result.PlayerId))
                {
                    if (group.Count() != 1)
                        continue;
                    var result = group.Single();
                    var session = result.Session;
                    if (result.Status != CurrentPlayerSessionStatus.Found || session is null ||
                        session.SessionId == Guid.Empty || session.ServerId != serverId || session.GameType != gameType ||
                        session.IsBot == true || session.ObservedSeconds < 0 || session.StartUtc == default ||
                        session.StartUtc > session.LastConfirmedUtc || !IsFresh(session.LastConfirmedUtc, now) ||
                        session.ObservedSeconds != (session.LastConfirmedUtc - session.StartUtc).Ticks / TimeSpan.TicksPerSecond)
                        continue;
                    durations.Add(result.PlayerId, session.ObservedSeconds);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Current session lookup timed out for server {ServerId}", serverId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Current session lookup failed for server {ServerId}", serverId);
        }

        return durations;
    }
}
