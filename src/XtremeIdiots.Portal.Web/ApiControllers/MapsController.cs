using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
[Authorize(Policy = AuthPolicies.AccessMaps)]
[Route("Maps")]
public class MapsController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapsController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    private readonly IRepositoryApiClient repositoryApiClient = repositoryApiClient ?? throw new ArgumentNullException(nameof(repositoryApiClient));

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
            var requestBody = await reader.ReadToEndAsync(cancellationToken);

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
                id, null, null, model.Search?.Value, model.Start, model.Length, order);

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
        }, nameof(GetMapListAjax));
    }
}
