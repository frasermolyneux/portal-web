using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for public server information pages. All actions are intentionally
/// anonymous — these endpoints surface public-safe data only (no IPs, GUIDs, risk
/// data, or player profiles). Do not add per-action [Authorize] without revisiting
/// the data-shape projections in each endpoint.
/// </summary>
[AllowAnonymous]
public class ServersController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<ServersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    /// <summary>
    /// Displays the main servers listing page
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                null, null, GameServerFilter.ServerListEnabled, 0, 50,
                GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve game servers for user {UserId}. API Success: {IsSuccess}",
                    User.XtremeIdiotsId(), gameServersApiResponse.IsSuccess);
                return RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController).Replace("Controller", ""), new { id = 500 });
            }

            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken).ConfigureAwait(false);
            var liveStatusLookup = liveStatusResponse.IsSuccess && liveStatusResponse.Result?.Data?.Items is not null
                ? liveStatusResponse.Result.Data.Items.ToDictionary(ls => ls.ServerId)
                : [];

            var result = gameServersApiResponse.Result.Data.Items
                .Select(gs =>
                {
                    liveStatusLookup.TryGetValue(gs.GameServerId, out var liveStatus);
                    return new ServersGameServerViewModel(gs, liveStatus);
                })
                .ToList();

            Logger.LogInformation("User {UserId} successfully retrieved {ServerCount} servers",
                User.XtremeIdiotsId(), result.Count);

            return View(result);
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the public server detail page with overview, maps, and player map tabs
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {ServerId} not found for public Details", id);
                return NotFound();
            }

            var gs = gameServerApiResponse.Result.Data;

            if (!gs.ServerListEnabled)
            {
                Logger.LogWarning("User {UserId} attempted to access non-public server {ServerId}", User.XtremeIdiotsId(), id);
                return NotFound();
            }

            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1
                .GetGameServerLiveStatus(gs.GameServerId, cancellationToken).ConfigureAwait(false);

            var viewModel = new ServerInfoViewModel
            {
                GameServer = gs,
                LiveStatus = liveStatusResponse.IsSuccess ? liveStatusResponse.Result?.Data : null
            };

            return View(viewModel);
        }, nameof(Details)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewStatistics(
        Guid id, [FromServices] ServerStatisticsService statistics, string panel = "players",
        CancellationToken cancellationToken = default)
    {
        var response = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data is not { ServerListEnabled: true })
            return NotFound();

        if (panel is not ("players" or "timeline"))
            return BadRequest();

        ViewData["ChartId"] = panel == "timeline" ? "si-mapsTimeline" : "si-playerCountChart";
        ViewData["ChartKind"] = panel;
        return PartialView("_ServerStatistics", await statistics.GetAsync(id, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Returns live player data for a server, projecting only public-safe fields
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLivePlayers(Guid id, [FromServices] CurrentSessionService sessions, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
            if (gameServerApiResponse.IsNotFound)
            {
                return Json(new { data = Array.Empty<object>() });
            }

            if (!gameServerApiResponse.IsSuccess || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Public server metadata unavailable while reading live players for {ServerId}. API Success: {IsSuccess}",
                    id, gameServerApiResponse.IsSuccess);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "Live player data is currently unavailable." });
            }

            if (!gameServerApiResponse.Result.Data.ServerListEnabled)
                return Json(new { data = Array.Empty<object>() });

            var livePlayersResponse = await repositoryApiClient.LiveStatus.V1.GetGameServerLivePlayers(id, cancellationToken).ConfigureAwait(false);

            if (!livePlayersResponse.IsSuccess || livePlayersResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Live player data unavailable for public server {ServerId}. API Success: {IsSuccess}",
                    id, livePlayersResponse.IsSuccess);
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    new { error = "Live player data is currently unavailable." });
            }

            var livePlayers = livePlayersResponse.Result.Data.Items;
            var uniquePlayers = livePlayers.Where(p => p.PlayerId is not null && p.PlayerId != Guid.Empty)
                .GroupBy(p => p.PlayerId!.Value).Where(group => group.Count() == 1)
                .Select(group => group.Key).ToHashSet();
            var durations = uniquePlayers.Count > 0 && await sessions.IsRosterFreshAsync(
                id, gameServerApiResponse.Result.Data.GameType, cancellationToken).ConfigureAwait(false)
                ? await sessions.GetAsync(id, gameServerApiResponse.Result.Data.GameType, uniquePlayers, cancellationToken).ConfigureAwait(false)
                : [];

            // Project only public-safe fields — no IPs, GUIDs, risk data, or player profiles
            var safePlayers = livePlayers
                .OrderBy(p => p.Num)
                .Select(p => new
                {
                    num = p.Num,
                    name = p.Name,
                    score = p.Score,
                    ping = p.Ping,
                    countryCode = p.GeoIntelligence?.CountryCode,
                    countryName = p.GeoIntelligence?.CountryName,
                    currentSessionObservedSeconds = p.PlayerId is { } playerId && uniquePlayers.Contains(playerId) &&
                        (p.GameServerServerId is null || p.GameServerServerId == id) &&
                        p.GameType == gameServerApiResponse.Result.Data.GameType &&
                        durations.TryGetValue(playerId, out var seconds) ? (long?)seconds : null
                })
                .ToList();

            return Json(new { data = safePlayers });
        }, nameof(GetLivePlayers)).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the map rotation for a server from the repository (not RCON)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPublicMapRotation(Guid id, [FromServices] MapExperienceService maps, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var rotation = (await maps.Rotations(cancellationToken).ConfigureAwait(false)).Servers
                .SingleOrDefault(s => s.GameServerId == id);
            if (rotation is null)
            {
                return Json(new { success = false, maps = Array.Empty<object>() });
            }

            return Json(new
            {
                success = true,
                maps = rotation.Maps.Select(map => new
                {
                    map.MapName,
                    mapTitle = map.MapName,
                    map.MapImageUri,
                    hasImage = !string.IsNullOrWhiteSpace(map.MapImageUri),
                    map.Likes,
                    map.Dislikes,
                    map.Votes,
                    map.Approval
                }),
                rotationTitle = rotation.RotationTitle,
                rotation.IsObservedHistory
            });
        }, nameof(GetPublicMapRotation)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the interactive map view showing recent player locations across
    /// public (server-list-enabled) servers only. Players from non-public servers
    /// are filtered out, and only coordinates + game type are projected.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Map(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var publicServersTask = repositoryApiClient.GameServers.V1.GetGameServers(
                null, null, GameServerFilter.ServerListEnabled, 0, 200,
                GameServerOrder.ServerListPosition, cancellationToken);
            var recentPlayersTask = repositoryApiClient.RecentPlayers.V1.GetRecentPlayers(
                null, null, DateTime.UtcNow.AddHours(-48), RecentPlayersFilter.GeoLocated,
                0, 200, null, cancellationToken);

            await Task.WhenAll(publicServersTask, recentPlayersTask).ConfigureAwait(false);

            var publicServersResponse = await publicServersTask.ConfigureAwait(false);
            var recentPlayersResponse = await recentPlayersTask.ConfigureAwait(false);

            if (!recentPlayersResponse.IsSuccess || recentPlayersResponse.Result?.Data?.Items is null
                || !publicServersResponse.IsSuccess || publicServersResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve recent players for map view. API Success: {IsSuccess}",
                    recentPlayersResponse.IsSuccess);
                ViewData["MapUnavailable"] = true;
                return View(new List<PublicPlayerMapPointViewModel>());
            }

            var publicServerIds = publicServersResponse.IsSuccess && publicServersResponse.Result?.Data?.Items is not null
                ? publicServersResponse.Result.Data.Items.Select(s => s.GameServerId).ToHashSet()
                : [];

            var safePoints = recentPlayersResponse.Result.Data.Items
                .Where(p => p.Lat.HasValue && p.Long.HasValue
                            && p.GameServerId.HasValue
                            && publicServerIds.Contains(p.GameServerId.Value))
                .Select(p => new PublicPlayerMapPointViewModel(p.Lat!.Value, p.Long!.Value, p.GameType.ToString()))
                .ToList();

            Logger.LogInformation("Public map view returning {PointCount} player points across {ServerCount} public servers",
                safePoints.Count, publicServerIds.Count);

            return View(safePoints);
        }, nameof(Map)).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns geo-located recent players for a specific server (for the Player Map tab)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetRecentPlayersForMap(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is { ServerListEnabled: false })
            {
                return Json(new { players = Array.Empty<object>() });
            }

            if (!gameServerApiResponse.IsSuccess || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Public server metadata unavailable for recent player map {ServerId}", id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Recent player observations are unavailable." });
            }

            var response = await repositoryApiClient.RecentPlayers.V1.GetRecentPlayers(
                null, id, DateTime.UtcNow.AddHours(-48), RecentPlayersFilter.GeoLocated,
                0, 200, null, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccess || response.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Recent player map observations unavailable for {ServerId}", id);
                return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Recent player observations are unavailable." });
            }

            var players = response.Result.Data.Items
                .Where(p => p.Lat.HasValue && p.Long.HasValue)
                .Select(p => new
                {
                    lat = p.Lat,
                    lng = p.Long,
                    gameType = p.GameType.ToString()
                })
                .ToList();

            return Json(new { players });
        }, nameof(GetRecentPlayersForMap)).ConfigureAwait(false);
    }
}
