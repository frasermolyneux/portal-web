using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize(Policy = AuthPolicies.GameServers_Read)]
[Route("GameServers")]
public class GameServersApiController(
    IRepositoryApiClient repositoryApiClient,
    IAuthorizationService authorizationService,
    TelemetryClient telemetryClient,
    ILogger<GameServersApiController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    [HttpPost("UpdateOrder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateOrder([FromBody] List<Guid> gameServerIds, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                null!,
                AuthPolicies.GameServers_Delete,
                nameof(UpdateOrder),
                "GameServer",
                "ReorderServers").ConfigureAwait(false);

            if (authResult is not null)
                return authResult;

            if (gameServerIds is null)
                return BadRequest(new { success = false, message = "No server IDs provided." });

            var dto = new UpdateGameServerOrderDto { GameServerIds = gameServerIds };
            var result = await repositoryApiClient.GameServers.V1.UpdateGameServerOrder(dto, cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Logger.LogWarning("Failed to update game server order for user {UserId}", User.XtremeIdiotsId());
                return StatusCode(500, new { success = false, message = "Failed to save server order. Please try again." });
            }

            TrackSuccessTelemetry("GameServerOrderUpdated", nameof(UpdateOrder), new Dictionary<string, string>
            {
                { "ServerCount", gameServerIds.Count.ToString() }
            });

            return Ok(new { success = true, message = "Server order saved successfully." });
        }, nameof(UpdateOrder)).ConfigureAwait(false);
    }
}
