using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for managing system status and monitoring operations
/// </summary>
/// <remarks>
/// Initializes a new instance of the StatusController
/// </remarks>
/// <param name="repositoryApiClient">Client for repository API operations</param>
/// <param name="agentTelemetryService">Service for querying agent telemetry from Application Insights</param>
/// <param name="telemetryClient">Client for application telemetry</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
/// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
[Authorize(Policy = AuthPolicies.AccessStatus)]
public class StatusController(
    IRepositoryApiClient repositoryApiClient,
    IAgentTelemetryService agentTelemetryService,
    TelemetryClient telemetryClient,
    ILogger<StatusController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Displays the ban file monitor status page showing synchronization status and file information
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with ban file monitor status information or empty list if none found</returns>
    [HttpGet]
    public async Task<IActionResult> BanFileStatus(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, UserProfileClaimType.BanFileMonitor];
            var (gameTypes, banFileMonitorIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            Logger.LogInformation("User {UserId} has access to {GameTypeCount} game types and {MonitorCount} ban file monitors",
                User.XtremeIdiotsId(), gameTypes.Length, banFileMonitorIds.Length);

            var banFileMonitorsApiResponse = await repositoryApiClient.BanFileMonitors.V1.GetBanFileMonitors(
                gameTypes, banFileMonitorIds, null, 0, 50, BanFileMonitorOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (banFileMonitorsApiResponse.IsNotFound || banFileMonitorsApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("No ban file monitors found for user {UserId}", User.XtremeIdiotsId());
                return View(Array.Empty<EditBanFileMonitorViewModel>());
            }

            List<EditBanFileMonitorViewModel> models = [];

            foreach (var banFileMonitor in banFileMonitorsApiResponse.Result.Data.Items)
            {
                var (actionResult, gameServerData) = await GetGameServerDataAsync(banFileMonitor.GameServerId, banFileMonitor.BanFileMonitorId, cancellationToken).ConfigureAwait(false);

                if (actionResult is not null)
                {
                    continue;
                }

                if (gameServerData is not null)
                {
                    models.Add(new EditBanFileMonitorViewModel
                    {
                        BanFileMonitorId = banFileMonitor.BanFileMonitorId,
                        FilePath = banFileMonitor.FilePath,
                        RemoteFileSize = banFileMonitor.RemoteFileSize,
                        LastSync = banFileMonitor.LastSync,
                        GameServerId = banFileMonitor.GameServerId,
                        GameServer = gameServerData
                    });
                }
            }

            TrackSuccessTelemetry("BanFileStatusRetrieved", nameof(BanFileStatus), new Dictionary<string, string>
            {
                { "MonitorCount", models.Count.ToString() },
                { "GameTypeCount", gameTypes.Length.ToString() }
            });

            Logger.LogInformation("User {UserId} successfully retrieved {MonitorCount} ban file monitor statuses",
                User.XtremeIdiotsId(), models.Count);

            var serverIds = models.Where(m => m.GameServer != null).Select(m => m.GameServerId).Distinct();
            var serverConfigs = await GameServerConfigHelper.FetchConfigsForServersAsync(
                repositoryApiClient, serverIds, Logger, cancellationToken).ConfigureAwait(false);
            ViewBag.ServerConfigs = serverConfigs;

            return View(models);
        }, nameof(BanFileStatus)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the agent status page showing telemetry for all agent-enabled game servers
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with agent status information for all servers</returns>
    [HttpGet]
    public async Task<IActionResult> AgentStatus(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServersApiResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                null, null, GameServerFilter.AgentEnabled, 0, 100,
                GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            var servers = gameServersApiResponse.IsSuccess && gameServersApiResponse.Result?.Data?.Items is not null
                ? [.. gameServersApiResponse.Result.Data.Items]
                : new List<GameServerDto>();

            IReadOnlyList<AgentServerSummary> telemetry = Array.Empty<AgentServerSummary>();
            try
            {
                telemetry = await agentTelemetryService.GetAllServersStatusAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to retrieve agent telemetry for status page");
            }

            var telemetryByServer = telemetry.ToDictionary(t => t.ServerId);

            var models = servers.Select(gs =>
            {
                telemetryByServer.TryGetValue(gs.GameServerId, out var summary);

                return new AgentServerSummary
                {
                    ServerId = gs.GameServerId,
                    ServerTitle = string.IsNullOrWhiteSpace(gs.LiveTitle) ? gs.Title : gs.LiveTitle,
                    GameType = gs.GameType.ToString(),
                    LastEventReceived = summary?.LastEventReceived,
                    EventsLastHour = summary?.EventsLastHour ?? 0,
                    PlayerCount = summary?.PlayerCount ?? 0,
                    CurrentMap = summary?.CurrentMap ?? gs.LiveMap,
                    IsAgentActive = summary?.IsAgentActive ?? false,
                    ActivityStatus = summary?.ActivityStatus ?? AgentActivityStatus.Offline
                };
            }).ToList();

            TrackSuccessTelemetry("AgentStatusRetrieved", nameof(AgentStatus), new Dictionary<string, string>
            {
                { "ServerCount", models.Count.ToString() },
                { "ActiveCount", models.Count(m => m.ActivityStatus == AgentActivityStatus.Active).ToString() },
                { "IdleCount", models.Count(m => m.ActivityStatus == AgentActivityStatus.Idle).ToString() },
                { "OfflineCount", models.Count(m => m.ActivityStatus == AgentActivityStatus.Offline).ToString() }
            });

            Logger.LogInformation("User {UserId} retrieved agent status for {ServerCount} servers",
                User.XtremeIdiotsId(), models.Count);

            return View(models);
        }, nameof(AgentStatus)).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves game server data for a specific ban file monitor
    /// </summary>
    /// <param name="gameServerId">The unique identifier of the game server</param>
    /// <param name="banFileMonitorId">The unique identifier of the ban file monitor</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Tuple containing potential action result for errors and game server data if successful</returns>
    private async Task<(IActionResult? ActionResult, GameServerDto? Data)> GetGameServerDataAsync(
        Guid gameServerId,
        Guid banFileMonitorId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var gameServerApiResponse = await repositoryApiClient.GameServers.V1.GetGameServer(gameServerId, cancellationToken).ConfigureAwait(false);

            if (gameServerApiResponse.IsNotFound || gameServerApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Game server {GameServerId} not found for ban file monitor {BanFileMonitorId}",
                    gameServerId, banFileMonitorId);
                return (null, null);
            }

            return (null, gameServerApiResponse.Result.Data);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error retrieving game server {GameServerId} for ban file monitor {BanFileMonitorId}",
                gameServerId, banFileMonitorId);

            TrackErrorTelemetry(ex, nameof(GetGameServerDataAsync), new Dictionary<string, string>
            {
                { nameof(gameServerId), gameServerId.ToString() },
                { nameof(banFileMonitorId), banFileMonitorId.ToString() }
            });

            return (null, null);
        }
    }
}