using System.Text.Json;
using XtremeIdiots.Portal.Web.Controllers;

namespace XtremeIdiots.Portal.Web.Tests.Controllers;

public class Cod4xPluginSettingsJsonHelperTests
{
    private readonly static JsonSerializerOptions jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void TryDeserialize_WithNestedBooleanStrings_NormalizesOnlyBooleanStringValues()
    {
        const string json = """
            {
              "enabled": "  TrUe  ",
              "trueString": "TRUE",
              "falseString": " fAlSe ",
              "nested": {
                "flag": " false ",
                "items": [" true ", { "flag": "FALSE" }, null, 17, [], {}]
              },
              "nativeBoolean": true,
              "nativeFalseBoolean": false,
              "nullChild": null,
              "emptyObject": {},
              "emptyArray": [],
              "nonBooleanString": "yes",
              "number": 42
            }
            """;

        var result = Cod4xPluginSettingsJsonHelper.TryDeserialize(json, jsonOptions, out var document);

        Assert.True(result);
        Assert.NotNull(document);
        Assert.True(document.Enabled);

        var extensionData = document.ExtensionData!;
        Assert.Equal(JsonValueKind.True, extensionData["trueString"].ValueKind);
        Assert.Equal(JsonValueKind.False, extensionData["falseString"].ValueKind);

        var nested = extensionData["nested"];
        Assert.Equal(JsonValueKind.False, nested.GetProperty("flag").ValueKind);
        var items = nested.GetProperty("items");
        Assert.Equal(JsonValueKind.True, items[0].ValueKind);
        Assert.Equal(JsonValueKind.False, items[1].GetProperty("flag").ValueKind);
        Assert.Equal(JsonValueKind.Null, items[2].ValueKind);
        Assert.Equal(17, items[3].GetInt32());
        Assert.Equal(JsonValueKind.Array, items[4].ValueKind);
        Assert.Empty(items[4].EnumerateArray());
        Assert.Equal(JsonValueKind.Object, items[5].ValueKind);
        Assert.Empty(items[5].EnumerateObject());

        Assert.Equal(JsonValueKind.True, extensionData["nativeBoolean"].ValueKind);
        Assert.Equal(JsonValueKind.False, extensionData["nativeFalseBoolean"].ValueKind);
        Assert.Equal(JsonValueKind.Null, extensionData["nullChild"].ValueKind);
        Assert.Equal(JsonValueKind.Object, extensionData["emptyObject"].ValueKind);
        Assert.Empty(extensionData["emptyObject"].EnumerateObject());
        Assert.Equal(JsonValueKind.Array, extensionData["emptyArray"].ValueKind);
        Assert.Empty(extensionData["emptyArray"].EnumerateArray());
        Assert.Equal("yes", extensionData["nonBooleanString"].GetString());
        Assert.Equal(42, extensionData["number"].GetInt32());
    }

    [Fact]
    public void TryDeserialize_WithJsonNull_ReturnsFalseAndNullDocument()
    {
        var result = Cod4xPluginSettingsJsonHelper.TryDeserialize("null", jsonOptions, out var document);

        Assert.False(result);
        Assert.Null(document);
    }

    [Fact]
    public void TryDeserialize_WithMalformedJson_ReturnsFalseAndNullDocument()
    {
        var result = Cod4xPluginSettingsJsonHelper.TryDeserialize("{", jsonOptions, out var document);

        Assert.False(result);
        Assert.Null(document);
    }
}
