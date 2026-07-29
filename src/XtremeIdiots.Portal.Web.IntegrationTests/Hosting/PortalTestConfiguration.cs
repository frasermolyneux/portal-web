namespace XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

internal static class PortalTestConfiguration
{
    public static IReadOnlyDictionary<string, string?> Values { get; } = new Dictionary<string, string?>
    {
        ["AzureAppConfiguration:Endpoint"] = string.Empty,
        ["ApplicationInsights:ConnectionString"] = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://application-insights.test.invalid",
        ["ApplicationInsights:EnableProfiler"] = "false",
        ["ApplicationInsights:EnableClientTelemetry"] = "false",
        ["GeoLocationApi:ApiKey"] = "test-api-key",
        ["ExternalAssets:Enabled"] = "false",
        ["GeoLocationApi:ApplicationAudience"] = "api://geolocation-test",
        ["GeoLocationApi:BaseUrl"] = "https://geolocation.test.invalid",
        ["RepositoryApi:ApplicationAudience"] = "api://repository-test",
        ["RepositoryApi:BaseUrl"] = "https://repository.test.invalid",
        ["ServersIntegrationApi:ApplicationAudience"] = "api://servers-test",
        ["ServersIntegrationApi:BaseUrl"] = "https://servers.test.invalid",
        ["SyncApi:ApplicationAudience"] = string.Empty,
        ["SyncApi:BaseUrl"] = string.Empty,
        ["XtremeIdiots:Auth:ClientId"] = "test-client-id",
        ["XtremeIdiots:Auth:ClientSecret"] = "test-client-secret",
        ["XtremeIdiots:Forums:ApiKey"] = "test-api-key",
        ["XtremeIdiots:Forums:BaseUrl"] = "https://forums.test.invalid",
        ["sql_connection_string"] = "Data Source=unused-by-tests",
    };
}
