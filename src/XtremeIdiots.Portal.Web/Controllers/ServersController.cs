using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for public server information pages
/// </summary>
[Authorize(Policy = AuthPolicies.AccessServers)]
public class ServersController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<ServersController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
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

            var liveStatusTask = repositoryApiClient.LiveStatus.V1.GetGameServerLiveStatus(gs.GameServerId, cancellationToken);
            var statsTask = repositoryApiClient.GameServersStats.V1.GetGameServerStatusStats(
                gs.GameServerId, DateTime.UtcNow.AddDays(-2), cancellationToken);

            await Task.WhenAll(liveStatusTask, statsTask).ConfigureAwait(false);

            var liveStatusResponse = await liveStatusTask.ConfigureAwait(false);
            var statsResponse = await statsTask.ConfigureAwait(false);

            var viewModel = new ServerInfoViewModel
            {
                GameServer = gs,
                LiveStatus = liveStatusResponse.IsSuccess ? liveStatusResponse.Result?.Data : null
            };

            if (statsResponse.IsSuccess && statsResponse.Result?.Data?.Items is not null)
            {
                viewModel.GameServerStats = [.. statsResponse.Result.Data.Items];

                GameServerStatDto? current = null;
                var orderedStats = statsResponse.Result.Data.Items.OrderBy(s => s.Timestamp).ToList();
                foreach (var stat in orderedStats)
                {
                    if (current is null) { current = stat; continue; }
                    if (current.MapName != stat.MapName)
                    {
                        viewModel.MapTimelineDataPoints.Add(new MapTimelineDataPoint(
                            current.MapName, current.Timestamp, stat.Timestamp));
                        current = stat;
                    }
                    if (stat == orderedStats.Last())
                        viewModel.MapTimelineDataPoints.Add(new MapTimelineDataPoint(
                            current.MapName, current.Timestamp, DateTime.UtcNow));
                }
            }

            return View(viewModel);
        }, nameof(Details)).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns live player data for a server, projecting only public-safe fields
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLivePlayers(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null || !gameServerApiResponse.Result.Data.ServerListEnabled)
            {
                return Json(new { data = Array.Empty<object>() });
            }

            var livePlayersResponse = await repositoryApiClient.LiveStatus.V1.GetGameServerLivePlayers(id, cancellationToken).ConfigureAwait(false);

            if (!livePlayersResponse.IsSuccess || livePlayersResponse.Result?.Data?.Items is null)
            {
                return Json(new { data = Array.Empty<object>() });
            }

            // Project only public-safe fields — no IPs, GUIDs, risk data, or player profiles
            var safePlayers = livePlayersResponse.Result.Data.Items
                .OrderBy(p => p.Num)
                .Select(p => new
                {
                    num = p.Num,
                    name = p.Name,
                    score = p.Score,
                    ping = p.Ping,
                    countryCode = p.GeoIntelligence?.CountryCode,
                    countryName = p.GeoIntelligence?.CountryName
                })
                .ToList();

            return Json(new { data = safePlayers });
        }, nameof(GetLivePlayers)).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns the map rotation for a server from the repository (not RCON)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetPublicMapRotation(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);
            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null || !gameServerApiResponse.Result.Data.ServerListEnabled)
            {
                return Json(new { success = false, maps = Array.Empty<object>() });
            }

            // Find active map rotation assignments for this server
            var assignmentsResponse = await repositoryApiClient.MapRotations.V1.GetServerAssignments(
                null, id, null, 0, 10, cancellationToken).ConfigureAwait(false);

            if (!assignmentsResponse.IsSuccess || assignmentsResponse.Result?.Data?.Items is null || !assignmentsResponse.Result.Data.Items.Any())
            {
                return Json(new { success = false, maps = Array.Empty<object>() });
            }

            // Use the first active/synced assignment
            var activeAssignment = assignmentsResponse.Result.Data.Items
                .FirstOrDefault(a => a.ActivationState == ActivationState.Active)
                ?? assignmentsResponse.Result.Data.Items.First();

            var rotationResponse = await repositoryApiClient.MapRotations.V1.GetMapRotation(
                activeAssignment.MapRotationId, cancellationToken).ConfigureAwait(false);

            if (!rotationResponse.IsSuccess || rotationResponse.Result?.Data is null)
            {
                return Json(new { success = false, maps = Array.Empty<object>() });
            }

            var rotation = rotationResponse.Result.Data;
            var orderedMaps = rotation.MapRotationMaps.OrderBy(m => m.SortOrder).ToList();

            // Fetch all map details in parallel to avoid N+1
            var mapTasks = orderedMaps.Select(rotMap =>
                repositoryApiClient.Maps.V1.GetMap(rotMap.MapId, cancellationToken));
            var mapResponses = await Task.WhenAll(mapTasks).ConfigureAwait(false);

            var enrichedMaps = mapResponses.Select(mapResponse =>
            {
                var mapData = mapResponse.Result?.Data;
                return new
                {
                    mapName = mapData?.MapName ?? "Unknown",
                    mapTitle = mapData?.DisplayName ?? mapData?.MapName ?? "Unknown",
                    mapImageUri = !string.IsNullOrEmpty(mapData?.MapImageUri) ? mapData.MapImageUri : null,
                    hasImage = !string.IsNullOrEmpty(mapData?.MapImageUri)
                };
            }).ToList();

            return Json(new { success = true, maps = enrichedMaps, rotationTitle = rotation.Title });
        }, nameof(GetPublicMapRotation)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the interactive map view showing recent player locations
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Map(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var response = await repositoryApiClient.RecentPlayers.V1.GetRecentPlayers(
                null, null, DateTime.UtcNow.AddHours(-48), RecentPlayersFilter.GeoLocated,
                0, 200, null, cancellationToken).ConfigureAwait(false);

            if (response.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve recent players for map view for user {UserId}. API Success: {IsSuccess}",
                    User.XtremeIdiotsId(), response.IsSuccess);
                return View(Array.Empty<object>());
            }

            Logger.LogInformation("User {UserId} successfully retrieved {PlayerCount} recent players for map view",
                User.XtremeIdiotsId(), response.Result.Data.Items.Count());

            return View(response.Result.Data.Items.ToList());
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
            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null || !gameServerApiResponse.Result.Data.ServerListEnabled)
            {
                return Json(new { players = Array.Empty<object>() });
            }

            var response = await repositoryApiClient.RecentPlayers.V1.GetRecentPlayers(
                null, id, DateTime.UtcNow.AddHours(-48), RecentPlayersFilter.GeoLocated,
                0, 200, null, cancellationToken).ConfigureAwait(false);

            if (response.Result?.Data?.Items is null)
            {
                return Json(new { players = Array.Empty<object>() });
            }

            var players = response.Result.Data.Items
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