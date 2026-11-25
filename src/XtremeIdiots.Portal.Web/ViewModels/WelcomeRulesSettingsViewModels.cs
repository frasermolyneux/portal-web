using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class WelcomeRulesGlobalSettingsViewModel : IValidatableObject
{
    public bool Enabled { get; set; }
    public int ConnectionDelaySeconds { get; set; } = 5;
    public int FreshnessSeconds { get; set; } = 120;
    public string RulesJson { get; set; } = "[]";

    [BindNever]
    public Dictionary<string, JsonElement> EnvelopeExtensionData { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!RuleSettingsJson.TryDeserializeRules(RulesJson, out _, out var error))
        {
            yield return new ValidationResult(error, [nameof(RulesJson)]);
            yield break;
        }

        var validation = new WelcomeRulesSettingsValidator().Validate(WelcomeRulesSettingsMapper.ToDocument(this));
        foreach (var item in validation.Errors)
            yield return new ValidationResult(item, [nameof(RulesJson)]);
    }
}

public sealed class WelcomeRulesServerSettingsViewModel : IValidatableObject
{
    public bool? Enabled { get; set; }
    public bool InheritGlobalRules { get; set; } = true;
    public int? ConnectionDelaySeconds { get; set; }
    public int? FreshnessSeconds { get; set; }
    public string LocalRulesJson { get; set; } = "[]";
    public string RuleOverridesJson { get; set; } = "[]";

    [BindNever]
    public Dictionary<string, JsonElement> EnvelopeExtensionData { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!RuleSettingsJson.TryDeserializeRules(LocalRulesJson, out _, out var rulesError))
        {
            yield return new ValidationResult(rulesError, [nameof(LocalRulesJson)]);
            yield break;
        }

        if (!RuleSettingsJson.TryDeserializeOverrides(RuleOverridesJson, out _, out var overridesError))
        {
            yield return new ValidationResult(overridesError, [nameof(RuleOverridesJson)]);
            yield break;
        }

        var validation = new WelcomeRulesSettingsValidator().ValidateServerOverride(WelcomeRulesSettingsMapper.ToDocument(this));
        foreach (var item in validation.Errors)
            yield return new ValidationResult(item, [nameof(LocalRulesJson)]);
    }
}

internal static class WelcomeRulesSettingsMapper
{
    public static void PopulateGlobal(WelcomeRulesGlobalSettingsViewModel model, WelcomeRulesSettingsDocument document)
    {
        var timing = document.Timing ?? new RuleTimingSettings();
        model.Enabled = document.Enabled == true;
        model.ConnectionDelaySeconds = timing.ConnectionDelaySeconds;
        model.FreshnessSeconds = timing.FreshnessSeconds;
        model.RulesJson = JsonSerializer.Serialize(document.Rules, RuleSettingsJson.Options);
        model.EnvelopeExtensionData = RuleSettingsJson.CloneExtensionData(document.ExtensionData);
    }

    public static void PopulateServer(WelcomeRulesServerSettingsViewModel model, WelcomeRulesServerSettingsDocument document)
    {
        model.Enabled = RuleSettingsJson.FromOverride(document.EnabledOverride);
        model.InheritGlobalRules = document.InheritGlobalRules;
        model.ConnectionDelaySeconds = document.Timing?.ConnectionDelaySeconds;
        model.FreshnessSeconds = document.Timing?.FreshnessSeconds;
        model.LocalRulesJson = JsonSerializer.Serialize(document.Rules, RuleSettingsJson.Options);
        model.RuleOverridesJson = JsonSerializer.Serialize(document.RuleOverrides, RuleSettingsJson.Options);
        model.EnvelopeExtensionData = RuleSettingsJson.CloneExtensionData(document.ExtensionData);
    }

    public static WelcomeRulesSettingsDocument ToDocument(WelcomeRulesGlobalSettingsViewModel model) => new()
    {
        Enabled = model.Enabled,
        Timing = new RuleTimingSettings
        {
            ConnectionDelaySeconds = model.ConnectionDelaySeconds,
            FreshnessSeconds = model.FreshnessSeconds
        },
        Rules = JsonSerializer.Deserialize<List<RuleDefinition>>(model.RulesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Rules must be an array."),
        ExtensionData = RuleSettingsJson.CloneExtensionData(model.EnvelopeExtensionData)
    };

    public static WelcomeRulesServerSettingsDocument ToDocument(WelcomeRulesServerSettingsViewModel model) => new()
    {
        EnabledOverride = RuleSettingsJson.ToOverride(model.Enabled),
        InheritGlobalRules = model.InheritGlobalRules,
        Timing = model.ConnectionDelaySeconds.HasValue || model.FreshnessSeconds.HasValue
            ? new RuleTimingOverrides { ConnectionDelaySeconds = model.ConnectionDelaySeconds, FreshnessSeconds = model.FreshnessSeconds }
            : null,
        Rules = JsonSerializer.Deserialize<List<RuleDefinition>>(model.LocalRulesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Rules must be an array."),
        RuleOverrides = JsonSerializer.Deserialize<List<RuleDefinitionOverride>>(model.RuleOverridesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Overrides must be an array."),
        ExtensionData = RuleSettingsJson.CloneExtensionData(model.EnvelopeExtensionData)
    };

    public static bool HasServerOverrides(WelcomeRulesServerSettingsViewModel model)
    {
        var document = ToDocument(model);
        return document.EnabledOverride != FeatureEnablementOverride.Inherit ||
            !document.InheritGlobalRules || document.Timing is not null ||
            document.Rules.Count > 0 || document.RuleOverrides.Count > 0 ||
            document.ExtensionData is { Count: > 0 };
    }

    public static bool TryGetEffectiveTimings(
        WelcomeRulesGlobalSettingsViewModel global,
        WelcomeRulesServerSettingsViewModel? server,
        out IReadOnlyDictionary<string, RuleTimingSettings> timings)
    {
        timings = new Dictionary<string, RuleTimingSettings>();
        if (!RuleSettingsJson.TryDeserializeRules(global.RulesJson, out _, out _)
            || (server is not null &&
                (!RuleSettingsJson.TryDeserializeRules(server.LocalRulesJson, out _, out _)
                 || !RuleSettingsJson.TryDeserializeOverrides(server.RuleOverridesJson, out _, out _))))
            return false;

        try
        {
            var effective = FeatureRuleSetMerger.Merge(ToDocument(global), server is null ? null : ToDocument(server));
            var resolved = new Dictionary<string, RuleTimingSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in effective.Rules)
            {
                if (!resolved.TryAdd(rule.Id, effective.Timing.Apply(rule.Timing)))
                    return false;
            }

            timings = resolved;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
