using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.SiegeMode;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class SiegeModeStatusViewModel
{
    public required Guid ServerId { get; init; }
    public SiegeSessionDto? Session { get; init; }
    public IReadOnlyList<RuleActionExecutionDto> Actions { get; init; } = [];
    public IReadOnlyList<SiegeCheckDto> Checks { get; init; } = [];
}
