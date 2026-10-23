using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.GeoLocation.Api.Client.V1;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Tags;
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
[Authorize(Policy = AuthPolicies.Players_Read)]
public class PlayersController(
    IAuthorizationService authorizationService,
    IIpIntelligenceService ipIntelligenceService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<PlayersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseController(telemetryClient, logger, configuration, auditLogger)
{

    /// <summary>
    /// Displays the main players index page
    /// </summary>
    /// <returns>The players index view</returns>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var allowedGameTypes = await authorizationService
                .GetAuthorizedGameTypesAsync(User, AuthPolicies.Players_Read)
                .ConfigureAwait(false);

            if (allowedGameTypes.Count == 0)
                return Forbid();

            if (allowedGameTypes.Count != GameTypeAuthorizationExtensions.DefinedGameTypes.Count)
                return RedirectToAction(nameof(GameIndex), new { id = allowedGameTypes[0] });

            var model = await BuildPlayersIndexViewModelAsync(null, allowedGameTypes, cancellationToken).ConfigureAwait(false);
            return View(model);
        }, nameof(Index)).ConfigureAwait(false);
    }

    /// <summary>
    /// Displays the players index page filtered by game type
    /// </summary>
    /// <param name="id">The game type to filter by</param>
    /// <returns>The players index view with game type filter applied</returns>
    [HttpGet]
    public async Task<IActionResult> GameIndex(GameType? id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (!id.HasValue)
                return RedirectToAction(nameof(Index));

            var authResult = await CheckAuthorizationAsync(
                authorizationService,
                id.Value,
                AuthPolicies.Players_Read,
                "view",
                "Players Index",
                $"GameType:{id.Value}").ConfigureAwait(false);
            if (authResult is not null)
                return authResult;

            var allowedGameTypes = await authorizationService
                .GetAuthorizedGameTypesAsync(User, AuthPolicies.Players_Read)
                .ConfigureAwait(false);
            var model = await BuildPlayersIndexViewModelAsync(id, allowedGameTypes, cancellationToken).ConfigureAwait(false);
            return View(nameof(Index), model);
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
            var (actionResult, playerData) = await GetAuthorizedPlayerAsync(id, "view", cancellationToken: cancellationToken).ConfigureAwait(false);
            if (actionResult is not null)
                return actionResult;

            var playerDetailsViewModel = new PlayerDetailsViewModel
            {
                Player = playerData!
            };

            if (playerData!.RelatedPlayers is not null && playerData.RelatedPlayers.Count != 0)
            {
                foreach (var rp in playerData.RelatedPlayers)
                {
                    if (rp is null)
                        continue;
                    playerDetailsViewModel.EnrichedRelatedPlayers.Add(RelatedPlayerEnrichedViewModel.FromRelatedPlayerDto(rp));
                }
            }

            return View(playerDetailsViewModel);
        }, nameof(Details)).ConfigureAwait(false);
    }

    private async Task<(IActionResult? ActionResult, PlayerDto? Data)> GetAuthorizedPlayerAsync(
        Guid id,
        string action,
        PlayerEntityOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var playerApiResponse = await repositoryApiClient.Players.V1.GetPlayer(id,
            options ?? (PlayerEntityOptions.Aliases | PlayerEntityOptions.IpAddresses | PlayerEntityOptions.AdminActions |
            PlayerEntityOptions.RelatedPlayers | PlayerEntityOptions.ProtectedNames | PlayerEntityOptions.Tags | PlayerEntityOptions.Counts)).ConfigureAwait(false);

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
            AuthPolicies.Players_Read,
            action,
            "Player",
            $"GameType:{playerData.GameType}",
            playerData).ConfigureAwait(false);

        return authResult is not null ? ((IActionResult? ActionResult, PlayerDto? Data))(authResult, null) : ((IActionResult? ActionResult, PlayerDto? Data))(null, playerData);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetIntelligence(Guid id, string? ipAddress, CancellationToken cancellationToken = default)
    {
        var (actionResult, player) = await GetAuthorizedPlayerAsync(id, nameof(GetIntelligence), PlayerEntityOptions.None, cancellationToken).ConfigureAwait(false);
        if (actionResult is not null)
            return actionResult;
        if (!string.Equals(player!.IpAddress, ipAddress, StringComparison.Ordinal))
            return NotFound();
        var intelligence = await ipIntelligenceService.GetAsync(player.IpAddress, cancellationToken).ConfigureAwait(false);
        ViewData["IpAddress"] = player.IpAddress;
        return PartialView("_Intelligence", intelligence);
    }

    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetIpHistory(Guid id, CancellationToken cancellationToken = default)
    {
        var (actionResult, playerData) = await GetAuthorizedPlayerAsync(id, nameof(GetIpHistory), PlayerEntityOptions.IpAddresses, cancellationToken).ConfigureAwait(false);
        if (actionResult is not null)
            return actionResult;
        var ipAddresses = (playerData!.PlayerIpAddresses ?? []).OrderByDescending(x => x.LastUsed).Take(10).ToList();
        var tasks = ipAddresses.Select(async ipAddress =>
        {
            var intelligence = await ipIntelligenceService.GetAsync(ipAddress.Address, cancellationToken).ConfigureAwait(false);
            return new
            {
                ipAddress.Address,
                countryCode = intelligence.Intelligence?.CountryCode,
                proxyCheckRiskScore = intelligence.RiskScore,
                intelligence.IsProxy,
                intelligence.IsVpn,
                intelligence.ProxyType,
                intelligence.Status,
                intelligence.ExpiresAtUtc
            };
        }).ToList();
        return Json(new { data = await Task.WhenAll(tasks).ConfigureAwait(false) });
    }

    private async Task<PlayersIndexViewModel> BuildPlayersIndexViewModelAsync(
        GameType? selectedGameType,
        IReadOnlyList<GameType> allowedGameTypes,
        CancellationToken cancellationToken)
    {
        var tags = await GetAllTagsAsync(cancellationToken).ConfigureAwait(false);

        return new PlayersIndexViewModel
        {
            AllowedGameTypes = allowedGameTypes,
            CanViewAllGames = allowedGameTypes.Count == GameTypeAuthorizationExtensions.DefinedGameTypes.Count,
            SelectedGameType = selectedGameType,
            Tags = tags
        };
    }

    private async Task<List<TagDto>> GetAllTagsAsync(CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        var tags = new List<TagDto>();
        var skip = 0;

        while (true)
        {
            var tagsResponse = await repositoryApiClient.Tags.V1.GetTags(skip, pageSize, cancellationToken).ConfigureAwait(false);

            if (!tagsResponse.IsSuccess || tagsResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve tags for players index for user {UserId}", User.XtremeIdiotsId());
                return [];
            }

            var page = tagsResponse.Result.Data.Items.Where(t => t != null).ToList();
            if (page.Count == 0)
            {
                break;
            }

            tags.AddRange(page);

            var pagination = tagsResponse.Result.Pagination;
            if (pagination is not null)
            {
                var totalAvailable = Math.Max(pagination.TotalCount, pagination.FilteredCount);
                var nextSkip = pagination.Skip + pagination.Top;
                if (nextSkip <= skip)
                {
                    break;
                }

                if (nextSkip >= totalAvailable)
                {
                    break;
                }

                skip = nextSkip;
                continue;
            }

            if (page.Count < pageSize)
            {
                break;
            }

            skip += page.Count;
        }

        return [.. tags
            .Where(t => t.TagId != Guid.Empty)
            .GroupBy(t => t.TagId)
            .Select(g => g.First())
            .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
