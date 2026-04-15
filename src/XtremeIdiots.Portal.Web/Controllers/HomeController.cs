using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Handles the main dashboard and home page functionality
/// </summary>
[Authorize]
public class HomeController(
    TelemetryClient telemetryClient,
    ILogger<HomeController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    // No additional dependencies required for current actions

    /// <summary>
    /// Displays the main dashboard for authenticated users
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Dashboard view with user-specific content</returns>
    [HttpGet]
    public IActionResult Index(CancellationToken cancellationToken = default)
    {
        return View();
    }
}