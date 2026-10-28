using System.Net;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed class SharedMapCatalogueHttpTests
{
    [Theory]
    [InlineData("/Maps", false)]
    [InlineData("/Maps/GameIndex/CallOfDuty4x", false)]
    [InlineData("/Maps/VoteLog", true)]
    public async Task SharedCatalogueFilters_RenderOneCod4Family(string route, bool authenticated)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, route);
        if (authenticated)
        {
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        }

        using var response = await host.Client.SendAsync(request);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("COD4 / COD4x", html, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"CallOfDuty4x\"", html, StringComparison.Ordinal);
        if (route.Contains("/GameIndex/", StringComparison.Ordinal))
        {
            Assert.Contains("data-source=\"/Maps/GetMapListAjax/CallOfDuty4\"", html, StringComparison.Ordinal);
        }
    }
}
