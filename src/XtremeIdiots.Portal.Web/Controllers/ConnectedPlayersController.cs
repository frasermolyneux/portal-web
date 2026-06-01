using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.Observability.ApplicationInsights.Auditing;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.AdminActions_Read)]
public class ConnectedPlayersController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<ConnectedPlayersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    private const int DefaultPageSize = 500;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var response = await repositoryApiClient.ConnectedPlayers.V1
                .GetConnectedPlayers(null, null, null, null, 0, DefaultPageSize, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccess || response.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve connected players index for user {UserId}", User.XtremeIdiotsId());
                return RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController)[..^10], new { id = 500 });
            }

            var allConnectedPlayers = response.Result.Data.Items.ToList();
            var totalCount = response.Result.Pagination?.TotalCount ?? allConnectedPlayers.Count;
            var skip = allConnectedPlayers.Count;

            while (skip < totalCount)
            {
                var pagedResponse = await repositoryApiClient.ConnectedPlayers.V1
                    .GetConnectedPlayers(null, null, null, null, skip, DefaultPageSize, cancellationToken)
                    .ConfigureAwait(false);

                if (!pagedResponse.IsSuccess || pagedResponse.Result?.Data?.Items is null)
                {
                    Logger.LogWarning("Failed loading connected players page at {LoadedCount}/{TotalCount}", skip, totalCount);
                    return RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController)[..^10], new { id = 500 });
                }

                var pageItems = pagedResponse.Result.Data.Items.ToList();
                if (pageItems.Count == 0)
                {
                    break;
                }

                allConnectedPlayers.AddRange(pageItems);
                skip += pageItems.Count;
            }

            var model = new ConnectedPlayersAdminViewModel
            {
                ConnectedPlayers = [.. allConnectedPlayers.Select(x => new ConnectedPlayerAdminItemViewModel
                {
                    ConnectedPlayerProfileId = x.ConnectedPlayerProfileId,
                    PlayerId = x.PlayerId,
                    UserProfileId = x.UserProfileId,
                    GameType = x.GameType,
                    Username = x.Username,
                    LinkMethod = x.LinkMethod.ToString(),
                    LinkedAtUtc = x.LinkedAtUtc,
                    UnlinkedAtUtc = x.UnlinkedAtUtc,
                    IsActive = x.IsActive
                })],
                FilterGameType = null,
                FilterIsActive = null,
                CurrentPage = 1,
                PageSize = DefaultPageSize,
                TotalCount = totalCount,
                IsSeniorAdmin = IsSeniorAdminUser()
            };

            return View(model);
        }, nameof(Index)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManualLink(CreateConnectedPlayerLinkInput input, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!IsSeniorAdminUser())
            {
                TrackUnauthorizedAccessAttempt(nameof(CreateManualLink), nameof(ConnectedPlayersController), "SeniorAdminRequired");
                return Forbid();
            }

            if (input.PlayerId == Guid.Empty || input.UserProfileId == Guid.Empty)
            {
                this.AddAlertDanger("PlayerId and UserProfileId are required.");
                return RedirectToAction(nameof(Index));
            }

            var linkedByUserProfileId = TryGetCurrentUserProfileId();
            if (linkedByUserProfileId is null)
            {
                this.AddAlertDanger("Your profile context is missing. Please sign out and back in before creating manual links.");
                return Forbid();
            }

            var apiResult = await repositoryApiClient.ConnectedPlayers.V1
                .CreateConnectedPlayerLink(new CreateConnectedPlayerLinkDto
                {
                    PlayerId = input.PlayerId,
                    UserProfileId = input.UserProfileId,
                    LinkedByUserProfileId = linkedByUserProfileId,
                    LinkMethod = ConnectedPlayerLinkMethod.AdminForced
                }, cancellationToken)
                .ConfigureAwait(false);

            if (apiResult.IsSuccess)
            {
                this.AddAlertSuccess("Connected player link created successfully.");
            }
            else
            {
                this.AddAlertDanger("Failed to create connected player link. Check IDs and try again.");
            }

            return RedirectToAction(nameof(Index));
        }, nameof(CreateManualLink)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForceUnlink(Guid connectedPlayerProfileId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!IsSeniorAdminUser())
            {
                TrackUnauthorizedAccessAttempt(nameof(ForceUnlink), nameof(ConnectedPlayersController), "SeniorAdminRequired");
                return Forbid();
            }

            if (connectedPlayerProfileId == Guid.Empty)
            {
                this.AddAlertDanger("ConnectedPlayerProfileId is required.");
                return RedirectToAction(nameof(Index));
            }

            var unlinkedByUserProfileId = TryGetCurrentUserProfileId();
            if (unlinkedByUserProfileId is null)
            {
                this.AddAlertDanger("Your profile context is missing. Please sign out and back in before unlinking.");
                return Forbid();
            }

            var apiResult = await repositoryApiClient.ConnectedPlayers.V1
                .ForceUnlinkConnectedPlayer(connectedPlayerProfileId, new ForceUnlinkConnectedPlayerDto
                {
                    UnlinkedByUserProfileId = unlinkedByUserProfileId
                }, cancellationToken)
                .ConfigureAwait(false);

            if (apiResult.IsSuccess)
            {
                this.AddAlertSuccess("Connected player link was unlinked successfully.");
            }
            else
            {
                this.AddAlertDanger("Failed to unlink connected player.");
            }

            return RedirectToAction(nameof(Index));
        }, nameof(ForceUnlink)).ConfigureAwait(false);
    }

    private bool IsSeniorAdminUser()
    {
        return User.HasClaim(claim => claim.Type == UserProfileClaimType.SeniorAdmin);
    }

    private Guid? TryGetCurrentUserProfileId()
    {
        return Guid.TryParse(User.UserProfileId(), out var profileId) ? profileId : null;
    }
}
