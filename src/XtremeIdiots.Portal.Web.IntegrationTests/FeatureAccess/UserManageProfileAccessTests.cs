using System.Net;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.FeatureAccess;

public sealed class UserManageProfileAccessTests : IAsyncLifetime
{
    private PortalWebTestHost host = null!;
    private UserManageProfileScenario scenario = null!;

    public async Task InitializeAsync()
    {
        scenario = new UserManageProfileScenario();
        host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
    }

    public async Task DisposeAsync()
    {
        await host.DisposeAsync();
    }

    [Fact]
    public async Task ManageProfile_route_is_reachable_for_cod5_head_admin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageProfile/{scenario.UserProfileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.HeadAdminCod5);

        var response = await host.Client.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Manage User Profile - Route Test User", content, StringComparison.Ordinal);
        Assert.Contains("route-test@example.invalid", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ManageProfile_route_remains_forbidden_for_game_admin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/User/ManageProfile/{scenario.UserProfileId}");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.GameAdmin);

        var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
