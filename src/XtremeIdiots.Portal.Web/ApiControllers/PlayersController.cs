using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using System.Security.Cryptography;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.Players;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller for player data operations
/// </summary>
[Authorize(Policy = AuthPolicies.Players_Read)]
[Route("Players")]
public class PlayersController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    IIpIntelligenceService ipIntelligenceService,
    IDataProtectionProvider dataProtectionProvider,
    TelemetryClient telemetryClient,
    ILogger<PlayersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{

    /// <summary>
    /// Gets players data for DataTable AJAX requests
    /// </summary>
    /// <param name="id">Optional game type filter</param>
    /// <param name="playersFilter">Filter type (UsernameAndGuid or IpAddress)</param>
    /// <param name="selectedTagId">Optional tag filter by tag ID</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data for DataTable display</returns>
    [HttpPost("GetPlayersAjax/{id?}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetPlayersAjax(GameType? id, [FromQuery] PlayersFilter? playersFilter, [FromQuery] Guid? selectedTagId, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            if (id.HasValue)
            {
                var authResult = await CheckAuthorizationAsync(
                    authorizationService,
                    id.Value,
                    AuthPolicies.Players_Read,
                    "search",
                    "Players",
                    $"GameType:{id.Value}").ConfigureAwait(false);
                if (authResult is not null)
                    return authResult;
            }
            else
            {
                var allowedGameTypes = await authorizationService
                    .GetAuthorizedGameTypesAsync(User, AuthPolicies.Players_Read)
                    .ConfigureAwait(false);
                if (allowedGameTypes.Count != GameTypeAuthorizationExtensions.DefinedGameTypes.Count)
                {
                    Logger.LogWarning("User {UserId} attempted an all-games player search without access to every game type",
                        User.XtremeIdiotsId());
                    return Forbid();
                }
            }

            var filter = playersFilter ?? PlayersFilter.UsernameAndGuid;

            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model received from user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            var order = GetPlayersOrderFromDataTable(model);
            var effectiveFilter = selectedTagId.HasValue ? PlayersFilter.Tag : filter;
            var filterString = selectedTagId.HasValue ? selectedTagId.Value.ToString() : model.Search?.Value;

            var playerCollectionApiResponse = await repositoryApiClient.Players.V1.GetPlayers(
                id,
                effectiveFilter,
                filterString,
                model.Start,
                Math.Clamp(model.Length, 1, 100),
                order,
                PlayerEntityOptions.Tags).ConfigureAwait(false);

            if (!playerCollectionApiResponse.IsSuccess || playerCollectionApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve players data for user {UserId} with filter {Filter}",
                    User.XtremeIdiotsId(), filter);
                return StatusCode(500, "Failed to retrieve players data");
            }

            var visiblePlayers = playerCollectionApiResponse.Result.Data.Items.Take(100).ToArray();
            var playerData = visiblePlayers.Select(player =>
                new
                {
                    player.PlayerId,
                    gameType = player.GameType.ToString(),
                    player.Username,
                    player.Guid,
                    player.SteamId,
                    player.IpAddress,
                    tags = (player.Tags ?? []).Select(t => new
                    {
                        t.TagId,
                        name = t.Tag?.Name,
                        tagHtml = t.Tag?.TagHtml
                    }),
                    player.FirstSeen,
                    player.LastSeen,
                    proxyCheckRiskScore = (int?)null,
                    isProxy = (bool?)null,
                    isVpn = (bool?)null,
                    proxyType = (string?)null,
                    countryCode = (string?)null
                }).ToList();

            Logger.LogInformation("Successfully retrieved {Count} players for user {UserId} with filter {Filter}",
                playerData.Count, User.XtremeIdiotsId(), filter);

            var enrichmentExpiresAtUtc = DateTime.UtcNow.AddMinutes(2);
            return Ok(new
            {
                model.Draw,
                enrichmentExpiresAtUtc,
                enrichmentToken = dataProtectionProvider.CreateProtector("Players.VisibleIntelligence.v1").Protect(
                    JsonConvert.SerializeObject(new VisiblePlayersSnapshot(
                        User.XtremeIdiotsId(), enrichmentExpiresAtUtc,
                        [.. visiblePlayers
                            .Select(p => new VisiblePlayer(p.PlayerId, p.GameType, p.IpAddress))]))),
                recordsTotal = playerCollectionApiResponse.Result?.Pagination?.TotalCount,
                recordsFiltered = playerCollectionApiResponse.Result?.Pagination?.FilteredCount,
                data = playerData
            });
        }, nameof(GetPlayersAjax)).ConfigureAwait(false);
    }

    public sealed record VisiblePlayer(Guid PlayerId, GameType GameType, string? IpAddress);
    public sealed record VisiblePlayersSnapshot(string? UserId, DateTime ExpiresAtUtc, VisiblePlayer[] Players);
    public sealed record IntelligenceBatchRequest(string Token, Guid[] PlayerIds);

    [HttpPost("GetVisibleIntelligence")]
    [ValidateAntiForgeryToken]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetVisibleIntelligence([FromBody] IntelligenceBatchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrEmpty(request.Token) || request.Token.Length > 100_000 ||
            request.PlayerIds is null || request.PlayerIds.Length is < 1 or > 100)
            return BadRequest();

        VisiblePlayersSnapshot? snapshot;
        try
        {
            snapshot = JsonConvert.DeserializeObject<VisiblePlayersSnapshot>(
                dataProtectionProvider.CreateProtector("Players.VisibleIntelligence.v1").Unprotect(request.Token));
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException)
        {
            return BadRequest();
        }

        if (snapshot is null || snapshot.Players is null || snapshot.Players.Length > 100 ||
            snapshot.ExpiresAtUtc <= DateTime.UtcNow || snapshot.UserId != User.XtremeIdiotsId())
            return BadRequest();

        var ids = request.PlayerIds.ToHashSet();
        var players = snapshot.Players.Where(p => ids.Contains(p.PlayerId)).ToArray();
        if (players.Select(p => p.PlayerId).Distinct().Count() != ids.Count)
            return BadRequest();

        foreach (var game in players.Select(p => p.GameType).Distinct())
        {
            if (!(await authorizationService.AuthorizeAsync(User, game, AuthPolicies.Players_Read).ConfigureAwait(false)).Succeeded)
                return Forbid();
        }

        var data = await Task.WhenAll(players.Select(async p =>
        {
            var intelligence = await ipIntelligenceService.GetAsync(p.IpAddress, cancellationToken).ConfigureAwait(false);
            return new
            {
                p.PlayerId,
                p.IpAddress,
                countryCode = intelligence.Intelligence?.CountryCode,
                proxyCheckRiskScore = intelligence.RiskScore,
                intelligence.IsProxy,
                intelligence.IsVpn,
                intelligence.ProxyType,
                intelligence.Status,
                intelligence.ExpiresAtUtc
            };
        })).ConfigureAwait(false);
        return Ok(new { data });
    }

    private static PlayersOrder GetPlayersOrderFromDataTable(DataTableAjaxPostModel model)
    {
        var order = PlayersOrder.LastSeenDesc;

        if (model.Order is not null
            && model.Order.Count != 0
            && model.Columns is not null
            && model.Columns.Count != 0)
        {
            var primaryOrder = model.Order.First();
            if (primaryOrder.Column < 0 || primaryOrder.Column >= model.Columns.Count)
            {
                return order;
            }

            var orderColumn = model.Columns[primaryOrder.Column].Name;
            var searchOrder = primaryOrder.Dir;

            order = orderColumn switch
            {
                "gameType" => searchOrder == "asc" ? PlayersOrder.GameTypeAsc : PlayersOrder.GameTypeDesc,
                "username" => searchOrder == "asc" ? PlayersOrder.UsernameAsc : PlayersOrder.UsernameDesc,
                "firstSeen" => searchOrder == "asc" ? PlayersOrder.FirstSeenAsc : PlayersOrder.FirstSeenDesc,
                "lastSeen" => searchOrder == "asc" ? PlayersOrder.LastSeenAsc : PlayersOrder.LastSeenDesc,
                _ => PlayersOrder.LastSeenDesc
            };
        }

        return order;
    }
}
