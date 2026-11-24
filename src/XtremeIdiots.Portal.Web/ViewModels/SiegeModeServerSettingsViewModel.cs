using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.SiegeMode;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class SiegeModeServerSettingsViewModel : IValidatableObject
{
    public bool? Enabled { get; set; }
    public bool InheritGlobalRules { get; set; } = true;
    public int? DurationMinutesOverride { get; set; }
    public string LocalRulesJson { get; set; } = "[]";
    public string RuleOverridesJson { get; set; } = "[]";

    [BindNever]
    public Dictionary<string, JsonElement> EnvelopeExtensionData { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!RuleSettingsJson.TryDeserializeRules(LocalRulesJson, out var rules, out var rulesError))
        {
            yield return new ValidationResult(rulesError, [nameof(LocalRulesJson)]);
            yield break;
        }

        if (!RuleSettingsJson.TryDeserializeOverrides(RuleOverridesJson, out var overrides, out var overridesError))
        {
            yield return new ValidationResult(overridesError, [nameof(RuleOverridesJson)]);
            yield break;
        }

        var validation = new SiegeModeSettingsValidator().ValidateServerOverride(new SiegeModeServerSettingsOverrideDocument
        {
            EnabledOverride = RuleSettingsJson.ToOverride(Enabled),
            InheritGlobalRules = InheritGlobalRules,
            DurationMinutesOverride = DurationMinutesOverride,
            Rules = rules,
            RuleOverrides = overrides
        });
        foreach (var error in validation.Errors)
            yield return new ValidationResult(error, [nameof(LocalRulesJson)]);
    }
}

internal static class SiegeModeServerSettingsMapper
{
    public static void Populate(SiegeModeServerSettingsViewModel model, SiegeModeServerSettingsOverrideDocument document)
    {
        model.Enabled = RuleSettingsJson.FromOverride(document.EnabledOverride);
        model.InheritGlobalRules = document.InheritGlobalRules;
        model.DurationMinutesOverride = document.DurationMinutesOverride;
        model.LocalRulesJson = JsonSerializer.Serialize(document.Rules, RuleSettingsJson.Options);
        model.RuleOverridesJson = JsonSerializer.Serialize(document.RuleOverrides, RuleSettingsJson.Options);
        model.EnvelopeExtensionData = RuleSettingsJson.CloneExtensionData(document.ExtensionData);
    }

    public static SiegeModeServerSettingsOverrideDocument ToDocument(SiegeModeServerSettingsViewModel model) => new()
    {
        EnabledOverride = RuleSettingsJson.ToOverride(model.Enabled),
        InheritGlobalRules = model.InheritGlobalRules,
        DurationMinutesOverride = model.DurationMinutesOverride,
        Rules = JsonSerializer.Deserialize<List<RuleDefinition>>(model.LocalRulesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Rules must be an array."),
        RuleOverrides = JsonSerializer.Deserialize<List<RuleDefinitionOverride>>(model.RuleOverridesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Overrides must be an array."),
        ExtensionData = RuleSettingsJson.CloneExtensionData(model.EnvelopeExtensionData)
    };

    public static bool HasOverrides(SiegeModeServerSettingsViewModel model)
    {
        var document = ToDocument(model);
        return document.EnabledOverride != FeatureEnablementOverride.Inherit ||
            !document.InheritGlobalRules || document.DurationMinutesOverride.HasValue ||
            document.Rules.Count > 0 || document.RuleOverrides.Count > 0 ||
            document.ExtensionData is { Count: > 0 };
    }
}
