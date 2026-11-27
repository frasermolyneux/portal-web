using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller providing activity log data for DataTables AJAX requests
/// </summary>
[Authorize(Policy = AuthPolicies.Users_ActivityLog)]
[Route("User")]
public class ActivityLogController(
    IActivityLogService activityLogService,
    IRepositoryApiClient repositoryApiClient,
    TelemetryClient telemetryClient,
    ILogger<ActivityLogController> logger,
    IConfiguration configuration,
    IAuditLogger auditLogger) : BaseApiController(telemetryClient, logger, configuration, auditLogger)
{
    private readonly static Dictionary<string, TimeSpan> timeRanges = new()
    {
        ["1h"] = TimeSpan.FromHours(1),
        ["6h"] = TimeSpan.FromHours(6),
        ["12h"] = TimeSpan.FromHours(12),
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7),
        ["30d"] = TimeSpan.FromDays(30),
    };

    /// <summary>
    /// Provides AJAX endpoint for retrieving paginated activity log data for DataTables
    /// </summary>
    [HttpPost("GetActivityLogAjax")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetActivityLogAjax(
        [FromQuery] string? timeRange,
        [FromQuery] string? categories,
        [FromQuery] string? eventNames,
        [FromQuery] string? actorId,
        CancellationToken cancellationToken = default)
    {
        return await ExecuteWithErrorHandlingAsync(async () =>
        {
            var reader = new StreamReader(Request.Body);
            var requestBody = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var model = JsonConvert.DeserializeObject<DataTableAjaxPostModel>(requestBody);

            if (model is null)
                return BadRequest("Invalid request body");

            var timeSpan = timeRanges.GetValueOrDefault(timeRange ?? "24h", TimeSpan.FromHours(24));

            var parsedCategories = ParseCategories(categories);
            var parsedEventNames = ParseCommaSeparated(eventNames);

            // Determine sort column and direction from DataTable model
            var sortColumn = "timestamp";
            var sortDirection = "desc";

            if (model.Order.Count > 0)
            {
                var orderItem = model.Order[0];
                var columnIndex = orderItem.Column;

                if (columnIndex >= 0 && columnIndex < model.Columns.Count)
                {
                    sortColumn = model.Columns[columnIndex].Name ?? "timestamp";
                }

                sortDirection = orderItem.Dir ?? "desc";
            }

            var searchTerm = model.Search?.Value;

            var result = await activityLogService.QueryEventsAsync(
                timeSpan,
                parsedCategories,
                parsedEventNames,
                searchTerm,
                model.Start,
                model.Length,
                sortColumn,
                sortDirection,
                actorId,
                cancellationToken).ConfigureAwait(false);

            return Ok(new
            {
                model.Draw,
                recordsTotal = result.TotalCount,
                recordsFiltered = result.FilteredCount,
                data = result.Entries
            });
        }, nameof(GetActivityLogAjax)).ConfigureAwait(false);
    }

    /// <summary>Searches users with an audit actor ID for the activity log filter.</summary>
    [HttpGet("GetActivityLogUsers")]
    public async Task<IActionResult> GetActivityLogUsers([FromQuery] string? term, CancellationToken cancellationToken = default)
    {
        var search = term?.Trim();
        if (string.IsNullOrEmpty(search) || search.Length < 2)
            return Ok(Array.Empty<object>());

        var response = await repositoryApiClient.UserProfiles.V1.GetUserProfiles(
            search, null, 0, 15, UserProfilesOrder.DisplayNameAsc, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccess || response.Result?.Data?.Items is null)
        {
            logger.LogWarning("Activity log user search failed: {StatusCode}", response.StatusCode);
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        return Ok(response.Result.Data.Items
            .Where(user => !string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(user.XtremeIdiotsForumId))
            .Select(user => new { id = user.XtremeIdiotsForumId, text = user.DisplayName })
            .ToArray());
    }

    /// <summary>
    /// Returns event names for given categories (used for cascading filter dropdown)
    /// </summary>
    [HttpGet("GetActivityLogEvents")]
    public IActionResult GetActivityLogEvents([FromQuery] string? categories)
    {
        var parsedCategories = ParseCategories(categories);

        if (parsedCategories.Count == 0)
        {
            var allEvents = ActivityLogEventMap.Events
                .Select(e => new { name = e.Key, category = e.Value.ToString() })
                .OrderBy(e => e.name)
                .ToList();

            return Ok(allEvents);
        }

        var events = parsedCategories
            .SelectMany(cat => ActivityLogEventMap.GetEventsByCategory(cat)
                .Select(e => new { name = e, category = cat.ToString() }))
            .OrderBy(e => e.name)
            .ToList();

        return Ok(events);
    }

    private static List<ActivityLogCategory> ParseCategories(string? categories)
    {
        return string.IsNullOrWhiteSpace(categories)
            ? []
            : [
            .. categories
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(c => Enum.TryParse<ActivityLogCategory>(c, out var cat) ? cat : (ActivityLogCategory?)null)
                .Where(c => c.HasValue)
                .Select(c => c!.Value)
                .Distinct()
        ];
    }

    private static List<string> ParseCommaSeparated(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : [
            .. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
        ];
    }
}
