using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.VpnProtection;

namespace XtremeIdiots.Portal.Web.ViewModels;

public sealed class VpnProtectionRuleViewModel
{
    [DisplayName("Rule Id")]
    [Required]
    [MaxLength(VpnProtectionSettingsConstants.MaxRuleIdLength)]
    public string Id { get; set; } = string.Empty;

    [DisplayName("Enabled")]
    public bool Enabled { get; set; } = true;

    public RuleConditionViewModel Condition { get; set; } = new();

    [DisplayName("Action")]
    public VpnProtectionAction Action { get; set; }

    [DisplayName("Post observations to forums")]
    public bool PostObservationToForums { get; set; } = true;

    [DisplayName("Reason Template")]
    [MaxLength(VpnProtectionSettingsConstants.MaxReasonTemplateLength)]
    public string? ReasonTemplate { get; set; }
}

public sealed class VpnProtectionRuleOverrideViewModel
{
    [DisplayName("Global Rule Id")]
    [Required]
    [MaxLength(VpnProtectionSettingsConstants.MaxRuleIdLength)]
    public string Id { get; set; } = string.Empty;

    [DisplayName("Enabled Override")]
    public bool? Enabled { get; set; }

    [DisplayName("Replace global condition")]
    public bool UseConditionOverride { get; set; }

    public RuleConditionViewModel Condition { get; set; } = new();

    [DisplayName("Action Override")]
    public VpnProtectionAction? Action { get; set; }

    [DisplayName("Post observations to forums override")]
    public bool? PostObservationToForums { get; set; }

    [DisplayName("Reason Template Override")]
    [MaxLength(VpnProtectionSettingsConstants.MaxReasonTemplateLength)]
    public string? ReasonTemplate { get; set; }
}

public sealed class VpnRuleEditorRowViewModel
{
    public string Prefix { get; init; } = string.Empty;
    public VpnProtectionRuleViewModel? Rule { get; init; }
    public VpnProtectionRuleOverrideViewModel? Override { get; init; }
    public IReadOnlyList<VpnProtectionRuleViewModel> GlobalRules { get; init; } = [];
    public bool SupportsDestructiveActions { get; init; } = true;
    public bool IsTemplate { get; init; }
}

public sealed class VpnRulesEditorViewModel
{
    public required string Id { get; init; }
    public required string Prefix { get; init; }
    public required string Title { get; init; }
    public required string AddLabel { get; init; }
    public bool IsOverride { get; init; }
    public bool SupportsDestructiveActions { get; init; } = true;
    public IReadOnlyList<VpnProtectionRuleViewModel> Rules { get; init; } = [];
    public IReadOnlyList<VpnProtectionRuleOverrideViewModel> Overrides { get; init; } = [];
    public IReadOnlyList<VpnProtectionRuleViewModel> GlobalRules { get; init; } = [];
}

public sealed class VpnProtectionGlobalSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled")]
    public bool Enabled { get; set; }

    public List<VpnProtectionRuleViewModel> Rules { get; set; } = [];

    [DisplayName("Excluded Player Tags")]
    public string? ExcludedPlayerTagsCsv { get; set; }

    public IReadOnlyList<string> AllowedExcludedPlayerTags { get; set; } = [];

    public bool ExcludedPlayerTagsCatalogAvailable { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var rule in Rules ?? [])
        {
            if (VpnProtectionSettingsJsonMapper.HasPlayerCondition(rule.Condition))
            {
                yield return new ValidationResult("VPN rules cannot use player conditions.", [nameof(Rules)]);
            }
        }

        foreach (var result in VpnProtectionSettingsViewModelValidation.Validate(
            VpnProtectionSettingsJsonMapper.ToDocument(this),
            ExcludedPlayerTagsCsv,
            AllowedExcludedPlayerTags,
            ExcludedPlayerTagsCatalogAvailable,
            nameof(Rules)))
        {
            yield return result;
        }
    }
}

