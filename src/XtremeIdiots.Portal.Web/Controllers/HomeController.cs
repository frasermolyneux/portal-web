using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Handles the public landing / home page. The content is a public marketing
/// page (community links, public server list via view component, donation block)
/// and must remain reachable without authentication.
/// </summary>
[AllowAnonymous]
public class HomeController(
    TelemetryClient telemetryClient,
    ILogger<HomeController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    // No additional dependencies required for current actions

    /// <summary>
    /// Displays the public landing page
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>Landing page view</returns>
    [HttpGet]
    public IActionResult Index(CancellationToken cancellationToken = default)
    {
        return View();
    }
}