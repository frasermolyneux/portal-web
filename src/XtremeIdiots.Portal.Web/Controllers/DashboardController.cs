using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for the admin dashboard, providing an at-a-glance operational overview.
/// </summary>
[Authorize(Policy = AuthPolicies.AccessDashboard)]
public class DashboardController(
    IRepositoryApiClient repositoryApiClient,
    IAgentTelemetryService agentTelemetryService,
    TelemetryClient telemetryClient,
    ILogger<DashboardController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{
    /// <summary>
    /// Displays the admin dashboard with summary cards, server health, moderation trends, and admin leaderboard.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var viewModel = new DashboardViewModel();

            // Fetch all data sources in parallel
            var summaryTask = repositoryApiClient.Dashboard.V1.GetDashboardSummary(cancellationToken);
            var leaderboardTask = repositoryApiClient.Dashboard.V1.GetAdminLeaderboard(30, cancellationToken);
            var trendTask = repositoryApiClient.Dashboard.V1.GetModerationTrend(30, cancellationToken);
            var utilizationTask = repositoryApiClient.Dashboard.V1.GetServerUtilization(cancellationToken);

            await Task.WhenAll(summaryTask, leaderboardTask, trendTask, utilizationTask).ConfigureAwait(false);

            // Summary
            var summaryResponse = await summaryTask.ConfigureAwait(false);
            if (summaryResponse.IsSuccess && summaryResponse.Result?.Data is not null)
            {
                viewModel.Summary = summaryResponse.Result.Data;
            }

            // Admin leaderboard
            var leaderboardResponse = await leaderboardTask.ConfigureAwait(false);
            if (leaderboardResponse.IsSuccess && leaderboardResponse.Result?.Data?.Items is not null)
            {
                viewModel.AdminLeaderboard = [.. leaderboardResponse.Result.Data.Items];
            }

            // Moderation trend
            var trendResponse = await trendTask.ConfigureAwait(false);
            if (trendResponse.IsSuccess && trendResponse.Result?.Data?.Items is not null)
            {
                viewModel.ModerationTrend = [.. trendResponse.Result.Data.Items];
            }

            // Server utilization
            var utilizationResponse = await utilizationTask.ConfigureAwait(false);
            if (utilizationResponse.IsSuccess && utilizationResponse.Result?.Data is not null)
            {
                viewModel.ServerUtilization = utilizationResponse.Result.Data;
            }

            // Agent telemetry (non-critical — dashboard renders without it)
            try
            {
                var telemetryTask = agentTelemetryService.GetAllServersStatusAsync(cancellationToken);
                var gameServersTask = repositoryApiClient.GameServers.V1.GetGameServers(
                    null, null, GameServerFilter.AgentEnabled, 0, 100,
                    GameServerOrder.ServerListPosition, cancellationToken);

                await Task.WhenAll(telemetryTask, gameServersTask).ConfigureAwait(false);

                var telemetry = await telemetryTask.ConfigureAwait(false);
                var gameServersResponse = await gameServersTask.ConfigureAwait(false);

                var serverLookup = gameServersResponse.IsSuccess && gameServersResponse.Result?.Data?.Items is not null
                    ? gameServersResponse.Result.Data.Items
                        .GroupBy(gs => gs.GameServerId)
                        .ToDictionary(g => g.Key, g => g.First())
                    : [];

                // Enrich telemetry with server names from the repository API
                var telemetryByServer = telemetry
                    .GroupBy(t => t.ServerId)
                    .ToDictionary(g => g.Key, g => g.First());
                viewModel.AgentStatuses = serverLookup.Values.Select(gs =>
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
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to retrieve agent telemetry for dashboard");
                viewModel.AgentTelemetryUnavailable = true;
            }

            TrackSuccessTelemetry("DashboardLoaded", nameof(Index));

            Logger.LogInformation("User {UserId} loaded admin dashboard", User.XtremeIdiotsId());

            return View(viewModel);
        }, nameof(Index)).ConfigureAwait(false);
    }
}
