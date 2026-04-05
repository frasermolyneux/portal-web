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
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Controllers;

/// <summary>
/// Manages player-related operations including viewing, searching, and analyzing player data
/// </summary>
[Authorize(Policy = AuthPolicies.AccessPlayers)]
public class PlayersController(
    IAuthorizationService authorizationService,
    IGeoLocationApiClient geoLocationClient,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
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
        return await ExecuteWithErrorHandlingAsync(() => Task.FromResult(View() as IActionResult), nameof(Index)).ConfigureAwait(false);
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
        }, nameof(GameIndex)).ConfigureAwait(false);
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
            var (actionResult, playerData) = await GetAuthorizedPlayerAsync(id, "view", cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var playerDetailsViewModel = new PlayerDetailsViewModel
            {
                Player = playerData!
            };

            await EnrichCurrentPlayerIntelligenceAsync(playerDetailsViewModel, playerData!, id, cancellationToken).ConfigureAwait(false);

            if (playerData!.PlayerIpAddresses is not null && playerData.PlayerIpAddresses.Count != 0)
            {
                await EnrichPlayerIpAddressesAsync(playerDetailsViewModel, playerData, id, cancellationToken).ConfigureAwait(false);
            }

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
                        if (!string.IsNullOrWhiteSpace(vm.IpAddress))
                        {
                            var intelligenceResult = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(vm.IpAddress, cancellationToken).ConfigureAwait(false);
                            if (intelligenceResult.IsSuccess && intelligenceResult.Result?.Data is not null)
                            {
                                var intelligence = intelligenceResult.Result.Data;

                                if (!string.IsNullOrWhiteSpace(intelligence.CountryCode))
                                {
                                    vm.CountryCode = intelligence.CountryCode;
                                }

                                if (intelligence.ProxyCheck is not null)
                                {
                                    vm.RiskScore = intelligence.ProxyCheck.RiskScore;
                                    vm.IsProxy = intelligence.ProxyCheck.IsProxy;
                                    vm.IsVpn = intelligence.ProxyCheck.IsVpn;
                                    vm.ProxyType = intelligence.ProxyCheck.ProxyType;
                                }
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
        }, nameof(Details)).ConfigureAwait(false);
    }

    private async Task<(IActionResult? ActionResult, PlayerDto? Data)> GetAuthorizedPlayerAsync(
        Guid id,
        string action,
        CancellationToken cancellationToken = default)
    {
        var playerApiResponse = await repositoryApiClient.Players.V1.GetPlayer(id,
            PlayerEntityOptions.Aliases | PlayerEntityOptions.IpAddresses | PlayerEntityOptions.AdminActions |
            PlayerEntityOptions.RelatedPlayers | PlayerEntityOptions.ProtectedNames | PlayerEntityOptions.Tags).ConfigureAwait(false);

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
            playerData).ConfigureAwait(false);

        return authResult is not null ? ((IActionResult? ActionResult, PlayerDto? Data))(authResult, null) : ((IActionResult? ActionResult, PlayerDto? Data))(null, playerData);
    }

    private async Task EnrichCurrentPlayerIntelligenceAsync(PlayerDetailsViewModel viewModel, PlayerDto playerData, Guid playerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playerData.IpAddress))
            return;

        try
        {
            var intelligenceResult = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(playerData.IpAddress, cancellationToken).ConfigureAwait(false);

            if (intelligenceResult.IsSuccess && intelligenceResult.Result?.Data is not null)
                viewModel.Intelligence = intelligenceResult.Result.Data;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to retrieve intelligence for IP {IpAddress} for player {PlayerId}",
                playerData.IpAddress, playerId);
        }
    }

    private async Task EnrichPlayerIpAddressesAsync(PlayerDetailsViewModel viewModel, PlayerDto playerData, Guid playerId, CancellationToken cancellationToken = default)
    {
        foreach (var ipAddress in playerData.PlayerIpAddresses.OrderByDescending(x => x.LastUsed).Take(10))
        {
            var enrichedIp = new PlayerIpAddressViewModel
            {
                IpAddressDto = ipAddress,
                IsCurrentIp = ipAddress.Address == playerData.IpAddress
            };

            try
            {
                var intelligenceResult = await geoLocationClient.GeoLookup.V1_1.GetIpIntelligence(ipAddress.Address, cancellationToken).ConfigureAwait(false);
                if (intelligenceResult.IsSuccess && intelligenceResult.Result?.Data is not null)
                {
                    enrichedIp.Intelligence = intelligenceResult.Result.Data;
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