using System.Net;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

[Trait("Category", "HttpIntegration")]
public sealed class ActivityLogAccessTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData(TestPrincipalProfiles.GameAdmin, HttpStatusCode.Forbidden)]
    [InlineData(TestPrincipalProfiles.SeniorAdmin, HttpStatusCode.OK)]
    public async Task UserLookup_RequiresActivityLogAccess(string? profile, HttpStatusCode expected)
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/User/GetActivityLogUsers?term=a");
        if (profile is not null)
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(expected, response.StatusCode);
    }
}
