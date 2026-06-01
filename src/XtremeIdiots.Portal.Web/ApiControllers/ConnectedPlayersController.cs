using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;

using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.ConnectedPlayers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Extensions;
using XtremeIdiots.Portal.Web.Models;

namespace XtremeIdiots.Portal.Web.ApiControllers;

[Authorize(Policy = AuthPolicies.AdminActions_Read)]
[Route("ConnectedPlayers")]
public class ConnectedPlayersController(
    IRepositoryApiClient repositoryApiClient,
    IMemoryCache memoryCache,
    TelemetryClient telemetryClient,
    ILogger<ConnectedPlayersController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{
    private const int FetchBatchSize = 500;

    [HttpPost("GetConnectedPlayersAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetConnectedPlayersAjax([FromQuery] GameType? gameType = null, [FromQuery] bool? isActive = null, CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);
            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model received for connected players from user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            if (model.Length < 0 || model.Start < 0)
            {
                return BadRequest("Invalid pagination values");
            }

            if (model.Columns is null || model.Columns.Count == 0)
            {
                return BadRequest("Invalid columns metadata");
            }

            if (model.Order is not null && model.Order.Count > 0)
            {
                var requestedColumnIndex = model.Order.First().Column;
                if (requestedColumnIndex < 0 || requestedColumnIndex >= model.Columns.Count)
                {
                    return BadRequest("Invalid order column index");
                }
            }

            var cacheKey = $"connected-players:{gameType?.ToString() ?? "all"}:{isActive?.ToString() ?? "all"}";
            if (!memoryCache.TryGetValue(cacheKey, out List<ConnectedPlayerDto>? allRows) || allRows is null)
            {
                allRows = [];
                var skip = 0;
                int? totalCount = null;

                while (!totalCount.HasValue || skip < totalCount.Value)
                {
                    var response = await repositoryApiClient.ConnectedPlayers.V1
                        .GetConnectedPlayers(null, null, gameType, isActive, skip, FetchBatchSize, cancellationToken)
                        .ConfigureAwait(false);

                    if (!response.IsSuccess || response.Result?.Data?.Items is null)
                    {
                        Logger.LogWarning("Failed to retrieve connected players data for user {UserId}", User.XtremeIdiotsId());
                        return StatusCode(500, "Failed to retrieve connected players data");
                    }

                    var items = response.Result.Data.Items.ToList();
                    allRows.AddRange(items);

                    totalCount = response.Result.Pagination?.TotalCount ?? allRows.Count;

                    if (items.Count == 0)
                    {
                        break;
                    }

                    skip += items.Count;
                }

                memoryCache.Set(cacheKey, allRows, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
                });
            }

            var recordsTotal = allRows.Count;

            IEnumerable<ConnectedPlayerDto> filtered = allRows;
            var searchTerm = model.Search?.Value?.Trim();
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                filtered = filtered.Where(x =>
                    x.Username.Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.PlayerId.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.UserProfileId.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.LinkMethod.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    x.GameType.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
            }

            var filteredList = ApplyOrdering(filtered, model).ToList();
            var recordsFiltered = filteredList.Count;

            var pageLength = model.Length <= 0 ? 10 : model.Length;
            var pageItems = filteredList
                .Skip(Math.Max(0, model.Start))
                .Take(pageLength)
                .Select(x => new
                {
                    connectedPlayerProfileId = x.ConnectedPlayerProfileId,
                    playerId = x.PlayerId,
                    userProfileId = x.UserProfileId,
                    gameType = x.GameType.ToString(),
                    username = x.Username,
                    linkMethod = x.LinkMethod.ToString(),
                    isActive = x.IsActive,
                    linkedAtUtc = x.LinkedAtUtc,
                    unlinkedAtUtc = x.UnlinkedAtUtc
                })
                .ToList();

            return Ok(new
            {
                model.Draw,
                recordsTotal,
                recordsFiltered,
                data = pageItems
            });
        }, nameof(GetConnectedPlayersAjax)).ConfigureAwait(false);
    }

    private static IEnumerable<ConnectedPlayerDto> ApplyOrdering(IEnumerable<ConnectedPlayerDto> source, DataTableAjaxPostModel model)
    {
        if (model.Order is null || model.Order.Count == 0)
        {
            return source.OrderByDescending(x => x.LinkedAtUtc);
        }

        var order = model.Order.First();
        var columnName = model.Columns[order.Column].Name;
        var isAsc = string.Equals(order.Dir, "asc", StringComparison.OrdinalIgnoreCase);

        return columnName switch
        {
            "gameType" => isAsc ? source.OrderBy(x => x.GameType) : source.OrderByDescending(x => x.GameType),
            "username" => isAsc ? source.OrderBy(x => x.Username) : source.OrderByDescending(x => x.Username),
            "linkMethod" => isAsc ? source.OrderBy(x => x.LinkMethod) : source.OrderByDescending(x => x.LinkMethod),
            "isActive" => isAsc ? source.OrderBy(x => x.IsActive) : source.OrderByDescending(x => x.IsActive),
            "linkedAtUtc" => isAsc ? source.OrderBy(x => x.LinkedAtUtc) : source.OrderByDescending(x => x.LinkedAtUtc),
            "unlinkedAtUtc" => isAsc ? source.OrderBy(x => x.UnlinkedAtUtc) : source.OrderByDescending(x => x.UnlinkedAtUtc),
            _ => source.OrderByDescending(x => x.LinkedAtUtc)
        };
    }
}
