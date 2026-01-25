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

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller for player data operations
/// </summary>
[Authorize(Policy = AuthPolicies.AccessPlayers)]
[Route("Players")]
public class PlayersController(
    IRepositoryApiClient repositoryApiClient,
    IGeoLocationApiClient geoLocationClient,
    IProxyCheckService proxyCheckService,
    TelemetryClient telemetryClient,
    ILogger<PlayersController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    private readonly IRepositoryApiClient repositoryApiClient = repositoryApiClient ?? throw new ArgumentNullException(nameof(repositoryApiClient));
    private readonly IGeoLocationApiClient geoLocationClient = geoLocationClient ?? throw new ArgumentNullException(nameof(geoLocationClient));
    private readonly IProxyCheckService proxyCheckService = proxyCheckService ?? throw new ArgumentNullException(nameof(proxyCheckService));

    /// <summary>
    /// Gets players data for DataTable AJAX requests
    /// </summary>
    /// <param name="id">Optional game type filter</param>
    /// <param name="playersFilter">Filter type (UsernameAndGuid or IpAddress)</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data for DataTable display</returns>
    [HttpPost("GetPlayersAjax/{id?}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetPlayersAjax(GameType? id, [FromQuery] PlayersFilter? playersFilter, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var filter = playersFilter ?? PlayersFilter.UsernameAndGuid;

            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model received from user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            var order = GetPlayersOrderFromDataTable(model);

            var playerCollectionApiResponse = await repositoryApiClient.Players.V1.GetPlayers(
                id, filter, model.Search?.Value, model.Start, model.Length, order, PlayerEntityOptions.None);

            if (!playerCollectionApiResponse.IsSuccess || playerCollectionApiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve players data for user {UserId} with filter {Filter}",
                    User.XtremeIdiotsId(), filter);
                return StatusCode(500, "Failed to retrieve players data");
            }

            var enrichedPlayers = await playerCollectionApiResponse.Result.Data.Items
                .EnrichWithPlayerDataAsync(proxyCheckService, geoLocationClient, Logger, cancellationToken);

            var playerData = enrichedPlayers.Select(player => new
            {
                player.PlayerId,
                player.GameType,
                player.Username,
                player.Guid,
                player.IpAddress,
                player.FirstSeen,
                player.LastSeen,
                ProxyCheckRiskScore = player.ProxyCheckRiskScore(),
                IsProxy = player.IsProxy(),
                IsVpn = player.IsVpn(),
                ProxyType = player.ProxyType(),
                CountryCode = player.CountryCode()
            }).ToList();

            TrackSuccessTelemetry("PlayersDataLoaded", nameof(GetPlayersAjax), new Dictionary<string, string>
            {
                { "GameType", id?.ToString() ?? "All" },
                { "Filter", filter.ToString() },
                { "ResultCount", playerData.Count.ToString() }
            });

            Logger.LogInformation("Successfully retrieved {Count} players for user {UserId} with filter {Filter}",
                playerData.Count, User.XtremeIdiotsId(), filter);

            return Ok(new
            {
                model.Draw,
                recordsTotal = playerCollectionApiResponse.Result?.Pagination?.TotalCount,
                recordsFiltered = playerCollectionApiResponse.Result?.Pagination?.FilteredCount,
                data = playerData
            });
        }, nameof(GetPlayersAjax));
    }

    private static PlayersOrder GetPlayersOrderFromDataTable(DataTableAjaxPostModel model)
    {
        var order = PlayersOrder.LastSeenDesc;

        if (model.Order is not null && model.Order.Count != 0)
        {
            var orderColumn = model.Columns[model.Order.First().Column].Name;
            var searchOrder = model.Order.First().Dir;

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
