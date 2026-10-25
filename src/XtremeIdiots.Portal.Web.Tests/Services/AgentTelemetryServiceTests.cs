using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class AgentTelemetryServiceTests
{
    [Theory]
    [InlineData(null, AgentActivityStatus.Unknown)]
    [InlineData(2, AgentActivityStatus.Active)]
    [InlineData(6, AgentActivityStatus.Offline)]
    public async Task HealthUsesHeartbeatIndependentlyOfRecentGameActivity(int? heartbeatAgeMinutes, AgentActivityStatus expected)
    {
        var serverId = Guid.NewGuid();
        DateTimeOffset? heartbeat = heartbeatAgeMinutes.HasValue
            ? DateTimeOffset.UtcNow.AddMinutes(-heartbeatAgeMinutes.Value)
            : null;
        var columns = new[]
        {
            MonitorQueryModelFactory.LogsTableColumn("serverId", LogsColumnType.String),
            MonitorQueryModelFactory.LogsTableColumn("lastHeartbeat", LogsColumnType.Datetime),
            MonitorQueryModelFactory.LogsTableColumn("lastEvent", LogsColumnType.Datetime),
            MonitorQueryModelFactory.LogsTableColumn("eventCount", LogsColumnType.Long),
            MonitorQueryModelFactory.LogsTableColumn("totalEvents", LogsColumnType.Long),
            MonitorQueryModelFactory.LogsTableColumn("sourceMode", LogsColumnType.String)
        };
        DateTimeOffset? lastEvent = heartbeatAgeMinutes == 2 ? null : DateTimeOffset.UtcNow;
        var row = MonitorQueryModelFactory.LogsTableRow(columns,
            [serverId.ToString(), heartbeat, lastEvent, 0L, 0L, "Plugin"]);
        var summary = MonitorQueryModelFactory.LogsQueryResult(
            [MonitorQueryModelFactory.LogsTable("PrimaryResult", columns, [row])],
            BinaryData.FromString("null"), BinaryData.FromString("null"), BinaryData.FromString("null"));
        var empty = MonitorQueryModelFactory.LogsQueryResult(
            [MonitorQueryModelFactory.LogsTable("PrimaryResult", [], [])],
            BinaryData.FromString("null"), BinaryData.FromString("null"), BinaryData.FromString("null"));
        var client = new Mock<LogsQueryClient>();
        var queries = new List<string>();
        client.Setup(c => c.QueryResourceAsync(It.IsAny<ResourceIdentifier>(), It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(), It.IsAny<LogsQueryOptions>(), It.IsAny<CancellationToken>()))
            .Returns((ResourceIdentifier _, string query, QueryTimeRange _, LogsQueryOptions _, CancellationToken _) =>
            {
                queries.Add(query);
                return Task.FromResult(Response.FromValue(query.Contains("| top 1", StringComparison.Ordinal) ? empty : summary,
                    Mock.Of<Response>()));
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationInsights:ResourceId"] = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Insights/components/test"
        }).Build();
        var service = new AgentTelemetryService(client.Object, configuration, NullLogger<AgentTelemetryService>.Instance);

        var overview = Assert.Single(await service.GetAllServersStatusAsync());
        var detail = await service.GetServerStatusAsync(serverId);

        Assert.Equal(expected, overview.ActivityStatus);
        Assert.Equal(expected, detail.ActivityStatus);
        Assert.Equal(expected == AgentActivityStatus.Active, overview.IsAgentActive);
        Assert.Equal(expected == AgentActivityStatus.Active, detail.IsAgentActive);
        Assert.Equal(heartbeat?.UtcDateTime, overview.LastHeartbeatReceived);
        Assert.Equal(heartbeat?.UtcDateTime, detail.LastHeartbeatReceived);
        Assert.Equal(lastEvent?.UtcDateTime, detail.LastEventReceived);
        Assert.Equal(0, overview.EventsLastHour);
        Assert.Equal("Plugin", overview.SourceMode);
        Assert.All(queries.Where(q => q.Contains("summarize", StringComparison.Ordinal)), query =>
        {
            Assert.Contains("maxif(timestamp, name == 'AgentHeartbeat' and cloud_RoleName == 'Portal Server Agent')", query);
            Assert.Contains("countif(name != 'AgentHeartbeat' and timestamp > ago(1h))", query);
        });
    }
}