public sealed class VpnProtectionServerSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled Override")]
    public TriStateOverrideValue EnabledOverride { get; set; } = TriStateOverrideValue.Inherit();

    [DisplayName("Enabled Override")]
    public bool? Enabled {
        get => EnabledOverride?.Value;
        set => EnabledOverride = TriStateOverrideValue.From(value);
    }

    [DisplayName("Inherit Global Rules")]
    public bool InheritGlobalRules { get; set; } = true;

    public List<VpnProtectionRuleViewModel> LocalRules { get; set; } = [];

    public List<VpnProtectionRuleOverrideViewModel> RuleOverrides { get; set; } = [];

    [DisplayName("Additional Excluded Player Tags")]
    public string? ExcludedPlayerTagsCsv { get; set; }

    public IReadOnlyList<string> AllowedExcludedPlayerTags { get; set; } = [];

    public bool ExcludedPlayerTagsCatalogAvailable { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var rule in LocalRules ?? [])
        {
            if (VpnProtectionSettingsJsonMapper.HasPlayerCondition(rule.Condition))
            {
                yield return new ValidationResult("VPN rules cannot use player conditions.", [nameof(LocalRules)]);
            }
        }

        foreach (var rule in RuleOverrides ?? [])
        {
            if (rule.UseConditionOverride && VpnProtectionSettingsJsonMapper.HasPlayerCondition(rule.Condition))
            {
                yield return new ValidationResult("VPN rules cannot use player conditions.", [nameof(RuleOverrides)]);
            }
        }

        foreach (var result in VpnProtectionSettingsViewModelValidation.Validate(
            VpnProtectionSettingsJsonMapper.ToDocument(this),
            ExcludedPlayerTagsCsv,
            AllowedExcludedPlayerTags,
            ExcludedPlayerTagsCatalogAvailable,
            nameof(LocalRules)))
        {
            yield return result;
        }
    }
}

internal static class VpnProtectionSettingsViewModelValidation
{
    public static IEnumerable<ValidationResult> Validate(
        VpnProtectionSettingsDocument document,
        string? excludedTagsCsv,
        IReadOnlyList<string> allowedTags,
        bool tagCatalogAvailable,
        string rulesMemberName)
    {
        var validation = new VpnProtectionSettingsValidator().Validate(document);
        foreach (var error in validation.Errors)
        {
            yield return new ValidationResult(error, [rulesMemberName]);
        }

        if (!tagCatalogAvailable)
        {
            yield break;
        }

        foreach (var validationResult in ValidateExcludedTags(excludedTagsCsv, allowedTags))
        {
            yield return validationResult;
        }
    }

    public static IEnumerable<ValidationResult> ValidateExcludedTags(
        string? excludedTagsCsv,
        IReadOnlyList<string> allowedTags)
    {
        var allowedTagSet = allowedTags.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in RequiredTagsSelection.SplitCsv(excludedTagsCsv))
        {
            if (!allowedTagSet.Contains(tag))
            {
                yield return new ValidationResult(
                    $"Excluded player tag '{tag}' is not available.",
                    [nameof(VpnProtectionGlobalSettingsViewModel.ExcludedPlayerTagsCsv)]);
            }
        }
    }
}

internal static class VpnProtectionSettingsJsonMapper
{
    private readonly static JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void PopulateGlobal(
        VpnProtectionGlobalSettingsViewModel model,
        VpnProtectionSettingsDocument document)
    {
        model.Enabled = document.Enabled ?? false;
        model.Rules = [.. document.Rules.Select(ToViewModel)];
        model.ExcludedPlayerTagsCsv = string.Join(", ", document.ExcludedPlayerTags);
    }

    public static void PopulateServer(
        VpnProtectionServerSettingsViewModel model,
        VpnProtectionSettingsDocument document)
    {
        model.Enabled = document.Enabled;
        model.InheritGlobalRules = document.InheritGlobalRules ?? true;
        model.LocalRules = [.. document.Rules.Select(ToViewModel)];
        model.RuleOverrides = [.. document.RuleOverrides.Select(ToViewModel)];
        model.ExcludedPlayerTagsCsv = string.Join(", ", document.ExcludedPlayerTags);
    }

    public static string BuildGlobalConfigurationJson(VpnProtectionGlobalSettingsViewModel model)
    {
        return JsonSerializer.Serialize(ToDocument(model), jsonOptions);
    }

    public static string BuildServerConfigurationJson(VpnProtectionServerSettingsViewModel model)
    {
        return JsonSerializer.Serialize(ToDocument(model), jsonOptions);
    }

