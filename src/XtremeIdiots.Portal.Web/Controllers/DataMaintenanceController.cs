using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

[Authorize(Policy = AuthPolicies.GlobalSettings_Admin)]
public class DataMaintenanceController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<DataMaintenanceController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(() =>
            Task.FromResult<IActionResult>(View(new DataMaintenanceViewModel())), nameof(Index)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LookupPlayer(string lookupPlayerId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var model = new DataMaintenanceViewModel
            {
                LookupPlayerId = lookupPlayerId?.Trim() ?? string.Empty
            };

            if (!Guid.TryParse(model.LookupPlayerId, out var playerId))
            {
                ModelState.AddModelError(nameof(DataMaintenanceViewModel.LookupPlayerId), "Player Id must be a valid GUID.");
                return View(nameof(Index), model);
            }

            var lookupResult = await TryGetPlayerPreviewAsync(playerId).ConfigureAwait(false);
            if (lookupResult.Outcome == PlayerPreviewLookupOutcome.NotFound)
            {
                this.AddAlertDanger("Player not found for the supplied Player Id.");
                return View(nameof(Index), model);
            }

            if (lookupResult.Outcome == PlayerPreviewLookupOutcome.Failed)
            {
                this.AddAlertDanger("Player lookup failed due to an upstream error. Please try again.");
                return View(nameof(Index), model);
            }

            model.Player = lookupResult.Player;
            return View(nameof(Index), model);
        }, nameof(LookupPlayer)).ConfigureAwait(false);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlayer(Guid playerId, string confirmationText, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (playerId == Guid.Empty)
            {
                this.AddAlertDanger("Player Id is required.");
                return RedirectToAction(nameof(Index));
            }

            var expectedConfirmation = playerId.ToString();
            if (!string.Equals(confirmationText?.Trim(), expectedConfirmation, StringComparison.OrdinalIgnoreCase))
            {
                var lookupResult = await TryGetPlayerPreviewAsync(playerId).ConfigureAwait(false);
                var model = new DataMaintenanceViewModel
                {
                    LookupPlayerId = expectedConfirmation,
                    Player = lookupResult.Player,
                    ConfirmationText = confirmationText?.Trim() ?? string.Empty
                };

                if (lookupResult.Outcome != PlayerPreviewLookupOutcome.Success)
                {
                    ModelState.AddModelError(string.Empty, "Player preview is unavailable. Please run lookup again before retrying delete.");

                    if (lookupResult.Outcome == PlayerPreviewLookupOutcome.Failed)
                    {
                        this.AddAlertDanger("Failed to refresh player preview. Please run lookup again.");
                    }
                    else
                    {
                        this.AddAlertDanger("Player was not found during preview refresh. Please run lookup again.");
                    }
                }

                ModelState.AddModelError(nameof(DataMaintenanceViewModel.ConfirmationText), "Confirmation text must exactly match the Player Id.");
                return View(nameof(Index), model);
            }

            var deleteResponse = await repositoryApiClient.DataMaintenance.V1.DeletePlayer(playerId, cancellationToken).ConfigureAwait(false);
            if (deleteResponse.IsSuccess)
            {
                TrackSuccessTelemetry("DataMaintenanceDeletePlayer", nameof(DeletePlayer), new Dictionary<string, string>
                {
                    ["PlayerId"] = playerId.ToString()
                });

                this.AddAlertSuccess($"Player {playerId} and associated data were deleted successfully.");
                return RedirectToAction(nameof(Index));
            }

            if (deleteResponse.IsNotFound)
            {
                this.AddAlertDanger("Player not found. It may have already been deleted.");
                return RedirectToAction(nameof(Index));
            }

            this.AddAlertDanger("Failed to delete player. Please try again.");

            var refreshedLookup = await TryGetPlayerPreviewAsync(playerId).ConfigureAwait(false);
            var failedModel = new DataMaintenanceViewModel
            {
                LookupPlayerId = playerId.ToString(),
                Player = refreshedLookup.Player
            };

            return View(nameof(Index), failedModel);
        }, nameof(DeletePlayer)).ConfigureAwait(false);
    }

    private async Task<PlayerPreviewLookupResult> TryGetPlayerPreviewAsync(Guid playerId)
    {
        var playerResponse = await repositoryApiClient.Players.V1
            .GetPlayer(playerId, PlayerEntityOptions.Counts)
            .ConfigureAwait(false);

        return playerResponse.IsSuccess && playerResponse.Result?.Data is { } player
            ? new PlayerPreviewLookupResult(PlayerPreviewLookupOutcome.Success, MapPlayerPreview(player))
            : playerResponse.IsNotFound
            ? new PlayerPreviewLookupResult(PlayerPreviewLookupOutcome.NotFound, null)
            : new PlayerPreviewLookupResult(PlayerPreviewLookupOutcome.Failed, null);
    }

    private static DataMaintenancePlayerPreviewViewModel MapPlayerPreview(PlayerDto player)
    {
        return new DataMaintenancePlayerPreviewViewModel
        {
            PlayerId = player.PlayerId,
            GameType = player.GameType,
            Username = player.Username,
            PlayerGuid = player.Guid,
            IpAddress = player.IpAddress,
            LastSeen = player.LastSeen,
            AliasCount = player.AliasCount,
            IpAddressCount = player.IpAddressCount,
            AdminActionCount = player.AdminActionCount,
            ProtectedNameCount = player.ProtectedNameCount,
            RelatedPlayerCount = player.RelatedPlayerCount,
            TagCount = player.TagCount
        };
    }

    private enum PlayerPreviewLookupOutcome
    {
        Success,
        NotFound,
        Failed
    }

    private sealed record PlayerPreviewLookupResult(PlayerPreviewLookupOutcome Outcome, DataMaintenancePlayerPreviewViewModel? Player);
}
