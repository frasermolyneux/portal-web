using System.Text.Json;
using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Services.Settings;

internal static class ChatCommandEditorCompatibility
{
    public static bool RequiresReadOnly(string json, GameType? gameType = null)
    {
        try
        {
            var document = Deserialize(json);
            if (document is null || !new ChatCommandSettingsValidator().Validate(document).IsValid)
                return true;

            var projectedJson = gameType is { } serverGameType
                ? ProjectServer(document, serverGameType)
                : ProjectGlobal(document);
            using var source = JsonDocument.Parse(json);
            using var projected = JsonDocument.Parse(projectedJson);
            return !IsRepresented(source.RootElement, projected.RootElement);
        }
        catch (JsonException)
        {
            return true;
        }
    }

    public static ChatCommandSettingsDocument? Deserialize(string json) => JsonSerializer.Deserialize<ChatCommandSettingsDocument>(json, NamespaceSettingsParser.LegacyBooleanSerializerOptions);

    private static string ProjectGlobal(ChatCommandSettingsDocument document)
    {
        var model = new ChatCommandGlobalSettingsViewModel();
        ChatCommandSettingsJsonMapper.PopulateGlobal(model, document);
        // Only !fu exposes its message payload in these editors.
        foreach (var command in model.Commands.Where(static command => !string.Equals(command.Name, "fu", StringComparison.OrdinalIgnoreCase)))
            command.Messages.Clear();
        return ChatCommandSettingsJsonMapper.BuildGlobalConfigurationJson(model);
    }

    private static string ProjectServer(ChatCommandSettingsDocument document, GameType gameType)
    {
        var model = new ChatCommandServerSettingsViewModel();
        ChatCommandSettingsJsonMapper.PopulateServer(model, document);
        var supportedNames = ChatCommandDescriptorCatalog.All
            .Where(descriptor => ChatCommandDescriptorCatalog.SupportsGameType(descriptor, gameType))
            .Select(static descriptor => descriptor.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        model.Commands.RemoveAll(command => !supportedNames.Contains(command.Name));
        foreach (var command in model.Commands.Where(static command => !string.Equals(command.Name, "fu", StringComparison.OrdinalIgnoreCase)))
            command.OverrideMessages = false;
        return ChatCommandSettingsJsonMapper.BuildServerConfigurationJson(model);
    }

    private static bool IsRepresented(JsonElement source, JsonElement projected, bool opaqueSettings = false)
    {
        // Check persisted content rather than fields the legacy editor adds.
        if (source.ValueKind == JsonValueKind.Object)
        {
            if (projected.ValueKind != JsonValueKind.Object)
                return false;

            var projectedProperties = projected.EnumerateObject()
                .ToDictionary(static property => property.Name, static property => property.Value,
                    opaqueSettings ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in source.EnumerateObject())
            {
                if (!seen.Add(property.Name)
                    || !projectedProperties.TryGetValue(property.Name, out var value)
                    || !IsRepresented(property.Value, value, opaqueSettings || string.Equals(property.Name, "settings", StringComparison.OrdinalIgnoreCase)))
                    return false;
            }

            return true;
        }

        if (source.ValueKind == JsonValueKind.Array)
        {
            if (projected.ValueKind != JsonValueKind.Array || source.GetArrayLength() != projected.GetArrayLength())
                return false;
            for (var index = 0; index < source.GetArrayLength(); index++)
            {
                if (!IsRepresented(source[index], projected[index], opaqueSettings))
                    return false;
            }

            return true;
        }

        if (source.ValueKind == JsonValueKind.String
            && projected.ValueKind is JsonValueKind.True or JsonValueKind.False
            && bool.TryParse(source.GetString(), out var legacyBoolean))
            return legacyBoolean == projected.GetBoolean();

        return JsonElement.DeepEquals(source, projected);
    }
}
