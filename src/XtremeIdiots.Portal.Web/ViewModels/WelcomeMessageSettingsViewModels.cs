using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using XtremeIdiots.Portal.Server.Events.Processor.App.Commands;

namespace XtremeIdiots.Portal.Web.ViewModels;

public static class WelcomeMessageSettingsViewModelConstants
{
    public const string Namespace = WelcomeMessageSettingsConstants.Namespace;
    public const int SupportedSchemaVersion = WelcomeMessageSettingsConstants.SupportedSchemaVersion;

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

    [DisplayName("Rules JSON")]
    public string RulesJson { get; set; } = "[]";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!WelcomeMessageSettingsJsonMapper.TryParseRules(RulesJson, out var error))
        {
            yield return new ValidationResult(error, [nameof(RulesJson)]);
        }
    }
}

public class WelcomeMessageServerSettingsViewModel : IValidatableObject
{
    [DisplayName("Enabled Override")]
    public bool? Enabled { get; set; }

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

    [DisplayName("Local Rules JSON")]
    public string LocalRulesJson { get; set; } = "[]";

    [DisplayName("Rule Overrides JSON")]
    public string RuleOverridesJson { get; set; } = "[]";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!WelcomeMessageSettingsJsonMapper.TryParseRules(LocalRulesJson, out var localError))
        {
            yield return new ValidationResult(localError, [nameof(LocalRulesJson)]);
        }

        if (!WelcomeMessageSettingsJsonMapper.TryParseRuleOverrides(RuleOverridesJson, out var overridesError))
        {
            yield return new ValidationResult(overridesError, [nameof(RuleOverridesJson)]);
        }
    }
}

internal static class WelcomeMessageSettingsJsonMapper
{
    private readonly static JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

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

