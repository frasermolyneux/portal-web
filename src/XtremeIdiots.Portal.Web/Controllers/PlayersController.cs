using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using MX.GeoLocation.Api.Client.V1;
using Newtonsoft.Json;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages player-related operations including viewing, searching, and analyzing player data
/// </summary>
/// <remarks>
/// Initializes a new instance of the PlayersController
/// </remarks>
/// <param name="authorizationService">Service for handling authorization checks</param>
/// <param name="geoLocationClient">Client for geolocation lookups</param>
/// <param name="repositoryApiClient">Client for accessing repository data</param>
/// <param name="telemetryClient">Client for tracking telemetry events</param>
/// <param name="proxyCheckService">Service for checking proxy/VPN status of IP addresses</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
[Authorize(Policy = AuthPolicies.AccessPlayers)]
public class PlayersController(
    IAuthorizationService authorizationService,
    IGeoLocationApiClient geoLocationClient,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    IProxyCheckService proxyCheckService,
    ILogger<PlayersController> logger,
    IConfiguration configuration) : BaseController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Displays the main players index page
    /// </summary>
    /// <returns>The players index view</returns>
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult(View() as IActionResult), nameof(Index));
    }

    /// <summary>
    /// Displays the players index page filtered by game type
    /// </summary>
    /// <param name="id">The game type to filter by</param>
    /// <returns>The players index view with game type filter applied</returns>
    [HttpGet]
    public async Task<IActionResult> GameIndex(GameType? id)
    {
        return await ExecuteWithErrorHandlingAsync(() =>
        {
            ViewData["GameType"] = id;
            return Task.FromResult(View(nameof(Index)) as IActionResult);
        }, nameof(GameIndex));
    }

    /// <summary>
    /// Displays detailed information about a specific player
    /// </summary>
    /// <param name="id">The player ID</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>The player details view with enriched data</returns>
    [HttpGet]
    public async Task<IActionResult> Details(Guid id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var (actionResult, playerData) = await GetAuthorizedPlayerAsync(id, "view", cancellationToken);
            if (actionResult is not null)
                return actionResult;

            var playerDetailsViewModel = new PlayerDetailsViewModel
            {
                Player = playerData!
            };

            await EnrichCurrentPlayerGeoLocationAsync(playerDetailsViewModel, playerData!, id);

            if (playerData!.PlayerIpAddresses is not null && playerData.PlayerIpAddresses.Count != 0)
            {
                await EnrichPlayerIpAddressesAsync(playerDetailsViewModel, playerData, id);
            }

            // Enrich related players (proxy + geo) so UI can use data attributes / helper formatting
            if (playerData.RelatedPlayers is not null && playerData.RelatedPlayers.Count != 0)
            {
                foreach (var rp in playerData.RelatedPlayers)
                {
                    if (rp is null)
                    {
                        continue;
                    }

                    var vm = RelatedPlayerEnrichedViewModel.FromRelatedPlayerDto(rp);
                    try
                    {
                        // Geo
                        if (!string.IsNullOrWhiteSpace(vm.IpAddress))
                        {
                            var geo = await geoLocationClient.GeoLookup.V1.GetGeoLocation(vm.IpAddress, cancellationToken);
                            if (geo.IsSuccess && geo.Result?.Data is not null && !string.IsNullOrWhiteSpace(geo.Result.Data.CountryCode))
                            {
                                vm.CountryCode = geo.Result.Data.CountryCode;
                            }
                            var proxy = await proxyCheckService.GetIpRiskDataAsync(vm.IpAddress, cancellationToken);
                            if (!proxy.IsError)
                            {
                                vm.RiskScore = proxy.RiskScore;
                                vm.IsProxy = proxy.IsProxy;
                                vm.IsVpn = proxy.IsVpn;
                                vm.ProxyType = proxy.Type;
                            }
                        }

                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning(ex, "Failed to enrich related player {RelatedPlayerId} for {PlayerId}", vm.PlayerId, id);
                    }
                    playerDetailsViewModel.EnrichedRelatedPlayers.Add(vm);
                }
            }

            TrackSuccessTelemetry("PlayerDetailsViewed", "ViewPlayerDetails", new Dictionary<string, string>
            {
                { "PlayerId", id.ToString() },
                { "GameType", playerData.GameType.ToString() },
                { "IpAddressCount", playerData.PlayerIpAddresses?.Count.ToString() ?? "0" }
            });

            return View(playerDetailsViewModel);
        }, nameof(Details));
    }

    private async Task<(IActionResult? ActionResult, PlayerDto? Data)> GetAuthorizedPlayerAsync(
        Guid id,
        string action,
        CancellationToken cancellationToken = default)
    {
        var playerApiResponse = await repositoryApiClient.Players.V1.GetPlayer(id,
            PlayerEntityOptions.Aliases | PlayerEntityOptions.IpAddresses | PlayerEntityOptions.AdminActions |
            PlayerEntityOptions.RelatedPlayers | PlayerEntityOptions.ProtectedNames | PlayerEntityOptions.Tags);

        if (playerApiResponse.IsNotFound)
        {
            Logger.LogWarning("Player {PlayerId} not found when attempting to {Action}", id, action);
            return (NotFound(), null);
        }

        if (playerApiResponse.Result?.Data is null)
        {
            Logger.LogWarning("Player data is null for {PlayerId} when attempting to {Action}", id, action);
            return (RedirectToAction(nameof(ErrorsController.Display), nameof(ErrorsController)[..^10], new { id = 500 }), null);
        }

        var playerData = playerApiResponse.Result.Data;

        var authResult = await CheckAuthorizationAsync(
            authorizationService,
            playerData.GameType,
            AuthPolicies.ViewPlayers,
            action,
            "Player",
            $"GameType:{playerData.GameType}",
            playerData);

        return authResult is not null ? ((IActionResult? ActionResult, PlayerDto? Data))(authResult, null) : ((IActionResult? ActionResult, PlayerDto? Data))(null, playerData);
    }

    private async Task EnrichCurrentPlayerGeoLocationAsync(PlayerDetailsViewModel viewModel, PlayerDto playerData, Guid playerId)
    {
        if (string.IsNullOrWhiteSpace(playerData.IpAddress))
            return;

        try
        {
            var getGeoLocationResult = await geoLocationClient.GeoLookup.V1.GetGeoLocation(playerData.IpAddress);

            if (getGeoLocationResult.IsSuccess && getGeoLocationResult.Result?.Data is not null)
                viewModel.GeoLocation = getGeoLocationResult.Result.Data;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve geolocation for IP {IpAddress} for player {PlayerId}",
                playerData.IpAddress, playerId);
        }
    }

    private async Task EnrichPlayerIpAddressesAsync(PlayerDetailsViewModel viewModel, PlayerDto playerData, Guid playerId)
    {
        foreach (var ipAddress in playerData.PlayerIpAddresses!.OrderByDescending(x => x.LastUsed).Take(10))
        {
            var enrichedIp = new PlayerIpAddressViewModel
            {
                IpAddressDto = ipAddress,
                IsCurrentIp = ipAddress.Address == playerData.IpAddress
            };

            try
            {

                var getGeoLocationResult = await geoLocationClient.GeoLookup.V1.GetGeoLocation(ipAddress.Address);
                if (getGeoLocationResult.IsSuccess && getGeoLocationResult.Result is not null)
                {
                    enrichedIp.GeoLocation = getGeoLocationResult.Result.Data;
                }

                var proxyCheck = await proxyCheckService.GetIpRiskDataAsync(ipAddress.Address);
                if (!proxyCheck.IsError)
                {
                    enrichedIp.ProxyCheck = proxyCheck;
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to enrich IP address {IpAddress} for player {PlayerId}",
                    ipAddress.Address, playerId);
            }

            viewModel.EnrichedIpAddresses.Add(enrichedIp);
        }
    }
}