    public static VpnProtectionSettingsDocument ToDocument(VpnProtectionGlobalSettingsViewModel model)
    {
        return new()
        {
            Enabled = model.Enabled,
            Rules = [.. (model.Rules ?? []).Select(ToContract)],
            ExcludedPlayerTags = RequiredTagsSelection.SplitCsv(model.ExcludedPlayerTagsCsv)
        };
    }

    public static VpnProtectionSettingsDocument ToDocument(VpnProtectionServerSettingsViewModel model)
    {
        return new()
        {
            Enabled = model.Enabled,
            InheritGlobalRules = model.InheritGlobalRules,
            Rules = [.. (model.LocalRules ?? []).Select(ToContract)],
            RuleOverrides = [.. (model.RuleOverrides ?? []).Select(ToContract)],
            ExcludedPlayerTags = RequiredTagsSelection.SplitCsv(model.ExcludedPlayerTagsCsv)
        };
    }

    public static bool HasServerOverrides(VpnProtectionServerSettingsViewModel model)
    {
        return model.Enabled.HasValue ||
            !model.InheritGlobalRules ||
            model.LocalRules.Count > 0 ||
            model.RuleOverrides.Count > 0 ||
            RequiredTagsSelection.SplitCsv(model.ExcludedPlayerTagsCsv).Length > 0;
    }

    private static VpnProtectionRuleViewModel ToViewModel(VpnProtectionRule rule)
    {
        return new()
        {
            Id = rule.Id,
            Enabled = rule.Enabled ?? true,
            Condition = ToViewModel(rule.Condition),
            Action = rule.Action,
            PostObservationToForums = rule.PostObservationToForums,
            ReasonTemplate = rule.ReasonTemplate
        };
    }

    private static VpnProtectionRuleOverrideViewModel ToViewModel(VpnProtectionRuleOverride ruleOverride)
    {
        return new()
        {
            Id = ruleOverride.Id,
            Enabled = ruleOverride.Enabled,
            UseConditionOverride = ruleOverride.Condition is not null,
            Condition = ToViewModel(ruleOverride.Condition),
            Action = ruleOverride.Action,
            PostObservationToForums = ruleOverride.PostObservationToForums,
            ReasonTemplate = ruleOverride.ReasonTemplate
        };
    }

    private static VpnProtectionRule ToContract(VpnProtectionRuleViewModel rule)
    {
        return new()
        {
            Id = rule.Id?.Trim() ?? string.Empty,
            Enabled = rule.Enabled,
            Condition = ToContract(rule.Condition),
            Action = rule.Action,
            PostObservationToForums = rule.PostObservationToForums,
            ReasonTemplate = NormalizeOptional(rule.ReasonTemplate)
        };
    }

    private static VpnProtectionRuleOverride ToContract(VpnProtectionRuleOverrideViewModel ruleOverride)
    {
        return new()
        {
            Id = ruleOverride.Id?.Trim() ?? string.Empty,
            Enabled = ruleOverride.Enabled,
            Condition = ruleOverride.UseConditionOverride ? ToContract(ruleOverride.Condition) : null,
            Action = ruleOverride.Action,
            PostObservationToForums = ruleOverride.PostObservationToForums,
            ReasonTemplate = NormalizeOptional(ruleOverride.ReasonTemplate)
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    internal static bool HasPlayerCondition(RuleConditionViewModel? condition)
        => condition is not null && (condition.PlayerSignal.HasValue ||
            (condition.Conditions ?? []).Any(HasPlayerCondition));

    private static RuleConditionViewModel ToViewModel(VpnProtectionCondition? condition)
        => condition is null ? new() : new()
        {
            GroupOperator = condition.GroupOperator,
            Conditions = [.. (condition.Conditions ?? []).Select(ToViewModel)],
            Signal = condition.Signal,
            Operator = condition.Operator,
            ExpectedValue = condition.ExpectedValue
        };

    private static VpnProtectionCondition ToContract(RuleConditionViewModel? condition)
        => condition is null ? new() : new()
        {
            GroupOperator = condition.GroupOperator,
            Conditions = condition.GroupOperator.HasValue
                ? [.. (condition.Conditions ?? []).Select(ToContract)]
                : null,
            Signal = condition.Signal,
            Operator = condition.Operator,
            ExpectedValue = NormalizeOptional(condition.ExpectedValue)
        };
}
