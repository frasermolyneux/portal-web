using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Public change log page. Surfaces portal development activity (commits, PRs,
/// build status) sourced directly from GitHub's public REST API via
/// client-side fetches — no portal backend data is returned from this
/// controller.
/// </summary>
[AllowAnonymous]
public class ChangeLogController(
    TelemetryClient telemetryClient,
    ILogger<ChangeLogController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    /// <summary>
    /// Displays the change log index page showing application updates and version history
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The change log view</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult<IActionResult>(View()), nameof(Index)).ConfigureAwait(false);
    }
}