        target.RulesJson = root.TryGetProperty("rules", out var rulesElement)
            ? JsonSerializer.Serialize(rulesElement, jsonOptions)
            : "[]";
    }

    public static void PopulateServer(WelcomeMessageServerSettingsViewModel target, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (root.TryGetProperty("enabled", out var enabledElement))
        {
            target.Enabled = enabledElement.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                JsonValueKind.Number => null,
                JsonValueKind.String => null,
                JsonValueKind.Object => null,
                JsonValueKind.Array => null,
                _ => null
            };
        }

        target.InheritGlobalRules = GetBoolProperty(root, "inheritGlobalRules", true);

        if (root.TryGetProperty("defaults", out var defaults) && defaults.ValueKind == JsonValueKind.Object)
        {
            target.CountryFallback = GetStringProperty(defaults, "countryFallback");
            target.StaleThresholdSeconds = GetNullableIntProperty(defaults, "staleThresholdSeconds");
            target.DefaultConnectionDelaySeconds = GetNullableIntProperty(defaults, "connectionDelaySeconds");
        }

        target.LocalRulesJson = root.TryGetProperty("rules", out var rulesElement)
            ? JsonSerializer.Serialize(rulesElement, jsonOptions)
            : "[]";

        target.RuleOverridesJson = root.TryGetProperty("ruleOverrides", out var overridesElement)
            ? JsonSerializer.Serialize(overridesElement, jsonOptions)
            : "[]";
    }

    public static string BuildGlobalConfigurationJson(WelcomeMessageGlobalSettingsViewModel model)
    {
        using var rulesDoc = ParseArrayDocumentOrEmpty(model.RulesJson);

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
            ["rules"] = rulesDoc.RootElement.Clone()
        };

        return JsonSerializer.Serialize(payload);
    }

    public static string BuildServerConfigurationJson(WelcomeMessageServerSettingsViewModel model)
    {
        using var localRulesDoc = ParseArrayDocumentOrEmpty(model.LocalRulesJson);
        using var ruleOverridesDoc = ParseArrayDocumentOrEmpty(model.RuleOverridesJson);

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
            ["rules"] = localRulesDoc.RootElement.Clone(),
            ["ruleOverrides"] = ruleOverridesDoc.RootElement.Clone()
        };

        return JsonSerializer.Serialize(payload);
    }

    public static bool TryParseRules(string? value, out string error)
    {
        try
        {
            using var doc = ParseArrayDocumentOrEmpty(value);
            if (!ValidateRulesArray(doc.RootElement, "rules", out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Rules JSON is invalid: {ex.Message}";
            return false;
        }
    }

    public static bool TryParseRuleOverrides(string? value, out string error)
    {
        try
        {
            using var doc = ParseArrayDocumentOrEmpty(value);
            if (!ValidateRuleOverridesArray(doc.RootElement, "ruleOverrides", out error))
            {
                return false;
            }

            error = string.Empty;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Rule overrides JSON is invalid: {ex.Message}";
            return false;
        }
    }

    private static JsonDocument ParseArrayDocumentOrEmpty(string? value)
    {
        var content = string.IsNullOrWhiteSpace(value) ? "[]" : value;
        var doc = JsonDocument.Parse(content);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            doc.Dispose();
            throw new JsonException("JSON value must be an array.");
        }

        return doc;
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
        if (!root.TryGetProperty(propertyName, out var prop))
        {
            return defaultValue;
        }

        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => defaultValue,
            JsonValueKind.Undefined => defaultValue,
            JsonValueKind.Number => defaultValue,
            JsonValueKind.String => defaultValue,
            JsonValueKind.Object => defaultValue,
            JsonValueKind.Array => defaultValue,
            _ => defaultValue
        };
    }

    private static bool ValidateRulesArray(JsonElement arrayElement, string path, out string error)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var rule in arrayElement.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                error = $"{path}[{index}] must be an object.";
                return false;
            }

            if (!rule.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                error = $"{path}[{index}].id is required.";
                return false;
            }

            var id = idElement.GetString()!.Trim();
            if (!seenIds.Add(id))
            {
                error = $"{path}[{index}].id must be unique.";
                return false;
            }

            if (!ValidateCommonRuleFields(rule, path, index, requireTemplate: true, out error))
            {
                return false;
            }

            index++;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateRuleOverridesArray(JsonElement arrayElement, string path, out string error)
    {
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var rule in arrayElement.EnumerateArray())
        {
            if (rule.ValueKind != JsonValueKind.Object)
            {
                error = $"{path}[{index}] must be an object.";
                return false;
            }

            if (!rule.TryGetProperty("id", out var idElement)
                || idElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(idElement.GetString()))
            {
                error = $"{path}[{index}].id is required.";
                return false;
            }

            var id = idElement.GetString()!.Trim();
            if (!seenIds.Add(id))
            {
                error = $"{path}[{index}].id must be unique.";
                return false;
            }

            if (!ValidateCommonRuleFields(rule, path, index, requireTemplate: false, out error))
            {
                return false;
            }

            index++;
        }

        error = string.Empty;
        return true;
    }

    private static bool ValidateCommonRuleFields(JsonElement rule, string path, int index, bool requireTemplate, out string error)
    {
        if (rule.TryGetProperty("priority", out var priorityElement)
            && priorityElement.ValueKind == JsonValueKind.Number
            && priorityElement.TryGetInt32(out var priority)
            && (priority < WelcomeMessageSettingsViewModelConstants.MinPriority
                || priority > WelcomeMessageSettingsViewModelConstants.MaxPriority))
        {
            error = $"{path}[{index}].priority must be between {WelcomeMessageSettingsViewModelConstants.MinPriority} and {WelcomeMessageSettingsViewModelConstants.MaxPriority}.";
            return false;
        }

        if (rule.TryGetProperty("messageTemplate", out var templateElement))
        {
            if (templateElement.ValueKind != JsonValueKind.String)
            {
                error = $"{path}[{index}].messageTemplate must be a string.";
                return false;
            }

            var template = templateElement.GetString() ?? string.Empty;
            if (template.Length > WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength)
            {
                error = $"{path}[{index}].messageTemplate must be <= {WelcomeMessageSettingsViewModelConstants.MaxMessageTemplateLength} characters.";
                return false;
            }
        }
        else if (requireTemplate)
        {
            error = $"{path}[{index}].messageTemplate is required.";
            return false;
        }

        if (rule.TryGetProperty("requiredTags", out var tagsElement))
        {
            if (tagsElement.ValueKind != JsonValueKind.Array)
            {
                error = $"{path}[{index}].requiredTags must be an array.";
                return false;
            }

            var tagCount = 0;
            foreach (var tag in tagsElement.EnumerateArray())
            {
                if (tag.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(tag.GetString()))
                {
                    error = $"{path}[{index}].requiredTags entries must be non-empty strings.";
                    return false;
                }

                tagCount++;
            }

            if (tagCount > WelcomeMessageSettingsViewModelConstants.MaxRequiredTags)
            {
                error = $"{path}[{index}].requiredTags supports at most {WelcomeMessageSettingsViewModelConstants.MaxRequiredTags} tags.";
                return false;
            }
        }

        if (rule.TryGetProperty("connectionDelaySeconds", out var delayElement)
            && delayElement.ValueKind == JsonValueKind.Number
            && delayElement.TryGetInt32(out var delay)
            && (delay < WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds
                || delay > WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds))
        {
            error = $"{path}[{index}].connectionDelaySeconds must be between {WelcomeMessageSettingsViewModelConstants.MinConnectionDelaySeconds} and {WelcomeMessageSettingsViewModelConstants.MaxConnectionDelaySeconds}.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
