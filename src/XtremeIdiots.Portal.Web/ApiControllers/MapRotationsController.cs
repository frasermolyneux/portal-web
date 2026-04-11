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

[Authorize(Policy = AuthPolicies.AccessMapRotations)]
[Route("MapRotations")]
public class MapRotationsApiController(
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<MapRotationsApiController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    [HttpPost("GetMapRotationsAjax/{id?}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetMapRotationsAjax(GameType? id, CancellationToken cancellationToken = default)
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

            var order = MapRotationsOrder.TitleAsc;

            if (model.Order is not null && model.Order.Count != 0)
            {
                var orderColumn = model.Columns[model.Order.First().Column].Name;
                var searchOrder = model.Order.First().Dir;

                order = orderColumn switch
                {
                    "title" => searchOrder == "asc" ? MapRotationsOrder.TitleAsc : MapRotationsOrder.TitleDesc,
                    "gameMode" => searchOrder == "asc" ? MapRotationsOrder.GameModeAsc : MapRotationsOrder.GameModeDesc,
                    "mapCount" => searchOrder == "asc" ? MapRotationsOrder.MapCountAsc : MapRotationsOrder.MapCountDesc,
                    "serverCount" => searchOrder == "asc" ? MapRotationsOrder.ServerCountAsc : MapRotationsOrder.ServerCountDesc,
                    "updatedAt" => searchOrder == "asc" ? MapRotationsOrder.UpdatedAtAsc : MapRotationsOrder.UpdatedAtDesc,
                    _ => MapRotationsOrder.TitleAsc
                };
            }

            var gameTypes = id.HasValue ? [id.Value] : Array.Empty<GameType>();

            var searchValue = !string.IsNullOrWhiteSpace(model.Search?.Value) ? model.Search.Value : null;

            // Extract server-side filter parameters from column search values
            MapRotationStatus? statusFilter = null;
            string? gameModeFilter = null;
            foreach (var col in model.Columns)
            {
                if (col.Name == "status" && !string.IsNullOrWhiteSpace(col.Search?.Value) && Enum.TryParse<MapRotationStatus>(col.Search.Value, out var parsedStatus))
                    statusFilter = parsedStatus;
                if (col.Name == "gameMode" && !string.IsNullOrWhiteSpace(col.Search?.Value))
                    gameModeFilter = col.Search.Value;
            }

            var apiResponse = await repositoryApiClient.MapRotations.V1.GetMapRotations(
                gameTypes, gameModeFilter, statusFilter, searchValue, null, model.Start, model.Length, order, cancellationToken).ConfigureAwait(false);

            if (!apiResponse.IsSuccess || apiResponse.Result?.Data is null)
            {
                Logger.LogWarning("Failed to retrieve map rotations data for user {UserId} and game type {GameType}",
                    User.XtremeIdiotsId(), id);
                return StatusCode(500, "Failed to retrieve map rotations data");
            }

            var items = apiResponse.Result.Data.Items?.ToList() ?? [];

            TrackSuccessTelemetry("MapRotationsListRetrieved", nameof(GetMapRotationsAjax), new Dictionary<string, string>
            {
                { "GameType", id?.ToString() ?? "All" },
                { "ResultCount", items.Count.ToString() }
            });

            return Ok(new
            {
                model.Draw,
                recordsTotal = apiResponse.Result?.Pagination?.TotalCount,
                recordsFiltered = apiResponse.Result?.Pagination?.FilteredCount,
                data = items.Select(r => new
                {
                    mapRotationId = r.MapRotationId,
                    gameType = r.GameType.ToString(),
                    title = r.Title,
                    description = r.Description,
                    gameMode = r.GameMode,
                    status = r.Status.ToString(),
                    category = r.Category,
                    sequenceOrder = r.SequenceOrder,
                    mapCount = r.MapRotationMaps?.Count ?? 0,
                    serverCount = r.ServerAssignments?.Count ?? 0,
                    version = r.Version,
                    createdByDisplayName = r.CreatedByDisplayName,
                    createdAt = r.CreatedAt.ToString("yyyy-MM-dd"),
                    updatedAt = r.UpdatedAt.ToString("yyyy-MM-dd")
                })
            });
        }, nameof(GetMapRotationsAjax)).ConfigureAwait(false);
    }
}
