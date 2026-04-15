using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.GeoLocation.Api.Client.V1;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using MX.Observability.ApplicationInsights.Auditing;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Handles IP address details and analytics for the XtremeIdiots Portal
/// </summary>
[Authorize(Policy = AuthPolicies.Players_Read)]
public class IPAddressesController(
    IAuthorizationService authorizationService,
    IGeoLocationApiClient geoLocationClient,
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
            if (string.IsNullOrWhiteSpace(ipAddress))
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

    private async Task<IPAddressDetailsViewModel> BuildIPAddressDetailsViewModelAsync(string ipAddress, CancellationToken cancellationToken)
    {
        var viewModel = new IPAddressDetailsViewModel
        {
            IpAddress = ipAddress
        };

        try
        {
            var intelligenceResult = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(ipAddress, cancellationToken).ConfigureAwait(false);
            if (intelligenceResult.IsSuccess && intelligenceResult.Result?.Data is not null)
            {
                viewModel.Intelligence = intelligenceResult.Result.Data;
                Logger.LogDebug("Successfully retrieved intelligence data for IP address {IpAddress}", ipAddress);
            }
            else
            {
                Logger.LogDebug("No intelligence data available for IP address {IpAddress}", ipAddress);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve intelligence data for IP address {IpAddress}", ipAddress);
        }

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