using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Api.Abstractions;
using MX.GeoLocation.Api.Client.V1;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Integrations.Forums;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1;
using XtremeIdiots.Portal.Integrations.Servers.Abstractions.Models.V1.Rcon;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ChatMessages;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Configurations;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Maps;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;
using XtremeIdiots.Portal.Web.Auth;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Models.ServerFeed;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;
using SystemTextJsonSerializer = System.Text.Json.JsonSerializer;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for server administration functionality including RCON commands and chat log management
/// </summary>
[Authorize(Policy = AuthPolicies.GameServers_Admin_Read)]
public class ServerAdminController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    IServersApiClient serversApiClient,
    IGeoLocationApiClient geoLocationClient,
    IAdminActionTopics adminActionTopics,
    IAgentTelemetryService agentTelemetryService,
    TelemetryClient telemetryClient,
    ILogger<ServerAdminController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    private readonly static string[] sensitiveEventDataKeyFragments =
    [
        "password",
        "secret",
        "token",
        "apikey",
        "api_key",
        "authorization",
        "cookie",
        "connectionstring",
        "connection_string"
    ];

    private readonly static JsonSerializerOptions cod4xPluginJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    /// <summary>
    /// Displays the main server administration dashboardwith available game servers
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the request</param>
    /// <returns>View with list of administrable game servers</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, AdditionalPermission.GameServers_Admin_Read];
            var (gameTypes, gameServerIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                gameTypes, gameServerIds, GameServerFilter.AgentEnabled, 0, 50,
                GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogError("Failed to retrieve game servers for server admin dashboard for user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction("Display", "Errors", new { id = 500 });
            }

            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken).ConfigureAwait(false);
            var liveStatusLookup = liveStatusResponse.IsSuccess && liveStatusResponse.Result?.Data?.Items is not null
                ? liveStatusResponse.Result.Data.Items.ToDictionary(ls => ls.ServerId)
                : [];

            var results = gameServersApiResponse.Result.Data.Items.Select(gs =>
            {
                liveStatusLookup.TryGetValue(gs.GameServerId, out var liveStatus);
                return new ServerAdminGameServerViewModel
                {
                    GameServer = gs,
                    LiveStatus = liveStatus,
                    GameServerQueryStatus = new ServerQueryStatusResponseDto(),
                    GameServerRconStatus = new ServerRconStatusResponseDto()
                };
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
            AuthPolicies.GameServers_Admin_Rcon,
            action,
            "GameServer",
            $"ServerId:{id},GameType:{gameServerData.GameType}",
            gameServerData).ConfigureAwait(false);

        return authResult is not null ? (authResult, null) : (null, gameServerData);
    }

    /// <summary>
    /// Displays the unified server detail page with tabbed admin sections.
    /// Tab visibility is determined by the user's permissions for each feature area.
    /// </summary>
    /// <param name="id">Game server ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Tabbed server detail view</returns>
    [HttpGet]
    public async Task<IActionResult> ServerDetail(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {ServerId} not found for ServerDetail", id);
                return NotFound();
            }

            var gs = gameServerApiResponse.Result.Data;

            // Fetch live status for this server
            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1.GetGameServerLiveStatus(gs.GameServerId, cancellationToken).ConfigureAwait(false);
            var liveStatus = liveStatusResponse.IsSuccess ? liveStatusResponse.Result?.Data : null;

            // Check per-tab permissions in parallel
            var rconAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Admin_Rcon);
            var chatAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.ChatLog_ReadServer);
            var mapRotAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.MapRotations_Read);
            var statusAuth = authorizationService.AuthorizeAsync(User, AuthPolicies.GameServers_BanFileMonitors_Read);
            var editAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Write);
            var feedEventsAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Admin_Read);
            var cod4xLifecycleAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle);

            // Check fine-grained RCON sub-action permissions in parallel
            var sayAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Admin_Rcon_Say);
            var mapCmdAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Admin_Rcon_Map);
            var restartSrvAuth = authorizationService.AuthorizeAsync(User, gs.GameType, AuthPolicies.GameServers_Admin_Rcon_Restart);

            await Task.WhenAll(rconAuth, chatAuth, mapRotAuth, statusAuth, editAuth, sayAuth, mapCmdAuth, restartSrvAuth, feedEventsAuth, cod4xLifecycleAuth).ConfigureAwait(false);

            var viewModel = new ServerDetailViewModel
            {
                GameServer = gs,
                LiveStatus = liveStatus,
                CanViewRcon = (await rconAuth.ConfigureAwait(false)).Succeeded,
                CanViewChatLog = (await chatAuth.ConfigureAwait(false)).Succeeded,
                CanViewMapRotation = (await mapRotAuth.ConfigureAwait(false)).Succeeded,
                CanViewStatus = (await statusAuth.ConfigureAwait(false)).Succeeded,
                CanEditServer = (await editAuth.ConfigureAwait(false)).Succeeded,
                CanViewFeedEvents = (await feedEventsAuth.ConfigureAwait(false)).Succeeded,
                CanManageCoD4xPluginLifecycle = gs.GameType == GameType.CallOfDuty4x && (await cod4xLifecycleAuth.ConfigureAwait(false)).Succeeded,
                CanSay = (await sayAuth.ConfigureAwait(false)).Succeeded,
                CanChangeMap = (await mapCmdAuth.ConfigureAwait(false)).Succeeded,
                CanRestartServer = (await restartSrvAuth.ConfigureAwait(false)).Succeeded
            };

            if (gs.GameType == GameType.CallOfDuty4x)
            {
                try
                {
                    var configurationResult = await repositoryApiClient.GameServerConfigurations.V1
                        .GetConfigurations(gs.GameServerId, cancellationToken).ConfigureAwait(false);

                    if (configurationResult.IsSuccess && configurationResult.Result?.Data?.Items is not null)
                    {
                        var cod4xPluginConfiguration = configurationResult.Result.Data.Items.FirstOrDefault(static config =>
                            string.Equals(config.Namespace, Cod4xPluginSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase));

                        if (cod4xPluginConfiguration is not null && !string.IsNullOrWhiteSpace(cod4xPluginConfiguration.Configuration))
                        {
                            var cod4xPluginSettings = SystemTextJsonSerializer.Deserialize<Cod4xPluginSettingsDocument>(
                                    cod4xPluginConfiguration.Configuration,
                                    cod4xPluginJsonOptions)
                                ?? new Cod4xPluginSettingsDocument();

                            viewModel.Cod4xPluginEnabled = cod4xPluginSettings.Enabled;
                            viewModel.Cod4xPluginRootDirectory = cod4xPluginSettings.PluginRootDirectory;
                            viewModel.Cod4xRuntimeCurrentVersion = cod4xPluginSettings.RuntimeState?.CurrentVersion;
                            viewModel.Cod4xRuntimePreviousKnownGoodVersion = cod4xPluginSettings.RuntimeState?.PreviousKnownGoodVersion;
                            viewModel.Cod4xRuntimeLastOperationId = cod4xPluginSettings.RuntimeState?.LastOperationId;
                            viewModel.Cod4xRuntimeLastOperationStatus = cod4xPluginSettings.RuntimeState?.LastOperationStatus ?? Cod4xPluginOperationStatus.Unknown;
                            viewModel.Cod4xRuntimeLastOperationUtc = cod4xPluginSettings.RuntimeState?.LastOperationUtc;
                            viewModel.Cod4xRuntimeLastError = cod4xPluginSettings.RuntimeState?.LastError;
                            viewModel.Cod4xOperationRequestOperationId = cod4xPluginSettings.OperationRequest?.OperationId;
                            viewModel.Cod4xOperationRequestAction = cod4xPluginSettings.OperationRequest?.Action ?? Cod4xPluginOperationAction.Unknown;
                            viewModel.Cod4xOperationRequestTargetVersion = cod4xPluginSettings.OperationRequest?.TargetVersion;
                            viewModel.Cod4xOperationRequestRequestedAtUtc = cod4xPluginSettings.OperationRequest?.RequestedAtUtc;
                            viewModel.Cod4xOperationRequestRequestedBy = cod4xPluginSettings.OperationRequest?.RequestedBy;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to load CoD4x plugin settings for server {ServerId}", id);
                }
            }

            // Fetch overview data (non-critical — page renders without it)
            try
            {
                var statsTask = repositoryApiClient.GameServersStats.V1.GetGameServerStatusStats(
                    gs.GameServerId, DateTime.UtcNow.AddDays(-2), cancellationToken);

                var agentTask = Task.Run(async () =>
                {
                    try
                    {
                        return await agentTelemetryService.GetServerStatusAsync(
                            gs.GameServerId, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to get agent status for server {ServerId}", id);
                        return null;
                    }
                }, cancellationToken);

                await Task.WhenAll(statsTask, agentTask).ConfigureAwait(false);

                var statsResponse = await statsTask.ConfigureAwait(false);
                if (statsResponse.IsSuccess && statsResponse.Result?.Data?.Items is not null)
                {
                    viewModel.GameServerStats = [.. statsResponse.Result.Data.Items];

                    // Build map timeline
                    GameServerStatDto? current = null;
                    var orderedStats = statsResponse.Result.Data.Items.OrderBy(s => s.Timestamp).ToList();
                    foreach (var stat in orderedStats)
                    {
                        if (current is null)
                        {
                            current = stat;
                            continue;
                        }

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

                viewModel.AgentStatus = await agentTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to load overview data for server {ServerId}", id);
            }

            // Load ban file monitors if user has status access
            if (viewModel.CanViewStatus)
            {
                try
                {
                    var bfmResponse = await repositoryApiClient.BanFileMonitors.V1.GetBanFileMonitors(
                        null, null, gs.GameServerId, 0, 50, BanFileMonitorOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

                    if (bfmResponse.IsSuccess && bfmResponse.Result?.Data?.Items is not null)
                    {
                        viewModel.BanFileMonitors = [.. bfmResponse.Result.Data.Items];
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Failed to load ban file monitors for server {ServerId}", id);
                }
            }

            Logger.LogInformation("User {UserId} loaded server detail for {ServerId}", User.XtremeIdiotsId(), id);

            return View(viewModel);
        }, nameof(ServerDetail)).ConfigureAwait(false);
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

            var getServerStatusResult = await GetServerStatusAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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
        var proxyCheckRiskScore = 0;
        var isProxy = false;
        var isVpn = false;
        var proxyType = string.Empty;

        // Try to find existing player profile by GUID
        string guid = rconPlayer.Guid?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(guid))
        {
            try
            {
                // Search for player by GUID using GetPlayers with filter
                var playerResponse = await repositoryApiClient.Players.V1.GetPlayers(
                    gameType, PlayersFilter.UsernameAndGuid, guid, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None).ConfigureAwait(false);

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

        // Get IP address enrichment data via V1.1 intelligence endpoint
        string ipAddress = rconPlayer.IpAddress?.ToString() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(ipAddress))
        {
            try
            {
                var intelligenceResult = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(ipAddress, cancellationToken).ConfigureAwait(false);
                if (intelligenceResult.IsSuccess && intelligenceResult.Result?.Data is not null)
                {
                    var intelligence = intelligenceResult.Result.Data;
                    countryCode = intelligence.CountryCode;

                    if (intelligence.ProxyCheck is not null)
                    {
                        proxyCheckRiskScore = intelligence.ProxyCheck.RiskScore;
                        isProxy = intelligence.ProxyCheck.IsProxy;
                        isVpn = intelligence.ProxyCheck.IsVpn;
                        proxyType = intelligence.ProxyCheck.ProxyType;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "Failed to retrieve intelligence data for IP {IpAddress}", ipAddress);
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
            proxyCheckRiskScore,
            isProxy,
            isVpn,
            proxyType
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

            var getServerStatusResult = await GetServerStatusAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            if (string.IsNullOrWhiteSpace(currentMapName))
                currentMapName = "Unknown";

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

            var getCurrentMapResult = await GetCurrentMapAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var getServerInfoResult = await GetServerInfoAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var getSystemInfoResult = await GetSystemInfoAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var getCommandListResult = await GetCommandListAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var sayAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData!.GameType,
                AuthPolicies.GameServers_Admin_Rcon_Say,
                nameof(SendSayCommand),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (sayAuthResult is not null)
                return sayAuthResult;

            if (string.IsNullOrWhiteSpace(message))
            {
                return Json(new { success = false, message = "Message cannot be empty" });
            }

            message = message.Trim();

            var sayResult = await SendSayAsync(id, gameServerData!.GameType, message, cancellationToken).ConfigureAwait(false);

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

            var getServerMapsResult = await GetServerMapsAsync(id, gameServerData!.GameType).ConfigureAwait(false);

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

            var mapAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData!.GameType,
                AuthPolicies.GameServers_Admin_Rcon_Map,
                nameof(LoadMap),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (mapAuthResult is not null)
                return mapAuthResult;

            if (string.IsNullOrWhiteSpace(mapName))
            {
                Logger.LogWarning("LoadMap called with empty map name for server {ServerId}", id);
                return Json(new { success = false, message = "Map name is required" });
            }

            Logger.LogInformation("Attempting to load map {MapName} on server {ServerId}", mapName, id);

            // Call the actual LoadMap RCON command
            var loadMapResult = await ChangeMapAsync(id, gameServerData!.GameType, mapName, cancellationToken).ConfigureAwait(false);

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

            var mapAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData!.GameType,
                AuthPolicies.GameServers_Admin_Rcon_Map,
                nameof(RestartMap),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (mapAuthResult is not null)
                return mapAuthResult;

            var restartResult = await RestartMapAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var mapAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData!.GameType,
                AuthPolicies.GameServers_Admin_Rcon_Map,
                nameof(FastRestartMap),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (mapAuthResult is not null)
                return mapAuthResult;

            var restartResult = await FastRestartMapAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var mapAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData!.GameType,
                AuthPolicies.GameServers_Admin_Rcon_Map,
                nameof(NextMap),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (mapAuthResult is not null)
                return mapAuthResult;

            var nextMapResult = await NextMapAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

            var restartAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData!.GameType,
                AuthPolicies.GameServers_Admin_Rcon_Restart,
                nameof(RestartServer),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (restartAuthResult is not null)
                return restartAuthResult;

            var restartResult = await RestartServerAsync(id, gameServerData!.GameType, cancellationToken).ConfigureAwait(false);

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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestCod4xPluginOperation(
        Guid id,
        Cod4xPluginOperationAction action,
        string? targetVersion,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {ServerId} not found when requesting CoD4x plugin lifecycle operation", id);
                return NotFound();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            if (gameServerData.GameType != GameType.CallOfDuty4x)
            {
                return Json(new { success = false, message = "CoD4x plugin lifecycle operations are only supported for CoD4x servers." });
            }

            var lifecycleAuthResult = await CheckAuthorizationAsync(
                authorizationService,
                gameServerData.GameType,
                AuthPolicies.GameServers_Admin_CoD4xPluginLifecycle,
                nameof(RequestCod4xPluginOperation),
                "GameServer",
                $"ServerId:{id},GameType:{gameServerData.GameType}",
                gameServerData).ConfigureAwait(false);

            if (lifecycleAuthResult is not null)
            {
                return lifecycleAuthResult;
            }

            if (!Enum.IsDefined(action) || action == Cod4xPluginOperationAction.Unknown)
            {
                return Json(new { success = false, message = "A valid CoD4x plugin operation is required." });
            }

            var normalizedTargetVersion = string.IsNullOrWhiteSpace(targetVersion)
                ? null
                : targetVersion.Trim();

            if (action == Cod4xPluginOperationAction.Install && string.IsNullOrWhiteSpace(normalizedTargetVersion))
            {
                return Json(new { success = false, message = "Target version is required for install operations." });
            }

            if (action == Cod4xPluginOperationAction.Install && normalizedTargetVersion is not null)
            {
                if (normalizedTargetVersion.Length > Cod4xPluginSettingsConstants.MaxVersionLength)
                {
                    return Json(new { success = false, message = $"Target version must be {Cod4xPluginSettingsConstants.MaxVersionLength} characters or fewer." });
                }

                if (!IsValidCod4xTargetVersion(normalizedTargetVersion))
                {
                    return Json(new { success = false, message = "Target version contains invalid characters." });
                }
            }

            if (action != Cod4xPluginOperationAction.Install)
            {
                normalizedTargetVersion = null;
            }

            var configResult = await repositoryApiClient.GameServerConfigurations.V1.GetConfigurations(id, cancellationToken).ConfigureAwait(false);
            if (!configResult.IsSuccess || configResult.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to load game server configurations for CoD4x operation request on server {ServerId}", id);
                return Json(new { success = false, message = "Unable to load current CoD4x plugin settings." });
            }

            var existingCod4xPluginConfig = configResult.Result.Data.Items.FirstOrDefault(static config =>
                string.Equals(config.Namespace, Cod4xPluginSettingsConstants.Namespace, StringComparison.OrdinalIgnoreCase));

            Cod4xPluginSettingsDocument cod4xPluginSettings;
            if (existingCod4xPluginConfig is null || string.IsNullOrWhiteSpace(existingCod4xPluginConfig.Configuration))
            {
                cod4xPluginSettings = new Cod4xPluginSettingsDocument();
            }
            else if (!Cod4xPluginSettingsJsonHelper.TryDeserialize(
                existingCod4xPluginConfig.Configuration,
                cod4xPluginJsonOptions,
                out var existingCod4xPluginSettings)
                || existingCod4xPluginSettings is null)
            {
                Logger.LogWarning("Failed to parse existing CoD4x plugin configuration for server {ServerId}", id);
                return Json(new { success = false, message = "Unable to parse current CoD4x plugin settings." });
            }
            else
            {
                cod4xPluginSettings = existingCod4xPluginSettings;
            }

            var requestedAtUtc = DateTimeOffset.UtcNow;
            var operationId = Guid.NewGuid().ToString("N");
            var requestedBy = User.Username() ?? "unknown";
            cod4xPluginSettings.SchemaVersion = Cod4xPluginSettingsConstants.SchemaVersion;
            cod4xPluginSettings.OperationRequest = new Cod4xPluginOperationRequest
            {
                OperationId = operationId,
                Action = action,
                TargetVersion = normalizedTargetVersion,
                RequestedAtUtc = requestedAtUtc,
                RequestedBy = requestedBy
            };

            var serializedConfiguration = SystemTextJsonSerializer.Serialize(cod4xPluginSettings, cod4xPluginJsonOptions);
            var upsertResult = await repositoryApiClient.GameServerConfigurations.V1.UpsertConfiguration(
                id,
                Cod4xPluginSettingsConstants.Namespace,
                new UpsertConfigurationDto { Configuration = serializedConfiguration },
                cancellationToken).ConfigureAwait(false);

            if (!upsertResult.IsSuccess)
            {
                Logger.LogWarning("Failed to persist CoD4x plugin lifecycle request for server {ServerId}", id);
                return Json(new { success = false, message = "Failed to queue CoD4x plugin operation request." });
            }

            TrackSuccessTelemetry("CoD4xPluginOperationRequested", nameof(RequestCod4xPluginOperation), new Dictionary<string, string>
            {
                { "ServerId", id.ToString() },
                { "Action", action.ToString() },
                { "OperationId", operationId },
                { "RequestedBy", requestedBy }
            });

            return Json(new
            {
                success = true,
                operationId,
                action = action.ToString(),
                targetVersion = normalizedTargetVersion,
                requestedAtUtc = requestedAtUtc,
                message = "CoD4x plugin operation request queued successfully."
            });
        }, nameof(RequestCod4xPluginOperation)).ConfigureAwait(false);
    }

    private static bool IsValidCod4xTargetVersion(string version)
    {
        if (string.IsNullOrEmpty(version) || !char.IsLetterOrDigit(version[0]))
        {
            return false;
        }

        for (var i = 1; i < version.Length; i++)
        {
            var current = version[i];
            if (!char.IsLetterOrDigit(current) && current is not '.' and not '_' and not '-')
            {
                return false;
            }
        }

        return true;
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
                AuthPolicies.AdminActions_Create,
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
                var kickResult = await KickPlayerAsync(id, gameServerData.GameType, playerSlot, cancellationToken).ConfigureAwait(false);

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
                AuthPolicies.AdminActions_Create,
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
                var normalizedPlayerGuid = playerGuid?.Trim() ?? string.Empty;
                var tempBanDurationDays = int.TryParse(Configuration["XtremeIdiots:Forums:DefaultTempBanDays"], out var days) ? days : 7;
                var tempBanDurationMinutes = Math.Max(1, tempBanDurationDays * 24 * 60);

                var rconSuccess = await TempBanPlayerAsync(
                    id,
                    gameServerData.GameType,
                    playerSlot,
                    normalizedPlayerGuid,
                    tempBanDurationMinutes,
                    cancellationToken).ConfigureAwait(false);

                if (!rconSuccess)
                {
                    Logger.LogWarning("Failed to temp ban player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                        playerName, playerSlot, id);
                    return Json(new { success = false, error = "RconFailed", message = "Failed to temp ban player from server" });
                }

                // Create admin action record with expiry if we have a GUID
                if (!string.IsNullOrWhiteSpace(normalizedPlayerGuid))
                {
                    DateTime? expiryDate = gameServerData.GameType == GameType.CallOfDuty4x
                        ? DateTime.UtcNow.AddMinutes(tempBanDurationMinutes)
                        : null;

                    await CreateAdminActionForRconOperationAsync(
                        gameServerData.GameType, normalizedPlayerGuid, playerName, AdminActionType.TempBan,
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

                var successMessage = gameServerData.GameType == GameType.CallOfDuty4x
                    ? $"Player {playerName} has been temp banned for {tempBanDurationDays} days"
                    : $"Player {playerName} has been temp banned using server default duration";

                return Json(new { success = true, message = successMessage });
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
                AuthPolicies.AdminActions_Create,
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
                var normalizedPlayerGuid = playerGuid?.Trim() ?? string.Empty;

                var rconSuccess = await BanPlayerAsync(
                    id,
                    gameServerData.GameType,
                    playerSlot,
                    normalizedPlayerGuid,
                    cancellationToken).ConfigureAwait(false);

                if (!rconSuccess)
                {
                    Logger.LogWarning("Failed to ban player {PlayerName} (slot {PlayerSlot}) from server {ServerId}",
                        playerName, playerSlot, id);
                    return Json(new { success = false, error = "RconFailed", message = "Failed to ban player from server" });
                }

                // Create admin action record if we have a GUID
                if (!string.IsNullOrWhiteSpace(normalizedPlayerGuid))
                {
                    await CreateAdminActionForRconOperationAsync(
                        gameServerData.GameType, normalizedPlayerGuid, playerName, AdminActionType.Ban,
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

    private async Task<bool> TempBanPlayerAsync(
        Guid serverId,
        GameType gameType,
        int playerSlot,
        string playerIdentifier,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        if (gameType == GameType.CallOfDuty4x)
        {
            if (string.IsNullOrWhiteSpace(playerIdentifier))
            {
                return false;
            }

            var cod4xBanResult = await serversApiClient.CoD4xRcon.V1.TempBanPlayerByPlayerIdentifier(
                serverId,
                new CoD4xTempBanRequestDto
                {
                    PlayerIdentifier = playerIdentifier,
                    DurationMinutes = durationMinutes
                },
                cancellationToken).ConfigureAwait(false);

            return cod4xBanResult.IsSuccess && cod4xBanResult.Result?.Data?.IsSuccess == true;
        }

        var tempBanResult = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => await serversApiClient.Cod2Rcon.V1.TempBan(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false),
            (int)GameType.CallOfDuty4 => await serversApiClient.Cod4Rcon.V1.TempBan(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false),
            (int)GameType.CallOfDuty5 => await serversApiClient.Cod5Rcon.V1.TempBan(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false),
            _ => throw CreateUnsupportedGameTypeException(nameof(TempBanPlayerAsync), gameType),
        };

        return tempBanResult.IsSuccess;
    }

    private async Task<bool> BanPlayerAsync(
        Guid serverId,
        GameType gameType,
        int playerSlot,
        string playerIdentifier,
        CancellationToken cancellationToken)
    {
        if (gameType == GameType.CallOfDuty4x)
        {
            return !string.IsNullOrWhiteSpace(playerIdentifier)
                ? await BanCoD4xPlayerByIdentifierAsync(serverId, playerIdentifier, cancellationToken).ConfigureAwait(false)
                : await BanCoD4xPlayerBySlotAsync(serverId, playerSlot, cancellationToken).ConfigureAwait(false);
        }

        var banResult = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => await serversApiClient.Cod2Rcon.V1.Ban(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false),
            (int)GameType.CallOfDuty4 => await serversApiClient.Cod4Rcon.V1.Ban(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false),
            (int)GameType.CallOfDuty5 => await serversApiClient.Cod5Rcon.V1.Ban(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false),
            _ => throw CreateUnsupportedGameTypeException(nameof(BanPlayerAsync), gameType),
        };

        return banResult.IsSuccess;
    }

    private async Task<bool> BanCoD4xPlayerByIdentifierAsync(Guid serverId, string playerIdentifier, CancellationToken cancellationToken)
    {
        var banResult = await serversApiClient.CoD4xRcon.V1.BanPlayerByPlayerIdentifier(
            serverId,
            new CoD4xPermBanRequestDto
            {
                PlayerIdentifier = playerIdentifier
            },
            cancellationToken).ConfigureAwait(false);

        return banResult.IsSuccess && banResult.Result?.Data?.IsSuccess == true;
    }

    private async Task<bool> BanCoD4xPlayerBySlotAsync(Guid serverId, int playerSlot, CancellationToken cancellationToken)
    {
        var banResult = await serversApiClient.CoD4xRcon.V1.BanClient(
            serverId,
            new CoD4xClientReasonRequestDto
            {
                ClientId = playerSlot
            },
            cancellationToken).ConfigureAwait(false);

        return banResult.IsSuccess;
    }

    private async Task<ApiResult<ServerRconStatusResponseDto>> GetServerStatusAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
#pragma warning disable IDE0010 // Populate switch
#pragma warning disable IDE0072 // Add missing cases
        return gameType switch
        {
            GameType.CallOfDuty2 => MapGameScopedStatus(
                await serversApiClient.Cod2Rcon.V1.Status(serverId, cancellationToken).ConfigureAwait(false)),
            GameType.CallOfDuty4 => MapGameScopedStatus(
                await serversApiClient.Cod4Rcon.V1.Status(serverId, cancellationToken).ConfigureAwait(false)),
            GameType.CallOfDuty5 => MapGameScopedStatus(
                await serversApiClient.Cod5Rcon.V1.Status(serverId, cancellationToken).ConfigureAwait(false)),
            GameType.CallOfDuty4x => await GetCoD4xStatusAsync(serverId, cancellationToken).ConfigureAwait(false),
            _ => throw CreateUnsupportedGameTypeException(nameof(GetServerStatusAsync), gameType),
        };
#pragma warning restore IDE0072 // Add missing cases
#pragma warning restore IDE0010 // Populate switch
    }

    private async Task<ApiResult<ServerRconStatusResponseDto>> GetCoD4xStatusAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var cod4xStatusResult = await serversApiClient.CoD4xRcon.V1.Status(serverId, cancellationToken).ConfigureAwait(false);
        if (!cod4xStatusResult.IsSuccess || cod4xStatusResult.Result?.Data is null)
        {
            return new ApiResult<ServerRconStatusResponseDto>(
                cod4xStatusResult.StatusCode,
                new ApiResponse<ServerRconStatusResponseDto>());
        }

        var mappedStatus = new ServerRconStatusResponseDto
        {
            Players =
            [
                .. cod4xStatusResult.Result.Data.Players.Select(p => new ServerRconPlayerDto
                {
                    Num = p.Num,
                    Guid = p.PlayerIdentifier,
                    Name = p.Name,
                    IpAddress = p.IpAddress,
                    Rate = p.Rate,
                    Ping = p.Ping ?? 0
                })
            ]
        };

        return new ApiResult<ServerRconStatusResponseDto>(
            HttpStatusCode.OK,
            new ApiResponse<ServerRconStatusResponseDto>(mappedStatus));
    }

    private static ApiResult<ServerRconStatusResponseDto> MapGameScopedStatus(ApiResult<RconStatusResponseDto> statusResult)
    {
        if (!statusResult.IsSuccess || statusResult.Result?.Data is null)
        {
            return new ApiResult<ServerRconStatusResponseDto>(
                statusResult.StatusCode,
                new ApiResponse<ServerRconStatusResponseDto>());
        }

        var mappedStatus = new ServerRconStatusResponseDto
        {
            Players =
            [
                .. statusResult.Result.Data.Players.Select(p => new ServerRconPlayerDto
                {
                    Num = p.Num,
                    Guid = p.Guid,
                    Name = p.Name,
                    IpAddress = p.IpAddress,
                    Rate = p.Rate,
                    Ping = p.Ping
                })
            ]
        };

        return new ApiResult<ServerRconStatusResponseDto>(
            HttpStatusCode.OK,
            new ApiResponse<ServerRconStatusResponseDto>(mappedStatus));
    }

    private Task<ApiResult<RconCurrentMapDto>> GetCurrentMapAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => serversApiClient.Cod2Rcon.V1.GetCurrentMap(serverId, cancellationToken),
            (int)GameType.CallOfDuty4 => serversApiClient.Cod4Rcon.V1.GetCurrentMap(serverId, cancellationToken),
            (int)GameType.CallOfDuty5 => serversApiClient.Cod5Rcon.V1.GetCurrentMap(serverId, cancellationToken),
            (int)GameType.Insurgency => serversApiClient.InsurgencyRcon.V1.GetCurrentMap(serverId, cancellationToken),
            (int)GameType.Rust => serversApiClient.RustRcon.V1.GetCurrentMap(serverId, cancellationToken),
            (int)GameType.Left4Dead2 => serversApiClient.L4d2Rcon.V1.GetCurrentMap(serverId, cancellationToken),
            (int)GameType.CallOfDuty4x => GetCoD4xCurrentMapAsync(serverId, cancellationToken),
            _ => throw CreateUnsupportedGameTypeException(nameof(GetCurrentMapAsync), gameType),
        };
    }

    private async Task<ApiResult<RconCurrentMapDto>> GetCoD4xCurrentMapAsync(Guid serverId, CancellationToken cancellationToken)
    {
        var cod4xStatusResult = await serversApiClient.CoD4xRcon.V1.Status(serverId, cancellationToken).ConfigureAwait(false);
        return cod4xStatusResult.IsSuccess && !string.IsNullOrWhiteSpace(cod4xStatusResult.Result?.Data?.MapName)
            ? new ApiResult<RconCurrentMapDto>(
                HttpStatusCode.OK,
                new ApiResponse<RconCurrentMapDto>(new RconCurrentMapDto(cod4xStatusResult.Result.Data.MapName!)))
            : new ApiResult<RconCurrentMapDto>(
                cod4xStatusResult.StatusCode,
                new ApiResponse<RconCurrentMapDto>());
    }

    private Task<ApiResult<string>> GetServerInfoAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => serversApiClient.Cod2Rcon.V1.ServerInfo(serverId, cancellationToken),
            (int)GameType.CallOfDuty4 => serversApiClient.Cod4Rcon.V1.ServerInfo(serverId, cancellationToken),
            (int)GameType.CallOfDuty5 => serversApiClient.Cod5Rcon.V1.ServerInfo(serverId, cancellationToken),
            (int)GameType.CallOfDuty4x => serversApiClient.CoD4xRcon.V1.ServerInfo(serverId, cancellationToken),
            _ => throw CreateUnsupportedGameTypeException(nameof(GetServerInfoAsync), gameType),
        };
    }

    private Task<ApiResult<string>> GetSystemInfoAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => serversApiClient.Cod2Rcon.V1.SystemInfo(serverId, cancellationToken),
            (int)GameType.CallOfDuty4 => serversApiClient.Cod4Rcon.V1.SystemInfo(serverId, cancellationToken),
            (int)GameType.CallOfDuty5 => serversApiClient.Cod5Rcon.V1.SystemInfo(serverId, cancellationToken),
            (int)GameType.CallOfDuty4x => serversApiClient.CoD4xRcon.V1.SystemInfo(serverId, cancellationToken),
            _ => throw CreateUnsupportedGameTypeException(nameof(GetSystemInfoAsync), gameType),
        };
    }

    private Task<ApiResult<string>> GetCommandListAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => serversApiClient.Cod2Rcon.V1.CmdList(serverId, cancellationToken),
            (int)GameType.CallOfDuty4 => serversApiClient.Cod4Rcon.V1.CmdList(serverId, cancellationToken),
            (int)GameType.CallOfDuty5 => serversApiClient.Cod5Rcon.V1.CmdList(serverId, cancellationToken),
            (int)GameType.CallOfDuty4x => serversApiClient.CoD4xRcon.V1.CmdList(serverId, cancellationToken),
            _ => throw CreateUnsupportedGameTypeException(nameof(GetCommandListAsync), gameType),
        };
    }

    private async Task<ApiResult> SendSayAsync(Guid serverId, GameType gameType, string message, CancellationToken cancellationToken)
    {
        var sendTask = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => serversApiClient.Cod2Rcon.V1.Say(serverId, new SayRequest { Message = message }, cancellationToken),
            (int)GameType.CallOfDuty4 => serversApiClient.Cod4Rcon.V1.Say(serverId, new SayRequest { Message = message }, cancellationToken),
            (int)GameType.CallOfDuty5 => serversApiClient.Cod5Rcon.V1.Say(serverId, new SayRequest { Message = message }, cancellationToken),
            (int)GameType.Insurgency => serversApiClient.InsurgencyRcon.V1.Say(serverId, new SayRequest { Message = message }, cancellationToken),
            (int)GameType.Rust => serversApiClient.RustRcon.V1.Say(serverId, new SayRequest { Message = message }, cancellationToken),
            (int)GameType.Left4Dead2 => serversApiClient.L4d2Rcon.V1.Say(serverId, new SayRequest { Message = message }, cancellationToken),
            (int)GameType.CallOfDuty4x => ToApiResultTask(serversApiClient.CoD4xRcon.V1.ConSay(
                serverId,
                new CoD4xMessageRequestDto { Message = message },
                cancellationToken)),
            _ => throw CreateUnsupportedGameTypeException(nameof(SendSayAsync), gameType),
        };

        return await sendTask.ConfigureAwait(false);
    }

    private Task<ApiResult<RconMapCollectionDto>> GetServerMapsAsync(Guid serverId, GameType gameType)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => serversApiClient.Cod2Rcon.V1.GetMaps(serverId),
            (int)GameType.CallOfDuty4 => serversApiClient.Cod4Rcon.V1.GetMaps(serverId),
            (int)GameType.CallOfDuty5 => serversApiClient.Cod5Rcon.V1.GetMaps(serverId),
            (int)GameType.CallOfDuty4x => serversApiClient.CoD4xRcon.V1.GetMaps(serverId),
            _ => throw CreateUnsupportedGameTypeException(nameof(GetServerMapsAsync), gameType),
        };
    }

    private async Task<ApiResult> ChangeMapAsync(Guid serverId, GameType gameType, string mapName, CancellationToken cancellationToken)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => ToApiResult(await serversApiClient.Cod2Rcon.V1.Map(
                serverId,
                new ChangeMapRequest { MapName = mapName },
                cancellationToken).ConfigureAwait(false)),
            (int)GameType.CallOfDuty4 => ToApiResult(await serversApiClient.Cod4Rcon.V1.Map(
                serverId,
                new ChangeMapRequest { MapName = mapName },
                cancellationToken).ConfigureAwait(false)),
            (int)GameType.CallOfDuty5 => ToApiResult(await serversApiClient.Cod5Rcon.V1.Map(
                serverId,
                new ChangeMapRequest { MapName = mapName },
                cancellationToken).ConfigureAwait(false)),
            (int)GameType.CallOfDuty4x => ToApiResult(await serversApiClient.CoD4xRcon.V1.Map(
                serverId,
                new CoD4xMapRequestDto { MapName = mapName },
                cancellationToken).ConfigureAwait(false)),
            _ => throw CreateUnsupportedGameTypeException(nameof(ChangeMapAsync), gameType),
        };
    }

    private async Task<ApiResult> RestartMapAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        var restartTask = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => ToApiResultTask(serversApiClient.Cod2Rcon.V1.RestartMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4 => ToApiResultTask(serversApiClient.Cod4Rcon.V1.RestartMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty5 => ToApiResultTask(serversApiClient.Cod5Rcon.V1.RestartMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4x => ToApiResultTask(serversApiClient.CoD4xRcon.V1.MapRestart(serverId, cancellationToken)),
            _ => throw CreateUnsupportedGameTypeException(nameof(RestartMapAsync), gameType),
        };

        return await restartTask.ConfigureAwait(false);
    }

    private async Task<ApiResult> FastRestartMapAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        var restartTask = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => ToApiResultTask(serversApiClient.Cod2Rcon.V1.FastRestartMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4 => ToApiResultTask(serversApiClient.Cod4Rcon.V1.FastRestartMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty5 => ToApiResultTask(serversApiClient.Cod5Rcon.V1.FastRestartMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4x => ToApiResultTask(serversApiClient.CoD4xRcon.V1.FastRestart(serverId, cancellationToken)),
            _ => throw CreateUnsupportedGameTypeException(nameof(FastRestartMapAsync), gameType),
        };

        return await restartTask.ConfigureAwait(false);
    }

    private async Task<ApiResult> NextMapAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        var nextMapTask = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => ToApiResultTask(serversApiClient.Cod2Rcon.V1.NextMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4 => ToApiResultTask(serversApiClient.Cod4Rcon.V1.NextMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty5 => ToApiResultTask(serversApiClient.Cod5Rcon.V1.NextMap(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4x => ToApiResultTask(serversApiClient.CoD4xRcon.V1.MapRotate(serverId, cancellationToken)),
            _ => throw CreateUnsupportedGameTypeException(nameof(NextMapAsync), gameType),
        };

        return await nextMapTask.ConfigureAwait(false);
    }

    private async Task<ApiResult> RestartServerAsync(Guid serverId, GameType gameType, CancellationToken cancellationToken)
    {
        var restartTask = (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => ToApiResultTask(serversApiClient.Cod2Rcon.V1.Restart(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4 => ToApiResultTask(serversApiClient.Cod4Rcon.V1.Restart(serverId, cancellationToken)),
            (int)GameType.CallOfDuty5 => ToApiResultTask(serversApiClient.Cod5Rcon.V1.Restart(serverId, cancellationToken)),
            (int)GameType.CallOfDuty4x => ToApiResultTask(serversApiClient.CoD4xRcon.V1.KillServer(serverId, cancellationToken)),
            _ => throw CreateUnsupportedGameTypeException(nameof(RestartServerAsync), gameType),
        };

        return await restartTask.ConfigureAwait(false);
    }

    private async Task<ApiResult> KickPlayerAsync(Guid serverId, GameType gameType, int playerSlot, CancellationToken cancellationToken)
    {
        return (int)gameType switch
        {
            (int)GameType.CallOfDuty2 => ToApiResult(await serversApiClient.Cod2Rcon.V1.Kick(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false)),
            (int)GameType.CallOfDuty4 => ToApiResult(await serversApiClient.Cod4Rcon.V1.Kick(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false)),
            (int)GameType.CallOfDuty5 => ToApiResult(await serversApiClient.Cod5Rcon.V1.Kick(
                serverId,
                new ClientSlotRequest { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false)),
            (int)GameType.CallOfDuty4x => ToApiResult(await serversApiClient.CoD4xRcon.V1.ClientKick(
                serverId,
                new CoD4xClientReasonRequestDto { ClientId = playerSlot },
                cancellationToken).ConfigureAwait(false)),
            _ => throw CreateUnsupportedGameTypeException(nameof(KickPlayerAsync), gameType),
        };
    }

    private async static Task<ApiResult> ToApiResultTask(Task<ApiResult<string>> apiResultTask)
    {
        var result = await apiResultTask.ConfigureAwait(false);
        return ToApiResult(result);
    }

    private static ApiResult ToApiResult(ApiResult<string> result)
    {
        return new ApiResult(result.StatusCode, new ApiResponse());
    }

    private static NotSupportedException CreateUnsupportedGameTypeException(string operationName, GameType gameType)
    {
        return new NotSupportedException($"{operationName} is not supported for game type '{gameType}'.");
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
                gameType, PlayersFilter.UsernameAndGuid, playerGuidStr, 0, 1, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None).ConfigureAwait(false);

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
                adminId,
                cancellationToken).ConfigureAwait(false);

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
                    timestamp = DateTime.SpecifyKind(m.Timestamp, DateTimeKind.Utc).ToString("o"),
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
                serverTime = DateTime.UtcNow.ToString("o")
            });
        }, nameof(GetServerLiveChatLog)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GetServerFeed(
        Guid id,
        DateTime? lastSeenTimestampUtc = null,
        string? lastSeenSourceType = null,
        string? lastSeenItemId = null,
        Guid? lastChatMessageId = null,
        Guid? lastEventId = null,
        bool includeChat = true,
        bool includeEvents = true,
        int minutes = 30,
        int maxItems = 100,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(id, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {ServerId} not found when retrieving server feed", id);
                return NotFound();
            }

            var gameServerData = gameServerApiResponse.Result.Data;

            var chatAuthResult = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.ChatLog_ReadServer).ConfigureAwait(false);
            if (!chatAuthResult.Succeeded)
            {
                TrackUnauthorizedAccessAttempt(nameof(GetServerFeed), "ServerFeed", $"ServerId:{id},GameType:{gameServerData.GameType}", gameServerData);
                return Unauthorized();
            }

            var eventAuthResult = await authorizationService.AuthorizeAsync(User, gameServerData.GameType, AuthPolicies.GameServers_Admin_Read).ConfigureAwait(false);
            var eventsAllowed = eventAuthResult.Succeeded;

            includeEvents = includeEvents && eventsAllowed;

            minutes = Math.Clamp(minutes, 5, 120);
            maxItems = Math.Clamp(maxItems, 20, 200);

            var sinceTime = DateTime.UtcNow.AddMinutes(-minutes);

            var chatItemsTask = includeChat
                ? GetServerFeedChatItemsAsync(id, sinceTime, cancellationToken)
                : Task.FromResult<IReadOnlyList<ServerFeedItemDto>>([]);

            var eventItemsTask = includeEvents
                ? GetServerFeedEventItemsAsync(id, sinceTime, cancellationToken)
                : Task.FromResult<IReadOnlyList<ServerFeedItemDto>>([]);

            await Task.WhenAll(chatItemsTask, eventItemsTask).ConfigureAwait(false);

            var combinedItems = (await chatItemsTask.ConfigureAwait(false))
                .Concat(await eventItemsTask.ConfigureAwait(false))
                .OrderByDescending(x => x.TimestampUtc)
                .ThenBy(x => x.SourceType, StringComparer.Ordinal)
                .ThenBy(x => x.ItemId, StringComparer.Ordinal)
                .ToList();

            if (lastSeenTimestampUtc.HasValue && !string.IsNullOrWhiteSpace(lastSeenSourceType) && !string.IsNullOrWhiteSpace(lastSeenItemId))
            {
                combinedItems =
                [
                    .. combinedItems.Where(x => IsNewerThanCursor(x, lastSeenTimestampUtc.Value, lastSeenSourceType, lastSeenItemId))
                ];
            }

            if (lastChatMessageId.HasValue)
            {
                var chatCursorItemId = BuildItemId("chat", lastChatMessageId.Value);
                combinedItems = [.. combinedItems.Where(x => !string.Equals(x.ItemId, chatCursorItemId, StringComparison.Ordinal))];
            }

            if (lastEventId.HasValue)
            {
                var eventCursorItemId = BuildItemId("event", lastEventId.Value);
                combinedItems = [.. combinedItems.Where(x => !string.Equals(x.ItemId, eventCursorItemId, StringComparison.Ordinal))];
            }

            var overrunDetected = combinedItems.Count > maxItems;
            if (overrunDetected)
            {
                combinedItems = [.. combinedItems.Take(maxItems)];
            }

            var latestItem = combinedItems.FirstOrDefault();
            var latestChatItem = combinedItems.FirstOrDefault(x => x.SourceType == "chat");
            var latestEventItem = combinedItems.FirstOrDefault(x => x.SourceType == "event");

            return Json(new ServerFeedResponseDto
            {
                Items = combinedItems,
                Cursor = new ServerFeedCursorDto
                {
                    LastSeenTimestampUtc = latestItem?.TimestampUtc,
                    LastSeenSourceType = latestItem?.SourceType,
                    LastSeenItemId = latestItem?.ItemId,
                    LastChatMessageId = ParseItemGuid(latestChatItem?.ItemId),
                    LastEventId = ParseItemGuid(latestEventItem?.ItemId)
                },
                SourceAuthorization = new ServerFeedSourceAuthorizationDto
                {
                    ChatAllowed = true,
                    EventsAllowed = eventsAllowed
                },
                Diagnostics = new ServerFeedDiagnosticsDto
                {
                    ChatCount = combinedItems.Count(x => x.SourceType == "chat"),
                    EventCount = combinedItems.Count(x => x.SourceType == "event"),
                    OverrunDetected = overrunDetected
                },
                ServerTimeUtc = DateTime.UtcNow.ToString("o")
            });
        }, nameof(GetServerFeed)).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<ServerFeedItemDto>> GetServerFeedChatItemsAsync(Guid serverId, DateTime sinceTime, CancellationToken cancellationToken)
    {
        var chatMessagesApiResponse = await repositoryApiClient.ChatMessages.V1.GetChatMessages(
            null,
            serverId,
            null,
            null,
            0,
            200,
            ChatMessageOrder.TimestampDesc,
            null,
            cancellationToken).ConfigureAwait(false);

        if (!chatMessagesApiResponse.IsSuccess || chatMessagesApiResponse.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Failed to retrieve chat messages for server feed {ServerId}", serverId);
            return [];
        }

        return
        [
            .. chatMessagesApiResponse.Result.Data.Items
                .Where(x => x.Timestamp >= sinceTime)
                .Select(ToChatFeedItem)
        ];
    }

    private async Task<IReadOnlyList<ServerFeedItemDto>> GetServerFeedEventItemsAsync(Guid serverId, DateTime sinceTime, CancellationToken cancellationToken)
    {
        var gameServerEventsApiResponse = await repositoryApiClient.GameServersEvents.V1.GetGameServerEvents(
            null,
            serverId,
            null,
            0,
            200,
            GameServerEventOrder.TimestampDesc,
            cancellationToken).ConfigureAwait(false);

        if (!gameServerEventsApiResponse.IsSuccess || gameServerEventsApiResponse.Result?.Data?.Items is null)
        {
            Logger.LogWarning("Failed to retrieve game server events for server feed {ServerId}", serverId);
            return [];
        }

        return
        [
            .. gameServerEventsApiResponse.Result.Data.Items
                .Where(x => x.Timestamp >= sinceTime)
                .Select(ToEventFeedItem)
        ];
    }

    private static ServerFeedItemDto ToChatFeedItem(ChatMessageDto item)
    {
        return new ServerFeedItemDto
        {
            ItemId = BuildItemId("chat", item.ChatMessageId),
            SourceType = "chat",
            TimestampUtc = DateTime.SpecifyKind(item.Timestamp, DateTimeKind.Utc),
            DisplayText = item.Message,
            Username = item.Username,
            PlayerId = item.PlayerId == Guid.Empty ? null : item.PlayerId,
            EventType = null,
            RawEventData = null,
            Locked = item.Locked
        };
    }

    private static ServerFeedItemDto ToEventFeedItem(GameServerEventDto item)
    {
        return new ServerFeedItemDto
        {
            ItemId = BuildItemId("event", item.GameServerEventId),
            SourceType = "event",
            TimestampUtc = DateTime.SpecifyKind(item.Timestamp, DateTimeKind.Utc),
            DisplayText = item.EventType,
            Username = item.GameServer?.Title,
            PlayerId = null,
            EventType = item.EventType,
            RawEventData = SanitizeEventData(item.EventData),
            Locked = false
        };
    }

    private static string? SanitizeEventData(string? rawEventData)
    {
        if (string.IsNullOrWhiteSpace(rawEventData))
            return rawEventData;

        try
        {
            var token = JToken.Parse(rawEventData);
            RedactSensitiveValues(token);
            var sanitizedJson = token.ToString(Formatting.None);
            return sanitizedJson.Length > 4000
                ? sanitizedJson[..4000]
                : sanitizedJson;
        }
        catch
        {
            var sanitizedText = RedactSensitiveText(rawEventData);
            return sanitizedText.Length > 4000
                ? sanitizedText[..4000]
                : sanitizedText;
        }
    }

    private static void RedactSensitiveValues(JToken token)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (IsSensitiveKey(property.Name))
                {
                    property.Value = "***";
                }
                else
                {
                    RedactSensitiveValues(property.Value);
                }
            }

            return;
        }

        if (token is JArray array)
        {
            foreach (var child in array)
            {
                RedactSensitiveValues(child);
            }
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        return sensitiveEventDataKeyFragments.Any(x => key.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static string RedactSensitiveText(string rawText)
    {
        var result = rawText;

        foreach (var fragment in sensitiveEventDataKeyFragments)
        {
            var pattern = $@"(?i)({Regex.Escape(fragment)}\s*[:=]\s*)([^;\r\n\s]+)";
            result = Regex.Replace(result, pattern, "$1***");

            var quotedJsonPattern = $"(?i)(\\\"[^\\\"]*{Regex.Escape(fragment)}[^\\\"]*\\\"\\s*:\\s*\\\")([^\\\"]*)(\\\")";
            result = Regex.Replace(result, quotedJsonPattern, "$1***$3");
        }

        return result;
    }

    private static string BuildItemId(string sourceType, Guid id)
    {
        return $"{sourceType}:{id:N}";
    }

    private static Guid? ParseItemGuid(string? itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        var separatorIndex = itemId.IndexOf(':', StringComparison.Ordinal);
        var guidSegment = separatorIndex >= 0 ? itemId[(separatorIndex + 1)..] : itemId;

        return Guid.TryParse(guidSegment, out var parsedGuid) ? parsedGuid : null;
    }

    private static bool IsNewerThanCursor(ServerFeedItemDto item, DateTime cursorTimestamp, string cursorSourceType, string cursorItemId)
    {
        if (item.TimestampUtc != cursorTimestamp)
            return item.TimestampUtc > cursorTimestamp;

        var sourceComparison = string.CompareOrdinal(item.SourceType, cursorSourceType);
        return sourceComparison != 0
            ? sourceComparison < 0
            : string.CompareOrdinal(item.ItemId, cursorItemId) < 0;
    }

    [HttpGet]
    [Authorize(Policy = AuthPolicies.ChatLog_Read)]
    public async Task<IActionResult> ChatLogIndex(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult<IActionResult>(View()), nameof(ChatLogIndex)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the server events index page showing game server events
    /// </summary>
    /// <returns>The server events index view</returns>
    [HttpGet]
    public async Task<IActionResult> ServerEventsIndex()
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult<IActionResult>(View()), nameof(ServerEventsIndex)).ConfigureAwait(false);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ChatLog_Read)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetChatLogAjax(bool? lockedOnly = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () => await GetChatLogPrivate(null, null, null, lockedOnly, cancellationToken), "GetChatLogAjax").ConfigureAwait(false);
    }

    /// <summary>
    /// Returns list of game servers the user can access for filtering (id, title, game type)
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JSON array of servers</returns>
    [HttpGet]
    [Authorize(Policy = AuthPolicies.GameServers_Admin_Read)]
    public async Task<IActionResult> GetGameServers(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                null, null, null, 0, 300, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (!gameServersApiResponse.IsSuccess || gameServersApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve server list for user {UserId}", User.XtremeIdiotsId());
                return Json(Array.Empty<object>());
            }

            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken).ConfigureAwait(false);
            var liveStatusLookup = liveStatusResponse.IsSuccess && liveStatusResponse.Result?.Data?.Items is not null
                ? liveStatusResponse.Result.Data.Items.ToDictionary(ls => ls.ServerId)
                : [];

            var results = gameServersApiResponse.Result.Data.Items
                    .Select(gs =>
                    {
                        liveStatusLookup.TryGetValue(gs.GameServerId, out var liveStatus);
                        var title = string.IsNullOrWhiteSpace(liveStatus?.Title) ? gs.Title : liveStatus.Title;
                        return new
                        {
                            id = gs.GameServerId,
                            title,
                            gameType = gs.GameType.ToString()
                        };
                    })
                .OrderBy(r => r.gameType)
                .ThenBy(r => r.title)
                .ToList();

            return Json(results);
        }, nameof(GetGameServers)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GameChatLog(GameType id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                id,
                AuthPolicies.ChatLog_Read,
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
                AuthPolicies.ChatLog_Read,
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
                AuthPolicies.ChatLog_ReadServer,
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
                AuthPolicies.ChatLog_ReadServer,
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
                AuthPolicies.ChatLog_ReadServer,
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
    [Authorize(Policy = AuthPolicies.ChatLog_ReadServer)]
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

            var chatMessage = chatMessageApiResponse.Result.Data;
            if (chatMessage.GameServer is not null)
            {
                var authResult = await CheckAuthorizationAsync(
                    authorizationService,
                    chatMessage.GameServer.GameType,
                    AuthPolicies.ChatLog_ReadServer,
                    nameof(ChatLogPermaLink),
                    "ChatMessage",
                    $"MessageId:{id},GameType:{chatMessage.GameServer.GameType}",
                    chatMessage).ConfigureAwait(false);

                if (authResult is not null)
                    return authResult;
            }

            return View(chatMessage);
        }, nameof(ChatLogPermaLink)).ConfigureAwait(false);
    }

    [HttpPost]
    [Authorize(Policy = AuthPolicies.ChatLog_Lock)]
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
                AuthPolicies.ChatLog_Lock,
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
        return (!string.IsNullOrEmpty(xrw) && xrw.Equals("XMLHttpRequest", StringComparison.OrdinalIgnoreCase)) || Request.Headers.Accept.Any(h => !string.IsNullOrEmpty(h) && h.Contains("application/json", StringComparison.OrdinalIgnoreCase));
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

            order = orderColumn switch
            {
                "timestamp" => searchOrder == "asc" ? ChatMessageOrder.TimestampAsc : ChatMessageOrder.TimestampDesc,
                _ => order
            };
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