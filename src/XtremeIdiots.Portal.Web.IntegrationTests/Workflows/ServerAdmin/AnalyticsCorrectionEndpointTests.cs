using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Abstractions;
using MX.Api.Client;
using MX.Api.Client.Auth;
using Newtonsoft.Json.Linq;
using RestSharp;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RecentPlayers;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.ServerAdmin;

[Trait("Category", "HttpIntegration")]
public sealed class AnalyticsCorrectionEndpointTests
{
    private const string RecordedResponse = /*lang=json,strict*/ """
        {"bucket":"OneDay","composition":null,"recordedActivityCoverage":"unknown","recordedActivity":[
          {"bucketStartUtc":"2026-11-01T00:00:00Z","bucketEndUtc":"2026-11-02T00:00:00Z",
           "recordedPlayers":7,"sessionStarts":4,"observedSeconds":9000.5}]}
        """;

    [Theory]
    [InlineData("", true)]
    [InlineData("&includeComposition=true", true)]
    [InlineData("&includeComposition=false", false)]
    public async Task DashboardProxyForwardsCompositionOptionAndRecordedActivity(string query, bool includeComposition)
    {
        var scenario = new MapControlScenario();
        using var rest = new CapturingAnalyticsTransport(RecordedResponse);
        Mock.Get(scenario.RepositoryClient.Object.DashboardAnalytics).Setup(api => api.V1).Returns(CreateDashboardApi(rest));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync(
            "/api/Analytics/dashboard/home?from=2026-11-01T00%3A00%3A00Z&to=2026-11-02T00%3A00%3A00Z&bucket=OneDay&top=6" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JTokenType.Null, json["composition"]!.Type);
        AssertRecordedActivity(json);
        Assert.Equal("v1/analytics/dashboard/home", rest.Resource);
        Assert.Equal(includeComposition ? "true" : "false", rest.Query["includeComposition"]);
        Assert.Equal("6", rest.Query["top"]);
    }

    [Fact]
    public async Task ServerTimeseriesProxyPreservesRecordedActivity()
    {
        var scenario = new MapControlScenario();
        using var rest = new CapturingAnalyticsTransport(RecordedResponse);
        Mock.Get(scenario.RepositoryClient.Object.ServerAnalytics).Setup(api => api.V1).Returns(CreateServerApi(rest));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync(
            $"/api/Analytics/server/timeseries?id={scenario.GameServerId}&from=2026-11-01T00%3A00%3A00Z&to=2026-11-02T00%3A00%3A00Z&bucket=OneDay&timezone=UTC");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertRecordedActivity(JObject.Parse(await response.Content.ReadAsStringAsync()));
        Assert.Equal($"v1/analytics/servers/{scenario.GameServerId}/timeseries", rest.Resource);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnalyticsProxyPreservesMissingActivityFromOlderHost(bool dashboard)
    {
        var scenario = new MapControlScenario();
        using var rest = new CapturingAnalyticsTransport(/*lang=json,strict*/ """{"bucket":"OneDay"}""");
        if (dashboard)
        {
            Mock.Get(scenario.RepositoryClient.Object.DashboardAnalytics).Setup(api => api.V1).Returns(CreateDashboardApi(rest));
        }
        else
        {
            Mock.Get(scenario.RepositoryClient.Object.ServerAnalytics).Setup(api => api.V1).Returns(CreateServerApi(rest));
        }

        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        var endpoint = dashboard ? "/api/Analytics/dashboard/home?" : $"/api/Analytics/server/timeseries?id={scenario.GameServerId}&";

        using var response = await host.Client.GetAsync(endpoint + "from=2026-11-01T00%3A00%3A00Z&to=2026-11-02T00%3A00%3A00Z&bucket=OneDay");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JTokenType.Null, json["recordedActivity"]!.Type);
        Assert.Equal(JTokenType.Null, json["recordedActivityCoverage"]!.Type);
    }

    private static void AssertRecordedActivity(JObject json)
    {
        Assert.Equal("unknown", json.Value<string>("recordedActivityCoverage"));
        var bucket = Assert.IsType<JObject>(Assert.Single(Assert.IsType<JArray>(json["recordedActivity"])));
        Assert.Equal(7, bucket.Value<int>("recordedPlayers"));
        Assert.Equal(4, bucket.Value<int>("sessionStarts"));
        Assert.Equal(9000.5m, bucket.Value<decimal>("observedSeconds"));
        Assert.Equal(DateTime.Parse("2026-11-01T00:00:00Z").ToUniversalTime(), bucket.Value<DateTime>("bucketStartUtc").ToUniversalTime());
        Assert.Equal(DateTime.Parse("2026-11-02T00:00:00Z").ToUniversalTime(), bucket.Value<DateTime>("bucketEndUtc").ToUniversalTime());
    }

