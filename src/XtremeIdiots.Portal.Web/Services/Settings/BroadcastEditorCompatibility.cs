using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.Text.Json;
using System.Text.Json.Serialization;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

internal static class BroadcastEditorCompatibility
{
    private readonly static JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LegacyBooleanConverter() }
    };

    public static bool RequiresReadOnly(string json, bool global = false)
    {
        try
        {
            using var source = JsonDocument.Parse(json);
            if (source.RootElement.ValueKind != JsonValueKind.Object || HasDuplicateProperties(source.RootElement))
                return true;

            var document = Deserialize(json);
            return document is null || !new BroadcastSettingsValidator().Validate(document).IsValid ||
                document.IntervalSeconds > 86400 ||
                document.ExtensionData is { Count: > 0 } ||
                document.Messages!.Any(message => message is null || message.ExtensionData is { Count: > 0 } ||
                    message.Message is { } text && (BroadcastMessageViewModel.HasNonRoundTrippableCharacters(text) ||
                        global && text.Length > GlobalSettingsViewModel.MaxFunnyMessageLength) ||
                    BroadcastMessageViewModel.HasNonRoundTrippableCharacters(message.Id));
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public static BroadcastSettingsDocument? Deserialize(string json) => JsonSerializer.Deserialize<BroadcastSettingsDocument>(json, Options);

    public static bool HasCompleteSubmission(
        ModelStateDictionary modelState,
        int? messageCount,
        IReadOnlyList<BroadcastMessageViewModel>? messages,
        bool server = false)
    {
        if (!modelState.ContainsKey(nameof(GlobalSettingsViewModel.BroadcastsEnabled))
            || !modelState.ContainsKey(nameof(GlobalSettingsViewModel.BroadcastsIntervalSeconds))
            || !modelState.ContainsKey(nameof(GlobalSettingsViewModel.BroadcastMessagesCount))
            || server && !modelState.ContainsKey(nameof(GameServerEditViewModel.BroadcastsMessagesMode))
            || messages is null || messageCount != messages.Count)
            return false;

        for (var i = 0; i < messages.Count; i++)
        {
            if (!modelState.ContainsKey($"BroadcastMessages[{i}].Id")
                || !modelState.ContainsKey($"BroadcastMessages[{i}].Message")
                || !modelState.ContainsKey($"BroadcastMessages[{i}].Enabled"))
                return false;
        }

        return true;
    }

    public static void ClearSubmittedModelState(ModelStateDictionary modelState, bool global = false)
    {
        foreach (var key in modelState.Keys.Where(key =>
            key.StartsWith("Broadcasts", StringComparison.OrdinalIgnoreCase) ||
            key.StartsWith("BroadcastMessages", StringComparison.OrdinalIgnoreCase) ||
            global && key.StartsWith("FunnyMessages", StringComparison.OrdinalIgnoreCase)).ToArray())
            modelState.Remove(key);
    }

    private static bool HasDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            return element.EnumerateObject().Any(property => !names.Add(property.Name) || HasDuplicateProperties(property.Value));
        }

        return element.ValueKind == JsonValueKind.Array && element.EnumerateArray().Any(HasDuplicateProperties);
    }

    private sealed class LegacyBooleanConverter : JsonConverter<bool>
    {
        public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType is JsonTokenType.True or JsonTokenType.False)
                return reader.GetBoolean();
            if (reader.TokenType == JsonTokenType.String && bool.TryParse(reader.GetString(), out var value))
                return value;
            throw new JsonException("Expected a boolean value.");
        }

        public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) => writer.WriteBooleanValue(value);
    }
}
