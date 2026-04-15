using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MX.Observability.ApplicationInsights.Auditing;
using Newtonsoft.Json;
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
                includeReads,
                searchTerm,
                model.Start,
                model.Length,
                sortColumn,
                sortDirection,
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

    /// <summary>
    /// Returns event names for given categories (used for cascading filter dropdown)
    /// </summary>
    [HttpGet("GetActivityLogEvents")]
    public IActionResult GetActivityLogEvents([FromQuery] string? categories, [FromQuery] bool includeReads = false)
    {
        var parsedCategories = ParseCategories(categories);

        if (parsedCategories.Count == 0)
        {
            var allEvents = ActivityLogEventMap.Events
                .Where(e => includeReads || e.Value.IsWrite)
                .Select(e => new { name = e.Key, category = e.Value.Category.ToString() })
                .OrderBy(e => e.name)
                .ToList();

            return Ok(allEvents);
        }

        var events = parsedCategories
            .SelectMany(cat => ActivityLogEventMap.GetEventsByCategory(cat, includeReads)
                .Select(e => new { name = e, category = cat.ToString() }))
            .OrderBy(e => e.name)
            .ToList();

        return Ok(events);
    }

    private static List<ActivityLogCategory> ParseCategories(string? categories)
    {
        if (string.IsNullOrWhiteSpace(categories))
            return [];

        return
        [
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
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return
        [
            .. value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct()
        ];
    }
}
