using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.RulesEngine;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class VpnProtectionGlobalSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled")]
    public bool Enabled { get; set; }

    public string RulesJson { get; set; } = "[]";

    [BindNever]
    public Dictionary<string, JsonElement> EnvelopeExtensionData { get; set; } = [];

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!RuleSettingsJson.TryDeserializeRules(RulesJson, out var rules, out var error))
        {
            yield return new ValidationResult(error, [nameof(RulesJson)]);
            yield break;
        }

        var validation = new VpnProtectionSettingsValidator().Validate(new VpnProtectionSettingsDocument
        {
            Enabled = Enabled,
            Rules = rules
        });
        foreach (var item in validation.Errors)
            yield return new ValidationResult(item, [nameof(RulesJson)]);
    }
}

public sealed class VpnProtectionServerSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled Override")]
    public bool? Enabled { get; set; }
    public bool InheritGlobalRules { get; set; } = true;
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

        var validation = new VpnProtectionSettingsValidator().ValidateServerOverride(new VpnProtectionServerSettingsDocument
        {
            EnabledOverride = RuleSettingsJson.ToOverride(Enabled),
            InheritGlobalRules = InheritGlobalRules,
            Rules = rules,
            RuleOverrides = overrides
        });
        foreach (var item in validation.Errors)
            yield return new ValidationResult(item, [nameof(LocalRulesJson)]);
    }
}

internal static class RuleSettingsJson
{
    internal readonly static JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static bool TryDeserializeRules(string? json, out List<RuleDefinition> rules, out string? error)
        => TryDeserialize(json, out rules, out error);

    public static bool TryDeserializeOverrides(string? json, out List<RuleDefinitionOverride> overrides, out string? error)
        => TryDeserialize(json, out overrides, out error);

    private static bool TryDeserialize<T>(string? json, out List<T> result, out string? error)
    {
        result = [];
        error = null;
        try
        {
            var parsed = JsonSerializer.Deserialize<List<T>>(json ?? string.Empty, Options);
            if (parsed is null)
            {
                error = "Rules must be an array.";
                return false;
            }

            result = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Invalid rules: {ex.Message}";
            return false;
        }
    }

    public static FeatureEnablementOverride ToOverride(bool? enabled) => enabled switch
    {
        true => FeatureEnablementOverride.Enabled,
        false => FeatureEnablementOverride.Disabled,
        null => FeatureEnablementOverride.Inherit
    };

    public static bool? FromOverride(FeatureEnablementOverride enabled) => enabled switch
    {
        FeatureEnablementOverride.Enabled => true,
        FeatureEnablementOverride.Disabled => false,
        FeatureEnablementOverride.Inherit => null,
        _ => throw new ArgumentOutOfRangeException(nameof(enabled))
    };

    public static Dictionary<string, JsonElement> CloneExtensionData(
        IReadOnlyDictionary<string, JsonElement>? extensionData)
        => extensionData?.ToDictionary(
            static entry => entry.Key,
            static entry => entry.Value.Clone(),
            StringComparer.Ordinal) ?? [];
}

internal static class VpnProtectionSettingsJsonMapper
{
    public static void PopulateGlobal(VpnProtectionGlobalSettingsViewModel model, VpnProtectionSettingsDocument document)
    {
        model.Enabled = document.Enabled == true;
        model.RulesJson = JsonSerializer.Serialize(document.Rules, RuleSettingsJson.Options);
        model.EnvelopeExtensionData = RuleSettingsJson.CloneExtensionData(document.ExtensionData);
    }

    public static void PopulateServer(VpnProtectionServerSettingsViewModel model, VpnProtectionServerSettingsDocument document)
    {
        model.Enabled = RuleSettingsJson.FromOverride(document.EnabledOverride);
        model.InheritGlobalRules = document.InheritGlobalRules;
        model.LocalRulesJson = JsonSerializer.Serialize(document.Rules, RuleSettingsJson.Options);
        model.RuleOverridesJson = JsonSerializer.Serialize(document.RuleOverrides, RuleSettingsJson.Options);
        model.EnvelopeExtensionData = RuleSettingsJson.CloneExtensionData(document.ExtensionData);
    }

    public static VpnProtectionSettingsDocument ToDocument(VpnProtectionGlobalSettingsViewModel model) => new()
    {
        Enabled = model.Enabled,
        Rules = JsonSerializer.Deserialize<List<RuleDefinition>>(model.RulesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Rules must be an array."),
        ExtensionData = RuleSettingsJson.CloneExtensionData(model.EnvelopeExtensionData)
    };

    public static VpnProtectionServerSettingsDocument ToDocument(VpnProtectionServerSettingsViewModel model) => new()
    {
        EnabledOverride = RuleSettingsJson.ToOverride(model.Enabled),
        InheritGlobalRules = model.InheritGlobalRules,
        Rules = JsonSerializer.Deserialize<List<RuleDefinition>>(model.LocalRulesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Rules must be an array."),
        RuleOverrides = JsonSerializer.Deserialize<List<RuleDefinitionOverride>>(model.RuleOverridesJson, RuleSettingsJson.Options)
            ?? throw new JsonException("Overrides must be an array."),
        ExtensionData = RuleSettingsJson.CloneExtensionData(model.EnvelopeExtensionData)
    };

    public static string BuildGlobalConfigurationJson(VpnProtectionGlobalSettingsViewModel model)
        => JsonSerializer.Serialize(ToDocument(model), RuleSettingsJson.Options);

    public static string BuildServerConfigurationJson(VpnProtectionServerSettingsViewModel model)
        => JsonSerializer.Serialize(ToDocument(model), RuleSettingsJson.Options);

    public static bool HasServerOverrides(VpnProtectionServerSettingsViewModel model)
    {
        var document = ToDocument(model);
        return document.EnabledOverride != FeatureEnablementOverride.Inherit ||
            !document.InheritGlobalRules || document.Rules.Count > 0 || document.RuleOverrides.Count > 0 ||
            document.ExtensionData is { Count: > 0 };
    }
}
