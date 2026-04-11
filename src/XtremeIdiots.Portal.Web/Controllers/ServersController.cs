using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for managing server information and functionality
/// </summary>
/// <remarks>
/// Initializes a new instance of the ServersController
/// </remarks>
/// <param name="repositoryApiClient">Client for repository API operations</param>
/// <param name="agentTelemetryService">Service for querying agent telemetry from Application Insights</param>
/// <param name="telemetryClient">Client for application telemetry</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
/// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
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
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with list of enabled servers or error page on failure</returns>
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

            var result = gameServersApiResponse.Result.Data.Items
                .Select(gs => new ServersGameServerViewModel(gs))
                .ToList();

            Logger.LogInformation("User {UserId} successfully retrieved {ServerCount} servers",
                User.XtremeIdiotsId(), result.Count);

            return View(result);
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the interactive map view showing recent player locations
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with geo-located recent players or empty list on failure</returns>
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
}