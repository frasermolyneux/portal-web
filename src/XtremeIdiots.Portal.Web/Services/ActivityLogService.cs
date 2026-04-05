using System.Text;
using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using XtremeIdiots.Portal.Web.Models.ActivityLog;

namespace XtremeIdiots.Portal.Web.Services;

public class ActivityLogService(
    LogsQueryClient logsQueryClient,
    IConfiguration configuration,
    ILogger<ActivityLogService> logger) : IActivityLogService
{
    private const int MaxPageSize = 100;

    private readonly static HashSet<string> knownPropertyKeys =
    [
        "LoggedInAdminId", "LoggedInUsername", "Controller", "Action"
    ];

    public async Task<ActivityLogQueryResult> QueryEventsAsync(
        TimeSpan timeRange,
        IReadOnlyList<ActivityLogCategory> categories,
        IReadOnlyList<string> eventNames,
        bool includeReads,
        string? searchTerm,
        int skip,
        int take,
        string sortColumn,
        string sortDirection,
        CancellationToken cancellationToken = default)
    {
        var resourceId = GetAppInsightsResourceId();

        var allowedEvents = GetAllowedEventNames(categories, eventNames, includeReads);
        if (allowedEvents.Count == 0)
        {
            return new ActivityLogQueryResult();
        }

        // Clamp pagination bounds
        skip = Math.Max(0, skip);
        take = Math.Clamp(take, 1, MaxPageSize);

        var eventFilter = BuildEventNameFilter(allowedEvents);
        var searchFilter = BuildSearchFilter(searchTerm);
        var orderBy = BuildOrderBy(sortColumn, sortDirection);

        // Query for total count (matching time range + event filter only)
        var countQuery = BuildCountQuery(timeRange, eventFilter);
        var totalCount = await ExecuteCountQueryAsync(resourceId, countQuery, timeRange, cancellationToken).ConfigureAwait(false);

        // Query for filtered count (including search)
        int filteredCount;
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            filteredCount = totalCount;
        }
        else
        {
            var filteredCountQuery = BuildCountQuery(timeRange, eventFilter, searchFilter);
            filteredCount = await ExecuteCountQueryAsync(resourceId, filteredCountQuery, timeRange, cancellationToken).ConfigureAwait(false);
        }

        // Query for page of data
        var dataQuery = BuildDataQuery(timeRange, eventFilter, searchFilter, orderBy, skip, take);
        var entries = await ExecuteDataQueryAsync(resourceId, dataQuery, timeRange, cancellationToken).ConfigureAwait(false);

        return new ActivityLogQueryResult
        {
            TotalCount = totalCount,
            FilteredCount = filteredCount,
            Entries = entries
        };
    }

    private string GetAppInsightsResourceId()
    {
        var resourceId = configuration["ApplicationInsights:ResourceId"];

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            logger.LogError("ApplicationInsights:ResourceId is not configured");
            throw new InvalidOperationException(
                "ApplicationInsights:ResourceId must be configured with the full Azure resource ID (e.g., /subscriptions/{sub}/resourceGroups/{rg}/providers/microsoft.insights/components/{name})");
        }

        return resourceId;
    }

    private static List<string> GetAllowedEventNames(IReadOnlyList<ActivityLogCategory> categories, IReadOnlyList<string> eventNames, bool includeReads)
    {
        if (eventNames.Count > 0)
        {
            return
            [
                .. eventNames
                    .Where(e => ActivityLogEventMap.Events.TryGetValue(e, out var mapping)
                        && (includeReads || mapping.IsWrite)
                        && (categories.Count == 0 || categories.Contains(mapping.Category)))
            ];
        }

        if (categories.Count > 0)
        {
            return
            [
                .. categories
                    .SelectMany(cat => ActivityLogEventMap.GetEventsByCategory(cat, includeReads))
                    .Distinct()
            ];
        }

        return
        [
            .. ActivityLogEventMap.Events
                .Where(e => includeReads || e.Value.IsWrite)
                .Select(e => e.Key)
        ];
    }

    private static string BuildEventNameFilter(IReadOnlyList<string> eventNames)
    {
        var escaped = eventNames.Select(e => $"'{EscapeKql(e)}'");
        return $"name in ({string.Join(", ", escaped)})";
    }

    private static string? BuildSearchFilter(string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
            return null;

        var escaped = EscapeKql(searchTerm);
        return $"(name contains '{escaped}' or tostring(customDimensions.LoggedInUsername) contains '{escaped}' or tostring(customDimensions.Controller) contains '{escaped}' or tostring(customDimensions.Action) contains '{escaped}' or tostring(customDimensions) contains '{escaped}')";
    }

    private static string BuildOrderBy(string sortColumn, string sortDirection)
    {
        var dir = sortDirection.Equals("asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

        return sortColumn.ToLowerInvariant() switch
        {
            "eventname" => $"name {dir}",
            "username" => $"tostring(customDimensions.LoggedInUsername) {dir}",
            "controller" => $"tostring(customDimensions.Controller) {dir}",
            _ => $"timestamp {dir}"
        };
    }

    private static string BuildCountQuery(TimeSpan timeRange, string eventFilter, string? searchFilter = null)
    {
        var sb = new StringBuilder();
        sb.Append("customEvents");
        sb.Append($" | where timestamp > ago({FormatTimeSpan(timeRange)})");
        sb.Append($" | where {eventFilter}");

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            sb.Append($" | where {searchFilter}");
        }

        sb.Append(" | count");
        return sb.ToString();
    }

    private static string BuildDataQuery(TimeSpan timeRange, string eventFilter, string? searchFilter, string orderBy, int skip, int take)
    {
        var sb = new StringBuilder();
        sb.Append("customEvents");
        sb.Append($" | where timestamp > ago({FormatTimeSpan(timeRange)})");
        sb.Append($" | where {eventFilter}");

        if (!string.IsNullOrWhiteSpace(searchFilter))
        {
            sb.Append($" | where {searchFilter}");
        }

        sb.Append($" | order by {orderBy}");
        sb.Append(" | project timestamp, name, customDimensions");
        sb.Append($" | serialize | extend _row = row_number() | where _row > {skip} and _row <= {skip + take} | project-away _row");

        return sb.ToString();
    }

    private async Task<int> ExecuteCountQueryAsync(string resourceId, string query, TimeSpan timeRange, CancellationToken cancellationToken)
    {
        var response = await logsQueryClient.QueryResourceAsync(
            new ResourceIdentifier(resourceId),
            query,
            new QueryTimeRange(timeRange),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (response.Value.Table.Rows.Count > 0)
        {
            return Convert.ToInt32(response.Value.Table.Rows[0][0]);
        }

        return 0;
    }

    private async Task<IReadOnlyList<ActivityLogEntry>> ExecuteDataQueryAsync(string resourceId, string query, TimeSpan timeRange, CancellationToken cancellationToken)
    {
        var response = await logsQueryClient.QueryResourceAsync(
            new ResourceIdentifier(resourceId),
            query,
            new QueryTimeRange(timeRange),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var entries = new List<ActivityLogEntry>();

        foreach (var row in response.Value.Table.Rows)
        {
            var entry = ParseRow(row, response.Value.Table.Columns);
            entries.Add(entry);
        }

        return entries;
    }

    private static ActivityLogEntry ParseRow(LogsTableRow row, IReadOnlyList<LogsTableColumn> columns)
    {
        var entry = new ActivityLogEntry();

        for (var i = 0; i < columns.Count; i++)
        {
            var columnName = columns[i].Name;
            var value = row[i];

            switch (columnName)
            {
                case "timestamp":
                    if (value is DateTimeOffset dto)
                        entry.Timestamp = dto;
                    else if (DateTime.TryParse(value?.ToString(), out var dt))
                        entry.Timestamp = new DateTimeOffset(dt, TimeSpan.Zero);
                    break;

                case "name":
                    entry.EventName = value?.ToString() ?? string.Empty;
                    var category = ActivityLogEventMap.GetCategory(entry.EventName);
                    entry.Category = category?.ToString() ?? "Unknown";
                    break;

                case "customDimensions":
                    ParseCustomDimensions(entry, value);
                    break;

                default:
                    break;
            }
        }

        return entry;
    }

    private static void ParseCustomDimensions(ActivityLogEntry entry, object? value)
    {
        if (value is null)
            return;

        Dictionary<string, string>? dimensions = null;

        if (value is IDictionary<string, object> dictObj)
        {
            dimensions = dictObj.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? string.Empty);
        }
        else
        {
            var json = value.ToString();
            if (!string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    dimensions = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                }
                catch (System.Text.Json.JsonException)
                {
                    // Dimension parsing failed — leave properties empty
                }
            }
        }

        if (dimensions is null)
            return;

        // Extract known fields to top-level properties
        if (dimensions.TryGetValue("LoggedInAdminId", out var userId))
            entry.UserId = userId;
        if (dimensions.TryGetValue("LoggedInUsername", out var username))
            entry.Username = username;
        if (dimensions.TryGetValue("Controller", out var controller))
            entry.Controller = controller;
        if (dimensions.TryGetValue("Action", out var action))
            entry.Action = action;

        // Remaining properties go into the Properties bag
        foreach (var kvp in dimensions)
        {
            if (!knownPropertyKeys.Contains(kvp.Key))
            {
                entry.Properties[kvp.Key] = kvp.Value;
            }
        }
    }

    private static string FormatTimeSpan(TimeSpan timeSpan)
    {
        if (timeSpan.TotalDays >= 1)
            return $"{(int)timeSpan.TotalDays}d";
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours}h";
        return $"{(int)timeSpan.TotalMinutes}m";
    }

    private static string EscapeKql(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
