using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.GameServers;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class RulesAuditViewModel
{
    public IReadOnlyList<GameServerDto> Servers { get; init; } = [];
    public Guid? ServerId { get; init; }
    public string? FeatureId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public RuleExecutionMatchStatus? MatchStatus { get; init; }
    public int Page { get; init; }
    public bool HasNext { get; init; }
    public IReadOnlyList<RuleExecutionAuditDto> Executions { get; init; } = [];
}

public sealed record RuleConditionFlowViewModel(
    RuleExecutionFlowConditionDto Condition,
    IReadOnlyList<RuleEvaluationNodeEvidence> Evidence,
    int[] Path);
