using System.Text.Json;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Cod4xPlugin;

namespace XtremeIdiots.Portal.Web.Controllers;

internal static class Cod4xPluginSettingsJsonHelper
{
    internal static bool TryDeserialize(string? json, JsonSerializerOptions options, out Cod4xPluginSettingsDocument? document)
    {
        document = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<Cod4xPluginSettingsDocument>(json, options);
            return document is not null;
        }
        catch (JsonException)
        {
            try
            {
                var normalizedJson = NormalizeBooleanStrings(json);
                document = JsonSerializer.Deserialize<Cod4xPluginSettingsDocument>(normalizedJson, options);
                return document is not null;
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }

    private static string NormalizeBooleanStrings(string json)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(json);
        if (node is null)
        {
            return json;
        }

        NormalizeBooleanNodes(node);
        return node.ToJsonString();
    }

    private static void NormalizeBooleanNodes(System.Text.Json.Nodes.JsonNode node)
    {
        if (node is System.Text.Json.Nodes.JsonObject jsonObject)
        {
            var keys = jsonObject.Select(kvp => kvp.Key).ToArray();
            foreach (var key in keys)
            {
                var child = jsonObject[key];
                if (child is null)
                {
                    continue;
                }

                if (child is System.Text.Json.Nodes.JsonValue value
                    && value.TryGetValue<string>(out var stringValue)
                    && bool.TryParse(stringValue, out var parsedBool))
                {
                    jsonObject[key] = parsedBool;
                    continue;
                }

                NormalizeBooleanNodes(child);
            }

            return;
        }

        if (node is System.Text.Json.Nodes.JsonArray jsonArray)
        {
            for (var i = 0; i < jsonArray.Count; i++)
            {
                var child = jsonArray[i];
                if (child is null)
                {
                    continue;
                }

                if (child is System.Text.Json.Nodes.JsonValue value
                    && value.TryGetValue<string>(out var stringValue)
                    && bool.TryParse(stringValue, out var parsedBool))
                {
                    jsonArray[i] = parsedBool;
                    continue;
                }

                NormalizeBooleanNodes(child);
            }
        }
    }
}
