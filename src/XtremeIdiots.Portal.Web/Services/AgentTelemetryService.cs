using System.Text;

using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;

namespace XtremeIdiots.Portal.Web.Services;

public class AgentTelemetryService(
    LogsQueryClient logsQueryClient,
    IConfiguration configuration,
    ILogger<AgentTelemetryService> logger) : IAgentTelemetryService
{
    private const int AgentActiveThresholdMinutes = 5;

    public async Task<AgentServerStatus> GetServerStatusAsync(Guid serverId, CancellationToken ct = default)
    {
        var resourceId = GetAppInsightsResourceId();
        var serverIdStr = serverId.ToString();

        var summaryQuery = new StringBuilder();
        summaryQuery.Append("customEvents");
        summaryQuery.Append(" | where timestamp > ago(24h)");
        summaryQuery.Append($" | where tostring(customDimensions.ServerId) == '{EscapeKql(serverIdStr)}'");
        summaryQuery.Append(" | summarize lastEvent=max(timestamp),");
        summaryQuery.Append(" totalEvents=countif(timestamp > ago(1h)),");
        summaryQuery.Append(" playerConnects=countif(name == 'PlayerConnected' and timestamp > ago(1h)),");
        summaryQuery.Append(" chatMessages=countif(name == 'ChatMessagePersisted' and timestamp > ago(1h)),");
        summaryQuery.Append(" bansDetected=countif(name == 'BanImported'),");
        summaryQuery.Append(" moderationTriggers=countif(name == 'ChatModerationTriggered')");

        var mapQuery = new StringBuilder();
        mapQuery.Append("customEvents");
        mapQuery.Append(" | where timestamp > ago(24h)");
        mapQuery.Append($" | where tostring(customDimensions.ServerId) == '{EscapeKql(serverIdStr)}'");
        mapQuery.Append(" | where name == 'MapChange'");
        mapQuery.Append(" | top 1 by timestamp desc");
        mapQuery.Append(" | project timestamp, mapName=tostring(customDimensions.MapName)");

        var summaryTask = ExecuteQueryAsync(resourceId, summaryQuery.ToString(), TimeSpan.FromHours(24), ct);
        var mapTask = ExecuteQueryAsync(resourceId, mapQuery.ToString(), TimeSpan.FromHours(24), ct);

        await Task.WhenAll(summaryTask, mapTask).ConfigureAwait(false);

        var summaryResponse = await summaryTask.ConfigureAwait(false);
        var mapResponse = await mapTask.ConfigureAwait(false);

        var status = new AgentServerStatus();

        if (summaryResponse.Table.Rows.Count > 0)
        {
            var row = summaryResponse.Table.Rows[0];
            var columns = summaryResponse.Table.Columns;

            var lastEvent = GetDateTimeValue(row, columns, "lastEvent");
            var totalEvents = GetIntValue(row, columns, "totalEvents");
            var playerConnects = GetIntValue(row, columns, "playerConnects");
            var chatMessages = GetIntValue(row, columns, "chatMessages");
            var bansDetected = GetIntValue(row, columns, "bansDetected");
            var moderationTriggers = GetIntValue(row, columns, "moderationTriggers");

            var isActive = lastEvent.HasValue &&
                           (DateTime.UtcNow - lastEvent.Value).TotalMinutes <= AgentActiveThresholdMinutes;

            status = status with
            {
                LastEventReceived = lastEvent,
                EventsLastHour = totalEvents,
                PlayersConnectedLastHour = playerConnects,
                ChatMessagesLastHour = chatMessages,
                BansDetectedLast24h = bansDetected,
                ModerationTriggersLast24h = moderationTriggers,
                IsAgentActive = isActive
            };
        }

        if (mapResponse.Table.Rows.Count > 0)
        {
            var row = mapResponse.Table.Rows[0];
            var columns = mapResponse.Table.Columns;

            status = status with
            {
                LastMapChange = GetDateTimeValue(row, columns, "timestamp"),
                LastMapName = GetStringValue(row, columns, "mapName")
            };
        }

        return status;
    }

    public async Task<IReadOnlyList<AgentServerSummary>> GetAllServersStatusAsync(CancellationToken ct = default)
    {
        var resourceId = GetAppInsightsResourceId();

        var query = new StringBuilder();
        query.Append("customEvents");
        query.Append(" | where timestamp > ago(1h)");
        query.Append(" | extend serverId = tostring(customDimensions.ServerId)");
        query.Append(" | where isnotempty(serverId)");
        query.Append(" | summarize lastEvent=max(timestamp), eventCount=count(),");
        query.Append(" playerConnects=countif(name == 'PlayerConnected'),");
        query.Append(" lastMap=take_any(tostring(customDimensions.MapName))");
        query.Append(" by serverId");
        query.Append(" | order by lastEvent desc");

        var response = await ExecuteQueryAsync(resourceId, query.ToString(), TimeSpan.FromHours(1), ct).ConfigureAwait(false);

        var results = new List<AgentServerSummary>();

        foreach (var row in response.Table.Rows)
        {
            var columns = response.Table.Columns;
            var serverIdStr = GetStringValue(row, columns, "serverId");

            if (!Guid.TryParse(serverIdStr, out var serverId))
                continue;

            var lastEvent = GetDateTimeValue(row, columns, "lastEvent");
            var isActive = lastEvent.HasValue &&
                           (DateTime.UtcNow - lastEvent.Value).TotalMinutes <= AgentActiveThresholdMinutes;

            results.Add(new AgentServerSummary
            {
                ServerId = serverId,
                LastEventReceived = lastEvent,
                EventsLastHour = GetIntValue(row, columns, "eventCount"),
                PlayerCount = GetIntValue(row, columns, "playerConnects"),
                CurrentMap = GetStringValue(row, columns, "lastMap"),
                IsAgentActive = isActive
            });
        }

        return results;
    }

    private async Task<LogsQueryResult> ExecuteQueryAsync(
        string resourceId, string query, TimeSpan timeRange, CancellationToken ct)
    {
        logger.LogDebug("Executing KQL query against App Insights: {Query}", query);

        var response = await logsQueryClient.QueryResourceAsync(
            new ResourceIdentifier(resourceId),
            query,
            new QueryTimeRange(timeRange),
            cancellationToken: ct).ConfigureAwait(false);

        return response.Value;
    }

    private string GetAppInsightsResourceId()
    {
        var resourceId = configuration["ApplicationInsights:ResourceId"];

        if (string.IsNullOrWhiteSpace(resourceId))
        {
            logger.LogError("ApplicationInsights:ResourceId is not configured");
            throw new InvalidOperationException(
                "ApplicationInsights:ResourceId must be configured with the full Azure resource ID");
        }

        return resourceId;
    }

    private static DateTime? GetDateTimeValue(LogsTableRow row, IReadOnlyList<LogsTableColumn> columns, string columnName)
    {
        var index = FindColumnIndex(columns, columnName);
        if (index < 0) return null;

        var value = row[index];
        if (value is DateTimeOffset dto) return dto.UtcDateTime;
        if (value is DateTime dt) return dt;
        if (DateTime.TryParse(value?.ToString(), out var parsed)) return parsed;
        return null;
    }

    private static int GetIntValue(LogsTableRow row, IReadOnlyList<LogsTableColumn> columns, string columnName)
    {
        var index = FindColumnIndex(columns, columnName);
        if (index < 0) return 0;

        var value = row[index];
        if (value is int i) return i;
        if (value is long l) return (int)l;
        if (int.TryParse(value?.ToString(), out var parsed)) return parsed;
        return 0;
    }

    private static string? GetStringValue(LogsTableRow row, IReadOnlyList<LogsTableColumn> columns, string columnName)
    {
        var index = FindColumnIndex(columns, columnName);
        if (index < 0) return null;
        return row[index]?.ToString();
    }

    private static int FindColumnIndex(IReadOnlyList<LogsTableColumn> columns, string columnName)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].Name.Equals(columnName, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static string EscapeKql(string value)
    {
        return value.Replace("\\", "\\\\").Replace("'", "\\'");
    }
}
