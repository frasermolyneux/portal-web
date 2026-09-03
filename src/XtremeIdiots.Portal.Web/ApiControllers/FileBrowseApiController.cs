using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Integrations.Servers.Api.Client.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize]
[Route("api/file-browse")]
public class FileBrowseApiController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    IServersApiClient serversApiClient,
    TelemetryClient telemetryClient,
    ILogger<FileBrowseApiController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{
    private readonly static Dictionary<string, FileBrowsePurpose> browsePurposes =
        new(StringComparer.Ordinal)
        {
            ["game-server-configuration"] = FileBrowsePurpose.GameServerConfiguration,
            ["file-transport-configuration"] = FileBrowsePurpose.FileTransportConfiguration,
            ["screenshot-configuration"] = FileBrowsePurpose.ScreenshotConfiguration,
            ["map-rotation-assignment"] = FileBrowsePurpose.MapRotationAssignment
        };

    [HttpGet("{gameServerId:guid}/browse")]
    public async Task<IActionResult> Browse(Guid gameServerId, [FromQuery] string? purpose, [FromQuery] string? path = null)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!TryParsePurpose(purpose, out var browsePurpose))
                return BadRequest("A supported file browse purpose is required.");

            var gameServerResponse = await repositoryApiClient.GameServers.V1.GetGameServer(gameServerId).ConfigureAwait(false);
            if (!gameServerResponse.IsSuccess || gameServerResponse.Result?.Data is null)
                return Forbid();

            var gameServer = gameServerResponse.Result.Data;

            foreach (var (resource, policy) in GetRequiredPolicies(browsePurpose, gameServer.GameType, gameServerId))
            {
                var authResult = await authorizationService.AuthorizeAsync(User, resource, policy).ConfigureAwait(false);
                if (!authResult.Succeeded)
                    return Forbid();
            }

            if (!gameServer.FileTransportEnabled || gameServer.FileTransportType == FileTransportType.Unknown)
                return BadRequest("File transport is not available for this server.");

            var result = await serversApiClient.FileBrowse.V1.BrowseDirectory(gameServerId, path).ConfigureAwait(false);

            return !result.IsSuccess || result.Result?.Data == null ? StatusCode((int)result.StatusCode, result.Result) : (IActionResult)Ok(result.Result.Data);
        }, nameof(Browse)).ConfigureAwait(false);
    }

    private static bool TryParsePurpose(string? value, out FileBrowsePurpose purpose)
    {
        purpose = default;
        return value is not null && browsePurposes.TryGetValue(value, out purpose);
    }

    private static IEnumerable<(object Resource, string Policy)> GetRequiredPolicies(
        FileBrowsePurpose purpose,
        GameType gameType,
        Guid gameServerId)
    {
        return purpose switch
        {
            FileBrowsePurpose.GameServerConfiguration =>
            [
                (gameType, AuthPolicies.GameServers_Write)
            ],
            FileBrowsePurpose.FileTransportConfiguration =>
            [
                (gameType, AuthPolicies.GameServers_Write),
                (gameType, AuthPolicies.GameServers_Credentials_FileTransport_Write)
            ],
            FileBrowsePurpose.ScreenshotConfiguration =>
            [
                (gameType, AuthPolicies.GameServers_Write),
                (gameType, AuthPolicies.GameServers_Admin_Screenshots_Configure)
            ],
            FileBrowsePurpose.MapRotationAssignment =>
            [
                ((gameType, gameServerId), AuthPolicies.MapRotations_Deploy)
            ],
            _ => []
        };
    }
}
