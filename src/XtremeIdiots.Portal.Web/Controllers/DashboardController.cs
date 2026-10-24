using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.Dashboard_Read)]
public class DashboardController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<DashboardController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    [HttpGet]
    public IActionResult Index(CancellationToken cancellationToken = default)
    {
        return View(new DashboardViewModel());
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewSummary(CancellationToken cancellationToken = default)
    {
        var response = await repositoryApiClient.Dashboard.V1.GetDashboardSummary(cancellationToken).ConfigureAwait(false);
        return !response.IsSuccess || response.Result?.Data is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : PartialView("_OverviewSummary", new DashboardViewModel { Summary = response.Result.Data });
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewLeaderboard(CancellationToken cancellationToken = default)
    {
        var response = await repositoryApiClient.Dashboard.V1.GetAdminLeaderboard(30, cancellationToken).ConfigureAwait(false);
        return !response.IsSuccess || response.Result?.Data?.Items is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : PartialView("_OverviewLeaderboard", new DashboardViewModel { AdminLeaderboard = [.. response.Result.Data.Items] });
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewTrend(CancellationToken cancellationToken = default)
    {
        var response = await repositoryApiClient.Dashboard.V1.GetModerationTrend(30, cancellationToken).ConfigureAwait(false);
        return !response.IsSuccess || response.Result?.Data?.Items is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : PartialView("_OverviewTrend", new DashboardViewModel { ModerationTrend = [.. response.Result.Data.Items] });
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewUtilization(CancellationToken cancellationToken = default)
    {
        var response = await repositoryApiClient.Dashboard.V1.GetServerUtilization(cancellationToken).ConfigureAwait(false);
        return !response.IsSuccess || response.Result?.Data is null
            ? StatusCode(StatusCodes.Status503ServiceUnavailable)
            : PartialView("_OverviewUtilization", new DashboardViewModel { ServerUtilization = response.Result.Data });
    }

    [HttpGet]
    public async Task<IActionResult> GetOverviewTelemetry(
        [FromServices] CachedAgentTelemetryService telemetry, CancellationToken cancellationToken = default)
    {
        var serversTask = repositoryApiClient.GameServers.V1.GetGameServers(
            null, null, GameServerFilter.AgentEnabled, 0, 100, GameServerOrder.ServerListPosition, cancellationToken);
        var liveTask = repositoryApiClient.LiveStatus.V1.GetAllGameServerLiveStatuses(cancellationToken);
        await Task.WhenAll(serversTask, liveTask).ConfigureAwait(false);
        var serversResponse = await serversTask.ConfigureAwait(false);
        if (!serversResponse.IsSuccess || serversResponse.Result?.Data?.Items is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        var liveResponse = await liveTask.ConfigureAwait(false);
        var liveLookup = liveResponse.IsSuccess && liveResponse.Result?.Data?.Items is not null
            ? liveResponse.Result.Data.Items.GroupBy(s => s.ServerId).ToDictionary(g => g.Key, g => g.First())
            : [];
        var result = await telemetry.GetAllAsync(cancellationToken).ConfigureAwait(false);
        var lookup = result.Data?.GroupBy(s => s.ServerId).ToDictionary(g => g.Key, g => g.First());
        var models = serversResponse.Result.Data.Items.Select(gs =>
        {
            var summary = lookup?.GetValueOrDefault(gs.GameServerId);
            liveLookup.TryGetValue(gs.GameServerId, out var live);
            return (summary ?? new AgentServerSummary
            {
                ServerId = gs.GameServerId,
                ActivityStatus = AgentActivityStatus.Offline
            }) with
            {
                ServerTitle = string.IsNullOrWhiteSpace(live?.Title) ? gs.Title : live.Title,
                GameType = gs.GameType.ToString(),
                CurrentMap = summary?.CurrentMap ?? live?.Map,
                TelemetryState = result.State
            };
        }).ToList();
        return PartialView("_OverviewTelemetry", new DashboardViewModel
        {
            AgentStatuses = models,
            AgentTelemetryUnavailable = result.State == OptionalDataState.Unavailable,
            AgentTelemetryStale = result.State == OptionalDataState.Stale
        });
    }
}
