using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Moderation;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

internal static class ModerationEditorCompatibility
{
    public static bool RequiresReadOnly(string json, bool global = false)
    {
        try
        {
            using var source = JsonDocument.Parse(json);
            if (source.RootElement.ValueKind != JsonValueKind.Object)
                return true;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (source.RootElement.EnumerateObject().Any(property => !names.Add(property.Name)))
                return true;

            var document = JsonSerializer.Deserialize<ModerationSettingsDocument>(json, NamespaceSettingsParser.LegacyBooleanSerializerOptions);
            return document is null || !new ModerationSettingsValidator().Validate(document).IsValid
                || document.ExtensionData is { Count: > 0 }
                || (global && document.ProtectedNameEnforcementEnabled.HasValue);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public static void ClearSubmittedModelState(ModelStateDictionary modelState)
    {
        foreach (var key in modelState.Keys.Where(static key => key.StartsWith("Moderation", StringComparison.OrdinalIgnoreCase)).ToArray())
            modelState.Remove(key);
    }

    public static void Restore(GlobalSettingsViewModel target, GlobalSettingsViewModel source)
    {
        target.ModerationHateSeverityThreshold = source.ModerationHateSeverityThreshold;
        target.ModerationViolenceSeverityThreshold = source.ModerationViolenceSeverityThreshold;
        target.ModerationSexualSeverityThreshold = source.ModerationSexualSeverityThreshold;
        target.ModerationSelfHarmSeverityThreshold = source.ModerationSelfHarmSeverityThreshold;
        target.ModerationMinMessageLength = source.ModerationMinMessageLength;
    }

    public static void Restore(GameServerEditViewModel target, GameServerEditViewModel source)
    {
        target.ModerationProtectedNameEnforcementEnabled = source.ModerationProtectedNameEnforcementEnabled;
        target.ModerationHateSeverityThreshold = source.ModerationHateSeverityThreshold;
        target.ModerationViolenceSeverityThreshold = source.ModerationViolenceSeverityThreshold;
        target.ModerationSexualSeverityThreshold = source.ModerationSexualSeverityThreshold;
        target.ModerationSelfHarmSeverityThreshold = source.ModerationSelfHarmSeverityThreshold;
        target.ModerationMinMessageLength = source.ModerationMinMessageLength;
    }
}
