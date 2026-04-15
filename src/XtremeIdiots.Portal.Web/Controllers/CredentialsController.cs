using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using MX.Observability.ApplicationInsights.Auditing.Models;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages the display and authorization of game server credentials for authenticated users
/// </summary>
/// <remarks>
/// This controller handles FTP and RCON credentials for game servers based on user authorization levels.
/// Users can view credentials only for servers they have explicit permission to access.
/// </remarks>
[Authorize(Policy = AuthPolicies.GameServers_Read)]
public class CredentialsController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<CredentialsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{

    /// <summary>
    /// Displays the credentials index page with game server credentials for authorized users
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with list of game servers and their accessible credentials</returns>
    /// <exception cref="UnauthorizedAccessException">Thrown when user lacks permission to view specific credentials</exception>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            string[] requiredClaims = [UserProfileClaimType.SeniorAdmin, UserProfileClaimType.HeadAdmin, UserProfileClaimType.GameAdmin, AdditionalPermission.GameServers_Credentials_Ftp_Read, AdditionalPermission.GameServers_Credentials_Rcon_Read];
            var (gameTypes, gameServerIds) = User.ClaimedGamesAndItems(requiredClaims);

            Logger.LogInformation("User {UserId} querying game servers for credentials with {GameTypeCount} game types and {GameServerIdCount} specific servers",
                User.XtremeIdiotsId(), gameTypes?.Length ?? 0, gameServerIds?.Length ?? 0);

            var gameServersList = await GetAuthorizedGameServersAsync(gameTypes, gameServerIds, cancellationToken).ConfigureAwait(false);
            if (gameServersList is null)
            {
                return RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController).Replace("Controller", ""), new { id = 500 });
            }

            await ApplyCredentialAuthorizationAsync(gameServersList, cancellationToken).ConfigureAwait(false);

            var serverConfigs = await GameServerConfigHelper.FetchConfigsForServersAsync(
                repositoryApiClient, gameServersList.Select(s => s.GameServerId), Logger, cancellationToken).ConfigureAwait(false);
            ViewBag.ServerConfigs = serverConfigs;

            TrackSuccessTelemetry(nameof(Index), nameof(CredentialsController), new Dictionary<string, string>
            {
                { nameof(CredentialsController), nameof(CredentialsController) },
                { "Resource", "GameServerCredentials" },
                { "Context", "CredentialsDisplay" },
                { "GameServerCount", gameServersList.Count.ToString() }
            });

            Logger.LogInformation("User {UserId} successfully viewed credentials for {GameServerCount} game servers",
                User.XtremeIdiotsId(), gameServersList.Count);

            return View(gameServersList);
        }, "Display credentials index page with game server credentials").ConfigureAwait(false);
    }

    private async Task<List<GameServerDto>?> GetAuthorizedGameServersAsync(GameType[]? gameTypes, Guid[]? gameServerIds, CancellationToken cancellationToken)
    {
        var aggregate = new Dictionary<Guid, GameServerDto>();
        var anySuccess = false;

        // Query by game types (HeadAdmin / GameAdmin breadth)
        if (gameTypes is not null && gameTypes.Length > 0)
        {
            var byGameTypesResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                gameTypes, null, null, 0, 50, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (byGameTypesResponse.IsSuccess && byGameTypesResponse.Result?.Data?.Items is not null)
            {
                foreach (var item in byGameTypesResponse.Result.Data.Items)
                {
                    if (!aggregate.ContainsKey(item.GameServerId))
                        aggregate[item.GameServerId] = item;
                }

                anySuccess = true;
            }
            else
            {
                Logger.LogWarning("Failed game type credential query for user {UserId}", User.XtremeIdiotsId());
                AuditLogger.LogAudit(AuditEvent.UserAction("CredentialsApiFailure", AuditAction.Read)
                    .WithActor(User.XtremeIdiotsId() ?? "Unknown", User.Username())
                    .WithProperty("Scope", "GameTypes")
                    .WithProperty("Controller", nameof(CredentialsController))
                    .WithProperty("Action", nameof(GetAuthorizedGameServersAsync))
                    .WithProperty("UserId", User.XtremeIdiotsId() ?? "Unknown")
                    .Build());
            }
        }

        // Query by explicit server IDs (per-server credential claims)
        if (gameServerIds is not null && gameServerIds.Length > 0)
        {
            var byServerIdsResponse = await repositoryApiClient.GameServers.V1.GetGameServers(
                null, gameServerIds, null, 0, 50, GameServerOrder.ServerListPosition, cancellationToken).ConfigureAwait(false);

            if (byServerIdsResponse.IsSuccess && byServerIdsResponse.Result?.Data?.Items is not null)
            {
                foreach (var item in byServerIdsResponse.Result.Data.Items)
                {
                    if (!aggregate.ContainsKey(item.GameServerId))
                        aggregate[item.GameServerId] = item;
                }

                anySuccess = true;
            }
            else
            {
                Logger.LogWarning("Failed server id credential query for user {UserId}", User.XtremeIdiotsId());
                AuditLogger.LogAudit(AuditEvent.UserAction("CredentialsApiFailure", AuditAction.Read)
                    .WithActor(User.XtremeIdiotsId() ?? "Unknown", User.Username())
                    .WithProperty("Scope", "ServerIds")
                    .WithProperty("Controller", nameof(CredentialsController))
                    .WithProperty("Action", nameof(GetAuthorizedGameServersAsync))
                    .WithProperty("UserId", User.XtremeIdiotsId() ?? "Unknown")
                    .Build());
            }
        }

        if (!anySuccess)
            return null;

        // Preserve insertion order (game types first, then server IDs) by iterating dictionary values
        return [.. aggregate.Values];
    }

    private async Task ApplyCredentialAuthorizationAsync(List<GameServerDto> gameServersList, CancellationToken cancellationToken)
    {
        foreach (var gameServerDto in gameServersList)
        {
            var ftpResource = new Tuple<GameType, Guid>(gameServerDto.GameType, gameServerDto.GameServerId);
            var canViewFtpCredential = await authorizationService.AuthorizeAsync(User, ftpResource, AuthPolicies.GameServers_Credentials_Ftp_Read).ConfigureAwait(false);

            if (!canViewFtpCredential.Succeeded)
            {
                TrackUnauthorizedAccessAttempt(nameof(AuthPolicies.GameServers_Credentials_Ftp_Read), "FtpCredential",
                    $"GameType:{gameServerDto.GameType},GameServerId:{gameServerDto.GameServerId}", gameServerDto);
            }

            var canViewRconCredential = await authorizationService.AuthorizeAsync(User, ftpResource, AuthPolicies.GameServers_Credentials_Rcon_Read).ConfigureAwait(false);

            if (!canViewRconCredential.Succeeded)
            {
                TrackUnauthorizedAccessAttempt(nameof(AuthPolicies.GameServers_Credentials_Rcon_Read), "RconCredential",
                    $"GameType:{gameServerDto.GameType},GameServerId:{gameServerDto.GameServerId}", gameServerDto);
            }
        }
    }
}