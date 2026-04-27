using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for system-level status monitoring. The legacy <c>BanFileStatus</c>
/// action was folded into the BanFileMonitors dashboard — see
/// <see cref="BanFileMonitorsController.Index"/>.
/// </summary>
[Authorize(Policy = AuthPolicies.GameServers_BanFileMonitors_Read)]
public class StatusController(
    IRepositoryApiClient repositoryApiClient,
    IAgentTelemetryService agentTelemetryService,
    TelemetryClient telemetryClient,
    ILogger<StatusController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    /// <summary>
    /// Permanent redirect for the legacy <c>/Status/BanFileStatus</c> URL into the
    /// new BanFileMonitors dashboard. Kept so bookmarks and external links continue
    /// to land on the right page.
    /// </summary>
    [HttpGet]
    public IActionResult BanFileStatus()
    {
        return RedirectToActionPermanent(nameof(BanFileMonitorsController.Index), "BanFileMonitors");
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

            var liveStatusResponse = await repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken).ConfigureAwait(false);
            var liveStatusLookup = liveStatusResponse.IsSuccess && liveStatusResponse.Result?.Data?.Items is not null
                ? liveStatusResponse.Result.Data.Items.ToDictionary(ls => ls.ServerId)
                : [];

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
                liveStatusLookup.TryGetValue(gs.GameServerId, out var liveStatus);

                return new AgentServerSummary
                {
                    ServerId = gs.GameServerId,
                    ServerTitle = string.IsNullOrWhiteSpace(liveStatus?.Title) ? gs.Title : liveStatus.Title,
                    GameType = gs.GameType.ToString(),
                    LastEventReceived = summary?.LastEventReceived,
                    EventsLastHour = summary?.EventsLastHour ?? 0,
                    PlayerCount = summary?.PlayerCount ?? 0,
                    CurrentMap = summary?.CurrentMap ?? liveStatus?.Map,
                    IsAgentActive = summary?.IsAgentActive ?? false,
                    ActivityStatus = summary?.ActivityStatus ?? AgentActivityStatus.Offline
                };
            }).ToList();

            Logger.LogInformation("User {UserId} retrieved agent status for {ServerCount} servers",
                User.XtremeIdiotsId(), models.Count);

            return View(models);
        }, nameof(AgentStatus)).ConfigureAwait(false);
    }
}
