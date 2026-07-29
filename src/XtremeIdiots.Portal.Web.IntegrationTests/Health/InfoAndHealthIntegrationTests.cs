using System.Net;
using System.Net.Http.Json;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Health;

public class InfoAndHealthIntegrationTests
{
    [Fact]
    public async Task Live_ReturnsOk()
    {
        await using var host = await PortalWebTestHost.CreateAsync();

        using var response = await host.Client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ReturnsOk()
    {
        await using var host = await PortalWebTestHost.CreateAsync();

        using var response = await host.Client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Info_ReturnsVersionPayload()
    {
        await using var host = await PortalWebTestHost.CreateAsync();

        using var response = await host.Client.GetAsync("/info");
        var payload = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(payload);
        Assert.Contains("buildVersion", payload.Keys);
    }
}
