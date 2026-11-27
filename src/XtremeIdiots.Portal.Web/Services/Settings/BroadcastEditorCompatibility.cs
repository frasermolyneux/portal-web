using System.Text.Json;
using System.Text.Json.Serialization;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;

namespace XtremeIdiots.Portal.Web.Services.Settings;

internal static class BroadcastEditorCompatibility
{
    private readonly static JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new LegacyBooleanConverter() }
    };

    public static bool RequiresReadOnly(string json)
    {
        try
        {
            var document = Deserialize(json);
            return document is null || !new BroadcastSettingsValidator().Validate(document).IsValid ||
                document.MessagesMode is not null || document.Enabled is null || document.IntervalSeconds is null ||
                document.ExtensionData is { Count: > 0 } ||
                document.Messages!.Any(message => message is null || message.Id is not null || message.ExtensionData is { Count: > 0 });
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public static BroadcastSettingsDocument? Deserialize(string json) => JsonSerializer.Deserialize<BroadcastSettingsDocument>(json, Options);

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
