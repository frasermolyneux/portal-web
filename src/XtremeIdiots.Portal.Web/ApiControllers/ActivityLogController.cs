using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using XtremeIdiots.Portal.Web.Auth.Constants;
using XtremeIdiots.Portal.Web.Models;
using XtremeIdiots.Portal.Web.Models.ActivityLog;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.ApiControllers;

/// <summary>
/// API controller providing activity log data for DataTables AJAX requests
/// </summary>
[Authorize(Policy = AuthPolicies.AccessActivityLog)]
[Route("User")]
public class ActivityLogController(
    IActivityLogService activityLogService,
    TelemetryClient telemetryClient,
    ILogger<ActivityLogController> logger,
    IConfiguration configuration) : BaseApiController(telemetryClient, logger, configuration)
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
        [FromQuery] string? category,
        [FromQuery] string? eventName,
        [FromQuery] bool includeReads = false,
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

            ActivityLogCategory? parsedCategory = null;
            if (!string.IsNullOrWhiteSpace(category) && Enum.TryParse<ActivityLogCategory>(category, out var cat))
            {
                parsedCategory = cat;
            }

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
                parsedCategory,
                eventName,
                includeReads,
                searchTerm,
                model.Start,
                model.Length,
                sortColumn,
                sortDirection,
                cancellationToken).ConfigureAwait(false);

            TrackSuccessTelemetry("ActivityLogQueried", nameof(GetActivityLogAjax), new Dictionary<string, string>
            {
                { "TimeRange", timeRange ?? "24h" },
                { "Category", category ?? "All" },
                { "IncludeReads", includeReads.ToString() },
                { "ResultCount", result.Entries.Count.ToString() }
            });

            return Ok(new
            {
                model.Draw,
                recordsTotal = result.TotalCount,
                recordsFiltered = result.FilteredCount,
                data = result.Entries
            });
        }, nameof(GetActivityLogAjax)).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns event names for a given category (used for cascading filter dropdown)
    /// </summary>
    [HttpGet("GetActivityLogEvents")]
    public IActionResult GetActivityLogEvents([FromQuery] string? category, [FromQuery] bool includeReads = false)
    {
        if (string.IsNullOrWhiteSpace(category) || !Enum.TryParse<ActivityLogCategory>(category, out var parsedCategory))
        {
            // Return all events matching scope
            var allEvents = ActivityLogEventMap.Events
                .Where(e => includeReads || e.Value.IsWrite)
                .Select(e => new { name = e.Key, category = e.Value.Category.ToString() })
                .OrderBy(e => e.name)
                .ToList();

            return Ok(allEvents);
        }

        var events = ActivityLogEventMap.GetEventsByCategory(parsedCategory, includeReads)
            .Select(e => new { name = e, category = parsedCategory.ToString() })
            .ToList();

        return Ok(events);
    }
}
