using Microsoft.AspNetCore.Mvc.Rendering;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class RulesCatalogueEditorViewModel
{
    public required string EditorId { get; init; }
    public required string FieldName { get; init; }
    public required string FeatureId { get; init; }
    public string RulesJson { get; init; } = "[]";
    public IReadOnlyList<SelectListItem> TagOptions { get; init; } = [];
    public bool IsOverride { get; init; }
    public bool IsServerContext { get; init; }
    public bool ShowInheritedRules { get; init; }
    public string GlobalRulesJson { get; init; } = "[]";
    // Omitted IDs are unknown; a caller must provide actual compatibility evidence.
    public IReadOnlyDictionary<string, bool> Compatibility { get; init; } = new Dictionary<string, bool>();
}
