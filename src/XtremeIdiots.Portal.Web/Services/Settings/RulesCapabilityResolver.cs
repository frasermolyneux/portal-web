using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Repository.Abstractions.Models.V1.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;

namespace XtremeIdiots.Portal.Web.Services.Settings;

internal static class RulesCapabilityResolver
{
    public static IReadOnlyDictionary<string, bool> Resolve(
        GameType gameType, string featureId, RuleServerCapabilitiesDto? snapshot)
    {
        var catalogue = RulesEngineCatalogueV1.Current;
        var feature = catalogue.Features.Single(item => item.FeatureId == featureId);
        var fresh = snapshot is not null && snapshot.GameType == gameType && snapshot.HasFreshStatus
            && snapshot.ExpiresAtUtc > DateTimeOffset.UtcNow;
        bool? Requirements(IReadOnlyList<string> integration, IReadOnlyList<string> runtime)
        {
            if (integration.Count == 0 && runtime.Count == 0)
                return true;
            if (!fresh)
                return null;
            return integration.All(snapshot!.IntegrationCapabilities.Contains)
                && runtime.All(snapshot.RuntimeCapabilities.Contains);
        }

        bool? Choice(IReadOnlyList<int> games, IReadOnlyList<string> integration, IReadOnlyList<string> runtime)
            => games.Contains((int)gameType) ? Requirements(integration, runtime) : false;
        static bool? Any(IEnumerable<bool?> values)
        {
            var results = values.ToArray();
            return results.Contains(true) ? true : results.Contains(null) ? null : false;
        }

        var featureSupport = feature.GameTypes.Contains((int)gameType)
            ? Any(catalogue.Triggers.Where(item => feature.TriggerIds.Contains(item.TriggerId))
                .Select(item => Choice(item.GameTypes, item.RequiredIntegrationCapabilities, item.RequiredRuntimeCapabilities)))
            : false;
        var result = new Dictionary<string, bool>();
        void Set(string id, bool? support)
        {
            var effective = featureSupport == false || support == false ? false :
                featureSupport == true && support == true ? true : (bool?)null;
            if (effective.HasValue)
                result[id] = effective.Value;
        }

        foreach (var condition in catalogue.Conditions.Where(item => feature.ConditionIds.Contains(item.ConditionId)))
        {
            var support = Choice(condition.GameTypes, [], condition.RuntimeCapabilities);
            if (condition.GameCapabilities.Count > 0 && support == true)
                support = null;
            Set(condition.ConditionId, support);
        }

        foreach (var action in catalogue.Actions.Where(item => feature.ActionTypeIds.Contains(item.ActionTypeId)))
        {
            var support = Choice(action.GameTypes, action.RequiredIntegrationCapabilities, action.RequiredRuntimeCapabilities);
            if (support != false && action.ExecutionProviders.Count > 0)
            {
                var provider = Any(action.ExecutionProviders.Select(item =>
                    Choice(item.GameTypes, item.RequiredIntegrationCapabilities, item.RequiredRuntimeCapabilities)));
                support = support == true ? provider : provider == false ? false : null;
            }

            Set(action.ActionTypeId, support);
        }

        return result;
    }

    public static IReadOnlyList<string> FindNewUnsupported(
        FeatureRuleSetOverrideDocument previous,
        FeatureRuleSetOverrideDocument proposed,
        IReadOnlyDictionary<string, bool> compatibility)
    {
        static Dictionary<(bool Override, string Rule, string Kind, string Id, string Action), int> Declarations(
            FeatureRuleSetOverrideDocument document)
        {
            var counts = new Dictionary<(bool, string, string, string, string), int>();
            void Add((bool, string, string, string, string) key)
                => counts[key] = counts.GetValueOrDefault(key) + 1;
            void Visit(RuleConditionNode? node, bool isOverride, string rule)
            {
                if (node is null)
                    return;
                if (node.ConditionId is { Length: > 0 } id)
                    Add((isOverride, rule, "condition", id, ""));
                if (node.Conditions is not null)
                    foreach (var child in node.Conditions)
                        Visit(child, isOverride, rule);
            }

            void Actions(IEnumerable<RuleActionDefinition>? actions, bool isOverride, string rule)
            {
                if (actions is null)
                    return;
                foreach (var action in actions)
                    Add((isOverride, rule, "action", action.TypeId, action.Id));
            }

            foreach (var rule in document.Rules)
            {
                Visit(rule.Condition, false, rule.Id);
                Actions(rule.Actions, false, rule.Id);
            }

            foreach (var rule in document.RuleOverrides)
            {
                Visit(rule.Condition, true, rule.RuleId);
                Actions(rule.Actions, true, rule.RuleId);
            }

            return counts;
        }

        var saved = Declarations(previous);
        return Declarations(proposed)
            .Where(item => item.Value > saved.GetValueOrDefault(item.Key)
                && (!compatibility.TryGetValue(item.Key.Id, out var supported) || !supported))
            .Select(item => $"Cannot add {item.Key.Kind} '{item.Key.Id}': support for this server is unverified or unavailable.")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
