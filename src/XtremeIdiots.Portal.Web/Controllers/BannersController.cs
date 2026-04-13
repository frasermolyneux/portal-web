using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for managing banner views
/// </summary>
/// <remarks>
/// API endpoints for banner data are in ApiControllers/BannersController
/// </remarks>
[Authorize]
public class BannersController(
    TelemetryClient telemetryClient,
    ILogger<BannersController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Displays the game servers list view for banner management
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View for displaying game servers list</returns>
    [HttpGet]
    public async Task<IActionResult> GameServersList(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            TrackSuccessTelemetry("GameServersListAccessed", nameof(GameServersList), new Dictionary<string, string>
            {
                { "Controller", nameof(BannersController) },
                { "Resource", nameof(GameServersList) },
                { "Context", "BannerManagement" }
            });

            return View();
        }, "Display game servers list view for banner management").ConfigureAwait(false);
    }
}