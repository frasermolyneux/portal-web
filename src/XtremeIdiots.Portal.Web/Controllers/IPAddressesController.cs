using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.GeoLocation.Api.Client.V1;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Handles IP address details and analytics for the XtremeIdiots Portal
/// </summary>
[Authorize(Policy = AuthPolicies.Players_Read)]
public class IPAddressesController(
    IAuthorizationService authorizationService,
    IIpIntelligenceService ipIntelligenceService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<IPAddressesController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{

    /// <summary>
    /// Displays detailed information about an IP address including geolocation, proxy status, and associated players
    /// </summary>
    /// <param name="ipAddress">The IP address to analyze</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>View with IP address details or NotFound if IP address is invalid</returns>
    [HttpGet]
    public async Task<IActionResult> Details(string ipAddress, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!System.Net.IPAddress.TryParse(ipAddress, out _))
            {
                Logger.LogWarning("User {UserId} attempted to view IP address details with null or empty IP address",
                    User.XtremeIdiotsId());
                return NotFound();
            }

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                ipAddress,
                AuthPolicies.Players_Read,
                nameof(Details),
                nameof(IPAddressesController),
                $"IpAddress:{ipAddress}",
                ipAddress).ConfigureAwait(false);

            if (authResult != null)
                return authResult;

            var viewModel = await BuildIPAddressDetailsViewModelAsync(ipAddress, cancellationToken).ConfigureAwait(false);

            return View(viewModel);
        }, "ViewIPAddressDetails").ConfigureAwait(false);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetIntelligence(string ipAddress, CancellationToken cancellationToken = default)
    {
        if (!System.Net.IPAddress.TryParse(ipAddress, out _))
            return NotFound();
        var authResult = await CheckAuthorizationAsync(
            authorizationService, ipAddress, AuthPolicies.Players_Read,
            nameof(GetIntelligence), nameof(IPAddressesController), $"IpAddress:{ipAddress}", ipAddress).ConfigureAwait(false);
        if (authResult is not null)
            return authResult;

        var associated = await repositoryApiClient.Players.V1.GetPlayersWithIpAddress(
            ipAddress, 0, 100, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None).ConfigureAwait(false);
        if (!associated.IsSuccess || associated.Result?.Data?.Items is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        var players = associated.Result.Data.Items;
        if (!players.Any())
            return NotFound();

        var canReadAssociation = false;
        foreach (var gameType in players.Select(p => p.GameType).Distinct())
        {
            if ((await authorizationService.AuthorizeAsync(User, gameType, AuthPolicies.Players_Read).ConfigureAwait(false)).Succeeded)
            {
                canReadAssociation = true;
                break;
            }
        }

        if (!canReadAssociation)
            return Forbid();

        var result = await ipIntelligenceService.GetAsync(ipAddress, cancellationToken).ConfigureAwait(false);
        ViewData["IntelligenceStatus"] = result.Status;
        return PartialView("Details", new IPAddressDetailsViewModel { IpAddress = ipAddress, Intelligence = result.Intelligence });
    }

    private async Task<IPAddressDetailsViewModel> BuildIPAddressDetailsViewModelAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var viewModel = new IPAddressDetailsViewModel
        {
            IpAddress = ipAddress
        };

        var playersResponse = await repositoryApiClient.Players.V1.GetPlayersWithIpAddress(
            ipAddress, 0, 100, PlayersOrder.LastSeenDesc, PlayerEntityOptions.None).ConfigureAwait(false);

        if (playersResponse.IsSuccess && playersResponse.Result?.Data is not null)
        {
            viewModel.Players = playersResponse.Result.Data.Items ?? [];
            viewModel.TotalPlayersCount = playersResponse.Result?.Pagination?.TotalCount ?? 0;
            Logger.LogDebug("Successfully retrieved {PlayerCount} players associated with IP address {IpAddress}",
                viewModel.TotalPlayersCount, ipAddress);
        }
        else
        {
            Logger.LogWarning("Failed to retrieve players for IP address {IpAddress}", ipAddress);
            viewModel.Players = [];
            viewModel.TotalPlayersCount = 0;
        }

        return viewModel;
    }
}
