namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class ServerFeatureRulesViewModel
{
    public required string Prefix { get; init; }
    public required string FeatureId { get; init; }
    public required string Title { get; init; }
    public bool? Enabled { get; init; }
    public bool GlobalEnabled { get; init; }
    public bool InheritGlobalRules { get; init; }
    public string LocalRulesJson { get; init; } = "[]";
    public string RuleOverridesJson { get; init; } = "[]";
    public string GlobalRulesJson { get; init; } = "[]";
    public int? DurationMinutesOverride { get; init; }
    public int? GlobalDurationMinutes { get; init; }
    public IReadOnlyList<RequiredTagOptionViewModel> Tags { get; init; } = [];
    public IReadOnlyDictionary<string, bool> Compatibility { get; init; } = new Dictionary<string, bool>();
    public bool HasFreshCapabilitySnapshot { get; init; }
}
