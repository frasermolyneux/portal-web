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

    [Fact]
    public async Task ExecutionFlow_ShowsRecordedEvaluationAndActionDecisions()
    {
        var now = DateTimeOffset.UtcNow;
        var evidence = new RuleEvaluationEvidence
        {
            Nodes =
            [
                new() { RuleId = "rule", RuleIndex = 0, Kind = RuleEvidenceNodeKind.Rule, Status = RuleEvaluationNodeStatus.DidNotMatch, ObservedAtUtc = now },
                new() { RuleId = "rule", RuleIndex = 0, Kind = RuleEvidenceNodeKind.Condition, ConditionPath = [0], ConditionId = "player.tags", Status = RuleEvaluationNodeStatus.DidNotMatch, ReasonCode = "tags.missing", ObservedAtUtc = now },
                new() { RuleId = "rule", RuleIndex = 0, Kind = RuleEvidenceNodeKind.Check, ConditionPath = [0], CheckId = "reputation", Status = RuleEvaluationNodeStatus.Unknown, Source = "provider", SourceVersion = "1", ObservedAtUtc = now }
            ],
            ActionDecisions =
            [
                new() { RuleId = "rule", RuleIndex = 0, ActionId = "send", ActionIndex = 0, Status = RuleActionDecisionStatus.NotRun, ReasonCode = "condition.false", ObservedAtUtc = now },
                new() { RuleId = "rule", RuleIndex = 0, ActionId = "next", ActionIndex = 1, Status = RuleActionDecisionStatus.Skipped, ObservedAtUtc = now }
            ],
            OmittedNodeCount = 1,
            OmittedActionDecisionCount = 2
        };

        var (status, html, _) = await RenderFlowAsync(CreateFlow(evidence));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Condition badges show recorded results", html);
        Assert.Contains("Did not match", html);
        Assert.Contains("Reason: tags.missing", html);
        Assert.Contains("Check: reputation", html);
        Assert.Contains("Source: provider", html);
        Assert.Contains("Source version: 1", html);
        Assert.Contains("Not run (recorded)", html);
        Assert.Contains("Skipped (recorded)", html);
        Assert.Contains("No action outcome recorded", html);
        Assert.Contains("1 additional evaluation results omitted", html);
        Assert.Contains("2 additional action decisions omitted", html);
    }

    [Fact]
    public async Task ExecutionFlow_ActionOutcomeSupersedesPriorNotRunDecision()
    {
        var evidence = new RuleEvaluationEvidence
        {
            ActionDecisions =
            [
                new() { RuleId = "rule", RuleIndex = 0, ActionId = "send", ActionIndex = 0, Status = RuleActionDecisionStatus.NotRun, ObservedAtUtc = DateTimeOffset.UtcNow }
            ]
        };
        var flow = CreateFlow(evidence);
        flow = flow with
        {
            Execution = flow.Execution with
            {
                Actions =
                [
                    new() { RuleId = "rule", RuleIndex = 0, ActionId = "send", ActionIndex = 0, Mode = RuleExecutionMode.Enforce, Outcome = RuleActionExecutionOutcome.OutcomeUnknown }
                ]
            }
        };

        var (status, html, _) = await RenderFlowAsync(flow);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Outcome unknown - may have run", html);
        Assert.Contains("Earlier decisions were superseded by recorded action outcomes", html);
        Assert.DoesNotContain("Not run (recorded)", html);
    }

    [Fact]
    public async Task ExecutionFlow_MissingStructureExplainsUnknownActionOutcome()
    {
        var flow = CreateFlow(null);
        flow = flow with
        {
            StructureStatus = RuleExecutionStructureStatus.Missing,
            Rules = [],
            Execution = flow.Execution with
            {
                Actions =
                [
                    new() { RuleId = "rule", ActionId = "send", Outcome = RuleActionExecutionOutcome.OutcomeUnknown }
                ]
            }
        };

        var (status, html, _) = await RenderFlowAsync(flow);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Historical rule structure was not recorded", html);
        Assert.Contains("Outcome unknown; the action may have run.", html);
        Assert.DoesNotContain("Not run (recorded)", html);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExecutionFlow_MissingOrUnsupportedEvidenceDoesNotInventActionOutcomes(bool unsupported)
    {
        var evidence = unsupported
            ? new RuleEvaluationEvidence
            {
                SchemaVersion = 999,
                ActionDecisions =
                [
                    new() { RuleId = "rule", ActionId = "send", Status = RuleActionDecisionStatus.NotRun, ObservedAtUtc = DateTimeOffset.UtcNow }
                ]
            }
            : null;
        var flow = CreateFlow(evidence);
        flow = flow with
        {
            Execution = flow.Execution with
            {
                MatchStatus = RuleExecutionMatchStatus.NoMatch,
                Diagnostics = [new() { Code = RuleExecutionDiagnosticCode.NoMatch, RuleId = "rule", ObservedAtUtc = DateTimeOffset.UtcNow }]
            }
        };

        var (status, html, _) = await RenderFlowAsync(flow);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("No match recorded", html);
        Assert.Contains("No action outcome recorded", html);
        Assert.DoesNotContain("Not run (recorded)", html);
        if (unsupported)
            Assert.Contains("evidence schema version 999 is not supported", html);
    }

    [Fact]
    public async Task ExecutionFlow_RejectsMalformedSupportedEvidence()
    {
        var evidence = new RuleEvaluationEvidence { Nodes = null! };

        var (status, _, location) = await RenderFlowAsync(CreateFlow(evidence));

        Assert.Equal(HttpStatusCode.Redirect, status);
        Assert.Contains("/Errors/Display/503", location);
    }

    [Fact]
    public async Task ExecutionFlow_UnknownDecisionIsNotPresentedAsNotRunOrDispatchUncertainty()
    {
        var evidence = new RuleEvaluationEvidence
        {
            ActionDecisions =
            [
                new() { RuleId = "rule", ActionId = "send", Status = RuleActionDecisionStatus.Unrecognized, ObservedAtUtc = DateTimeOffset.UtcNow }
            ]
        };

        var (status, html, _) = await RenderFlowAsync(CreateFlow(evidence));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Contains("Unrecognized recorded decision", html);
        Assert.DoesNotContain("Not run (recorded)", html);
        Assert.DoesNotContain("Outcome unknown - may have run", html);
    }

    private static RuleExecutionFlowDto CreateFlow(RuleEvaluationEvidence? evidence) => new()
    {
        Execution = new RuleExecutionAuditDto { ExecutionId = Guid.NewGuid(), EvaluationEvidence = evidence },
        StructureStatus = RuleExecutionStructureStatus.Available,
        Rules =
        [
            new()
            {
                Id = "rule",
                Condition = new RuleExecutionFlowConditionDto
                {
                    GroupOperator = "And",
                    Conditions = [new() { ConditionId = "player.tags" }]
                },
                Actions =
                [
                    new() { Id = "send", ActionIndex = 0 },
                    new() { Id = "next", ActionIndex = 1 },
                    new() { Id = "unrecorded", ActionIndex = 2 }
                ]
            }
        ]
    };

    private async static Task<(HttpStatusCode Status, string Html, string? Location)> RenderFlowAsync(RuleExecutionFlowDto flow)
    {
        var scenario = new FileTransportScenario();
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1)
            .Setup(api => api.GetRuleExecutionFlow(flow.Execution.ExecutionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK, new ApiResponse<RuleExecutionFlowDto>(flow)));
        await using var host = await PortalWebTestHost.CreateAsync(scenario.ConfigureServices);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/RulesAudit/Execution/{flow.Execution.ExecutionId}?fragment=true");
        request.Headers.Add(TestAuthenticationDefaults.HeaderName, TestPrincipalProfiles.SeniorAdmin);
        using var response = await host.Client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.Location?.OriginalString);
    }
}
