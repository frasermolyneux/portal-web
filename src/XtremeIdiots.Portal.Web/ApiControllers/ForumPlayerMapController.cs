using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[ApiController]
[Route("api/integration/v1/player-map")]
[EnableCors("ForumIntegration")]
[SkipStatusCodePages]
public sealed class ForumPlayerMapController(
    IRepositoryApiClient repository, IAuthorizationService authorization, ILogger<ForumPlayerMapController> logger) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        const int pageSize = 200;
        var now = DateTime.UtcNow;
        var since = now.AddHours(-48);
        var statusesTask = repository.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken);
        var recentTask = repository.RecentPlayers.V1.GetRecentPlayers(null, null, since, RecentPlayersFilter.GeoLocated,
            0, pageSize, RecentPlayersOrder.TimestampDesc, cancellationToken);
        var publicServers = new Dictionary<Guid, GameType>();
        var skip = 0;
        do
        {
            var page = await repository.GameServers.V1.GetGameServers(null, null, GameServerFilter.ServerListEnabled,
                skip, pageSize, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);
            if (!page.IsSuccess || page.Result?.Data?.Items is not { } items ||
                page.Result.Pagination?.FilteredCount is not { } total)
            {
                await Task.WhenAll(statusesTask, recentTask).ConfigureAwait(false);
                return Unavailable("public servers");
            }

            var count = 0;
            foreach (var server in items)
            {
                count++;
                if (server.ServerListEnabled)
                    publicServers.TryAdd(server.GameServerId, server.GameType);
            }

            skip += count;
            if (skip >= total)
                break;
            if (count == 0)
            {
                await Task.WhenAll(statusesTask, recentTask).ConfigureAwait(false);
                return Unavailable("public server pagination");
            }
        } while (true);

        await Task.WhenAll(statusesTask, recentTask).ConfigureAwait(false);
        var statuses = await statusesTask.ConfigureAwait(false);
        var recent = await recentTask.ConfigureAwait(false);
        if (!statuses.IsSuccess || statuses.Result?.Data?.Items is not { } statusItems)
            return Unavailable("live server status");
        if (!recent.IsSuccess || recent.Result?.Data?.Items is not { } recentItems)
            return Unavailable("recent observations");

        var liveServers = statusItems.Where(s => s.IsOnline && s.CurrentPlayers > 0 && publicServers.ContainsKey(s.ServerId))
            .Select(s => s.ServerId).Distinct();
        var liveIds = new HashSet<Guid>();
        var locatedIds = new HashSet<Guid>();
        var observations = new List<Observation>();
        foreach (var batch in liveServers.Chunk(4))
        {
            var snapshots = await Task.WhenAll(batch.Select(async id =>
                (id, result: await repository.LiveStatus.V1.GetGameServerLivePlayers(id, cancellationToken).ConfigureAwait(false))))
                .ConfigureAwait(false);
            foreach (var (id, result) in snapshots)
            {
                if (!result.IsSuccess || result.Result?.Data?.Items is not { } livePlayers)
                    return Unavailable("live observations");

                foreach (var player in livePlayers)
                {
                    if (player.PlayerId is { } playerId && playerId != Guid.Empty)
                        liveIds.Add(playerId);
                    AddObservation(player.PlayerId, publicServers[id], "live", player.Name,
                        player.GeoIntelligence?.Latitude, player.GeoIntelligence?.Longitude);
                }
            }
        }

        foreach (var player in recentItems.OrderByDescending(p => p.Timestamp))
        {
            if (player.GameServerId is not { } id || !publicServers.TryGetValue(id, out var game) ||
                (player.PlayerId is { } playerId && liveIds.Contains(playerId)))
                continue;
            AddObservation(player.PlayerId, game, "recentlySeen", player.Name, player.Lat, player.Long);
        }

        var points = observations.GroupBy(row => (row.Lat, row.Lng, row.Game, row.Activity))
            // Repeated unidentified recent rows cannot establish that a cell contains several players.
            .Where(cell => cell.Count(row => row.Identified || row.Activity == "live") >= 3)
            .Select(cell => new MapPoint(cell.Key.Lat, cell.Key.Lng, cell.Key.Game.ToString(), cell.Key.Activity, cell.Count()))
            .ToArray();
        var players = new List<MapPlayer>();
        if (User.Identity?.IsAuthenticated == true)
        {
            foreach (var game in observations.Where(row => !string.IsNullOrWhiteSpace(row.Name)).GroupBy(row => row.Game))
            {
                if (!(await authorization.AuthorizeAsync(User, game.Key, AuthPolicies.Players_Read).ConfigureAwait(false)).Succeeded)
                    continue;
                players.AddRange(game.Select(row => new MapPlayer(row.Lat, row.Lng, game.Key.ToString(), row.Activity, row.Name!)));
            }
        }

        return Ok(new PlayerMap(now, since, points, players));

        void AddObservation(Guid? playerId, GameType game, string activity, string? name, double? latitude, double? longitude)
        {
            if (latitude is not (>= -90 and <= 90) || longitude is not (>= -180 and <= 180) ||
                (latitude == 0 && longitude == 0))
                return;
            var identified = playerId is { } id && id != Guid.Empty;
            if (identified && !locatedIds.Add(playerId!.Value))
                return;
            observations.Add(new Observation(CellCenter(latitude.Value, 90), CellCenter(longitude.Value, 180),
                game, activity, name, identified));
        }
    }

    private IActionResult Unavailable(string dependency)
    {
        logger.LogWarning("Forum player map is unavailable: {Dependency}", dependency);
        return StatusCode(StatusCodes.Status503ServiceUnavailable);
    }

    private static double CellCenter(double coordinate, int limit) =>
        Math.Min(Math.Floor(coordinate / 5) * 5 + 2.5, limit - 2.5);

    private sealed record Observation(double Lat, double Lng, GameType Game, string Activity, string? Name, bool Identified);
    public sealed record MapPoint(double Lat, double Lng, string GameType, string Activity, int Count);
    public sealed record MapPlayer(double Lat, double Lng, string GameType, string Activity, string Name);
    public sealed record PlayerMap(DateTime AsOfUtc, DateTime RecentSinceUtc, IReadOnlyList<MapPoint> Points, IReadOnlyList<MapPlayer> Players);
}
