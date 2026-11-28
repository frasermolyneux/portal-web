using XtremeIdiots.Portal.Settings.Contracts.V1.Contracts.Broadcasts;
using XtremeIdiots.Portal.Web.Services.Settings;
using XtremeIdiots.Portal.Web.ViewModels;

namespace XtremeIdiots.Portal.Web.Tests.Services.Settings;

public class BroadcastEditorCompatibilityTests
{
    [Theory]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messages":[]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":"false","intervalSeconds":500,"messages":[{"message":"Hi","enabled":"true"}]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messagesMode":"Inherit","messages":[]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messagesMode":"Replace","messages":[]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messages":[{"id":"one","message":"Hi"}]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messages":[],"future":1}""", true)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messages":[{"message":"Hi","future":1}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"enabled":null,"intervalSeconds":500,"messages":[]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":null,"messages":[]}""", false)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messagesMode":null,"messages":[]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messagesMode":"Future","messages":[]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"schemaVersion":2,"enabled":true,"intervalSeconds":500,"messages":[]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messagesMode":1,"messages":[]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"intervalSeconds":500,"messages":null}""", true)]
    [InlineData(/*lang=json,strict*/ """{"intervalSeconds":86401,"messages":[]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"enabled":true,"Enabled":false,"messages":[]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"messages":[{"id":"one","ID":"two","message":"Hi"}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"messages":[{"id":"one","message":"Hi"},{"id":"one","message":"Again"}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"messages":[{"message":"first\nsecond"}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"messages":[{"message":"first\u0000second"}]}""", true)]
    [InlineData(/*lang=json,strict*/ """{"messages":[{"id":"a\u0000b","message":"Hi"}]}""", true)]
    [InlineData("invalid", true)]
    public void RequiresReadOnly_DetectsUneditableDocuments(string json, bool expected)
    {
        Assert.Equal(expected, BroadcastEditorCompatibility.RequiresReadOnly(json));
    }

    [Fact]
    public void RequiresReadOnly_GlobalMessagesLongerThanTheEditorLimitAreProtected()
    {
        var json = $$"""{"messages":[{"message":"{{new string('a', 121)}}"}]}""";

        Assert.True(BroadcastEditorCompatibility.RequiresReadOnly(json, global: true));
        Assert.False(BroadcastEditorCompatibility.RequiresReadOnly(json));
    }

    [Fact]
    public void ProtectedBroadcasts_AreNeitherUpsertedNorDeleted()
    {
        var serializer = new NamespaceSettingsSerializer();
        var global = new GlobalSettingsViewModel { BroadcastsReadOnly = true };
        var globalConfigurations = serializer.BuildGlobalSettingsConfigurations(global);
        Assert.DoesNotContain(globalConfigurations, entry => entry.Namespace == BroadcastSettingsConstants.Namespace);
        Assert.DoesNotContain(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);

        var server = new GameServerEditViewModel { BroadcastsReadOnly = true };
        server.GameServer.AgentEnabled = true;
        server.BroadcastsEnabled = null;
        var serverConfigurations = serializer.BuildGameServerConfigurations(server, false, false, false);
        Assert.DoesNotContain(serverConfigurations, entry => entry.Namespace == BroadcastSettingsConstants.Namespace);
        Assert.DoesNotContain(BroadcastSettingsConstants.Namespace, serializer.DeletedNamespaces);
    }
}
