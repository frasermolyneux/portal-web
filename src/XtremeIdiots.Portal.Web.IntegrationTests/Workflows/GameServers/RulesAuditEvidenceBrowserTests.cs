using Microsoft.Playwright;
using Moq;
using MX.Api.Abstractions;
using System.Net;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Web.IntegrationTests.Authentication;
using XtremeIdiots.Portal.Web.IntegrationTests.Playwright;

namespace XtremeIdiots.Portal.Web.IntegrationTests.Workflows.GameServers;

[Trait("Category", "Browser")]
public sealed class RulesAuditEvidenceBrowserTests
{
    [Fact]
    public async Task ExecutionFlow_ShowsOnlyCorrelatedEvidenceAndRecordedOutcomes()
    {
        var scenario = new FileTransportScenario();
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var flow = new RuleExecutionFlowDto
        {
            Execution = new RuleExecutionAuditDto
            {
                ExecutionId = id,
                EvaluationEvidence = new RuleEvaluationEvidence
                {
                    Nodes =
                    [
                        new() { RuleId = "rule", Kind = RuleEvidenceNodeKind.Rule, Status = RuleEvaluationNodeStatus.DidNotMatch, ObservedAtUtc = now },
                        new() { RuleId = "rule", Kind = RuleEvidenceNodeKind.Condition, ConditionPath = [0], ConditionId = "player.tags", Status = RuleEvaluationNodeStatus.DidNotMatch, ObservedAtUtc = now },
                        new() { RuleId = "other", Kind = RuleEvidenceNodeKind.Condition, ConditionPath = [1], ConditionId = "player.age", Status = RuleEvaluationNodeStatus.Matched, ObservedAtUtc = now },
                        new() { RuleId = "rule", Kind = RuleEvidenceNodeKind.Condition, ConditionPath = [1], ConditionId = "different", Status = RuleEvaluationNodeStatus.Matched, ObservedAtUtc = now }
                    ],
                    ActionDecisions =
                    [
                        new() { RuleId = "rule", ActionId = "not-run", Status = RuleActionDecisionStatus.NotRun, ObservedAtUtc = now },
                        new() { RuleId = "rule", ActionId = "uncertain", ActionIndex = 1, Status = RuleActionDecisionStatus.NotRun, ObservedAtUtc = now },
                        new() { RuleId = "other", ActionId = "unrecorded", ActionIndex = 2, Status = RuleActionDecisionStatus.NotRun, ObservedAtUtc = now },
                        new() { RuleId = "rule", ActionId = "different", ActionIndex = 2, Status = RuleActionDecisionStatus.NotRun, ObservedAtUtc = now }
                    ]
                },
                Actions =
                [
                    new() { RuleId = "rule", ActionId = "uncertain", ActionIndex = 1, Outcome = RuleActionExecutionOutcome.OutcomeUnknown, Mode = RuleExecutionMode.Enforce }
                ]
            },
            StructureStatus = RuleExecutionStructureStatus.Available,
            Rules =
            [
                new()
                {
                    Id = "rule",
                    Condition = new RuleExecutionFlowConditionDto
                    {
                        GroupOperator = "And",
                        Conditions = [new() { ConditionId = "player.tags" }, new() { ConditionId = "player.age" }]
                    },
                    Actions =
                    [
                        new() { Id = "not-run", ActionIndex = 0 },
                        new() { Id = "uncertain", ActionIndex = 1 },
                        new() { Id = "unrecorded", ActionIndex = 2 }
                    ]
                }
            ]
        };
        Mock.Get(scenario.RepositoryClient.Object.RuleExecutions.V1)
            .Setup(api => api.GetRuleExecutionFlow(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApiResult<RuleExecutionFlowDto>(HttpStatusCode.OK, new ApiResponse<RuleExecutionFlowDto>(flow)));
        await using var browser = await BrowserFixture.CreateAsync(TestPrincipalProfiles.SeniorAdmin, scenario.ConfigureServices);
        await browser.Page.GotoAsync(new Uri(browser.Host.BaseAddress, $"/RulesAudit/Execution/{id}").AbsoluteUri);
        var rule = browser.Page.Locator("[data-rule-id='rule']");

        await Assertions.Expect(rule.Locator("[data-condition-path='0']")).ToContainTextAsync("Did not match");
        await Assertions.Expect(rule.Locator("[data-condition-path='1'] .badge")).ToHaveCountAsync(0);
        await Assertions.Expect(rule.Locator("[data-action-id='not-run']")).ToContainTextAsync("Not run (recorded)");
        await Assertions.Expect(rule.Locator("[data-action-id='uncertain']")).ToContainTextAsync("Outcome unknown - may have run");
        await Assertions.Expect(rule.Locator("[data-action-id='uncertain']")).Not.ToContainTextAsync("Not run");
        await Assertions.Expect(rule.Locator("[data-action-id='unrecorded']")).ToContainTextAsync("No action outcome recorded");
        browser.AssertNoBrowserErrors();
    }
}
