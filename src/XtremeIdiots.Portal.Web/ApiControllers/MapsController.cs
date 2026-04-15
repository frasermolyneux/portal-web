using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller for maps data operations
/// </summary>
[Authorize(Policy = AuthPolicies.MapRotations_Read)]
[Route("Maps")]
public class MapsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapsController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{

    /// <summary>
    /// Provides paginated, searchable map data for DataTables Ajax requests
    /// </summary>
    /// <param name="id">Optional game type to filter maps</param>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data formatted for DataTables consumption</returns>
    [HttpPost("GetMapListAjax/{id?}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetMapListAjax(GameType? id, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable request body for user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            var order = MapsOrder.MapNameAsc;

            if (model.Order is not null && model.Order.Count != 0)
            {
                var orderColumn = model.Columns[model.Order.First().Column].Name;
                var searchOrder = model.Order.First().Dir;

                order = orderColumn switch
                {
                    "mapName" => searchOrder == "asc" ? MapsOrder.MapNameAsc : MapsOrder.MapNameDesc,
                    "popularity" => searchOrder == "asc" ? MapsOrder.PopularityAsc : MapsOrder.PopularityDesc,
                    "gameType" => searchOrder == "asc" ? MapsOrder.GameTypeAsc : MapsOrder.GameTypeDesc,
                    _ => MapsOrder.MapNameAsc
                };
            }

            var mapsApiResponse = await repositoryApiClient.Maps.V1.GetMaps(
                id, null, null, model.Search?.Value, model.Start, model.Length, order).ConfigureAwait(false);

            if (!mapsApiResponse.IsSuccess || mapsApiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Failed to retrieve maps data for user {UserId} and game type {GameType}",
                    User.XtremeIdiotsId(), id);
                return StatusCode(500, "Failed to retrieve maps data");
            }

            return Ok(new
            {
                model.Draw,
                recordsTotal = mapsApiResponse.Result?.Pagination?.TotalCount,
                recordsFiltered = mapsApiResponse.Result?.Pagination?.FilteredCount,
                data = mapsApiResponse?.Result?.Data?.Items?.Select(mapItem => new
                {
                    mapItem.MapId,
                    gameType = mapItem.GameType.ToString(),
                    mapItem.MapName,
                    mapItem.MapFiles,
                    mapItem.MapImageUri,
                    mapItem.TotalLikes,
                    mapItem.TotalDislikes,
                    mapItem.TotalVotes,
                    mapItem.LikePercentage,
                    mapItem.DislikePercentage,
                    builtIn = BuiltInMaps.IsBuiltIn(mapItem.GameType, mapItem.MapName)
                })
            });
        }, nameof(GetMapListAjax)).ConfigureAwait(false);
    }

    /// <summary>
    /// Provides paginated map vote data for DataTables Ajax requests
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data formatted for DataTables consumption</returns>
    [HttpPost("GetMapVotesAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetMapVotesAjax(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable request body for map votes from user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            GameType? gameType = null;
            if (Request.Query.TryGetValue("gameType", out var gameTypeValues) && Enum.TryParse<GameType>(gameTypeValues.FirstOrDefault(), out var gt))
                gameType = gt;

            var order = MapVotesOrder.TimestampDesc;
            if (model.Order is not null && model.Order.Count != 0)
            {
                var orderColumn = model.Columns[model.Order.First().Column].Name;
                var searchOrder = model.Order.First().Dir;

                order = orderColumn switch
                {
                    "timestamp" => searchOrder == "asc" ? MapVotesOrder.TimestampAsc : MapVotesOrder.TimestampDesc,
                    "mapName" => searchOrder == "asc" ? MapVotesOrder.MapNameAsc : MapVotesOrder.MapNameDesc,
                    _ => MapVotesOrder.TimestampDesc
                };
            }

            var apiResponse = await repositoryApiClient.Maps.V1.GetMapVotes(
                gameType, null, model.Start, model.Length, order, cancellationToken).ConfigureAwait(false);

            if (!apiResponse.IsSuccess || apiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Failed to retrieve map votes data for user {UserId}", User.XtremeIdiotsId());
                return StatusCode(500, "Failed to retrieve map votes data");
            }

            var items = apiResponse.Result.Data.Items?.ToList() ?? [];

            return Ok(new
            {
                model.Draw,
                recordsTotal = apiResponse.Result.Pagination?.TotalCount,
                recordsFiltered = apiResponse.Result.Pagination?.FilteredCount,
                data = items.Select(v => new
                {
                    gameType = v.Map?.GameType.ToString(),
                    mapName = v.Map?.MapName,
                    playerName = v.Player?.Username,
                    playerId = v.PlayerId,
                    serverName = v.GameServer?.Title,
                    like = v.Like,
                    timestamp = DateTime.SpecifyKind(v.Timestamp, DateTimeKind.Utc).ToString("o")
                })
            });
        }, nameof(GetMapVotesAjax)).ConfigureAwait(false);
    }
}
