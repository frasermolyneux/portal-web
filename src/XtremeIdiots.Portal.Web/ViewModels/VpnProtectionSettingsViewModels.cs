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

    [DisplayName("Signal")]
    public VpnProtectionSignal Signal { get; set; }

    [DisplayName("Operator")]
    public VpnProtectionComparisonOperator Operator { get; set; }

    [DisplayName("Expected Value")]
    [Required]
    [MaxLength(VpnProtectionSettingsConstants.MaxExpectedValueLength)]
    public string ExpectedValue { get; set; } = string.Empty;

    [DisplayName("Action")]
    public VpnProtectionAction Action { get; set; }

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

    [DisplayName("Signal Override")]
    public VpnProtectionSignal? Signal { get; set; }

    [DisplayName("Operator Override")]
    public VpnProtectionComparisonOperator? Operator { get; set; }

    [DisplayName("Expected Value Override")]
    [MaxLength(VpnProtectionSettingsConstants.MaxExpectedValueLength)]
    public string? ExpectedValue { get; set; }

    [DisplayName("Action Override")]
    public VpnProtectionAction? Action { get; set; }

    [DisplayName("Reason Template Override")]
    [MaxLength(VpnProtectionSettingsConstants.MaxReasonTemplateLength)]
    public string? ReasonTemplate { get; set; }
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
        return VpnProtectionSettingsViewModelValidation.Validate(
            VpnProtectionSettingsJsonMapper.ToDocument(this),
            ExcludedPlayerTagsCsv,
            AllowedExcludedPlayerTags,
            ExcludedPlayerTagsCatalogAvailable,
            nameof(Rules));
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
        return VpnProtectionSettingsViewModelValidation.Validate(
            VpnProtectionSettingsJsonMapper.ToDocument(this),
            ExcludedPlayerTagsCsv,
            AllowedExcludedPlayerTags,
            ExcludedPlayerTagsCatalogAvailable,
            nameof(LocalRules));
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
            Signal = rule.Signal,
            Operator = rule.Operator,
            ExpectedValue = rule.ExpectedValue,
            Action = rule.Action,
            ReasonTemplate = rule.ReasonTemplate
        };
    }

    private static VpnProtectionRuleOverrideViewModel ToViewModel(VpnProtectionRuleOverride ruleOverride)
    {
        return new()
        {
            Id = ruleOverride.Id,
            Enabled = ruleOverride.Enabled,
            Signal = ruleOverride.Signal,
            Operator = ruleOverride.Operator,
            ExpectedValue = ruleOverride.ExpectedValue,
            Action = ruleOverride.Action,
            ReasonTemplate = ruleOverride.ReasonTemplate
        };
    }

    private static VpnProtectionRule ToContract(VpnProtectionRuleViewModel rule)
    {
        return new()
        {
            Id = rule.Id?.Trim() ?? string.Empty,
            Enabled = rule.Enabled,
            Signal = rule.Signal,
            Operator = rule.Operator,
            ExpectedValue = rule.ExpectedValue?.Trim() ?? string.Empty,
            Action = rule.Action,
            ReasonTemplate = NormalizeOptional(rule.ReasonTemplate)
        };
    }

    private static VpnProtectionRuleOverride ToContract(VpnProtectionRuleOverrideViewModel ruleOverride)
    {
        return new()
        {
            Id = ruleOverride.Id?.Trim() ?? string.Empty,
            Enabled = ruleOverride.Enabled,
            Signal = ruleOverride.Signal,
            Operator = ruleOverride.Operator,
            ExpectedValue = NormalizeOptional(ruleOverride.ExpectedValue),
            Action = ruleOverride.Action,
            ReasonTemplate = NormalizeOptional(ruleOverride.ReasonTemplate)
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}