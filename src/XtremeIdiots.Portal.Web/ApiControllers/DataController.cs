using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Newtonsoft.Json;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.UserProfiles;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller providing data endpoints for AJAX requests from the portal interface
/// </summary>
/// <remarks>
/// This controller handles DataTables AJAX requests for various entities including players, maps, and users.
/// All endpoints require appropriate authorization and return data in DataTables-compatible format.
/// </remarks>
/// <remarks>
/// Initializes a new instance of the DataController
/// </remarks>
/// <param name="repositoryApiClient">Client for repository API operations</param>
/// <param name="telemetryClient">Client for application telemetry</param>
/// <param name="logger">Logger instance for this controller</param>
/// <param name="configuration">Application configuration</param>
/// <exception cref="ArgumentNullException">Thrown when required dependencies are null</exception>
[Route("api/[controller]")]
public class DataController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<DataController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{

    /// <summary>
    /// Retrieves players data for DataTables AJAX requests with username and GUID filtering
    /// </summary>
    /// <param name="id">Optional game type to filter players by</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>DataTables-compatible JSON response with player data</returns>
    [HttpPost("Players/GetPlayersAjax")]
    [Authorize(Policy = AuthPolicies.AccessPlayers)]
    public async Task<IActionResult> GetPlayersAjax(GameType? id, [FromQuery] PlayersFilter? playersFilter, CancellationToken cancellationToken = default)
    {
        var filter = playersFilter ?? PlayersFilter.UsernameAndGuid;
        return await GetPlayersAjaxPrivate(filter, id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves maps data for DataTables AJAX requests with sorting and filtering support
    /// </summary>
    /// <param name="id">Optional game type to filter maps by</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>DataTables-compatible JSON response with map data</returns>
    [HttpPost("Maps/GetMapListAjax")]
    [Authorize(Policy = AuthPolicies.AccessMaps)]
    public async Task<IActionResult> GetMapListAjax(GameType? id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync().ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable request body for user {UserId}", User.XtremeIdiotsId());
                return BadRequest();
            }

            var order = MapsOrder.MapNameAsc;

            var orderColumn = model.Columns[model.Order.First().Column].Name;
            var searchOrder = model.Order.First().Dir;

            order = orderColumn switch
            {
                "mapName" => searchOrder == "asc" ? MapsOrder.MapNameAsc : MapsOrder.MapNameDesc,
                "popularity" => searchOrder == "asc" ? MapsOrder.PopularityAsc : MapsOrder.PopularityDesc,
                "gameType" => searchOrder == "asc" ? MapsOrder.GameTypeAsc : MapsOrder.GameTypeDesc,
                _ => order
            };

            var mapsApiResponse = await repositoryApiClient.Maps.V1.GetMaps(id, null, null, model.Search?.Value, model.Start, model.Length, order).ConfigureAwait(false);

            if (!mapsApiResponse.IsSuccess || mapsApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Failed to retrieve maps data for user {UserId} and game type {GameType}",
                    User.XtremeIdiotsId(), id);
                return StatusCode(500, "Failed to retrieve maps data");
            }

            TrackSuccessTelemetry("MapsListRetrieved", nameof(GetMapListAjax), new Dictionary<string, string>
            {
                { "GameType", id?.ToString() ?? "All" },
                { "ResultCount", mapsApiResponse.Result.Data.Items?.Count().ToString() ?? "0" }
            });

            return Ok(new
            {
                model.Draw,
                recordsTotal = mapsApiResponse.Result?.Pagination?.TotalCount,
                recordsFiltered = mapsApiResponse.Result?.Pagination?.FilteredCount,
                data = mapsApiResponse?.Result?.Data?.Items
            });
        }, nameof(GetMapListAjax)).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves user profiles data for DataTables AJAX requests
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>DataTables-compatible JSON response with user profile data</returns>
    [HttpPost("Users/GetUsersAjax")]
    [Authorize(Policy = AuthPolicies.AccessUsers)]
    public async Task<IActionResult> GetUsersAjax(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid request body for users AJAX endpoint");
                return BadRequest();
            }

            UserProfileFilter? userProfileFilter = null;
            var rawFlag = HttpContext.Request.Query["userFlag"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(rawFlag) && Enum.TryParse<UserProfileFilter>(rawFlag, true, out var parsedFlag))
            {
                userProfileFilter = parsedFlag;
            }

            // Map ordering (API supports DisplayName only at present)
            var order = UserProfilesOrder.DisplayNameAsc;
            if (model.Order?.Count > 0)
            {
                var orderColumn = model.Columns[model.Order.First().Column].Name;
                var dir = model.Order.First().Dir;
                if (orderColumn.Equals("displayName", StringComparison.OrdinalIgnoreCase))
                {
                    order = dir.Equals("asc", StringComparison.OrdinalIgnoreCase)
                        ? UserProfilesOrder.DisplayNameAsc
                        : UserProfilesOrder.DisplayNameDesc;
                }
            }

            var userProfileResponseDto = await repositoryApiClient.UserProfiles.V1.GetUserProfiles(
                model.Search?.Value, userProfileFilter, model.Start, model.Length, order, cancellationToken).ConfigureAwait(false);

            if (userProfileResponseDto.Result?.Data is null)
            {
                Logger.LogWarning("Invalid API response for users AJAX endpoint");
                return BadRequest();
            }

            var profileItems = userProfileResponseDto.Result.Data.Items?.ToList() ?? [];
            var idStrings = profileItems.Select(p => p.UserProfileId.ToString()).ToList();
            // NOTE: API controller does not have UserManager; enrichment limited unless injected. Keeping original response.
            return Ok(new
            {
                model.Draw,
                recordsTotal = userProfileResponseDto.Result?.Pagination?.TotalCount,
                recordsFiltered = userProfileResponseDto.Result?.Pagination?.FilteredCount,
                data = profileItems
            });
        }, nameof(GetUsersAjax)).ConfigureAwait(false);
    }

    /// <summary>
    /// Private helper method to handle different types of player AJAX requests with filtering and sorting
    /// </summary>
    /// <param name="filter">The filter type to apply to player queries</param>
    /// <param name="gameType">Optional game type to filter players by</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>DataTables-compatible JSON response with filtered player data</returns>
    private async Task<IActionResult> GetPlayersAjaxPrivate(PlayersFilter filter, GameType? gameType, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid request model for players AJAX from user {UserId}", User.XtremeIdiotsId());
                return BadRequest();
            }

            var order = PlayersOrder.LastSeenDesc;

            if (model.Order?.Count > 0)
            {
                var orderColumn = model.Columns[model.Order.First().Column].Name;
                var searchOrder = model.Order.First().Dir;

                order = orderColumn switch
                {
                    "username" => searchOrder == "asc" ? PlayersOrder.UsernameAsc : PlayersOrder.UsernameDesc,
                    "gameType" => searchOrder == "asc" ? PlayersOrder.GameTypeAsc : PlayersOrder.GameTypeDesc,
                    "firstSeen" => searchOrder == "asc" ? PlayersOrder.FirstSeenAsc : PlayersOrder.FirstSeenDesc,
                    "lastSeen" => searchOrder == "asc" ? PlayersOrder.LastSeenAsc : PlayersOrder.LastSeenDesc,
                    _ => order
                };
            }

            var playersApiResponse = await repositoryApiClient.Players.V1.GetPlayers(
                gameType, filter, model.Search?.Value, model.Start, model.Length, order, PlayerEntityOptions.None).ConfigureAwait(false);

            if (!playersApiResponse.IsSuccess || playersApiResponse.Result?.Data is null)
            {
                Logger.LogError("Failed to retrieve players for user {UserId} with filter {Filter}",
                    User.XtremeIdiotsId(), filter);
                return StatusCode(500, "Failed to retrieve players data");
            }

            TrackSuccessTelemetry("PlayersListLoaded", nameof(GetPlayersAjax), new Dictionary<string, string>
            {
                { "Filter", filter.ToString() },
                { "GameType", gameType?.ToString() ?? "All" },
                { "ResultCount", playersApiResponse.Result.Data.Items?.Count().ToString() ?? "0" }
            });

            return Ok(new
            {
                model.Draw,
                recordsTotal = playersApiResponse.Result?.Pagination?.TotalCount,
                recordsFiltered = playersApiResponse.Result?.Pagination?.FilteredCount,
                data = playersApiResponse?.Result?.Data?.Items
            });
        }, nameof(GetPlayersAjax), $"filter: {filter}, gameType: {gameType}").ConfigureAwait(false);
    }
}