using Moq;
using System.Net;
using System.Text.RegularExpressions;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.MapRotations;

[Trait("Category", "HttpIntegration")]
public sealed partial class MapRotationRecoveryHttpTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Apply_RequiresAntiforgeryAndServerScopedAuthorization(bool permitted)
    {
        var scenario = new MapRotationRecoveryScenario(permitted: permitted);
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.MapRotationDeployer);
        using var rejected = await host.Client.PostAsync("/MapRotations/ApplyAssignment", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["assignmentId"] = scenario.AssignmentId.ToString(), ["mapRotationId"] = scenario.RotationId.ToString() }));
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        Assert.Equal("/Errors/Display/400", rejected.Headers.Location?.OriginalString);
        scenario.Sync.Verify(x => x.TriggerApply(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        using var page = await host.Client.GetAsync($"/MapRotations/AssignmentStatus/{scenario.AssignmentId}");
        var html = await page.Content.ReadAsStringAsync();
        var token = AntiforgeryToken().Match(html).Groups[1].Value;
        Assert.NotEmpty(token);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/MapRotations/ApplyAssignment")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["assignmentId"] = scenario.AssignmentId.ToString(),
                ["mapRotationId"] = scenario.RotationId.ToString(),
                ["__RequestVerificationToken"] = token
            })
        };
        request.Headers.Add("Cookie", string.Join("; ", page.Headers.GetValues("Set-Cookie").Select(value => value.Split(';', 2)[0])));
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(permitted ? $"/MapRotations/AssignmentStatus/{scenario.AssignmentId}" : "/Errors/Display/401",
            response.Headers.Location?.OriginalString);
        scenario.Sync.Verify(x => x.TriggerApply(scenario.AssignmentId, It.IsAny<CancellationToken>()), permitted ? Times.Once : Times.Never);
    }

    [Fact]
    public async Task AssignmentReaderWithoutDeployPermission_CanReadLiveProgress()
    {
        var scenario = new MapRotationRecoveryScenario(busy: true);
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        host.Client.DefaultRequestHeaders.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.MapRotationReader);
        using var page = await host.Client.GetAsync($"/MapRotations/AssignmentStatus/{scenario.AssignmentId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        using var progress = await host.Client.GetAsync($"/MapRotations/GetSyncProgress?instanceId=maprot-apply-{scenario.AssignmentId}");
        Assert.Equal(HttpStatusCode.OK, progress.StatusCode);
        Assert.Contains("\"runtimeStatus\":\"Running\"", await progress.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        scenario.Sync.Verify(x => x.TriggerApply(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"")]
    private static partial Regex AntiforgeryToken();
}
