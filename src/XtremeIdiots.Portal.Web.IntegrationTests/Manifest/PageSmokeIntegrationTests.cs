using System.Net;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Manifest;

public class PageSmokeIntegrationTests
{
    private readonly static string[] deterministicPageRoutes =
    [
        "/AdminActions/Global",
        "/Analytics",
        "/Analytics/Game",
        "/Analytics/Global",
        "/Analytics/Player",
        "/User",
        "/User/ActivityLog",
        "/User/Permissions",
        "/User/PermissionsReport",
    ];

    [Fact]
    public async Task DeterministicAdminPages_RenderSuccessfully()
    {
        await using var host = await PortalWebTestHost.CreateAsync();
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        List<string> failures = [];

        foreach (var route in deterministicPageRoutes)
        {
            using var response = await host.Client.GetAsync(route);
            var finalPath = response.RequestMessage?.RequestUri?.AbsolutePath;

            if (response.StatusCode != HttpStatusCode.OK ||
                response.Content.Headers.ContentType?.MediaType != "text/html" ||
                !string.Equals(finalPath, route, StringComparison.OrdinalIgnoreCase))
            {
                failures.Add($"{route}: status {(int)response.StatusCode}, content {response.Content.Headers.ContentType}, final path {finalPath}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }
}
