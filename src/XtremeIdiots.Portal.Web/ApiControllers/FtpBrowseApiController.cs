using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize]
[Route("api/ftp")]
public class FtpBrowseApiController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    IServersApiClient serversApiClient,
    TelemetryClient telemetryClient,
    ILogger<FtpBrowseApiController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{
    [HttpGet("{gameServerId:guid}/browse")]
    public async Task<IActionResult> Browse(Guid gameServerId, [FromQuery] string? path = null)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var gameServerResponse = await repositoryApiClient.GameServers.V1.GetGameServer(gameServerId).ConfigureAwait(false);
            if (!gameServerResponse.IsSuccess || gameServerResponse.Result?.Data is null)
                return Forbid();

            var gameServer = gameServerResponse.Result.Data;
            var authResult = await authorizationService.AuthorizeAsync(User, gameServer.GameType, AuthPolicies.GameServers_Credentials_Ftp_Write).ConfigureAwait(false);
            if (!authResult.Succeeded)
                return Forbid();

            var result = await serversApiClient.FtpBrowse.V1.BrowseDirectory(gameServerId, path).ConfigureAwait(false);

            if (!result.IsSuccess || result.Result?.Data == null)
                return StatusCode((int)result.StatusCode);

            return Ok(result.Result.Data);
        }, nameof(Browse)).ConfigureAwait(false);
    }
}
