using XtremeIdiots.Portal.Repository.Abstractions.Constants.V1;
using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.ChatCommands;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class ChatCommandEditorCompatibilityTests
{
    [Theory]
    [InlineData(/*lang=json,strict*/ """{"schemaVersion":1,"commands":{}}""", false, false)]
    [InlineData(/*lang=json,strict*/ """{"schemaVersion":1,"commands":{"fu":{"enabled":"false","freshnessSeconds":4,"requiredTags":["staff"],"settings":{"messages":[{"message":"true","enabled":"true"}]}}}}""", false, false)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":"Hello"}]}}}}""", false, false)]
    [InlineData(/*lang=json,strict*/ """{"defaults":{"enabled":"true","freshnessSeconds":{"default":5,"readOnly":5,"mutating":3},"requiredTags":["staff"]},"commands":{}}""", false, true)]
    [InlineData(/*lang=json,strict*/ """{"schemaVersion":2,"commands":{}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":null}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{},"future":{"value":true}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"defaults":{"future":true},"commands":{}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"defaults":{"freshnessSeconds":{"future":4}},"commands":{}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"enabled":true,"policy":{"rules":[],"defaultDecision":"Deny"}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"replies":{"denied":["No"]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"policy":null,"replies":null}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"future-command":{"enabled":true}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"portalpluginhealth":{"enabled":true}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":"Hello"}],"future":true}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":"Hello","future":true}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":"Hello","Enabled":true}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"Message":""}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":"first\nsecond"}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":"first\rsecond"}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"commands":{"settings":{"messages":[{"message":"not shown"}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"settings":{"messages":[{"message":false}]}}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{"fu":{"enabled":null}}}""", true, true)]
    [InlineData(/*lang=json,strict*/ """{"commands":{},"COMMANDS":{}}""", true, true)]
    [InlineData("not-json", true, true)]
    public void RequiresReadOnly_DetectsUnrepresentableDocuments(string json, bool globalReadOnly, bool serverReadOnly)
    {
        Assert.Equal(globalReadOnly, ChatCommandEditorCompatibility.RequiresReadOnly(json));
        Assert.Equal(serverReadOnly, ChatCommandEditorCompatibility.RequiresReadOnly(json, GameType.CallOfDuty4));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProtectedChatCommands_AreNeitherUpsertedNorDeleted(bool agentEnabled)
    {
        var serializer = new NamespaceSettingsSerializer();
        var global = new GlobalSettingsViewModel { ChatCommandsReadOnly = true };
        var globalConfigurations = serializer.BuildGlobalSettingsConfigurations(global);
        Assert.DoesNotContain(globalConfigurations, entry => entry.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.DoesNotContain(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);
        Assert.NotEmpty(globalConfigurations);

        var server = new GameServerEditViewModel { ChatCommandsReadOnly = true };
        server.GameServer.AgentEnabled = agentEnabled;
        var serverConfigurations = serializer.BuildGameServerConfigurations(server, false, false, false);
        Assert.DoesNotContain(serverConfigurations, entry => entry.Namespace == ChatCommandSettingsConstants.Namespace);
        Assert.DoesNotContain(ChatCommandSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }
}
