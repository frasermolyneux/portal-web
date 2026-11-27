using Azure;
using Azure.Core;
using Azure.Monitor.Query;
using Azure.Monitor.Query.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using XtremeIdiots.Portal.Web.Services;

namespace XtremeIdiots.Portal.Web.Tests.Services;

public sealed class ActivityLogServiceTests
{
    [Theory]
    [InlineData(null, null, 2)]
    [InlineData("123'\\45", "map", 3)]
    [InlineData("12345", null, 3)]
    public async Task QueryEvents_FiltersCountsAndPageByExactActor(string? actorId, string? search, int expectedQueries)
    {
        var columns = new[] { MonitorQueryModelFactory.LogsTableColumn("Count", LogsColumnType.Long) };
        var count = MonitorQueryModelFactory.LogsQueryResult(
            [MonitorQueryModelFactory.LogsTable("PrimaryResult", columns, [MonitorQueryModelFactory.LogsTableRow(columns, [12L])])],
            BinaryData.FromString("null"), BinaryData.FromString("null"), BinaryData.FromString("null"));
        var page = MonitorQueryModelFactory.LogsQueryResult(
            [MonitorQueryModelFactory.LogsTable("PrimaryResult", [], [])],
            BinaryData.FromString("null"), BinaryData.FromString("null"), BinaryData.FromString("null"));
        var client = new Mock<LogsQueryClient>();
        var queries = new List<string>();
        client.Setup(c => c.QueryResourceAsync(It.IsAny<ResourceIdentifier>(), It.IsAny<string>(),
                It.IsAny<QueryTimeRange>(), It.IsAny<LogsQueryOptions>(), It.IsAny<CancellationToken>()))
            .Returns((ResourceIdentifier _, string query, QueryTimeRange _, LogsQueryOptions _, CancellationToken _) =>
            {
                queries.Add(query);
                return Task.FromResult(Response.FromValue(query.EndsWith("| count", StringComparison.Ordinal) ? count : page, Mock.Of<Response>()));
            });
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ApplicationInsights:ResourceId"] = "/subscriptions/test/resourceGroups/test/providers/Microsoft.Insights/components/test"
        }).Build();
        var service = new ActivityLogService(client.Object, configuration, NullLogger<ActivityLogService>.Instance);

        var result = await service.QueryEventsAsync(TimeSpan.FromHours(24), [], [], search, 10, 5, "timestamp", "desc", actorId);

        Assert.Equal(12, result.TotalCount);
        Assert.Equal(12, result.FilteredCount);
        Assert.Equal(expectedQueries, queries.Count);
        Assert.DoesNotContain("Audit.ActorId", queries[0]);
        var dataQuery = queries[^1];
        Assert.Contains("_row > 10 and _row <= 15", dataQuery);
        if (actorId is not null)
        {
            var escaped = actorId.Replace("\\", "\\\\").Replace("'", "\\'");
            var predicate = $"tostring(customDimensions.[\"Audit.ActorId\"]) == '{escaped}'";
            Assert.Contains(predicate, queries[1]);
            Assert.Contains(predicate, dataQuery);
            Assert.True(dataQuery.IndexOf(predicate, StringComparison.Ordinal) < dataQuery.IndexOf("row_number()", StringComparison.Ordinal));
        }

        if (search is not null)
        {
            Assert.Contains("name contains 'map'", queries[1]);
            Assert.Contains("name contains 'map'", dataQuery);
        }
    }
}
