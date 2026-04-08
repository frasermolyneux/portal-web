using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
                viewModel.AgentStatuses = await agentTelemetryService.GetAllServersStatusAsync(cancellationToken).ConfigureAwait(false);
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
