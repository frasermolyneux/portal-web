using System.Net;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

public sealed class UserManageProfileAccessTests : IAsyncLifetime
{
    private PortalWebTestHost host = null!;

    public async Task InitializeAsync()
    {
        host = await PortalWebTestHost.CreateAsync();
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Fact]
    public async Task ManageProfile_route_is_reachable_for_cod5_head_admin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/User/ManageProfile/11111111-1111-1111-1111-111111111111");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdminCod5);

        var response = await host.Client.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.False(IsLoginRedirect(response));
    }

    private static bool IsLoginRedirect(HttpResponseMessage response)
    {
        if (response.StatusCode is not (HttpStatusCode.Redirect or HttpStatusCode.Found or HttpStatusCode.MovedPermanently))
        {
            return false;
        }

        var location = response.Headers.Location?.ToString();
        return location is not null && location.Contains("Login", StringComparison.OrdinalIgnoreCase);
    }
}
