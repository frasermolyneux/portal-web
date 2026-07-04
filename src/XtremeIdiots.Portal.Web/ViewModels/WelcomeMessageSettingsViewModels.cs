using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.WelcomeMessages;

namespace XtremeIdiots.Portal.Web.ViewModels;

public static class WelcomeMessageSettingsViewModelConstants
{
    public const string Namespace = WelcomeMessageSettingsConstants.Namespace;
    public const int SupportedSchemaVersion = WelcomeMessageSettingsConstants.SchemaVersion;

    public const int MinPriority = WelcomeMessageSettingsConstants.MinPriority;
    public const int MaxPriority = WelcomeMessageSettingsConstants.MaxPriority;
    public const int MaxMessageTemplateLength = WelcomeMessageSettingsConstants.MaxMessageTemplateLength;
    public const int MaxRequiredTags = WelcomeMessageSettingsConstants.MaxRequiredTags;

    public const int MinConnectionDelaySeconds = WelcomeMessageSettingsConstants.MinConnectionDelaySeconds;
    public const int MaxConnectionDelaySeconds = WelcomeMessageSettingsConstants.MaxConnectionDelaySeconds;

    public const int DefaultStaleThresholdSeconds = WelcomeMessageSettingsConstants.DefaultStaleThresholdSeconds;
    public const int MinStaleThresholdSeconds = WelcomeMessageSettingsConstants.MinStaleThresholdSeconds;
    public const int MaxStaleThresholdSeconds = WelcomeMessageSettingsConstants.MaxStaleThresholdSeconds;

    public const int DefaultConnectionDelaySeconds = WelcomeMessageSettingsConstants.DefaultConnectionDelaySeconds;
    public const string DefaultCountryFallback = WelcomeMessageSettingsConstants.DefaultCountryFallback;
}

public class WelcomeMessageRuleEntryViewModel
{
    [DisplayName("Rule Id")]
    [Required]
    public string Id { get; set; } = string.Empty;

    [DisplayName("Enabled")]
    public bool Enabled { get; set; } = true;

    [DisplayName("Priority")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinPriority, WelcomeMessageSettingsViewModelConstants.MaxPriority)]
    public int Priority { get; set; }

    [DisplayName("Visibility")]
    public WelcomeMessageVisibility Visibility { get; set; } = WelcomeMessageVisibility.Private;

    [DisplayName("Message Template")]
    [Required]
    [MaxLength(WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength)]
    public string MessageTemplate { get; set; } = string.Empty;

    [DisplayName("Required Tags")]
    public string? RequiredTagsCsv { get; set; }

    [DisplayName("Delay Override (seconds)")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds, WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds)]
    public int? ConnectionDelaySeconds { get; set; }
}

public class WelcomeMessageRuleOverrideEntryViewModel
{
    [DisplayName("Rule Id")]
    [Required]
    public string Id { get; set; } = string.Empty;

    [DisplayName("Enabled Override")]
    public bool? Enabled { get; set; }

    [DisplayName("Priority Override")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinPriority, WelcomeMessageSettingsViewModelConstants.MaxPriority)]
    public int? Priority { get; set; }

    [DisplayName("Visibility Override")]
    public WelcomeMessageVisibility? Visibility { get; set; }

    [DisplayName("Message Template Override")]
    [MaxLength(WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength)]
    public string? MessageTemplate { get; set; }

    [DisplayName("Override Required Tags")]
    public bool OverrideRequiredTags { get; set; }

    [DisplayName("Required Tags Override")]
    public string? RequiredTagsCsv { get; set; }

    [DisplayName("Delay Override (seconds)")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds, WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds)]
    public int? ConnectionDelaySeconds { get; set; }
}

public class WelcomeMessageGlobalSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled")]
    public bool Enabled { get; set; } = true;

    [DisplayName("Country Fallback")]
    [MaxLength(80)]
    public string CountryFallback { get; set; } = WelcomeMessageSettingsViewModelConstants.DefaultCountryFallback;

    [DisplayName("Stale Threshold (seconds)")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinStaleThresholdSeconds, WelcomeMessageSettingsViewModelConstants.MaxStaleThresholdSeconds)]
    public int StaleThresholdSeconds { get; set; } = WelcomeMessageSettingsViewModelConstants.DefaultStaleThresholdSeconds;

    [DisplayName("Default Delay (seconds)")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds, WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds)]
    public int DefaultConnectionDelaySeconds { get; set; } = WelcomeMessageSettingsViewModelConstants.DefaultConnectionDelaySeconds;

    public List<WelcomeMessageRuleEntryViewModel> Rules { get; set; } = [];

    public IReadOnlyList<string> AllowedRequiredTags { get; set; } = [];

    // Defaults to false: the tags catalog is not loaded during model binding, so required-tag validation
    // must stay dormant until ApplyAvailableRequiredTags supplies the real allow-list. A true default would
    // flag every assigned tag as unavailable during binding and silently block the settings save.
    public bool RequiredTagsCatalogAvailable { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in WelcomeMessageSettingsValidation.ValidateRules(
            Rules,
            nameof(Rules),
            requireTemplate: true,
            AllowedRequiredTags,
            RequiredTagsCatalogAvailable))
        {
            yield return result;
        }
    }
}

public class WelcomeMessageServerSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled Override")]
    public TriStateOverrideValue EnabledOverride { get; set; } = TriStateOverrideValue.Inherit();

    [DisplayName("Enabled Override")]
    public bool? Enabled { get => EnabledOverride?.Value; set => EnabledOverride = TriStateOverrideValue.From(value); }

    [DisplayName("Inherit Global Rules")]
    public bool InheritGlobalRules { get; set; } = true;

    [DisplayName("Country Fallback Override")]
    [MaxLength(80)]
    public string? CountryFallback { get; set; }

    [DisplayName("Stale Threshold Override (seconds)")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinStaleThresholdSeconds, WelcomeMessageSettingsViewModelConstants.MaxStaleThresholdSeconds)]
    public int? StaleThresholdSeconds { get; set; }

    [DisplayName("Default Delay Override (seconds)")]
    [Range(WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds, WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds)]
    public int? DefaultConnectionDelaySeconds { get; set; }

    public List<WelcomeMessageRuleEntryViewModel> LocalRules { get; set; } = [];

    public List<WelcomeMessageRuleOverrideEntryViewModel> RuleOverrides { get; set; } = [];

    public IReadOnlyList<string> AllowedRequiredTags { get; set; } = [];

    // Defaults to false: the tags catalog is not loaded during model binding (see the global variant).
    public bool RequiredTagsCatalogAvailable { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in WelcomeMessageSettingsValidation.ValidateRules(
            LocalRules,
            nameof(LocalRules),
            requireTemplate: true,
            AllowedRequiredTags,
            RequiredTagsCatalogAvailable))
        {
            yield return result;
        }

        foreach (var result in WelcomeMessageSettingsValidation.ValidateRuleOverrides(
            RuleOverrides,
            nameof(RuleOverrides),
            AllowedRequiredTags,
            RequiredTagsCatalogAvailable))
        {
            yield return result;
        }
    }
}

internal static class WelcomeMessageSettingsValidation
{
    public static IEnumerable<ValidationResult> ValidateRules(
        IReadOnlyList<WelcomeMessageRuleEntryViewModel>? rules,
        string memberPrefix,
        bool requireTemplate,
        IReadOnlyList<string> allowedRequiredTags,
        bool requiredTagsCatalogAvailable)
    {
        if (rules is null)
        {
            yield break;
        }

        var allowedTagSet = allowedRequiredTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            var id = rule.Id.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                yield return new ValidationResult("Rule id is required.", [$"{memberPrefix}[{i}].Id"]);
                continue;
            }

            if (!seenIds.Add(id))
            {
                yield return new ValidationResult("Rule id must be unique.", [$"{memberPrefix}[{i}].Id"]);
            }

            if (rule.Priority is < WelcomeMessageSettingsViewModelConstants.MinPriority
                or > WelcomeMessageSettingsViewModelConstants.MaxPriority)
            {
                yield return new ValidationResult(
                    $"Priority must be between {WelcomeMessageSettingsViewModelConstants.MinPriority} and {WelcomeMessageSettingsViewModelConstants.MaxPriority}.",
                    [$"{memberPrefix}[{i}].Priority"]);
            }

            var template = (rule.MessageTemplate ?? string.Empty).Trim();
            if (requireTemplate && string.IsNullOrWhiteSpace(template))
            {
                yield return new ValidationResult("Message template is required.", [$"{memberPrefix}[{i}].MessageTemplate"]);
            }
            else if (template.Length > WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength)
            {
                yield return new ValidationResult(
                    $"Message template must be <= {WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength} characters.",
                    [$"{memberPrefix}[{i}].MessageTemplate"]);
            }

