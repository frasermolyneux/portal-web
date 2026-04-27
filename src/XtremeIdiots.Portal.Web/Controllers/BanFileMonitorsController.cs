using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.AdminActions;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.CentralBanFileStatus;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.LiveStatus;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Read-only dashboard for ban file monitor status. The monitor row itself is owned
/// and upserted by the server agent — admins can no longer create, edit, or delete
/// monitors. To enable / disable monitoring for a server, toggle
/// <c>GameServer.BanFileSyncEnabled</c> on the GameServers/Edit page; the FTP path
/// is resolved automatically from per-game-type rules + <c>GameServer.BanFileRootPath</c>
/// + the live mod observed by the agent.
/// </summary>
[Authorize(Policy = AuthPolicies.GameServers_BanFileMonitors_Read)]
public class BanFileMonitorsController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<BanFileMonitorsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    /// <summary>
    /// Dashboard showing per-server status (last check / push / import, mod match,
    /// per-tag counts) plus per-game-type rollups (active permanent + temp bans,
    /// central blob freshness, line counts) so admins can see at a glance whether
    /// each server is protected and in sync with the central ban file.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        [FromQuery] bool showAll = false,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, AdditionalPermission.GameServers_BanFileMonitors_Read];
            var (gameTypes, banFileMonitorIds) = User.ClaimedGamesAndItemsForViewing(requiredClaims);

            // Fetch monitor rows + live status + central status + active-ban counts in parallel.
            // Each is independent and the dashboard composes them.
            var monitorsTask = repositoryApiClient.BanFileMonitors.V1.GetBanFileMonitors(
                gameTypes, banFileMonitorIds, null, 0, 200, BanFileMonitorOrder.ServerListPosition, cancellationToken);
            var liveStatusTask = repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken);
            var centralStatusTask = repositoryApiClient.CentralBanFileStatus.V1.GetCentralBanFileStatuses(cancellationToken);
            var activeBanCountsTask = repositoryApiClient.AdminActions.V1.GetActiveBanCounts(null, cancellationToken);

            await Task.WhenAll(monitorsTask, liveStatusTask, centralStatusTask, activeBanCountsTask).ConfigureAwait(false);

            var monitorsResponse = await monitorsTask.ConfigureAwait(false);
            if (!monitorsResponse.IsSuccess || monitorsResponse.Result?.Data?.Items is null)
            {
                Logger.LogError("Failed to retrieve ban file monitors for user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction("Display", "Errors", new { id = 500 });
            }

            var allMonitors = monitorsResponse.Result.Data.Items.ToList();

            // Active monitors are the ones the agent will actually run a check loop for.
            // Anything else (sync disabled or agent disabled) clutters the dashboard with
            // stale data and is hidden by default; admins can opt in via ?showAll=true to
            // diagnose misconfigurations.
            static bool IsActive(XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors.BanFileMonitorDto m)
            {
                return m.GameServer is not null
                    && m.GameServer.BanFileSyncEnabled
                    && m.GameServer.AgentEnabled;
            }

            var hiddenCount = showAll ? 0 : allMonitors.Count(m => !IsActive(m));
            var monitors = showAll
                ? (IReadOnlyList<XtremeIdiots.Portal.Repository.Abstractions.Models.V1.BanFileMonitors.BanFileMonitorDto>)allMonitors
                : allMonitors.Where(IsActive).ToList();

            var liveStatusResponse = await liveStatusTask.ConfigureAwait(false);
            var liveStatusLookup = liveStatusResponse.IsSuccess && liveStatusResponse.Result?.Data?.Items is not null
                ? liveStatusResponse.Result.Data.Items.DistinctBy(ls => ls.ServerId).ToDictionary(ls => ls.ServerId)
                : [];

            var centralStatusResponse = await centralStatusTask.ConfigureAwait(false);
            var centralStatusByGameType = centralStatusResponse.IsSuccess && centralStatusResponse.Result?.Data?.Items is not null
                ? centralStatusResponse.Result.Data.Items.ToDictionary(s => s.GameType)
                : [];

            var activeBanCountsResponse = await activeBanCountsTask.ConfigureAwait(false);
            var activeBanCountsByGameType = activeBanCountsResponse.IsSuccess && activeBanCountsResponse.Result?.Data?.Items is not null
                ? activeBanCountsResponse.Result.Data.Items.ToDictionary(c => c.GameType)
                : [];

            // Per-game-type cards reflect the full ban-counts view (DB has the same active
            // bans regardless of which servers happen to be enabled), but the visible
            // game-type set follows the currently-displayed monitors.
            var visibleGameTypes = monitors
                .Where(m => m.GameServer is not null)
                .Select(m => m.GameServer.GameType)
                .Distinct()
                .OrderBy(gt => gt)
                .ToList();

            var gameTypeCards = visibleGameTypes.Select(gt => new BanFileMonitorGameTypeCard
            {
                GameType = gt,
                ActiveBanCounts = activeBanCountsByGameType.GetValueOrDefault(gt),
                CentralStatus = centralStatusByGameType.GetValueOrDefault(gt)
            }).ToList();

            var serverIds = monitors.Where(m => m.GameServer is not null).Select(m => m.GameServerId).Distinct();
            var serverConfigs = await GameServerConfigHelper.FetchConfigsForServersAsync(
                repositoryApiClient, serverIds, Logger, cancellationToken).ConfigureAwait(false);

            var viewModel = new BanFileMonitorsDashboardViewModel
            {
                Monitors = monitors,
                LiveStatusLookup = liveStatusLookup,
                ServerConfigs = serverConfigs,
                GameTypeCards = gameTypeCards,
                ShowingAll = showAll,
                HiddenInactiveCount = hiddenCount
            };

            return View(viewModel);
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Read-only details page for a single ban file monitor — surfaces every status
    /// field for diagnostic deep-dives.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, banFileMonitorData) = await GetAuthorizedBanFileMonitorAsync(
                id, AuthPolicies.GameServers_BanFileMonitors_Read, nameof(Details), cancellationToken).ConfigureAwait(false);

            if (actionResult is not null)
                return actionResult;

            var serverConfigs = await GameServerConfigHelper.FetchConfigsForServersAsync(
                repositoryApiClient, [banFileMonitorData!.GameServerId], Logger, cancellationToken).ConfigureAwait(false);
            ViewBag.ServerConfigs = serverConfigs;

            // Pull the live status so the Details page can also call out a mod mismatch.
            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken).ConfigureAwait(false);
            ViewBag.LiveStatus = liveStatusResponse.IsSuccess && liveStatusResponse.Result?.Data?.Items is not null
                ? liveStatusResponse.Result.Data.Items.FirstOrDefault(ls => ls.ServerId == banFileMonitorData.GameServerId)
                : null;

            return View(banFileMonitorData);
        }, nameof(Details)).ConfigureAwait(false);
    }

    private async Task<(IActionResult? ActionResult, BanFileMonitorDto? BanFileMonitor)> GetAuthorizedBanFileMonitorAsync(
        Guid id,
        string policy,
        string action,
        CancellationToken cancellationToken = default)
    {
        var banFileMonitorApiResponse = await repositoryApiClient.BanFileMonitors.V1.GetBanFileMonitor(id, cancellationToken).ConfigureAwait(false);

        if (banFileMonitorApiResponse.IsNotFound || banFileMonitorApiResponse.Result?.Data?.GameServer is null)
        {
            Logger.LogWarning("Ban file monitor {BanFileMonitorId} not found when {Action}", id, action);
            return (NotFound(), null);
        }

        var banFileMonitorData = banFileMonitorApiResponse.Result.Data;
        var gameServerData = banFileMonitorData.GameServer;

        var authorizationResource = new Tuple<GameType, Guid>(gameServerData.GameType, gameServerData.GameServerId);
        var authResult = await CheckAuthorizationAsync(
            authorizationService,
            authorizationResource,
            policy,
            action,
            "BanFileMonitor",
            $"GameType:{gameServerData.GameType},GameServerId:{gameServerData.GameServerId}",
            banFileMonitorData).ConfigureAwait(false);

        return authResult is not null
            ? (authResult, null)
            : (null, banFileMonitorData);
    }
}
