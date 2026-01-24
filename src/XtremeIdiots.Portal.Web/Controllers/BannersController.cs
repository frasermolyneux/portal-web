using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Controller for managing banner views
/// </summary>
/// <remarks>
/// API endpoints for banner data are in ApiControllers/BannersController
/// </remarks>
[Authorize(Policy = AuthPolicies.AccessHome)]
public class BannersController(
    IAuthorizationService authorizationService,
    TelemetryClient telemetryClient,
    ILogger<BannersController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{
    private readonly IAuthorizationService authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));

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
            var authorizationResult = await authorizationService.AuthorizeAsync(User, null, AuthPolicies.AccessHome);
            if (!authorizationResult.Succeeded)
            {
                TrackUnauthorizedAccessAttempt("Access", nameof(GameServersList), "BannerManagement", null);
                return Unauthorized();
            }

            TrackSuccessTelemetry("GameServersListAccessed", nameof(GameServersList), new Dictionary<string, string>
            {
                { "Controller", nameof(BannersController) },
                { "Resource", nameof(GameServersList) },
                { "Context", "BannerManagement" }
            });

            return View();
        }, "Display game servers list view for banner management");
    }
}