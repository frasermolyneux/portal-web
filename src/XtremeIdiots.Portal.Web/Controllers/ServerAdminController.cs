using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.GeoLocation.Api.Client.V1;

using Newtonsoft.Json;

using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for server administration functionality including RCON commands and chat log management
/// </summary>
/// <remarks>
/// Initializes a new instance of the ServerAdminController
/// </remarks>
/// <param name="authorizationService">Service for handling authorization policies</param>
/// <param name="repositoryApiClient">Client for accessing repository data</param>
/// <param name="serversApiClient">Client for server RCON operations</param>
/// <param name="telemetryClient">Client for tracking telemetry events</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
[Authorize(Policy = AuthPolicies.AccessServerAdmin)]
public class ServerAdminController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    IServersApiClient serversApiClient,
    IGeoLocationApiClient geoLocationClient,
    IProxyCheckService proxyCheckService,
    IAdminActionTopics adminActionTopics,
    TelemetryClient telemetryClient,
    ILogger<ServerAdminController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{
    private const string DefaultForumBaseUrl = "https://www.xtremeidiots.com/forums/topic/";
    private const string DefaultFallbackAdminId = "21145";
    private const int DefaultTempBanDurationDays = 7;

    /// <summary>
    /// Displays the main server administration dashboard with available game servers
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request</param>
    /// <returns>View with list of administrable game servers</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var requiredClaims = new[] { UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, UserProfileClaimType.ServerAdmin };
            var (gameTypes, gameServerIds) = User.ClaimedGamesAndItems(requiredClaims);

            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                gameTypes, gameServerIds, GameServerFilter.LiveTrackingEnabled, 0, 50,
                GameServerOrder.BannerServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogError("Failed to retrieve game servers for server admin dashboard for user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction("Display", "Errors", new { id = 500 });
            }

            var results = gameServersApiResponse.Result.Data.Items.Select(gs => new ServerAdminGameServerViewModel
            {
                GameServer = gs,
                GameServerQueryStatus = new ServerQueryStatusResponseDto(),
                GameServerRconStatus = new ServerRconStatusResponseDto()
            }).ToList();

            Logger.LogInformation("Successfully loaded {Count} game servers for user {UserId} server admin dashboard",
                results.Count, User.XtremeIdiotsId());

            return View(results);
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Helper method to retrieve and authorize access to a game server
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="action">Action being performed for logging</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tuple containing potential action result for unauthorized access and game server data</returns>
    private async Task<(IActionResult? ActionResult, GameServerDto? GameServer)> GetAuthorizedGameServerAsync(
        Guid id,
        string action,
        CancellationToken cancellationToken = default)
    {
        var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

        if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
        {
            Logger.LogWarning("Game server {ServerId} not found when {Action}", id, action);
            return (NotFound(), null);
        }

        var gameServerData = gameServerApiResponse.Result.Data;
        var authResult = await CheckAuthorizationAsync(
            authorizationService,
            gameServerData.GameType,
            AuthPolicies.ViewLiveRcon,
            action,
            "GameServer",
            $"ServerId:{id},GameType:{gameServerData.GameType}",
            gameServerData).ConfigureAwait(false);

        return authResult is not null ? (authResult, null) : (null, gameServerData);
    }

    /// <summary>
    /// Displays the RCON interface for a specific game server
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>RCON interface view for the server</returns>
    [HttpGet]
    public async Task<IActionResult> ViewRcon(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(ViewRcon), cancellationToken).ConfigureAwait(false);
            return actionResult is not null ? actionResult : View(gameServerData);
        }, nameof(ViewRcon)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets enriched RCON player data including profiles, IP geolocation, and risk assessment
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with enriched player data for DataTables</returns>
    [HttpGet]
    public async Task<IActionResult> GetRconPlayers(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetRconPlayers), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getServerStatusResult = await serversApiClient.Rcon.V1.GetServerStatus(id).ConfigureAwait(false);

            if (!getServerStatusResult.IsSuccess || getServerStatusResult.Result?.Data?.Players is null)
            {
                return Json(new { data = Array.Empty<object>() });
            }

            var rconPlayers = getServerStatusResult.Result.Data.Players;
            List<object> enrichedPlayers = [];

            foreach (var rconPlayer in rconPlayers)
            {
                var enrichedPlayer = await EnrichRconPlayerDataAsync(rconPlayer, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);
                enrichedPlayers.Add(enrichedPlayer);
            }

            return Json(new { data = enrichedPlayers });
        }, nameof(GetRconPlayers)).ConfigureAwait(false);
    }

    /// <summary>
    /// Enriches RCON player data with profile information, geolocation, and risk assessment
    /// </summary>
    /// <param name="rconPlayer">The RCON player data from the game server</param>
    /// <param name="gameType">The type of game being played</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Enriched player data object with additional context</returns>
    private async Task<object> EnrichRconPlayerDataAsync(dynamic rconPlayer, GameType gameType, CancellationToken cancellationToken)
    {
        PlayerDto? playerProfile = null;
        string? countryCode = null;
        ProxyCheckResult? proxyCheck = null;

        // Try to find existing player profile by GUID
        string guid = rconPlayer.Guid?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(guid))
        {
            try
            {
                // Search for player by GUID using GetPlayers with filter
                var playerResponse = await repositoryApiClient.Players.V1.GetPlayers(
                    gameType, null, guid, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None).ConfigureAwait(false);

                if (playerResponse.IsSuccess && playerResponse.Result?.Data?.Items?.Any() == true)
                {
                    playerProfile = playerResponse.Result.Data.Items.First();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to retrieve player profile for GUID {Guid}", guid);
            }
        }

        // Get IP address enrichment data
        string ipAddress = rconPlayer.IpAddress?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            try
            {
                // Get geolocation country code
                var geoResponse = await geoLocationClient.GeoLookup.V1.GetGeoLocation(ipAddress, cancellationToken).ConfigureAwait(false);
                if (geoResponse.IsSuccess && geoResponse.Result?.Data is not null)
                {
                    countryCode = geoResponse.Result.Data.CountryCode;
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to retrieve geolocation for IP {IpAddress}", ipAddress);
            }

            try
            {
                // Get ProxyCheck risk assessment
                proxyCheck = await proxyCheckService.GetIpRiskDataAsync(ipAddress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to retrieve proxy check data for IP {IpAddress}", ipAddress);
            }
        }

        return new
        {
            num = rconPlayer.Num,
            name = rconPlayer.Name?.ToString() ?? string.Empty,
            guid = guid,
            ipAddress = ipAddress,
            rate = rconPlayer.Rate,
            playerId = playerProfile?.PlayerId,
            username = playerProfile?.Username,
            countryCode,
            proxyCheckRiskScore = proxyCheck?.RiskScore ?? 0,
            isProxy = proxyCheck?.IsProxy ?? false,
            isVpn = proxyCheck?.IsVpn ?? false,
            proxyType = proxyCheck?.Type ?? string.Empty
        };
    }

    /// <summary>
    /// Gets the server status including current map and player count
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with server status data</returns>
    [HttpGet]
    public async Task<IActionResult> GetServerStatus(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetServerStatus), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getServerStatusResult = await serversApiClient.Rcon.V1.GetServerStatus(id).ConfigureAwait(false);

            if (!getServerStatusResult.IsSuccess || getServerStatusResult.Result?.Data is null)
            {
                return Json(new { success = false, message = "Failed to get server status" });
            }

            var status = getServerStatusResult.Result.Data;

            // Get current map image from repository
            string? mapImageUri = null;
            string? currentMapName = null;

            // Try to get map name from status data (property name may vary)
            try
            {
                // Attempt to get map name from dynamic status object
                var statusDynamic = (dynamic)status;

                // Try various property names that different games might use
                currentMapName = statusDynamic.MapName?.ToString() ?? statusDynamic.Map?.ToString() ?? statusDynamic.mapname?.ToString() ?? statusDynamic.map?.ToString() ?? null;

                // Log what properties are available for debugging
                if (string.IsNullOrWhiteSpace(currentMapName))
                {
                    var statusJson = JsonConvert.SerializeObject(status);
                    Logger.LogDebug("Server status for {ServerId}: {Status}", id, statusJson);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to extract map name from server status for {ServerId}", id);
            }

            if (string.IsNullOrWhiteSpace(currentMapName)) currentMapName = "Unknown";

            if (!string.IsNullOrWhiteSpace(currentMapName) &&
                currentMapName != "Unknown")
            {
                var mapsApiResponse = await repositoryApiClient.Maps.V1.GetMaps(
                    gameServerData!.GameType,
                    [currentMapName],
                    null, null, 0, 1, MapsOrder.MapNameAsc, cancellationToken).ConfigureAwait(false);

                mapImageUri = mapsApiResponse.Result?.Data?.Items?.FirstOrDefault()?.MapImageUri;
            }

            var playerCount = status.Players?.Count ?? 0;

            return Json(new
            {
                success = true,
                currentMap = currentMapName,
                mapImageUri,
                playerCount,
                maxPlayers = 32, // Default, could be from server config
                hostname = gameServerData!.Hostname,
                gameType = gameServerData.GameType.ToString()
            });
        }, nameof(GetServerStatus)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the current map information from the server using the new dedicated endpoint
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with current map information including map name and image URI</returns>
    [HttpGet]
    public async Task<IActionResult> GetCurrentMap(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetCurrentMap), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getCurrentMapResult = await serversApiClient.Rcon.V1.GetCurrentMap(id).ConfigureAwait(false);

            if (!getCurrentMapResult.IsSuccess || getCurrentMapResult.Result?.Data is null)
            {
                return Json(new { success = false, message = "Failed to get current map" });
            }

            var currentMapDto = getCurrentMapResult.Result.Data;
            var currentMapName = currentMapDto.MapName;

            // Get current map image from repository
            string? mapImageUri = null;

            if (!string.IsNullOrWhiteSpace(currentMapName))
            {
                var mapsApiResponse = await repositoryApiClient.Maps.V1.GetMaps(
                    gameServerData!.GameType,
                    [currentMapName],
                    null, null, 0, 1, MapsOrder.MapNameAsc, cancellationToken).ConfigureAwait(false);

                mapImageUri = mapsApiResponse.Result?.Data?.Items?.FirstOrDefault()?.MapImageUri;
            }

            return Json(new
            {
                success = true,
                currentMap = currentMapName,
                mapImageUri
            });
        }, nameof(GetCurrentMap)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets raw server information from RCON for display in the UI tooltip
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with raw server info text</returns>
    [HttpGet]
    public async Task<IActionResult> GetServerInfo(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetServerInfo), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getServerInfoResult = await serversApiClient.Rcon.V1.GetServerInfo(id).ConfigureAwait(false);

            if (!getServerInfoResult.IsSuccess || getServerInfoResult.Result?.Data is null)
            {
                return Json(new { success = false, message = "Failed to get server info" });
            }

            var serverInfo = getServerInfoResult.Result.Data;

            return Json(new { success = true, serverInfo });
        }, nameof(GetServerInfo)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets raw system information from RCON for display in the UI
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with raw system info text</returns>
    [HttpGet]
    public async Task<IActionResult> GetSystemInfo(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetSystemInfo), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getSystemInfoResult = await serversApiClient.Rcon.V1.GetSystemInfo(id).ConfigureAwait(false);

            if (!getSystemInfoResult.IsSuccess || getSystemInfoResult.Result?.Data is null)
            {
                return Json(new { success = false, message = "Failed to get system info" });
            }

            var systemInfo = getSystemInfoResult.Result.Data;

            return Json(new { success = true, systemInfo });
        }, nameof(GetSystemInfo)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets raw command list from RCON for display in the UI
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with raw command list text</returns>
    [HttpGet]
    public async Task<IActionResult> GetCommandList(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetCommandList), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getCommandListResult = await serversApiClient.Rcon.V1.GetCommandList(id).ConfigureAwait(false);

            if (!getCommandListResult.IsSuccess || getCommandListResult.Result?.Data is null)
            {
                return Json(new { success = false, message = "Failed to get command list" });
            }

            var commandList = getCommandListResult.Result.Data;

            return Json(new { success = true, commandList });
        }, nameof(GetCommandList)).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a 'say' command to broadcast a message to all players on the server
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="message">Message to broadcast</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON result indicating success or failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendSayCommand(Guid id, string message, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(SendSayCommand), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            if (string.IsNullOrWhiteSpace(message))
            {
                return Json(new { success = false, message = "Message cannot be empty" });
            }

            // Limit message length
            message = message.Trim();
            if (message.Length > 255)
            {
                message = message[..255];
            }

            var sayResult = await serversApiClient.Rcon.V1.Say(id, message).ConfigureAwait(false);

            if (!sayResult.IsSuccess)
            {
                Logger.LogWarning("Failed to send say command to server {ServerId}", id);
                return Json(new { success = false, message = "Failed to send message to server" });
            }

            TrackSuccessTelemetry("SayCommandSent", nameof(SendSayCommand), new Dictionary<string, string>
            {
                { "GameServerId", id.ToString() },
                { "MessageLength", message.Length.ToString() }
            });

            return Json(new { success = true, message = "Message sent to server" });
        }, nameof(SendSayCommand)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the map rotation for a specific game server
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON with list of maps in rotation with their metadata</returns>
    [HttpGet]
    public async Task<IActionResult> GetMapRotation(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetMapRotation), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var getServerMapsResult = await serversApiClient.Rcon.V1.GetServerMaps(id).ConfigureAwait(false);

            if (!getServerMapsResult.IsSuccess || getServerMapsResult.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to get map rotation for server {ServerId}", id);
                return Json(new { success = false, maps = Array.Empty<object>() });
            }

            var rconMaps = getServerMapsResult.Result.Data.Items;

            // Get map details from repository for images and metadata
            var mapNames = rconMaps.Select(m => m.MapName).ToArray();
            var mapsApiResponse = await repositoryApiClient.Maps.V1.GetMaps(
                gameServerData!.GameType,
                mapNames,
                null, null, 0, 100, MapsOrder.MapNameAsc, cancellationToken).ConfigureAwait(false);

            var mapDetails = mapsApiResponse.Result?.Data?.Items?.ToDictionary(m => m.MapName, m => m)
                ?? [];

            var enrichedMaps = rconMaps.Select(rconMap =>
            {
                var mapDetail = mapDetails.GetValueOrDefault(rconMap.MapName);
                return new
                {
                    mapName = rconMap.MapName,
                    mapTitle = mapDetail?.MapName ?? rconMap.MapName,
                    mapImageUri = mapDetail?.MapImageUri,
                    hasImage = mapDetail?.MapImageUri is not null
                };
            }).ToList();

            return Json(new { success = true, maps = enrichedMaps });
        }, nameof(GetMapRotation)).ConfigureAwait(false);
    }

    /// <summary>
    /// Loads a specific map on the game server via RCON command
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="mapName">Name of the map to load</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON result indicating success or failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LoadMap(
        Guid id,
        string mapName,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(LoadMap), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            if (string.IsNullOrWhiteSpace(mapName))
            {
                Logger.LogWarning("LoadMap called with empty map name for server {ServerId}", id);
                return Json(new { success = false, message = "Map name is required" });
            }

            Logger.LogInformation("Attempting to load map {MapName} on server {ServerId}", mapName, id);

            // Call the actual LoadMap RCON command
            var loadMapResult = await serversApiClient.Rcon.V1.ChangeMap(id, mapName).ConfigureAwait(false);

            if (!loadMapResult.IsSuccess)
            {
                Logger.LogError("Failed to load map {MapName} on server {ServerId}", mapName, id);
                return Json(new { success = false, message = "Failed to load map. Please check server logs for details." });
            }

            TrackSuccessTelemetry("MapLoaded", nameof(LoadMap), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "GameType", gameServerData!.GameType.ToString() },
                { "MapName", mapName }
            });

            Logger.LogInformation(
                "Map {MapName} successfully loaded on server {ServerId}",
                mapName,
                id);

            return Json(new { success = true, message = $"Map '{mapName}' is now loading" });
        }, nameof(LoadMap)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestartMap(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(RestartMap), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var restartResult = await serversApiClient.Rcon.V1.RestartMap(id).ConfigureAwait(false);

            if (!restartResult.IsSuccess)
            {
                Logger.LogError("Failed to restart map on server {ServerId}", id);
                return Json(new { success = false, message = "Failed to restart map" });
            }

            TrackSuccessTelemetry("MapRestarted", nameof(RestartMap), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "GameType", gameServerData!.GameType.ToString() }
            });

            return Json(new { success = true, message = "Map restart command sent successfully" });
        }, nameof(RestartMap)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FastRestartMap(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(FastRestartMap), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var restartResult = await serversApiClient.Rcon.V1.FastRestartMap(id).ConfigureAwait(false);

            if (!restartResult.IsSuccess)
            {
                Logger.LogError("Failed to fast restart map on server {ServerId}", id);
                return Json(new { success = false, message = "Failed to fast restart map" });
            }

            TrackSuccessTelemetry("MapFastRestarted", nameof(FastRestartMap), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "GameType", gameServerData!.GameType.ToString() }
            });

            return Json(new { success = true, message = "Fast restart command sent successfully" });
        }, nameof(FastRestartMap)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NextMap(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(NextMap), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var nextMapResult = await serversApiClient.Rcon.V1.NextMap(id).ConfigureAwait(false);

            if (!nextMapResult.IsSuccess)
            {
                Logger.LogError("Failed to load next map on server {ServerId}", id);
                return Json(new { success = false, message = "Failed to load next map" });
            }

            TrackSuccessTelemetry("NextMapTriggered", nameof(NextMap), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "GameType", gameServerData!.GameType.ToString() }
            });

            return Json(new { success = true, message = "Next map command sent successfully" });
        }, nameof(NextMap)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestartServer(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(RestartServer), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var restartResult = await serversApiClient.Rcon.V1.Restart(id).ConfigureAwait(false);

            if (!restartResult.IsSuccess)
            {
                Logger.LogError("Failed to restart server {ServerId}", id);
                return Json(new { success = false, message = "Failed to restart server" });
            }

            TrackSuccessTelemetry("ServerRestarted", nameof(RestartServer), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "GameType", gameServerData!.GameType.ToString() }
            });

            return Json(new { success = true, message = "Server restart command sent successfully" });
        }, nameof(RestartServer)).ConfigureAwait(false);
    }

    /// <summary>
    /// Kicks a player from the server via RCON and creates a Kick admin action
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="playerSlot">Player slot number</param>
    /// <param name="playerGuid">Player GUID</param>
    /// <param name="playerName">Player name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON result indicating success or failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> KickRconPlayer(Guid id, int playerSlot, string playerGuid, string playerName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(KickRconPlayer), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            // Check authorization for creating kick admin actions
            var authorizationResource = (gameServerData!.GameType, AdminActionType.Kick);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.CreateAdminAction,
                "Kick",
                "RconPlayer",
                $"GameType:{gameServerData.GameType},ServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult is not null)
                return Json(new { success = false, error = "Unauthorized", message = "You don't have permission to kick players" });

            if (string.IsNullOrWhiteSpace(playerName))
            {
                Logger.LogWarning("Invalid player data provided by user {UserId} for kick action", User.XtremeIdiotsId());
                return Json(new { success = false, error = "InvalidInput", message = "Invalid player data provided" });
            }

            try
            {
                // Kick the player via RCON using slot number
                var kickResult = await serversApiClient.Rcon.V1.KickPlayer(id, playerSlot).ConfigureAwait(false);

                if (!kickResult.IsSuccess)
                {
                    Logger.LogWarning("Failed to kick player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                        playerName, playerSlot, id);
                    return Json(new { success = false, error = "RconFailed", message = "Failed to kick player from server" });
                }

                // Create admin action record if we have a GUID
                if (!string.IsNullOrWhiteSpace(playerGuid))
                {
                    await CreateAdminActionForRconOperationAsync(
                        gameServerData.GameType, playerGuid, playerName, AdminActionType.Kick,
                        $"Player kicked from {gameServerData.Title} via RCON by {User.Username()}",
                        cancellationToken).ConfigureAwait(false);
                }

                TrackSuccessTelemetry("RconPlayerKicked", nameof(KickRconPlayer), new Dictionary<string, string>
                {
                    { "ServerId", id.ToString() },
                    { "PlayerSlot", playerSlot.ToString() },
                    { "GameType", gameServerData.GameType.ToString() }
                });

                return Json(new { success = true, message = $"Player {playerName} has been kicked" });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error kicking player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                    playerName, playerSlot, id);
                return Json(new { success = false, error = "Exception", message = "An error occurred while kicking the player" });
            }
        }, nameof(KickRconPlayer)).ConfigureAwait(false);
    }

    /// <summary>
    /// Temporarily bans a player from the server via RCON and creates a TempBan admin action
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="playerSlot">Player slot number</param>
    /// <param name="playerGuid">Player GUID</param>
    /// <param name="playerName">Player name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON result indicating success or failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TempBanRconPlayer(Guid id, int playerSlot, string playerGuid, string playerName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(TempBanRconPlayer), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            // Check authorization for creating temp ban admin actions
            var authorizationResource = (gameServerData!.GameType, AdminActionType.TempBan);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.CreateAdminAction,
                "TempBan",
                "RconPlayer",
                $"GameType:{gameServerData.GameType},ServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult is not null)
                return Json(new { success = false, error = "Unauthorized", message = "You don't have permission to temp ban players" });

            if (string.IsNullOrWhiteSpace(playerName))
            {
                Logger.LogWarning("Invalid player data provided by user {UserId} for temp ban action", User.XtremeIdiotsId());
                return Json(new { success = false, error = "InvalidInput", message = "Invalid player data provided" });
            }

            try
            {
                // Ban the player via RCON using slot number (most servers don't have separate temp ban RCON command)
                var banResult = await serversApiClient.Rcon.V1.BanPlayer(id, playerSlot).ConfigureAwait(false);

                if (!banResult.IsSuccess)
                {
                    Logger.LogWarning("Failed to temp ban player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                        playerName, playerSlot, id);
                    return Json(new { success = false, error = "RconFailed", message = "Failed to temp ban player from server" });
                }

                // Create admin action record with expiry if we have a GUID
                if (!string.IsNullOrWhiteSpace(playerGuid))
                {
                    var expiryDate = DateTime.UtcNow.AddDays(DefaultTempBanDurationDays);
                    await CreateAdminActionForRconOperationAsync(
                        gameServerData.GameType, playerGuid, playerName, AdminActionType.TempBan,
                        $"Player temp banned from {gameServerData.Title} via RCON by {User.Username()}. Please update with proper reason.",
                        cancellationToken,
                        expiryDate).ConfigureAwait(false);
                }

                TrackSuccessTelemetry("RconPlayerTempBanned", nameof(TempBanRconPlayer), new Dictionary<string, string>
                {
                    { "ServerId", id.ToString() },
                    { "PlayerSlot", playerSlot.ToString() },
                    { "GameType", gameServerData.GameType.ToString() }
                });

                return Json(new { success = true, message = $"Player {playerName} has been temp banned for {DefaultTempBanDurationDays} days" });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error temp banning player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                    playerName, playerSlot, id);
                return Json(new { success = false, error = "Exception", message = "An error occurred while temp banning the player" });
            }
        }, nameof(TempBanRconPlayer)).ConfigureAwait(false);
    }

    /// <summary>
    /// Permanently bans a player from the server via RCON and creates a Ban admin action
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="playerSlot">Player slot number</param>
    /// <param name="playerGuid">Player GUID</param>
    /// <param name="playerName">Player name</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON result indicating success or failure</returns>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BanRconPlayer(Guid id, int playerSlot, string playerGuid, string playerName, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(BanRconPlayer), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            // Check authorization for creating ban admin actions
            var authorizationResource = (gameServerData!.GameType, AdminActionType.Ban);
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                authorizationResource,
                AuthPolicies.CreateAdminAction,
                "Ban",
                "RconPlayer",
                $"GameType:{gameServerData.GameType},ServerId:{id}",
                gameServerData).ConfigureAwait(false);

            if (authResult is not null)
                return Json(new { success = false, error = "Unauthorized", message = "You don't have permission to ban players" });

            if (string.IsNullOrWhiteSpace(playerName))
            {
                Logger.LogWarning("Invalid player data provided by user {UserId} for ban action", User.XtremeIdiotsId());
                return Json(new { success = false, error = "InvalidInput", message = "Invalid player data provided" });
            }

            try
            {
                // Ban the player via RCON using slot number
                var banResult = await serversApiClient.Rcon.V1.BanPlayer(id, playerSlot).ConfigureAwait(false);

                if (!banResult.IsSuccess)
                {
                    Logger.LogWarning("Failed to ban player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                        playerName, playerSlot, id);
                    return Json(new { success = false, error = "RconFailed", message = "Failed to ban player from server" });
                }

                // Create admin action record if we have a GUID
                if (!string.IsNullOrWhiteSpace(playerGuid))
                {
                    await CreateAdminActionForRconOperationAsync(
                        gameServerData.GameType, playerGuid, playerName, AdminActionType.Ban,
                        $"Player banned from {gameServerData.Title} via RCON by {User.Username()}. Please update with proper reason.",
                        cancellationToken).ConfigureAwait(false);
                }

                TrackSuccessTelemetry("RconPlayerBanned", nameof(BanRconPlayer), new Dictionary<string, string>
                {
                    { "ServerId", id.ToString() },
                    { "PlayerSlot", playerSlot.ToString() },
                    { "GameType", gameServerData.GameType.ToString() }
                });

                return Json(new { success = true, message = $"Player {playerName} has been permanently banned" });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error banning player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                    playerName, playerSlot, id);
                return Json(new { success = false, error = "Exception", message = "An error occurred while banning the player" });
            }
        }, nameof(BanRconPlayer)).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates an admin action record for an RCON operation (kick/ban)
    /// </summary>
    /// <param name="gameType">Game type</param>
    /// <param name="playerGuidStr">Player GUID as string</param>
    /// <param name="playerName">Player name</param>
    /// <param name="actionType">Type of admin action</param>
    /// <param name="text">Admin action description</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="expires">Optional expiry date for temp bans</param>
    private async Task CreateAdminActionForRconOperationAsync(
        GameType gameType,
        string playerGuidStr,
        string playerName,
        AdminActionType actionType,
        string text,
        CancellationToken cancellationToken,
        DateTime? expires = null)
    {
        try
        {
            // Try to find existing player profile by searching with GUID
            var playerResponse = await repositoryApiClient.Players.V1.GetPlayers(
                gameType, null, playerGuidStr, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None).ConfigureAwait(false);

            if (!playerResponse.IsSuccess || playerResponse.Result?.Data?.Items?.Any() != true)
            {
                Logger.LogWarning("Player with GUID {Guid} not found in database, cannot create admin action", playerGuidStr);
                return;
            }

            var playerId = playerResponse.Result.Data.Items.First().PlayerId;
            var adminId = User.XtremeIdiotsId();

            var forumTopicId = await adminActionTopics.CreateTopicForAdminAction(
                actionType,
                gameType,
                playerId,
                playerName,
                DateTime.UtcNow,
                text,
                adminId).ConfigureAwait(false);

            var createAdminActionDto = new CreateAdminActionDto(playerId, actionType, text)
            {
                AdminId = adminId,
                Expires = expires,
                ForumTopicId = forumTopicId
            };

            await repositoryApiClient.AdminActions.V1.CreateAdminAction(createAdminActionDto, cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Created admin action {ActionType} for player {PlayerName} ({Guid}) by user {UserId}",
                actionType, playerName, playerGuidStr, adminId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to create admin action for RCON operation on player {PlayerName} ({Guid})",
                playerName, playerGuidStr);
        }
    }

    [HttpGet]
    public async Task<IActionResult> KickPlayer(Guid id, string num, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(KickPlayer), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            if (string.IsNullOrWhiteSpace(num))
            {
                Logger.LogWarning("Invalid player slot number provided by user {UserId} for server {ServerId}: {PlayerSlot}",
                    User.XtremeIdiotsId(), id, num);
                return NotFound();
            }

            this.AddAlertSuccess($"Player in slot {num} has been kicked");

            TrackSuccessTelemetry("PlayerKicked", nameof(KickPlayer), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "PlayerSlot", num },
                { "GameType", gameServerData!.GameType.ToString() }
            });

            return RedirectToAction(nameof(ViewRcon), new { id });
        }, nameof(KickPlayer)).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets live chat log messages for a specific game server (used in RCON view)
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="lastMessageId">Optional ID of the last message received (for incremental updates)</param>
    /// <param name="minutes">Number of minutes of chat history to retrieve (default: 30)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON array of recent chat messages</returns>
    [HttpGet]
    public async Task<IActionResult> GetServerLiveChatLog(Guid id, Guid? lastMessageId = null, int minutes = 30, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, gameServerData) = await GetAuthorizedGameServerAsync(id, nameof(GetServerLiveChatLog), cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            // Limit the time window to prevent excessive database queries
            minutes = Math.Min(minutes, 120);
            minutes = Math.Max(minutes, 5);

            var sinceTime = DateTime.UtcNow.AddMinutes(-minutes);

            // If lastMessageId is provided, only get messages newer than that
            var chatMessagesApiResponse = await repositoryApiClient.ChatMessages.V1.GetChatMessages(
                null, id, null, null,
                0, 100, ChatMessageOrder.TimestampDesc, null, cancellationToken).ConfigureAwait(false);

            if (!chatMessagesApiResponse.IsSuccess || chatMessagesApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve chat messages for server {ServerId}", id);
                return Json(new { messages = Array.Empty<object>(), count = 0 });
            }

            var messages = chatMessagesApiResponse.Result.Data.Items
                .Where(m => m.Timestamp >= sinceTime)
                .OrderByDescending(m => m.Timestamp)
                .Take(50)
                .Select(m => new
                {
                    chatMessageId = m.ChatMessageId,
                    timestamp = m.Timestamp.ToString("yyyy-MM-dd HH:mm"),
                    username = m.Username,
                    message = m.Message,
                    playerId = m.PlayerId,
                    locked = m.Locked
                })
                .ToList();

            // If lastMessageId was provided, only return messages newer than that
            if (lastMessageId.HasValue)
            {
                var lastIndex = messages.FindIndex(m => m.chatMessageId == lastMessageId.Value);
                if (lastIndex > 0)
                {
                    messages = [.. messages.Take(lastIndex)];
                }
                else if (lastIndex == 0)
                {
                    messages = [];
                }
            }

            return Json(new
            {
                messages,
                count = messages.Count,
                serverTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
            });
        }, nameof(GetServerLiveChatLog)).ConfigureAwait(false);
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.ViewGlobalChatLog)]
    public async Task<IActionResult> ChatLogIndex(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult<IActionResult>(View()), nameof(ChatLogIndex)).ConfigureAwait(false);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ViewGlobalChatLog)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetChatLogAjax(bool? lockedOnly = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () => await GetChatLogPrivate(null, null, null, lockedOnly, cancellationToken), "GetChatLogAjax").ConfigureAwait(false);
    }

    /// <summary>
    /// Returns list of game servers the user can access for chat log filtering (id, title, game type)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON array of servers</returns>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.AccessServerAdmin)]
    public async Task<IActionResult> GetChatLogServers(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            // Return broad list (no per-user claim filtering) relying on policy authorization already performed.
            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                null, null, null, 0, 300, GameServerOrder.BannerServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve chat log server list for user {UserId}", User.XtremeIdiotsId());
                return Json(Array.Empty<object>());
            }

            var results = gameServersApiResponse.Result.Data.Items
                    .Select(gs => new
                    {
                        id = gs.GameServerId,
                        title = string.IsNullOrWhiteSpace(gs.LiveTitle) ? gs.Title : gs.LiveTitle,
                        gameType = gs.GameType.ToString()
                    })
                .OrderBy(r => r.gameType)
                .ThenBy(r => r.title)
                .ToList();

            return Json(results);
        }, nameof(GetChatLogServers)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GameChatLog(GameType id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                id,
                AuthPolicies.ViewGlobalChatLog,
                "View",
                "GameChatLog",
                $"GameType:{id}",
                id).ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            ViewData["GameType"] = id;
            return View(nameof(ChatLogIndex));
        }, "GameChatLog").ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetGameChatLogAjax(GameType id, bool? lockedOnly = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                id,
                AuthPolicies.ViewGameChatLog,
                "GetGameChatLogAjax",
                "GameChatLog",
                $"GameType:{id}",
                id).ConfigureAwait(false);

            return authResult ?? await GetChatLogPrivate(id, null, null, lockedOnly, cancellationToken).ConfigureAwait(false);
        }, "GetGameChatLogAjax").ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> ServerChatLog(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {ServerId} not found when accessing server chat log", id);
                return NotFound();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.ViewServerChatLog,
                "View",
                "ServerChatLog",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            ViewData["GameServerId"] = id;
            return View(nameof(ChatLogIndex));
        }, nameof(ServerChatLog)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetServerChatLogAjax(Guid id, bool? lockedOnly = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {ServerId} not found when getting server chat log data", id);
                return NotFound();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.ViewServerChatLog,
                "GetServerChatLogAjax",
                "ServerChatLog",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            return authResult ?? await GetChatLogPrivate(null, id, null, lockedOnly, cancellationToken).ConfigureAwait(false);
        }, nameof(GetServerChatLogAjax)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetPlayerChatLog(Guid id, bool? lockedOnly = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var playerApiResponse = await repositoryApiClient.Players.V1.GetPlayer(id, PlayerEntityOptions.None).ConfigureAwait(false);

            if (playerApiResponse.IsNotFound || playerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Player {PlayerId} not found when getting player chat log data", id);
                return NotFound();
            }

            var playerData = playerApiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                playerData.GameType,
                AuthPolicies.ViewServerChatLog,
                "GetPlayerChatLog",
                "PlayerChatLog",
                $"PlayerId:{id},GameType:{playerData.GameType}",
                playerData).ConfigureAwait(false);

            return authResult is not null
                ? authResult
                : await GetChatLogPrivate(playerData.GameType, null, playerData.PlayerId, lockedOnly, cancellationToken).ConfigureAwait(false);
        }, nameof(GetPlayerChatLog)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> ChatLogPermaLink(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var chatMessageApiResponse = await repositoryApiClient.ChatMessages.V1.GetChatMessage(id, cancellationToken).ConfigureAwait(false);

            if (chatMessageApiResponse.IsNotFound || chatMessageApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Chat message {MessageId} not found when accessing permalink", id);
                return NotFound();
            }

            return View(chatMessageApiResponse.Result.Data);
        }, nameof(ChatLogPermaLink)).ConfigureAwait(false);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.LockChatMessages)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleChatMessageLock(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var chatMessageApiResponse = await repositoryApiClient.ChatMessages.V1.GetChatMessage(id, cancellationToken).ConfigureAwait(false);

            if (chatMessageApiResponse.IsNotFound || chatMessageApiResponse.Result?.Data?.GameServer is null)
            {
                Logger.LogWarning("Chat message {MessageId} not found when toggling lock status", id);
                return JsonOrStatus(NotFound(), new { success = false, error = "NotFound", chatMessageId = id });
            }

            var chatMessageData = chatMessageApiResponse.Result.Data;

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                chatMessageData.GameServer.GameType,
                AuthPolicies.LockChatMessages,
                "ToggleLock",
                "ChatMessage",
                $"MessageId:{id},GameType:{chatMessageData.GameServer.GameType}",
                chatMessageData).ConfigureAwait(false);

            if (authResult != null)
                return JsonOrStatus(authResult, new { success = false, error = "Unauthorized", chatMessageId = id });

            var toggleResponse = await repositoryApiClient.ChatMessages.V1.SetLock(id, !chatMessageData.Locked, cancellationToken).ConfigureAwait(false);

            if (!toggleResponse.IsSuccess)
            {
                Logger.LogError("Failed to toggle lock status for chat message {MessageId} by user {UserId}", id, User.XtremeIdiotsId());
                this.AddAlertDanger("An error occurred while updating the chat message lock status.");
                return JsonOrStatus(RedirectToAction(nameof(ChatLogPermaLink), new { id }), new { success = false, error = "ToggleFailed", chatMessageId = id });
            }

            TrackSuccessTelemetry("ChatMessageLockToggled", nameof(ToggleChatMessageLock), new Dictionary<string, string>
            {
                { "MessageId", id.ToString() },
                { "GameType", chatMessageData.GameServer.GameType.ToString() }
            });

            // If AJAX / fetch request (DataTables inline toggle), return JSON payload instead of redirect
            if (IsJsonRequest())
                return Json(new { success = true, chatMessageId = id, locked = !chatMessageData.Locked });

            this.AddAlertSuccess("Chat message lock status has been updated successfully.");
            return RedirectToAction(nameof(ChatLogPermaLink), new { id });
        }, nameof(ToggleChatMessageLock)).ConfigureAwait(false);
    }

    private bool IsJsonRequest()
    {
        var xrw = Request.Headers.XRequestedWith.ToString();
        if (!string.IsNullOrEmpty(xrw) && xrw.Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Request.Headers.Accept.Any(h => !string.IsNullOrEmpty(h) && h.Contains("application/json", StringComparison.OrdinalIgnoreCase));
    }

    private IActionResult JsonOrStatus(IActionResult nonJsonResult, object jsonPayload)
    {
        return IsJsonRequest() ? Json(jsonPayload) : nonJsonResult;
    }

    private async Task<IActionResult> GetChatLogPrivate(GameType? gameType, Guid? gameServerId, Guid? playerId, bool? lockedOnly = null, CancellationToken cancellationToken = default)
    {
        var reader = new StreamReader(Request.Body);
        var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

        if (model is null)
        {
            Logger.LogWarning("Invalid chat log request model for user {UserId}", User.XtremeIdiotsId());
            return BadRequest();
        }

        var order = ChatMessageOrder.TimestampDesc;
        if (model.Order is not null && model.Order.Count != 0)
        {
            var orderColumn = model.Columns[model.Order.First().Column].Name;
            var searchOrder = model.Order.First().Dir;

            switch (orderColumn)
            {
                case "timestamp":
                    order = searchOrder == "asc" ? ChatMessageOrder.TimestampAsc : ChatMessageOrder.TimestampDesc;
                    break;
                default:
                    break;
            }
        }

        if (model.Search?.Value?.StartsWith("locked:", StringComparison.OrdinalIgnoreCase) == true)
        {
            lockedOnly = true;
            model.Search.Value = model.Search.Value[7..].Trim();
        }

        var chatMessagesApiResponse = await repositoryApiClient.ChatMessages.V1.GetChatMessages(
            gameType, gameServerId, playerId, model.Search?.Value,
            model.Start, model.Length, order, lockedOnly, cancellationToken).ConfigureAwait(false);

        if (!chatMessagesApiResponse.IsSuccess || chatMessagesApiResponse.Result?.Data is null)
        {
            Logger.LogError("Failed to retrieve chat messages for user {UserId}", User.XtremeIdiotsId());
            return RedirectToAction("Display", "Errors", new { id = 500 });
        }

        return Json(new
        {
            model.Draw,
            recordsTotal = chatMessagesApiResponse.Result?.Pagination?.TotalCount,
            recordsFiltered = chatMessagesApiResponse.Result?.Pagination?.FilteredCount,
            data = chatMessagesApiResponse?.Result?.Data?.Items
        });
    }
}