            var tags = SplitCsv(rule.RequiredTagsCsv);
            if (tags.Length > WelcomeMessageSettingsViewModelConstants.MaxRequiredTags)
            {
                yield return new ValidationResult(
                    $"Required tags supports at most {WelcomeMessageSettingsViewModelConstants.MaxRequiredTags} tags.",
                    [$"{memberPrefix}[{i}].RequiredTagsCsv"]);
            }

            foreach (var invalidTag in tags.Where(tag => requiredTagsCatalogAvailable && !allowedTagSet.Contains(tag)))
            {
                yield return new ValidationResult(
                    $"Required tag '{invalidTag}' is not available.",
                    [$"{memberPrefix}[{i}].RequiredTagsCsv"]);
            }

            if (rule.ConnectionDelaySeconds.HasValue
                && (rule.ConnectionDelaySeconds.Value < WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds
                    || rule.ConnectionDelaySeconds.Value > WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds))
            {
                yield return new ValidationResult(
                    $"Delay override must be between {WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds} and {WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds}.",
                    [$"{memberPrefix}[{i}].ConnectionDelaySeconds"]);
            }
        }
    }

    public static IEnumerable<ValidationResult> ValidateRuleOverrides(
        IReadOnlyList<WelcomeMessageRuleOverrideEntryViewModel>? overrides,
        string memberPrefix,
        IReadOnlyList<string> allowedRequiredTags,
        bool requiredTagsCatalogAvailable)
    {
        if (overrides is null)
        {
            yield break;
        }

        var allowedTagSet = allowedRequiredTags
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < overrides.Count; i++)
        {
            var row = overrides[i];
            var id = row.Id.Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                yield return new ValidationResult("Rule id is required.", [$"{memberPrefix}[{i}].Id"]);
                continue;
            }

            if (!seenIds.Add(id))
            {
                yield return new ValidationResult("Rule id must be unique.", [$"{memberPrefix}[{i}].Id"]);
            }

            if (!string.IsNullOrWhiteSpace(row.MessageTemplate)
                && row.MessageTemplate.Trim().Length > WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength)
            {
                yield return new ValidationResult(
                    $"Message template override must be <= {WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength} characters.",
                    [$"{memberPrefix}[{i}].MessageTemplate"]);
            }

            if (row.OverrideRequiredTags)
            {
                var tags = SplitCsv(row.RequiredTagsCsv);
                if (tags.Length > WelcomeMessageSettingsViewModelConstants.MaxRequiredTags)
                {
                    yield return new ValidationResult(
                        $"Required tags override supports at most {WelcomeMessageSettingsViewModelConstants.MaxRequiredTags} tags.",
                        [$"{memberPrefix}[{i}].RequiredTagsCsv"]);
                }

                foreach (var invalidTag in tags.Where(tag => requiredTagsCatalogAvailable && !allowedTagSet.Contains(tag)))
                {
                    yield return new ValidationResult(
                        $"Required tag '{invalidTag}' is not available.",
                        [$"{memberPrefix}[{i}].RequiredTagsCsv"]);
                }
            }

            if (row.ConnectionDelaySeconds.HasValue
                && (row.ConnectionDelaySeconds.Value < WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds
                    || row.ConnectionDelaySeconds.Value > WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds))
            {
                yield return new ValidationResult(
                    $"Delay override must be between {WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds} and {WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds}.",
                    [$"{memberPrefix}[{i}].ConnectionDelaySeconds"]);
            }
        }
    }

    public static string[] SplitCsv(string? value)
    {
        return RequiredTagsSelection.SplitCsv(value);
    }
}