    private static DashboardAnalyticsApi CreateDashboardApi(IRestClientService rest) => new(NullLogger<BaseApi<RepositoryApiClientOptions>>.Instance, new TestTokenProvider(), rest, CreateClientOptions());

    private static ServerAnalyticsApi CreateServerApi(IRestClientService rest) => new(NullLogger<BaseApi<RepositoryApiClientOptions>>.Instance, new TestTokenProvider(), rest, CreateClientOptions());

    private static RepositoryApiClientOptions CreateClientOptions() => new() { BaseUrl = "https://example.test", MaxRetryCount = 1 };

    private sealed class TestTokenProvider : IApiTokenProvider
    {
        public Task<string> GetAccessTokenAsync(string audience, CancellationToken cancellationToken = default) => Task.FromResult("test-token");
    }

    private sealed class CapturingAnalyticsTransport(string data) : IRestClientService
    {
        public string Resource { get; private set; } = string.Empty;
        public Dictionary<string, string> Query { get; private set; } = [];

        public Task<RestResponse> ExecuteAsync(string baseUrl, RestRequest request, CancellationToken cancellationToken = default)
        {
            Resource = request.Resource;
            Query = request.Parameters.Where(parameter => parameter.Type == ParameterType.QueryString)
                .ToDictionary(parameter => parameter.Name ?? string.Empty, parameter => parameter.Value?.ToString() ?? string.Empty);
            return Task.FromResult(new RestResponse { StatusCode = HttpStatusCode.OK, Content = """{"data":""" + data + "}" });
        }

        public Task<RestResponse> ExecuteWithNamedOptionsAsync(string optionsName, RestRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData("/api/Analytics/dashboard/home")]
    [InlineData("/api/Analytics/server/timeseries")]
    public async Task RecordedAnalyticsEndpointsRequireDashboardRead(string path)
    {
        var scenario = new MapControlScenario();
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var anonymous = await host.Client.GetAsync(path);
        Assert.True(anonymous.StatusCode is HttpStatusCode.Redirect or HttpStatusCode.Unauthorized);
        using var restrictedRequest = new HttpRequestMessage(HttpMethod.Get, path);
        restrictedRequest.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.GameServerWriterWithoutRcon);
        using var restricted = await host.Client.SendAsync(restrictedRequest);
        Assert.Equal(HttpStatusCode.Forbidden, restricted.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicMapDoesNotDescribeUnavailableObservationsAsEmptyOrOnline(bool unavailable)
    {
        var scenario = new MapControlScenario();
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServers(
                null, null, GameServerFilter.ServerListEnabled, 0, 200, GameServerOrder.ServerListPosition, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<GameServerDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<GameServerDto>>(new CollectionModel<GameServerDto>([]))));
        scenario.RepositoryClient.Setup(r => r.RecentPlayers.V1.GetRecentPlayers(
                null, null, It.IsAny<DateTime?>(), RecentPlayersFilter.GeoLocated, 0, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(unavailable
                ? new ApiResult<CollectionModel<RecentPlayerDto>>(HttpStatusCode.ServiceUnavailable)
                : new ApiResult<CollectionModel<RecentPlayerDto>>(HttpStatusCode.OK,
                    new ApiResponse<CollectionModel<RecentPlayerDto>>(new CollectionModel<RecentPlayerDto>([]))));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var response = await host.Client.GetAsync("/Servers/Map");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Players Online", html);
        Assert.Contains("Not live players", html);
        Assert.Contains("up to 200 records before public-server filtering", html);
        if (unavailable)
        {
            Assert.Contains("currently unavailable", html);
            Assert.DoesNotContain("0 location observations shown", html);
        }
        else
            Assert.Contains("0 location observations shown", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ServerMapFailuresAreNotSuccessfulEmptySnapshots(bool metadataUnavailable)
    {
        var scenario = new MapControlScenario();
        var server = JObject.FromObject(scenario.GameServer);
        server[nameof(GameServerDto.ServerListEnabled)] = true;
        scenario.RepositoryClient.Setup(r => r.GameServers.V1.GetGameServer(scenario.GameServerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(metadataUnavailable
                ? new ApiResult<GameServerDto>(HttpStatusCode.ServiceUnavailable)
                : new ApiResult<GameServerDto>(HttpStatusCode.OK, new ApiResponse<GameServerDto>(server.ToObject<GameServerDto>())));
        scenario.RepositoryClient.Setup(r => r.RecentPlayers.V1.GetRecentPlayers(
                null, scenario.GameServerId, It.IsAny<DateTime?>(), RecentPlayersFilter.GeoLocated, 0, 200, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RecentPlayerDto>>(HttpStatusCode.ServiceUnavailable));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);

        using var response = await host.Client.GetAsync($"/Servers/GetRecentPlayersForMap/{scenario.GameServerId}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var json = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotNull(json["error"]);
        Assert.Null(json["players"]);
    }
}
