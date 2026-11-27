using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MX.Api.Client;
using MX.Api.Client.Auth;
using RestSharp;
using System.Net;
using System.Text.Json;
using XtremeIdiots.Portal.Repository.Api.Client.V1;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.Analytics;

[Trait("Category", "HttpIntegration")]
public sealed class AnalyticsProxyHttpTests
{
    [Theory]
    [InlineData("player/overview", true)]
    [InlineData("player/overview", false)]
    [InlineData("player/timeseries", true)]
    [InlineData("player/timeseries", false)]
    [InlineData("global/timeseries", true)]
    [InlineData("global/timeseries", false)]
    [InlineData("game/timeseries", true)]
    [InlineData("game/timeseries", false)]
    [InlineData("game/overview", true)]
    [InlineData("game/overview", false)]
    public async Task AnalyticsProxyPreservesSupportedAndMissingMetrics(string endpoint, bool supported)
    {
        using var transport = new AnalyticsTransport(_ => Response(endpoint, supported));
        var repository = CreateRepository(transport);
        await using var host = await PortalWebTestHost.CreateAsync(services => ConfigureServices(services, repository));
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);

        using var response = await host.Client.GetAsync(
            $"/api/Analytics/{endpoint}?gameType=CallOfDuty4&from=2026-11-01T00%3A00%3A00Z&to=2026-11-04T00%3A00%3A00Z&bucket=OneDay");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEmpty(transport.Resource);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var json = document.RootElement;
        if (endpoint == "player/overview")
        {
            AssertOptionalNumber(json, "observedPlayerRecords", supported, 12);
            AssertOptionalNumber(json, "recordedPlayers", supported, 0);
            AssertOptionalNumber(json, "sessionStarts", supported, 2);
            AssertCoverage(json, supported);
        }
        else if (endpoint == "player/timeseries")
        {
            AssertCoverage(json, supported);
            var points = json.GetProperty("points");
            AssertOptionalNumber(points[0], "sessionStarts", supported, 0);
            AssertOptionalNumber(points[1], "sessionStarts", supported, 2);
            Assert.Equal(points[0].GetProperty("bucketStartUtc").GetDateTime(), json.GetProperty("series")[0].GetProperty("values")[0].GetProperty("bucketStartUtc").GetDateTime());
        }
        else if (endpoint.EndsWith("/timeseries", StringComparison.Ordinal))
        {
            var points = json.GetProperty("points");
            AssertOptionalNumber(points[0], "statsSampleCount", supported, 0);
            AssertOptionalNumber(points[1], "statsSampleCount", supported, 3);
            Assert.Equal(JsonValueKind.Null, points[2].GetProperty("statsSampleCount").ValueKind);
            Assert.Equal(points[0].GetProperty("bucketStartUtc").GetDateTime(), json.GetProperty("series")[0].GetProperty("values")[0].GetProperty("bucketStartUtc").GetDateTime());
        }
        else
        {
            AssertOptionalNumber(json, "statsSampleCount", supported, 0);
        }
    }

    private static void AssertOptionalNumber(JsonElement json, string property, bool supported, int expected)
    {
        var value = json.GetProperty(property);
        if (supported)
            Assert.Equal(expected, value.GetInt32());
        else
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    private static void AssertCoverage(JsonElement json, bool supported)
    {
        var value = json.GetProperty("trackingCoverage");
        if (supported)
            Assert.Equal("unknown", value.GetString());
        else
            Assert.Equal(JsonValueKind.Null, value.ValueKind);
    }

    internal static string Response(string endpoint, bool supported)
    {
        if (endpoint == "player/overview")
            return supported
                ? """{"activePlayers":4,"observedPlayerRecords":12,"recordedPlayers":0,"sessionStarts":2,"trackingCoverage":"unknown"}"""
                : """{"activePlayers":4}""";
        if (endpoint == "game/overview")
            return supported ? """{"avgPlayers":0,"statsSampleCount":0}""" : """{"avgPlayers":0}""";

        var points = endpoint == "player/timeseries"
            ? supported
                ? """[{"bucketStartUtc":"2026-11-01T00:00:00Z","sessionStarts":0},{"bucketStartUtc":"2026-11-02T00:00:00Z","sessionStarts":2}]"""
                : """[{"bucketStartUtc":"2026-11-01T00:00:00Z"},{"bucketStartUtc":"2026-11-02T00:00:00Z"}]"""
            : supported
                ? """[{"bucketStartUtc":"2026-11-01T00:00:00Z","statsSampleCount":0},{"bucketStartUtc":"2026-11-02T00:00:00Z","statsSampleCount":3},{"bucketStartUtc":"2026-11-03T00:00:00Z","statsSampleCount":null}]"""
                : """[{"bucketStartUtc":"2026-11-01T00:00:00Z"},{"bucketStartUtc":"2026-11-02T00:00:00Z"},{"bucketStartUtc":"2026-11-03T00:00:00Z"}]""";
        var player = endpoint == "player/timeseries";
        var coverage = player && supported ? ""","trackingCoverage":"unknown" """ : "";
        var key = player ? "activePlayers" : "avgPlayers";
        var thirdLabel = player ? "" : ""","2026-11-03T00:00:00Z" """;
        var thirdValue = player ? "" : """,{"bucketStartUtc":"2026-11-03T00:00:00Z","value":2}""";
        return """{"bucket":"OneDay","labels":["2026-11-01T00:00:00Z","2026-11-02T00:00:00Z" """
            + thirdLabel + """],"series":[{"key":"""
            + JsonSerializer.Serialize(key) + ""","values":[{"bucketStartUtc":"2026-11-01T00:00:00Z","value":0},{"bucketStartUtc":"2026-11-02T00:00:00Z","value":0}"""
            + thirdValue + """]}],"points":"""
            + points + coverage + "}";
    }

    internal static Mock<IRepositoryApiClient> CreateRepository(AnalyticsTransport transport)
    {
        var repository = new Mock<IRepositoryApiClient> { DefaultValue = DefaultValue.Mock };
        var options = new RepositoryApiClientOptions { BaseUrl = "https://example.test", MaxRetryCount = 1 };
        var logger = NullLogger<BaseApi<RepositoryApiClientOptions>>.Instance;
        var tokens = new TestTokenProvider();
        Mock.Get(repository.Object.PlayerAnalyticsV2).Setup(api => api.V1).Returns(new PlayerAnalyticsV2Api(logger, tokens, transport, options));
        Mock.Get(repository.Object.GlobalAnalytics).Setup(api => api.V1).Returns(new GlobalAnalyticsApi(logger, tokens, transport, options));
        Mock.Get(repository.Object.GameAnalytics).Setup(api => api.V1).Returns(new GameAnalyticsApi(logger, tokens, transport, options));
        return repository;
    }

    internal static void ConfigureServices(IServiceCollection services, Mock<IRepositoryApiClient> repository)
    {
        services.RemoveAll<IRepositoryApiClient>();
        services.AddSingleton(repository.Object);
    }

    private sealed class TestTokenProvider : IApiTokenProvider
    {
        public Task<string> GetAccessTokenAsync(string audience, CancellationToken cancellationToken = default) => Task.FromResult("test-token");
    }

    internal sealed class AnalyticsTransport(Func<string, string> response) : IRestClientService
    {
        public string Resource { get; private set; } = string.Empty;

        public Task<RestResponse> ExecuteAsync(string baseUrl, RestRequest request, CancellationToken cancellationToken = default)
        {
            Resource = request.Resource;
            return Task.FromResult(new RestResponse { StatusCode = HttpStatusCode.OK, Content = """{"data":""" + response(request.Resource) + "}" });
        }

        public Task<RestResponse> ExecuteWithNamedOptionsAsync(string optionsName, RestRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
