using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
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

            var models = servers.Select(gs =>
            {
                liveStatusLookup.TryGetValue(gs.GameServerId, out var liveStatus);

                return new AgentServerSummary
                {
                    ServerId = gs.GameServerId,
                    ServerTitle = string.IsNullOrWhiteSpace(liveStatus?.Title) ? gs.Title : liveStatus.Title,
                    GameType = gs.GameType.ToString(),
                    PlayerCount = liveStatus?.CurrentPlayers ?? 0,
                    CurrentMap = liveStatus?.Map,
                    TelemetryState = OptionalDataState.Unavailable
                };
            }).ToList();

            Logger.LogInformation("User {UserId} retrieved agent status for {ServerCount} servers",
                User.XtremeIdiotsId(), models.Count);

            return View(models);
        }, nameof(AgentStatus)).ConfigureAwait(false);
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewTelemetry(
        [FromServices] CachedAgentTelemetryService telemetry, CancellationToken cancellationToken = default)
    {
        var result = await telemetry.GetAllAsync(cancellationToken).ConfigureAwait(false);
        return result.State == OptionalDataState.Unavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : PartialView("_AgentStatusTelemetry", result);
    }
}
