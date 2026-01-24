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
/// API controller for admin actions data operations
/// </summary>
[Authorize(Policy = AuthPolicies.AccessAdminActionsController)]
[Route("AdminActions")]
public class AdminActionsController(
    IAuthorizationService authorizationService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<AdminActionsController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
{
    private readonly IAuthorizationService authorizationService = authorizationService ?? throw new ArgumentNullException(nameof(authorizationService));
    private readonly IRepositoryApiClient repositoryApiClient = repositoryApiClient ?? throw new ArgumentNullException(nameof(repositoryApiClient));

    /// <summary>
    /// Provides server-side paginated admin actions for DataTables AJAX endpoint.
    /// Supports optional filters for game type, admin action filter type, and admin ID.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data for DataTables display</returns>
    [HttpPost("GetAdminActionsAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetAdminActionsAjax(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);
            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model for admin actions by user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            // Extract optional custom filters passed via additional POST data (DataTables 'ajax.data' lambda)
            GameType? gameType = null;
            AdminActionFilter? apiFilter = null;
            string? adminId = null;
            if (Request.Query.TryGetValue("gameType", out var gameTypeValues) && Enum.TryParse<GameType>(gameTypeValues.FirstOrDefault(), out var gt))
                gameType = gt;
            if (Request.Query.TryGetValue("adminActionFilter", out var filterValues) && Enum.TryParse<AdminActionFilter>(filterValues.FirstOrDefault(), out var f))
                apiFilter = f;
            if (Request.Query.TryGetValue("adminId", out var adminIdValues))
                adminId = adminIdValues.FirstOrDefault();

            var order = AdminActionOrder.CreatedDesc;
            if (model.Order?.Count > 0)
            {
                var dir = model.Order.First().Dir;
                // Attempt to use CreatedAsc if available when user sorts ascending on Created column (index 0)
                if (model.Order.First().Column == 0 && dir == "asc")
                {
                    try
                    {
                        order = AdminActionOrder.CreatedAsc;
                    }
                    catch
                    {
                        order = AdminActionOrder.CreatedDesc;
                    }
                }
            }

            var apiResponse = await repositoryApiClient.AdminActions.V1.GetAdminActions(
                gameType, null, adminId, apiFilter, model.Start, model.Length, order, cancellationToken);

            if (!apiResponse.IsSuccess || apiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve admin actions list for user {UserId}", User.XtremeIdiotsId());
                return StatusCode(500, "Failed to retrieve admin actions data");
            }

            var items = apiResponse.Result.Data.Items.ToList();

            TrackSuccessTelemetry("AdminActionsDataLoaded", nameof(GetAdminActionsAjax), new Dictionary<string, string>
            {
                { "GameType", gameType?.ToString() ?? "All" },
                { "Filter", apiFilter?.ToString() ?? "None" },
                { "ResultCount", items.Count.ToString() }
            });

            Logger.LogInformation("Successfully retrieved {Count} admin actions for user {UserId}",
                items.Count, User.XtremeIdiotsId());

            return Ok(new
            {
                model.Draw,
                recordsTotal = apiResponse.Result.Pagination?.TotalCount,
                recordsFiltered = apiResponse.Result.Pagination?.FilteredCount,
                data = items.Select(a => new
                {
                    adminActionId = a.AdminActionId,
                    created = a.Created.ToString("yyyy-MM-dd HH:mm"),
                    gameType = a.Player?.GameType.ToString(),
                    type = a.Type.ToString(),
                    player = a.Player?.Username,
                    playerId = a.PlayerId,
                    guid = a.Player?.Guid,
                    admin = a.UserProfile?.DisplayName ?? "Unclaimed",
                    expires = a.Expires?.ToString("yyyy-MM-dd HH:mm") ?? (a.Type == AdminActionType.Ban ? "Never" : string.Empty)
                })
            });
        }, nameof(GetAdminActionsAjax));
    }

    /// <summary>
    /// Provides server-side paginated unclaimed bans for DataTables AJAX endpoint.
    /// Always filters by <see cref="AdminActionFilter.UnclaimedBans"/> and supports optional game type filter.
    /// Includes per-row authorization flag indicating whether the current user can claim the ban.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data for DataTables display</returns>
    [HttpPost("GetUnclaimedAdminActionsAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetUnclaimedAdminActionsAjax(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);
            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model for unclaimed admin actions by user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            GameType? gameType = null;
            if (Request.Query.TryGetValue("gameType", out var gameTypeValues) && Enum.TryParse<GameType>(gameTypeValues.FirstOrDefault(), out var gt))
                gameType = gt;

            var order = AdminActionOrder.CreatedDesc;
            if (model.Order?.Count > 0)
            {
                var dir = model.Order.First().Dir;
                if (model.Order.First().Column == 0 && dir == "asc")
                {
                    try
                    {
                        order = AdminActionOrder.CreatedAsc;
                    }
                    catch
                    {
                        order = AdminActionOrder.CreatedDesc;
                    }
                }
            }

            var apiResponse = await repositoryApiClient.AdminActions.V1.GetAdminActions(
                gameType, null, null, AdminActionFilter.UnclaimedBans, model.Start, model.Length, order, cancellationToken);

            if (!apiResponse.IsSuccess || apiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve unclaimed admin actions list for user {UserId}", User.XtremeIdiotsId());
                return StatusCode(500, "Failed to retrieve unclaimed admin actions data");
            }

            var items = apiResponse.Result.Data.Items.ToList();
            var responseItems = new List<object>(items.Count);
            foreach (var a in items)
            {
                var canClaim = false;
                if (a.Player?.GameType is GameType gtClaim)
                {
                    var auth = await authorizationService.AuthorizeAsync(User, gtClaim, AuthPolicies.ClaimAdminAction);
                    canClaim = auth.Succeeded;
                }

                responseItems.Add(new
                {
                    adminActionId = a.AdminActionId,
                    created = a.Created.ToString("yyyy-MM-dd HH:mm"),
                    gameType = a.Player?.GameType.ToString(),
                    type = a.Type.ToString(),
                    player = a.Player?.Username,
                    playerId = a.PlayerId,
                    guid = a.Player?.Guid,
                    admin = a.UserProfile?.DisplayName ?? "Unclaimed",
                    expires = a.Expires?.ToString("yyyy-MM-dd HH:mm") ?? (a.Type == AdminActionType.Ban ? "Never" : string.Empty),
                    canClaim
                });
            }

            TrackSuccessTelemetry("UnclaimedAdminActionsDataLoaded", nameof(GetUnclaimedAdminActionsAjax), new Dictionary<string, string>
            {
                { "GameType", gameType?.ToString() ?? "All" },
                { "ResultCount", responseItems.Count.ToString() }
            });

            Logger.LogInformation("Successfully retrieved {Count} unclaimed admin actions for user {UserId}",
                responseItems.Count, User.XtremeIdiotsId());

            return Ok(new
            {
                model.Draw,
                recordsTotal = apiResponse.Result.Pagination?.TotalCount,
                recordsFiltered = apiResponse.Result.Pagination?.FilteredCount,
                data = responseItems
            });
        }, nameof(GetUnclaimedAdminActionsAjax));
    }

    /// <summary>
    /// Provides server-side paginated admin actions for the currently logged in admin ("My Actions")
    /// </summary>
    /// <param name="cancellationToken">Cancellation token for the async operation</param>
    /// <returns>JSON data for DataTables display</returns>
    [HttpPost("GetMyAdminActionsAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetMyAdminActionsAjax(CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            using var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken);

            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);
            if (model is null)
            {
                Logger.LogWarning("Invalid DataTable model for my admin actions by user {UserId}", User.XtremeIdiotsId());
                return BadRequest("Invalid request data");
            }

            GameType? gameType = null;
            AdminActionFilter? apiFilter = null;
            if (Request.Query.TryGetValue("gameType", out var gameTypeValues) && Enum.TryParse<GameType>(gameTypeValues.FirstOrDefault(), out var gt))
                gameType = gt;
            if (Request.Query.TryGetValue("adminActionFilter", out var filterValues) && Enum.TryParse<AdminActionFilter>(filterValues.FirstOrDefault(), out var f))
                apiFilter = f;

            // Always constrain to current user
            var adminId = User.XtremeIdiotsId();

            var order = AdminActionOrder.CreatedDesc;
            if (model.Order?.Count > 0)
            {
                var dir = model.Order.First().Dir;
                if (model.Order.First().Column == 0 && dir == "asc")
                {
                    try
                    {
                        order = AdminActionOrder.CreatedAsc;
                    }
                    catch
                    {
                        order = AdminActionOrder.CreatedDesc;
                    }
                }
            }

            var apiResponse = await repositoryApiClient.AdminActions.V1.GetAdminActions(
                gameType, null, adminId, apiFilter, model.Start, model.Length, order, cancellationToken);

            if (!apiResponse.IsSuccess || apiResponse.Result?.Data?.Items is null)
            {
                Logger.LogWarning("Failed to retrieve my admin actions list for user {UserId}", User.XtremeIdiotsId());
                return StatusCode(500, "Failed to retrieve my admin actions data");
            }

            var items = apiResponse.Result.Data.Items.ToList();

            TrackSuccessTelemetry("MyAdminActionsDataLoaded", nameof(GetMyAdminActionsAjax), new Dictionary<string, string>
            {
                { "GameType", gameType?.ToString() ?? "All" },
                { "Filter", apiFilter?.ToString() ?? "None" },
                { "ResultCount", items.Count.ToString() }
            });

            Logger.LogInformation("Successfully retrieved {Count} admin actions for current user {UserId}",
                items.Count, User.XtremeIdiotsId());

            return Ok(new
            {
                model.Draw,
                recordsTotal = apiResponse.Result.Pagination?.TotalCount,
                recordsFiltered = apiResponse.Result.Pagination?.FilteredCount,
                data = items.Select(a => new
                {
                    created = a.Created.ToString("yyyy-MM-dd HH:mm"),
                    gameType = a.Player?.GameType.ToString(),
                    type = a.Type.ToString(),
                    player = a.Player?.Username,
                    playerId = a.PlayerId,
                    guid = a.Player?.Guid,
                    expires = a.Expires?.ToString("yyyy-MM-dd HH:mm") ?? (a.Type == AdminActionType.Ban ? "Never" : string.Empty),
                    id = a.AdminActionId,
                    text = a.Text
                })
            });
        }, nameof(GetMyAdminActionsAjax));
    }
}
