using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Hosting;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Trait("Category", "HttpIntegration")]
public sealed class RulesAuditAccessTests
{
    [Fact]
    public async Task RulesAudit_RejectsAnonymousRequests()
    {
        var scenario = new FileTransportScenario();
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        using var response = await host.Client.GetAsync("/RulesAudit");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1).Verify(api =>
            api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.HeadAdmin)]
    [InlineData(TestPrincipalProfiles.GameAdmin)]
    [InlineData(TestPrincipalProfiles.Moderator)]
    public async Task RulesAudit_RejectsNonSeniorAdmins(string profile)
    {
        var scenario = new FileTransportScenario();
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/RulesAudit");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);

        using var response = await host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1).Verify(api =>
            api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(TestPrincipalProfiles.HeadAdmin)]
    [InlineData(TestPrincipalProfiles.GameAdmin)]
    [InlineData(TestPrincipalProfiles.Moderator)]
    public async Task ExecutionFlow_RejectsNonSeniorAdmins(string profile)
    {
        var scenario = new FileTransportScenario();
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        foreach (var query in new[] { "", "?fragment=true" })
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/RulesAudit/Execution/{Guid.NewGuid()}{query}");
            request.Headers.Add(TestAuthenticationDefaults.HeaderName, profile);
            using var response = await host.Client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1).Verify(api =>
            api.GetRuleExecutionFlow(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RulesAudit_AllowsSeniorAdminAndRetiresLegacyRoute()
    {
        var scenario = new FileTransportScenario();
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1)
            .Setup(api => api.SearchRuleExecutionAudit(It.IsAny<RuleExecutionAuditQueryDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<CollectionModel<RuleExecutionAuditDto>>(HttpStatusCode.OK,
                new ApiResponse<CollectionModel<RuleExecutionAuditDto>>(new CollectionModel<RuleExecutionAuditDto>([]))));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/RulesAudit");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var response = await host.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var legacy = new HttpRequestMessage(HttpMethod.Get, $"/WelcomeExecutions?id={scenario.GameServerId}");
        legacy.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var legacyResponse = await host.Client.SendAsync(legacy);
        Assert.Equal(HttpStatusCode.Redirect, legacyResponse.StatusCode);
        Assert.Contains("/Errors/Display/404", legacyResponse.Headers.Location?.OriginalString);
    }
}