internal static class WelcomeMessageSettingsJsonMapper
{
    public static void PopulateGlobal(
        WelcomeMessageGlobalSettingsViewModel target,
        WelcomeMessageSettingsDocument document)
    {
        target.Enabled = document.Enabled ?? true;

        if (document.Defaults is not null)
        {
            target.CountryFallback = document.Defaults.CountryFallback ?? target.CountryFallback;
            target.StaleThresholdSeconds = document.Defaults.StaleThresholdSeconds ?? target.StaleThresholdSeconds;
            target.DefaultConnectionDelaySeconds = document.Defaults.ConnectionDelaySeconds ?? target.DefaultConnectionDelaySeconds;
        }

        target.Rules =
        [
            .. document.Rules
                .Where(static rule => !string.IsNullOrWhiteSpace(rule.Id))
                .Select(rule => new WelcomeMessageRuleEntryViewModel
                {
                    Id = rule.Id.Trim(),
                    Enabled = rule.Enabled ?? true,
                    Priority = rule.Priority ?? 0,
                    Visibility = rule.Visibility ?? WelcomeMessageVisibility.Private,
                    MessageTemplate = (rule.MessageTemplate ?? string.Empty).Trim(),
                    RequiredTagsCsv = string.Join(", ", (rule.RequiredTags ?? []).Where(static tag => !string.IsNullOrWhiteSpace(tag))),
                    ConnectionDelaySeconds = rule.ConnectionDelaySeconds
                })
        ];
    }

    public static void PopulateServer(
        WelcomeMessageServerSettingsViewModel target,
        WelcomeMessageSettingsDocument document)
    {
        target.Enabled = document.Enabled;
        target.InheritGlobalRules = document.InheritGlobalRules ?? true;

        if (document.Defaults is not null)
        {
            target.CountryFallback = document.Defaults.CountryFallback;
            target.StaleThresholdSeconds = document.Defaults.StaleThresholdSeconds;
            target.DefaultConnectionDelaySeconds = document.Defaults.ConnectionDelaySeconds;
        }

        target.LocalRules =
        [
            .. document.Rules
                .Where(static rule => !string.IsNullOrWhiteSpace(rule.Id))
                .Select(rule => new WelcomeMessageRuleEntryViewModel
                {
                    Id = rule.Id.Trim(),
                    Enabled = rule.Enabled ?? true,
                    Priority = rule.Priority ?? 0,
                    Visibility = rule.Visibility ?? WelcomeMessageVisibility.Private,
                    MessageTemplate = (rule.MessageTemplate ?? string.Empty).Trim(),
                    RequiredTagsCsv = string.Join(", ", (rule.RequiredTags ?? []).Where(static tag => !string.IsNullOrWhiteSpace(tag))),
                    ConnectionDelaySeconds = rule.ConnectionDelaySeconds
                })
        ];

        target.RuleOverrides =
        [
            .. document.RuleOverrides
                .Where(static rule => !string.IsNullOrWhiteSpace(rule.Id))
                .Select(rule => new WelcomeMessageRuleOverrideEntryViewModel
                {
                    Id = rule.Id.Trim(),
                    Enabled = rule.Enabled,
                    Priority = rule.Priority,
                    Visibility = rule.Visibility,
                    MessageTemplate = rule.MessageTemplate,
                    OverrideRequiredTags = rule.RequiredTags is not null,
                    RequiredTagsCsv = string.Join(", ", (rule.RequiredTags ?? []).Where(static tag => !string.IsNullOrWhiteSpace(tag))),
                    ConnectionDelaySeconds = rule.ConnectionDelaySeconds
                })
        ];
    }

