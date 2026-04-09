using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize(Policy = AuthPolicies.EditGameServerFtp)]
[Route("api/ftp")]
public class FtpBrowseApiController(
    IServersApiClient serversApiClient,
    TelemetryClient telemetryClient,
    ILogger<FtpBrowseApiController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    [HttpGet("{gameServerId:guid}/browse")]
    public async Task<IActionResult> Browse(Guid gameServerId, [FromQuery] string? path = null)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var result = await serversApiClient.FtpBrowse.V1.BrowseDirectory(gameServerId, path).ConfigureAwait(false);

            if (!result.IsSuccess || result.Result?.Data == null)
                return StatusCode((int)result.StatusCode);

            return Ok(result.Result.Data);
        }, nameof(Browse)).ConfigureAwait(false);
    }
}