    public static void PopulateGlobal(WelcomeMessageGlobalSettingsViewModel target, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        target.Enabled = !root.TryGetProperty("enabled", out var enabledElement)
            || enabledElement.ValueKind != JsonValueKind.False;

        if (root.TryGetProperty("defaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
        {
            target.CountryFallback = GetStringProperty(defaults, "countryFallback") ?? target.CountryFallback;
            target.StaleThresholdSeconds = GetIntProperty(defaults, "staleThresholdSeconds", target.StaleThresholdSeconds);
            target.DefaultConnectionDelaySeconds = GetIntProperty(defaults, "connectionDelaySeconds", target.DefaultConnectionDelaySeconds);
        }

        target.Rules = root.TryGetProperty("rules", out var rulesElement) && rulesElement.ValueKind == JsonValueKind.Array
            ? ParseRules(rulesElement)
            : [];
    }

    public static void PopulateServer(WelcomeMessageServerSettingsViewModel target, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("enabled", out var enabledElement))
        {
            target.Enabled = enabledElement.ValueKind == JsonValueKind.True
                ? true
                : enabledElement.ValueKind == JsonValueKind.False
                    ? false
                    : null;
        }

        target.InheritGlobalRules = GetBoolProperty(root, "inheritGlobalRules", true);

        if (root.TryGetProperty("defaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
        {
            target.CountryFallback = GetStringProperty(defaults, "countryFallback");
            target.StaleThresholdSeconds = GetNullableIntProperty(defaults, "staleThresholdSeconds");
            target.DefaultConnectionDelaySeconds = GetNullableIntProperty(defaults, "connectionDelaySeconds");
        }

        target.LocalRules = root.TryGetProperty("rules", out var rulesElement) && rulesElement.ValueKind == JsonValueKind.Array
            ? ParseRules(rulesElement)
            : [];

        target.RuleOverrides = root.TryGetProperty("ruleOverrides", out var overridesElement) && overridesElement.ValueKind == JsonValueKind.Array
            ? ParseRuleOverrides(overridesElement)
            : [];
    }

    public static string BuildGlobalConfigurationJson(WelcomeMessageGlobalSettingsViewModel model)
    {
        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = WelcomeMessageSettingsViewModelConstants.SupportedSchemaVersion,
            ["enabled"] = model.Enabled,
            ["defaults"] = new Dictionary<string, object?>
            {
                ["countryFallback"] = string.IsNullOrWhiteSpace(model.CountryFallback)
                    ? WelcomeMessageSettingsViewModelConstants.DefaultCountryFallback
                    : model.CountryFallback.Trim(),
                ["connectionDelaySeconds"] = model.DefaultConnectionDelaySeconds,
                ["staleThresholdSeconds"] = model.StaleThresholdSeconds
            },
            ["rules"] = BuildRulePayload(model.Rules)
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string BuildServerConfigurationJson(WelcomeMessageServerSettingsViewModel model)
    {
        var defaults = new Dictionary<string, object?>();
        var hasDefaults = false;

        if (!string.IsNullOrWhiteSpace(model.CountryFallback))
        {
            defaults["countryFallback"] = model.CountryFallback.Trim();
            hasDefaults = true;
        }

        if (model.StaleThresholdSeconds.HasValue)
        {
            defaults["staleThresholdSeconds"] = model.StaleThresholdSeconds.Value;
            hasDefaults = true;
        }

        if (model.DefaultConnectionDelaySeconds.HasValue)
        {
            defaults["connectionDelaySeconds"] = model.DefaultConnectionDelaySeconds.Value;
            hasDefaults = true;
        }

        var payload = new Dictionary<string, object?>
        {
            ["schemaVersion"] = WelcomeMessageSettingsViewModelConstants.SupportedSchemaVersion,
            ["enabled"] = model.Enabled,
            ["inheritGlobalRules"] = model.InheritGlobalRules,
            ["defaults"] = hasDefaults ? defaults : null,
            ["rules"] = BuildRulePayload(model.LocalRules),
            ["ruleOverrides"] = BuildRuleOverridePayload(model.RuleOverrides)
        };

        return JsonSerializer.Serialize(payload);
    }

    private static List<WelcomeMessageRuleEntryViewModel> ParseRules(JsonElement rulesElement)
    {
        List<WelcomeMessageRuleEntryViewModel> rules = [];

        foreach (var rule in rulesElement.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = (GetStringProperty(rule, "id") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            rules.Add(new WelcomeMessageRuleEntryViewModel
            {
                Id = id,
                Enabled = GetNullableBoolProperty(rule, "enabled") ?? true,
                Priority = GetNullableIntProperty(rule, "priority") ?? 0,
                Visibility = ParseVisibility(rule, "visibility") ?? WelcomeMessageVisibility.Private,
                MessageTemplate = (GetStringProperty(rule, "messageTemplate") ?? string.Empty).Trim(),
                RequiredTagsCsv = string.Join(", ", GetStringArray(rule, "requiredTags")),
                ConnectionDelaySeconds = GetNullableIntProperty(rule, "connectionDelaySeconds")
            });
        }

        return rules;
    }

    private static List<WelcomeMessageRuleOverrideEntryViewModel> ParseRuleOverrides(JsonElement overridesElement)
    {
        List<WelcomeMessageRuleOverrideEntryViewModel> rules = [];

        foreach (var rule in overridesElement.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var id = (GetStringProperty(rule, "id") ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var hasTags = rule.TryGetProperty("requiredTags", out _);

            rules.Add(new WelcomeMessageRuleOverrideEntryViewModel
            {
                Id = id,
                Enabled = GetNullableBoolProperty(rule, "enabled"),
                Priority = GetNullableIntProperty(rule, "priority"),
                Visibility = ParseVisibility(rule, "visibility"),
                MessageTemplate = GetStringProperty(rule, "messageTemplate"),
                OverrideRequiredTags = hasTags,
                RequiredTagsCsv = hasTags ? string.Join(", ", GetStringArray(rule, "requiredTags")) : null,
                ConnectionDelaySeconds = GetNullableIntProperty(rule, "connectionDelaySeconds")
            });
        }

        return rules;
    }

    private static object[] BuildRulePayload(List<WelcomeMessageRuleEntryViewModel>? rules)
    {
        return rules is null || rules.Count == 0
            ? []
            : rules
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Id))
            .Select(rule =>
            {
                Dictionary<string, object?> payload = [];
                payload["id"] = rule.Id.Trim();
                payload["enabled"] = rule.Enabled;
                payload["priority"] = rule.Priority;
                payload["visibility"] = rule.Visibility;
                payload["messageTemplate"] = (rule.MessageTemplate ?? string.Empty).Trim();

                var tags = WelcomeMessageSettingsValidation.SplitCsv(rule.RequiredTagsCsv);
                if (tags.Length > 0)
                {
                    payload["requiredTags"] = tags;
                }

                if (rule.ConnectionDelaySeconds.HasValue)
                {
                    payload["connectionDelaySeconds"] = rule.ConnectionDelaySeconds.Value;
                }

                return payload;
            })
            .ToArray();
    }

    private static object[] BuildRuleOverridePayload(List<WelcomeMessageRuleOverrideEntryViewModel>? overrides)
    {
        return overrides is null || overrides.Count == 0
            ? []
            : overrides
            .Where(static rule => !string.IsNullOrWhiteSpace(rule.Id))
            .Select(rule =>
            {
                Dictionary<string, object?> payload = [];
                payload["id"] = rule.Id.Trim();

                if (rule.Enabled.HasValue)
                {
                    payload["enabled"] = rule.Enabled.Value;
                }

                if (rule.Priority.HasValue)
                {
                    payload["priority"] = rule.Priority.Value;
                }

                if (rule.Visibility.HasValue)
                {
                    payload["visibility"] = rule.Visibility.Value;
                }

                if (!string.IsNullOrWhiteSpace(rule.MessageTemplate))
                {
                    payload["messageTemplate"] = rule.MessageTemplate.Trim();
                }

                if (rule.OverrideRequiredTags)
                {
                    payload["requiredTags"] = WelcomeMessageSettingsValidation.SplitCsv(rule.RequiredTagsCsv);
                }

                if (rule.ConnectionDelaySeconds.HasValue)
                {
                    payload["connectionDelaySeconds"] = rule.ConnectionDelaySeconds.Value;
                }

                return payload;
            })
            .ToArray();
    }

    private static string? GetStringProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }

    private static int GetIntProperty(JsonElement root, string propertyName, int defaultValue)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : defaultValue;
    }

    private static int? GetNullableIntProperty(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static bool GetBoolProperty(JsonElement root, string propertyName, bool defaultValue)
    {
        return !root.TryGetProperty(propertyName, out var prop)
            ? defaultValue
            : prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Undefined => defaultValue,
                JsonValueKind.Object => defaultValue,
                JsonValueKind.Array => defaultValue,
                JsonValueKind.String => defaultValue,
                JsonValueKind.Number => defaultValue,
                JsonValueKind.Null => defaultValue,
                _ => defaultValue
            };
    }

    private static bool? GetNullableBoolProperty(JsonElement root, string propertyName)
    {
        return !root.TryGetProperty(propertyName, out var prop)
            ? null
            : prop.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Undefined => null,
                JsonValueKind.Object => null,
                JsonValueKind.Array => null,
                JsonValueKind.String => null,
                JsonValueKind.Number => null,
                JsonValueKind.Null => null,
                _ => null
            };
    }

    private static WelcomeMessageVisibility? ParseVisibility(JsonElement root, string propertyName)
    {
        return !root.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String
            ? null
            : Enum.TryParse<WelcomeMessageVisibility>(prop.GetString(), true, out var parsed)
                ? parsed
                : null;
    }

    private static string[] GetStringArray(JsonElement root, string propertyName)
    {
        return !root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array
            ? []
            :
            [
                .. property
                    .EnumerateArray()
                    .Where(static item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(static item => !string.IsNullOrWhiteSpace(item))
                    .Select(static item => item!.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            ];
    }
}
